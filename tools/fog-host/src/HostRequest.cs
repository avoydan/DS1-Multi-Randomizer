using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FogHost
{
    public sealed record CombinedFile(string Path, string BaseSha256, string CombinedSha256);

    public sealed record PreservedNpcParam(int Id, int BaseId);

    /// <summary>Schema-v3 request (apps/backend/src/adapters/fogHostProcess.ts `FogHostRequest`).</summary>
    public sealed class HostRequest
    {
        public const int SchemaVersion = 3;
        public const string LegacyFogModAnnotations = "legacy-fogmod";
        public const string CompactAnnotations = "compact-v1";

        /// <summary>Contract option key -> FogMod option flag (docs/FOGHOST_INTEGRATION.md).</summary>
        public static readonly IReadOnlyList<(string Key, string Flag)> OptionFlags = new[]
        {
            ("traversableFogs", "world"),
            ("bossFogs", "boss"),
            ("areaWarps", "warp"),
            ("majorPvpFogs", "major"),
            ("minorPvpFogs", "minor"),
            ("lordvesselGates", "lordvessel"),
            ("randomStart", "start"),
            ("disconnectedFogs", "unconnected"),
            ("noFallBedOfChaos", "bboc"),
            ("scaling", "scale"),
            ("glitchedLogic", "hard"),
            ("requireLordSouls", "lords"),
            ("pacifistMode", "pacifist"),
        };

        public static readonly IReadOnlyCollection<string> Languages = new HashSet<string>(StringComparer.Ordinal)
        {
            "ENGLISH", "FRENCH", "GERMAN", "ITALIAN", "JAPANESE", "KOREAN",
            "NSPANISH", "POLISH", "PORTUGUESE", "RUSSIAN", "SCHINESE", "SPANISH", "TCHINESE",
        };

        private static readonly Regex Sha256 = new Regex("^[a-f0-9]{64}$", RegexOptions.CultureInvariant);
        private static readonly Regex SharedPath = new Regex(
            @"^(map/MapStudio/[^/]+\.msb|event/[^/]+\.emevd\.dcx|param/GameParam/GameParam\.parambnd\.dcx)$",
            RegexOptions.CultureInvariant);

        public bool DryRun { get; private set; }
        public string Mode => DryRun ? "dryRun" : "write";
        public uint Seed { get; private set; }
        public IReadOnlyDictionary<string, bool> Options { get; private set; }
        public string Language { get; private set; }
        public string FogDataRoot { get; private set; }
        public string StagingDirectory { get; private set; }
        public string LogPath { get; private set; }
        public string AnnotationSource { get; private set; }
        public string PristineGameDirectory { get; private set; }
        public string ExpectedAnnotationFingerprint { get; private set; }
        public string BaseDirectory { get; private set; }
        public IReadOnlyList<CombinedFile> CombinedFiles { get; private set; }
        public IReadOnlyList<PreservedNpcParam> PreservedNpcParams { get; private set; }

        public static HostRequest Parse(string json)
        {
            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(json);
            }
            catch (JsonException e)
            {
                throw Invalid($"Request is not valid JSON: {e.Message}");
            }
            using (document)
            {
                JsonElement root = document.RootElement;
                ExpectKeys(root, "request", "schemaVersion", "game", "mode", "seed", "options", "language",
                    "fogDataRoot", "stagingDirectory", "logPath", "annotationSource", "pristineGameDirectory",
                    "expectedAnnotationFingerprint", "compatibility");
                if (!root.GetProperty("schemaVersion").TryGetInt32(out int schema) || schema != SchemaVersion)
                    throw Invalid($"request.schemaVersion must be {SchemaVersion}.");
                if (String(root, "game") != "DS1R") throw Invalid("request.game must be \"DS1R\"; FogHost supports Dark Souls Remastered only.");
                string mode = String(root, "mode");
                if (mode != "dryRun" && mode != "write") throw Invalid("request.mode must be \"dryRun\" or \"write\".");

                JsonElement seed = root.GetProperty("seed");
                if (seed.ValueKind != JsonValueKind.Number || !seed.TryGetUInt32(out uint seedValue))
                    throw Invalid("request.seed must be an integer in the uint32 range.");

                JsonElement options = root.GetProperty("options");
                ExpectKeys(options, "request.options", OptionFlags.Select(o => o.Key).ToArray());
                var optionValues = new Dictionary<string, bool>(StringComparer.Ordinal);
                foreach ((string key, _) in OptionFlags)
                {
                    JsonElement value = options.GetProperty(key);
                    if (value.ValueKind != JsonValueKind.True && value.ValueKind != JsonValueKind.False)
                        throw Invalid($"request.options.{key} must be a boolean.");
                    optionValues[key] = value.GetBoolean();
                }

                string language = String(root, "language");
                if (!Languages.Contains(language)) throw Invalid($"request.language \"{language}\" is not a supported FogMod language.");

                var request = new HostRequest
                {
                    DryRun = mode == "dryRun",
                    Seed = seedValue,
                    Options = optionValues,
                    Language = language,
                    FogDataRoot = AbsolutePath(root, "fogDataRoot"),
                    StagingDirectory = AbsolutePath(root, "stagingDirectory"),
                    LogPath = AbsolutePath(root, "logPath"),
                    AnnotationSource = String(root, "annotationSource"),
                    PristineGameDirectory = AbsolutePath(root, "pristineGameDirectory"),
                    ExpectedAnnotationFingerprint = String(root, "expectedAnnotationFingerprint"),
                };

                if (request.AnnotationSource != LegacyFogModAnnotations && request.AnnotationSource != CompactAnnotations)
                    throw Invalid($"request.annotationSource must be \"{LegacyFogModAnnotations}\" or \"{CompactAnnotations}\".");
                if (request.AnnotationSource == CompactAnnotations && !Sha256.IsMatch(request.ExpectedAnnotationFingerprint))
                    throw Invalid("request.expectedAnnotationFingerprint must be a lowercase SHA-256 for Compact annotations.");
                if (request.AnnotationSource == LegacyFogModAnnotations && request.ExpectedAnnotationFingerprint.Length != 0)
                    throw Invalid("request.expectedAnnotationFingerprint must be empty for legacy FogMod annotations.");

                JsonElement compatibility = root.GetProperty("compatibility");
                ExpectKeys(compatibility, "request.compatibility", "baseDirectory", "combinedFiles", "preservedNpcParams");
                request.BaseDirectory = AbsolutePath(compatibility, "baseDirectory");

                JsonElement files = compatibility.GetProperty("combinedFiles");
                if (files.ValueKind != JsonValueKind.Array) throw Invalid("request.compatibility.combinedFiles must be an array.");
                var combined = new List<CombinedFile>();
                foreach (JsonElement file in files.EnumerateArray())
                {
                    ExpectKeys(file, "request.compatibility.combinedFiles[]", "path", "baseSha256", "combinedSha256");
                    var entry = new CombinedFile(String(file, "path"), String(file, "baseSha256"), String(file, "combinedSha256"));
                    if (!SharedPath.IsMatch(entry.Path)) throw Invalid($"Combined file path is not a shared MSB/EMEVD/GameParam path: {entry.Path}");
                    if (!Sha256.IsMatch(entry.BaseSha256) || !Sha256.IsMatch(entry.CombinedSha256))
                        throw Invalid($"Combined file hashes must be lowercase SHA-256: {entry.Path}");
                    combined.Add(entry);
                }
                if (combined.Select(f => f.Path).Distinct(StringComparer.Ordinal).Count() != combined.Count)
                    throw Invalid("Combined file paths must be unique.");
                request.CombinedFiles = combined.OrderBy(f => f.Path, StringComparer.Ordinal).ToList();

                JsonElement preserved = compatibility.GetProperty("preservedNpcParams");
                if (preserved.ValueKind != JsonValueKind.Array) throw Invalid("request.compatibility.preservedNpcParams must be an array.");
                var rows = new List<PreservedNpcParam>();
                foreach (JsonElement row in preserved.EnumerateArray())
                {
                    ExpectKeys(row, "request.compatibility.preservedNpcParams[]", "id", "baseId");
                    if (!row.GetProperty("id").TryGetInt32(out int id) || id <= 0
                        || !row.GetProperty("baseId").TryGetInt32(out int baseId) || baseId <= 0)
                        throw Invalid("Preserved NpcParam ids must be positive int32 values.");
                    rows.Add(new PreservedNpcParam(id, baseId));
                }
                if (rows.Select(r => r.Id).Distinct().Count() != rows.Count) throw Invalid("Preserved NpcParam ids must be unique.");
                request.PreservedNpcParams = rows.OrderBy(r => r.Id).ToList();
                return request;
            }
        }

        private static void ExpectKeys(JsonElement element, string where, params string[] keys)
        {
            if (element.ValueKind != JsonValueKind.Object) throw Invalid($"{where} must be an object.");
            var actual = element.EnumerateObject().Select(p => p.Name).ToList();
            var unknown = actual.Except(keys, StringComparer.Ordinal).ToList();
            var missing = keys.Except(actual, StringComparer.Ordinal).ToList();
            if (actual.Count != actual.Distinct(StringComparer.Ordinal).Count()) throw Invalid($"{where} has duplicate keys.");
            if (unknown.Count > 0) throw Invalid($"{where} has unknown keys: {string.Join(", ", unknown)}.");
            if (missing.Count > 0) throw Invalid($"{where} is missing keys: {string.Join(", ", missing)}.");
        }

        private static string String(JsonElement element, string key)
        {
            JsonElement value = element.GetProperty(key);
            if (value.ValueKind != JsonValueKind.String) throw Invalid($"{key} must be a string.");
            return value.GetString();
        }

        private static string AbsolutePath(JsonElement element, string key)
        {
            string value = String(element, key);
            if (string.IsNullOrWhiteSpace(value) || !Path.IsPathFullyQualified(value))
                throw Invalid($"{key} must be an absolute path.");
            return Path.GetFullPath(value);
        }

        private static FogHostException Invalid(string message) =>
            new FogHostException(HostErrors.RequestInvalid, message) { Stage = "validate-request" };
    }
}
