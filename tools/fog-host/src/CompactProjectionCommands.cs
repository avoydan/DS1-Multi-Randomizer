using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;

namespace FogHost
{
    /// <summary>Read-only Compact catalog projection for backend item-tracking constraints.</summary>
    public static class CompactProjectionCommands
    {
        public const string ProjectionFormat = "ds1tr-compact-fog-projection-v1";

        public static bool Handles(string[] args) => args.Length > 0 && args[0] == "compact-projection";

        public static int Run(string[] args)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
            if ((args.Length != 7 && args.Length != 9) || args[1] != "--data-root" || args[3] != "--pristine-root"
                || (args.Length == 7 ? args[5] != "--out" : args[5] != "--catalog-version" || args[7] != "--out"))
            {
                Console.Error.WriteLine("Usage: FogHost compact-projection --data-root <dir> --pristine-root <dir> --out <json>");
                return 2;
            }

            string originalDirectory = Environment.CurrentDirectory;
            try
            {
                string dataRoot = Path.GetFullPath(args[2]);
                string pristineRoot = Path.GetFullPath(args[4]);
                string catalogVersion = args.Length == 9 ? args[6] : FogCompactCatalogV1.Version;
                string output = Path.GetFullPath(args[args.Length - 1]);
                DataRoot.Verify(dataRoot, includeLegacyAnnotations: false);
                Environment.CurrentDirectory = dataRoot;
                FogDsrFacts facts = FogDsrFacts.Read(dataRoot, pristineRoot, includeLegacyAnnotations: false);
                FogCompactBackendProjection projection;
                string fingerprint;
                if (catalogVersion == FogCompactCatalogV2.Version)
                {
                    FogCompactCatalogV2 catalog = FogCompactCatalogBuilder.BuildV2(facts);
                    if (!catalog.IsComplete) throw new InvalidDataException("Compact annotation coverage is incomplete.");
                    projection = catalog.CreateBackendProjection();
                    fingerprint = catalog.Fingerprint;
                }
                else if (catalogVersion == FogCompactCatalogV1.Version)
                {
                    FogCompactCatalogV1 catalog = FogCompactCatalogBuilder.Build(facts);
                    if (!catalog.IsComplete) throw new InvalidDataException("Compact annotation coverage is incomplete.");
                    projection = catalog.CreateBackendProjection();
                    fingerprint = catalog.Fingerprint;
                }
                else throw new InvalidDataException($"Unsupported Compact catalog version '{catalogVersion}'.");

                var response = new
                {
                    format = ProjectionFormat,
                    catalogVersion = projection.CatalogVersion,
                    catalogFingerprint = fingerprint,
                    items = projection.Items,
                    lotStarts = projection.LotStarts,
                };
                byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(response, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                });
                string parent = Path.GetDirectoryName(output);
                if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
                string temporary = output + ".tmp";
                File.WriteAllBytes(temporary, bytes);
                File.Move(temporary, output, true);
                return 0;
            }
            catch (Exception error)
            {
                Console.Error.WriteLine($"COMPACT_PROJECTION_FAILED: {error.Message}");
                return 5;
            }
            finally
            {
                Environment.CurrentDirectory = originalDirectory;
            }
        }
    }
}
