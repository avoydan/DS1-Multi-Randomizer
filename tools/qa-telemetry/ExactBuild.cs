using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace QaTelemetry;

public sealed record SignatureDefinition(string Name, string Pattern, int DisplacementOffset, int InstructionLength, string Purpose, string EvidenceUrl);
public sealed record SignatureMatch(string Name, int MatchCount, string Status, int? MatchRva, int? TargetRva, string? TargetSection, string Purpose, string EvidenceUrl);
public sealed record ExactBuildAuditResult(string Status, string ExecutablePath, string ExecutableSha256, string ExpectedSha256, string ToolVersion, string TelemetryDefinitionSha256, SignatureMatch[] Signatures);

public static class ExactBuildProfile
{
    public const string CalibrationExecutablePath = @"C:\Users\Adam\VisualStudioProjects\ds1-triple-randomizer-v5-isolation\.workspace\qa\runtime\DARK SOULS REMASTERED\DarkSoulsRemastered.exe";
    public const string ExpectedExecutablePath = @"C:\Users\Adam\VisualStudioProjects\ds1-triple-randomizer-v5-isolation\.workspace\qa\runtime\DARK SOULS REMASTERED - CEASELESS QA\DarkSoulsRemastered.exe";
    public const string ExpectedExecutableSha256 = "a45aaa36dd2f6cc151670a639ea5547043cf38ea79ff4178b963c6ed71f98d7b";
    public const string ExpectedRuntimeDigest = "5f182859f78f285a89a31a608ea28dc5b1cb069b082aa906637da11aea2873ef";
    public const string ExpectedCleanManifestSha256 = "89a9dc2a0fa7dbf990521e7adb9f27586c140334676639a88edeb1bd4b2d3729";
    public const string MarkerProfileId = "dsr-1.03.1-executable-telemetry-v2";

    // These signatures and offsets are candidate definitions from community tooling.
    // Exact-build uniqueness checks below verify the byte anchors, not the runtime
    // meaning of the pointer chains; those stay CANDIDATE_UNVERIFIED until owner validation.
    public static readonly SignatureDefinition[] Definitions =
    [
        new("worldChrMan", "48 8B 0D ?? ?? ?? ?? 0F 28 F1 48 85 C9 74 ?? 48 89 7C", 3, 7,
            "WorldChrMan singleton; candidate player and position root", "https://github.com/FrankvdStam/SoulSplitter/blob/main/src/SoulMemory/DarkSouls1/Remastered.cs"),
        new("eventFlags", "48 8B 0D ?? ?? ?? ?? 99 33 C2 45 33 C0 2B C2 8D 50 F6", 3, 7,
            "Event flag manager singleton", "https://github.com/FrankvdStam/SoulSplitter/blob/main/src/SoulMemory/DarkSouls1/Remastered.cs"),
        new("worldChrMapCandidate", "48 8B 05 ?? ?? ?? ?? 48 8B 48 68 48 85 C9 0F 84 ?? ?? ?? ?? 48 39 5E 10 0F 84 ?? ?? ?? ?? 48", 3, 7,
            "Candidate player OnlineAreaID field; not yet proven to be canonical map identity", "https://raw.githubusercontent.com/JohrnaJohrna/RemasterCETable/master/DarkSoulsRemastered.CT")
    ];

    public static string ToolVersion => typeof(ExactBuildProfile).Assembly.GetName().Version?.ToString(3) ?? "unknown";

