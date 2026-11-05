using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using YamlDotNet.Serialization;
using static FogMod.AnnotationData;

namespace FogHost
{
    /// <summary>
    /// Read-only projection of the UPSTREAM item model for the backend (generation profile ds1tr-gen-v7): the key items and the item lots the original fog.txt declares
    /// (<c>KeyItems</c> and <c>LotLocations</c>). The Items randomizer places progression items using exactly this list; FogMod's own <c>ItemReader</c> then finds where each one
    /// ended up in the staged item lots. Nothing here is derived from a catalog, a snapshot or a reduced "required" set.
    /// </summary>
    public static class UpstreamProjectionCommands
    {
        public const string ProjectionFormat = "ds1tr-upstream-fog-projection-v1";

        public static bool Handles(string[] args) => args.Length > 0 && args[0] == "upstream-projection";

        public static int Run(string[] args)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
            if (args.Length != 5 || args[1] != "--data-root" || args[3] != "--out")
            {
                Console.Error.WriteLine("Usage: FogHost upstream-projection --data-root <dir> --out <json>");
                return 2;
            }
            try
            {
                string root = Path.GetFullPath(args[2]);
                string output = Path.GetFullPath(args[4]);
                DataRoot.VerifyForSource(root, HostRequest.UpstreamV7Annotations);
                byte[] json = Build(root);
                File.WriteAllBytes(output + ".tmp", json);
                File.Move(output + ".tmp", output, true);
                return 0;
            }
            catch (FogHostException e)
            {
                Console.Error.WriteLine($"{e.Code}: {e.Message}");
                return 4;
            }
            catch (Exception e)
            {
                Console.Error.WriteLine($"UPSTREAM_PROJECTION_FAILED: {e.Message}");
                return 5;
            }
        }

        /// <summary>The projection of a verified data root, as UTF-8 JSON (deterministic: ascending lots, key items in file order).</summary>
        public static byte[] Build(string root)
        {
            Annotations ann;
            using (StreamReader reader = File.OpenText(Path.Combine(root, "dist", "fog.txt"))) ann = new DeserializerBuilder().Build().Deserialize<Annotations>(reader);
            using var stream = new MemoryStream();
            using (var json = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false }))
            {
                json.WriteStartObject();
                json.WriteString("format", ProjectionFormat);
                json.WriteString("annotationSha256", DataRoot.AnnotationSha256(root));
                json.WriteStartArray("items");
                foreach (Item item in ann.KeyItems)
                {
                    string[] parts = item.ID.Split(':');
                    if (parts.Length != 2 || !int.TryParse(parts[0], out int equipType) || !int.TryParse(parts[1], out int itemId))
                        throw new InvalidOperationException($"Key item {item.Name} has an unsupported id {item.ID}.");
                    json.WriteStartObject();
                    json.WriteNumber("equipType", equipType);
                    json.WriteNumber("itemId", itemId);
                    // The same rule as the legacy tracking snapshot: random-only and glitched-logic-only items do not constrain placement.
                    json.WriteBoolean("required", !item.HasTag("randomonly") && !item.HasTag("hard"));
                    json.WriteString("name", item.Name);
                    json.WriteEndObject();
                }
                json.WriteEndArray();
                json.WriteStartArray("lotStarts");
                foreach (int lot in ann.LotLocations.Keys.OrderBy(l => l)) json.WriteNumberValue(lot);
                json.WriteEndArray();
                json.WriteEndObject();
            }
            return stream.ToArray();
        }
    }
}
