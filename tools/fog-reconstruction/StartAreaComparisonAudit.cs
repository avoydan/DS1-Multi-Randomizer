using System;
using System.IO;
using System.Linq;
using FogHost;
using YamlDotNet.Serialization;

namespace FogReconstruction
{
    /// <summary>
    /// Development-only shape check for the project-authored start-area slice. It reads the
    /// reference solely to report comparison counts; it does not copy reference values into
    /// project policy or emit reference edge targets/conditions.
    /// </summary>
    internal static class StartAreaComparisonAudit
    {
        public static void Write(string referenceRoot)
        {
            string path = Path.Combine(referenceRoot, "dist", "fog.txt");
            if (!File.Exists(path)) throw new FileNotFoundException("The comparison-only Fog reference is missing.", path);

            FogMod.AnnotationData.Annotations reference;
            using (StreamReader reader = File.OpenText(path))
                reference = new DeserializerBuilder().Build().Deserialize<FogMod.AnnotationData.Annotations>(reader);
            if (reference?.Areas == null) throw new InvalidDataException("The comparison-only reference has no area list.");

            FogArea[] projectAreas = FogProjectAnnotationPolicy.CustomStartAreaNodes.ToArray();
            int found = 0;
            int exactTags = 0;
            int exactScalingBase = 0;
            int exactLinks = 0;
            int exactConsumedRecords = 0;
            int referenceLinks = 0;

            foreach (FogArea project in projectAreas)
            {
                FogMod.AnnotationData.Area expected = reference.Areas.SingleOrDefault(area =>
                    string.Equals(area.Name, project.FogModName.Value, StringComparison.Ordinal));
                if (expected == null)
                {
                    Console.WriteLine($"{project.Id.Value}: projection key absent from comparison reference");
                    continue;
                }

                found++;
                bool tagsMatch = Tags(project.Tags).SequenceEqual(Tags(expected.Tags), StringComparer.Ordinal);
                bool baseMatch = project.ScalingBase == null
                    ? expected.ScalingBase == null
                    : string.Equals(project.ScalingBase.Value.Value, expected.ScalingBase, StringComparison.Ordinal);
                int actualLinks = project.To?.Count ?? 0;
                int expectedLinks = expected.To?.Count ?? 0;
                bool linksMatch = actualLinks == expectedLinks;
                if (linksMatch)
                {
                    for (int index = 0; index < actualLinks; index++)
                    {
                        FogAreaLink actual = project.To[index];
                        FogMod.AnnotationData.Side referenceLink = expected.To[index];
                        if (!string.Equals(actual.Destination.Value, referenceLink.Area, StringComparison.Ordinal)
                            || !string.Equals(actual.Condition?.ToFogModCondition(FogProjectAnnotationPolicy.ResolveAreaProjection), referenceLink.Cond, StringComparison.Ordinal)
                            || !ActiveLinkTags(actual.Tags).SequenceEqual(ActiveLinkTags(referenceLink.TagList), StringComparer.Ordinal))
                        {
                            linksMatch = false;
                            break;
                        }
                    }
                }

                if (tagsMatch) exactTags++;
                if (baseMatch) exactScalingBase++;
                if (linksMatch) exactLinks++;
                if (tagsMatch && baseMatch && linksMatch) exactConsumedRecords++;
                referenceLinks += expectedLinks;

                Console.WriteLine($"{project.Id.Value}: tags={(tagsMatch ? "match" : "different")}, scalingBase={(baseMatch ? "match" : "different")}, projectLinks={actualLinks}, referenceLinkCount={expectedLinks}, links={(linksMatch ? "match" : "open")}");
            }

            Console.WriteLine($"Start-area comparison only: matchedKeys={found}/{projectAreas.Length}, consumedRecordsExact={exactConsumedRecords}/{found}, tags={exactTags}/{found}, scalingBases={exactScalingBase}/{found}, linkLists={exactLinks}/{found}, referenceLinksAcrossSlice={referenceLinks}. No reference edge targets or conditions were emitted.");
        }

        private static string[] Tags(FogAreaTags tags) => new[]
        {
            (FogAreaTags.Boss, "boss"),
            (FogAreaTags.Optional, "optional"),
            (FogAreaTags.Trivial, "trivial"),
        }.Where(pair => tags.HasFlag(pair.Item1)).Select(pair => pair.Item2).OrderBy(value => value, StringComparer.Ordinal).ToArray();

        private static string[] Tags(string tags) => (tags ?? string.Empty)
            .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

        private static string[] ActiveLinkTags(FogAreaLinkTags tags) => new[]
        {
            (FogAreaLinkTags.Hard, "hard"),
            (FogAreaLinkTags.Shortcut, "shortcut"),
            (FogAreaLinkTags.Temporary, "temp"),
        }.Where(pair => tags.HasFlag(pair.Item1)).Select(pair => pair.Item2).OrderBy(value => value, StringComparer.Ordinal).ToArray();

        private static string[] ActiveLinkTags(System.Collections.Generic.IEnumerable<string> tags) => (tags ?? Array.Empty<string>())
            .Where(tag => tag == "hard" || tag == "shortcut" || tag == "temp")
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
    }
}
