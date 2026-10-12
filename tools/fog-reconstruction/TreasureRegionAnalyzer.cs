using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using FogHost;

namespace FogReconstruction
{
    /// <summary>Writes a development report of DSR treasure positions and geometrically containing MSB regions.</summary>
    public static class TreasureRegionAnalyzer
    {
        public static void Write(string dataRoot, string pristineGameRoot, string outputPath)
        {
            if (string.IsNullOrWhiteSpace(outputPath)) throw new ArgumentException("An output path is required.", nameof(outputPath));
            FogDsrFacts facts = FogDsrFacts.Read(dataRoot, pristineGameRoot);
            string fullPath = Path.GetFullPath(outputPath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));

            using var stream = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            using var json = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
            var lots = facts.TreasureLots.OrderBy(lot => lot.Map.Value, StringComparer.Ordinal)
                .ThenBy(lot => lot.TreasureEntityId).ThenBy(lot => lot.Slot).ToArray();
            int withPosition = lots.Count(lot => lot.PartTransform != null);
            int withCollision = lots.Count(lot => !string.IsNullOrWhiteSpace(lot.PartCollisionName));
            int positionedWithNoRegion = lots.Count(lot => lot.PartTransform != null
                && FogDsrSpatialIndex.ContainingRegions(facts.Maps[lot.Map], lot.PartTransform).Count == 0);

            json.WriteStartObject();
            json.WriteString("format", "ds1tr-dsr-treasure-region-candidates-1");
            json.WriteString("interpretation", "Geometric containment candidates only; region names are not assigned Fog logical-area semantics by this report.");
            json.WriteNumber("treasureLotReferences", lots.Length);
            json.WriteNumber("referencesWithUniquePartTransform", withPosition);
            json.WriteNumber("referencesWithPartCollisionName", withCollision);
            json.WriteNumber("positionedReferencesWithNoContainingRegion", positionedWithNoRegion);
            json.WriteStartArray("references");
            foreach (FogDsrTreasureLotFact lot in lots)
            {
                json.WriteStartObject();
                json.WriteString("map", lot.Map.Value);
                json.WriteNumber("treasureEntityId", lot.TreasureEntityId);
                json.WriteString("partName", lot.PartName);
                json.WriteString("partCollisionName", lot.PartCollisionName);
                json.WriteBoolean("startDisabled", lot.StartDisabled);
                json.WriteBoolean("inChest", lot.InChest);
                json.WriteNumber("slot", lot.Slot);
                json.WriteNumber("itemLotId", lot.ItemLotId);
                if (lot.PartTransform == null)
                {
                    json.WriteNull("partPosition");
                    json.WriteStartArray("containingRegions");
                    json.WriteEndArray();
                }
                else
                {
                    json.WriteStartObject("partPosition");
                    json.WriteNumber("x", lot.PartTransform.X);
                    json.WriteNumber("y", lot.PartTransform.Y);
                    json.WriteNumber("z", lot.PartTransform.Z);
                    json.WriteNumber("yaw", lot.PartTransform.Yaw);
                    json.WriteEndObject();

                    json.WriteStartArray("containingRegions");
                    foreach (FogDsrRegionFact region in FogDsrSpatialIndex.ContainingRegions(facts.Maps[lot.Map], lot.PartTransform))
                    {
                        json.WriteStartObject();
                        json.WriteString("name", region.Name);
                        json.WriteNumber("entityId", region.EntityId);
                        json.WriteString("shape", region.Shape.Kind.ToString());
                        json.WriteEndObject();
                    }
                    json.WriteEndArray();
                }
                json.WriteEndObject();
            }
            json.WriteEndArray();
            json.WriteEndObject();
            json.Flush();
        }
    }
}