    public static string DefinitionSha256 => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        string.Join("\n", Definitions.Select(d => $"signature|{d.Name}|{d.Pattern}|{d.DisplacementOffset}|{d.InstructionLength}|{d.Purpose}|{d.EvidenceUrl}")
            .Concat([CandidateTelemetryReader.DefinitionDescriptor, EventFlagAddressing.DefinitionDescriptor]))))).ToLowerInvariant();

    public static string ExecutableProfileId => "dsr-executable-profile-v2-" + Convert.ToHexString(SHA256.HashData(
        Encoding.ASCII.GetBytes(ExpectedExecutableSha256 + "|" + DefinitionSha256 + "|" + ToolVersion))).ToLowerInvariant()[..16];

    public static bool IsApprovedExecutablePath(string executablePath)
    {
        string full = SafePaths.NormalizeFullPath(executablePath);
        return string.Equals(full, CalibrationExecutablePath, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(full, ExpectedExecutablePath, StringComparison.OrdinalIgnoreCase);
    }

    public static void RequireApprovedExecutable(string executablePath, string executableSha256)
    {
        SafePaths.RejectSteamPath(executablePath);
        if (!IsApprovedExecutablePath(executablePath))
            throw new QaTelemetryException("QA_PROCESS_PATH_MISMATCH", "Telemetry accepts only the exact clean-calibration or package-applied QA executable path.");
        if (!FixedEquals(executableSha256, ExpectedExecutableSha256))
            throw new QaTelemetryException("QA_EXECUTABLE_HASH_MISMATCH", "Telemetry accepts only the locked exact-build executable hash.");
    }

    public static ExactBuildAuditResult Audit(string executablePath)
    {
        string full = SafePaths.NormalizeFullPath(executablePath);
        SafePaths.RejectSteamPath(full);
        if (!IsApprovedExecutablePath(full))
            throw new QaTelemetryException("QA_PROCESS_PATH_MISMATCH", "Static build audit accepts only the exact clean-calibration or package-applied QA executable path.");
        SafePaths.EnsureNoReparseAncestors(full, leafMustExist: true);
        SafePaths.EnsureUniqueFile(full);
        string hash = SafePaths.HashFile(full);
        if (!FixedEquals(hash, ExpectedExecutableSha256))
            throw new QaTelemetryException("QA_EXECUTABLE_HASH_MISMATCH", "The isolated QA executable hash differs from the exact-build lock.");

        byte[] image = File.ReadAllBytes(full);
        (int imageSize, List<PeSection> sections, PeSection[] textSections) = ReadPe(image);
        var results = new List<SignatureMatch>();
        foreach (SignatureDefinition definition in Definitions)
        {
            byte?[] pattern = ParsePattern(definition.Pattern);
            (int Offset, PeSection Section)[] hits = textSections
                .SelectMany(section => FindMatches(image.AsSpan(section.RawOffset, section.RawSize), pattern)
                    .Select(offset => (checked(offset + section.RawOffset), section)))
                .ToArray();
            if (hits.Length != 1)
            {
                results.Add(new(definition.Name, hits.Length, "UNVERIFIED", null, null, null, definition.Purpose, definition.EvidenceUrl));
                continue;
            }

            int hit = hits[0].Offset;
            PeSection text = hits[0].Section;
            int matchRva = checked(text.VirtualAddress + hit - text.RawOffset);
            int displacement = BinaryPrimitives.ReadInt32LittleEndian(image.AsSpan(hit + definition.DisplacementOffset, 4));
            int targetRva = checked(matchRva + definition.InstructionLength + displacement);
            PeSection? target = sections.FirstOrDefault(section => section.ContainsRva(targetRva));
            bool targetValid = targetRva >= 0 && targetRva < imageSize && target is not null &&
                (target.Name.Equals(".data", StringComparison.OrdinalIgnoreCase) || target.Name.Equals(".bss", StringComparison.OrdinalIgnoreCase));
            results.Add(new(definition.Name, 1, targetValid ? "CANDIDATE_UNVERIFIED" : "UNVERIFIED",
                matchRva, targetValid ? targetRva : null, target?.Name, definition.Purpose, definition.EvidenceUrl));
        }

        string status = results.All(x => x.Status == "CANDIDATE_UNVERIFIED") ? "EXACT_BUILD_SIGNATURES_UNIQUE_CANDIDATE" : "EXACT_BUILD_SIGNATURES_UNVERIFIED";
        return new(status, full, hash, ExpectedExecutableSha256, ToolVersion, DefinitionSha256, results.ToArray());
    }

    public static int[] FindMatches(ReadOnlySpan<byte> data, IReadOnlyList<byte?> pattern)
    {
        if (pattern.Count == 0 || pattern.Count > data.Length) return [];
        var hits = new List<int>();
        for (int offset = 0; offset <= data.Length - pattern.Count; offset++)
        {
            bool match = true;
            for (int i = 0; i < pattern.Count; i++)
            {
                if (pattern[i].HasValue && data[offset + i] != pattern[i]!.Value)
                {
                    match = false;
                    break;
                }
            }
            if (match) hits.Add(offset);
        }
        return hits.ToArray();
    }

    public static byte?[] ParsePattern(string value) => value.Split(' ', StringSplitOptions.RemoveEmptyEntries)
        .Select(token => token is "?" or "??" ? (byte?)null : Convert.ToByte(token, 16)).ToArray();

    private static (int ImageSize, List<PeSection> Sections, PeSection[] TextSections) ReadPe(byte[] image)
    {
        if (image.Length < 0x100 || image[0] != (byte)'M' || image[1] != (byte)'Z') throw new InvalidDataException("Executable is not a valid PE image.");
        int peOffset = BinaryPrimitives.ReadInt32LittleEndian(image.AsSpan(0x3c, 4));
        if (peOffset < 0x40 || peOffset > image.Length - 24 || BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(peOffset, 4)) != 0x00004550)
            throw new InvalidDataException("PE signature is invalid.");
        ushort machine = BinaryPrimitives.ReadUInt16LittleEndian(image.AsSpan(peOffset + 4, 2));
        ushort sectionCount = BinaryPrimitives.ReadUInt16LittleEndian(image.AsSpan(peOffset + 6, 2));
        ushort optionalSize = BinaryPrimitives.ReadUInt16LittleEndian(image.AsSpan(peOffset + 20, 2));
        int optionalOffset = peOffset + 24;
        if (machine != 0x8664 || BinaryPrimitives.ReadUInt16LittleEndian(image.AsSpan(optionalOffset, 2)) != 0x20b)
            throw new InvalidDataException("Exact QA build must be a 64-bit x64 PE image.");
        int imageSize = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(optionalOffset + 56, 4)));
        int sectionTable = optionalOffset + optionalSize;
        if (sectionCount == 0 || sectionTable > image.Length - sectionCount * 40) throw new InvalidDataException("PE section table is invalid.");
        var sections = new List<PeSection>(sectionCount);
        for (int i = 0; i < sectionCount; i++)
        {
            int offset = sectionTable + i * 40;
            string name = Encoding.ASCII.GetString(image, offset, 8).TrimEnd('\0');
            var section = new PeSection(name,
                checked((int)BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(offset + 8, 4))),
                checked((int)BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(offset + 12, 4))),
                checked((int)BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(offset + 16, 4))),
                checked((int)BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(offset + 20, 4))));
            if (section.RawOffset < 0 || section.RawSize < 0 || section.RawOffset > image.Length - section.RawSize)
                throw new InvalidDataException("PE section raw bounds are invalid.");
            sections.Add(section);
        }
        PeSection[] textSections = sections.Where(x => x.Name == ".text").ToArray();
        if (textSections.Length == 0) throw new InvalidDataException("PE image has no .text section.");
        return (imageSize, sections, textSections);
    }

    private static bool FixedEquals(string a, string b) => a.Length == b.Length && CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(a.ToLowerInvariant()), Encoding.ASCII.GetBytes(b.ToLowerInvariant()));

    private sealed record PeSection(string Name, int VirtualSize, int VirtualAddress, int RawSize, int RawOffset)
    {
        public bool ContainsRva(int rva) => rva >= VirtualAddress && rva < VirtualAddress + Math.Max(VirtualSize, RawSize);
    }
}
