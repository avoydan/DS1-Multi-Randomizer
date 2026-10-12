using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using FogHost;
using FogMod;

namespace FogReconstruction
{
    public enum AnnotationDifferenceClass
    {
        Exact,
        RepresentationOnly,
        IntentionalBehavioralEquivalent,
        BehaviorChanging,
        Unresolved,
    }

    public sealed record AnnotationCategoryComparison(
        string Category,
        string ReferenceSha256,
        string CatalogSha256,
        bool Equivalent,
        AnnotationDifferenceClass Classification);

    public sealed record AnnotationSemanticComparison(
        string Format,
        string ReferenceSha256,
        string CatalogSha256,
        IReadOnlyList<AnnotationCategoryComparison> Categories)
    {
        public bool HasUnresolvedDifferences => Categories.Any(category => !category.Equivalent && category.Classification == AnnotationDifferenceClass.Unresolved);
        public bool HasBehaviorChangingDifferences => Categories.Any(category => !category.Equivalent && category.Classification == AnnotationDifferenceClass.BehaviorChanging);

        public byte[] ToJson()
        {
            using var stream = new MemoryStream();
            using (var json = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
            {
                json.WriteStartObject();
                json.WriteString("format", Format);
                json.WriteString("referenceSha256", ReferenceSha256);
                json.WriteString("catalogSha256", CatalogSha256);
                json.WriteBoolean("hasUnresolvedDifferences", HasUnresolvedDifferences);
                json.WriteBoolean("hasBehaviorChangingDifferences", HasBehaviorChangingDifferences);
                json.WriteStartArray("categories");
                foreach (AnnotationCategoryComparison category in Categories)
                {
                    json.WriteStartObject();
                    json.WriteString("category", category.Category);
                    json.WriteString("referenceSha256", category.ReferenceSha256);
                    json.WriteString("catalogSha256", category.CatalogSha256);
                    json.WriteBoolean("equivalent", category.Equivalent);
                    json.WriteString("classification", category.Classification.ToString());
                    json.WriteEndObject();
                }
                json.WriteEndArray();
                json.WriteEndObject();
            }
            stream.WriteByte((byte)'\n');
            return stream.ToArray();
        }
    }

    /// <summary>Development-only consumed-model comparer. It never loads or serializes the reference source.</summary>
    public static class AnnotationSemanticComparer
    {
        public const string Format = "ds1tr-fog-annotation-semantic-comparison-2";

        public static AnnotationSemanticComparison Compare(
            AnnotationData.Annotations reference,
            FogAnnotationCatalog catalog,
            IReadOnlyDictionary<string, AnnotationDifferenceClass> reviewedDifferences = null)
        {
            if (reference == null) throw new ArgumentNullException(nameof(reference));
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
            IReadOnlyDictionary<string, string> referenceCategories = FogAnnotationFingerprint.ComputeCategories(reference);
            IReadOnlyDictionary<string, string> catalogCategories = FogAnnotationFingerprint.ComputeCategories(catalog.ToFogModAnnotations());
            var categories = new List<AnnotationCategoryComparison>();
            foreach (string name in referenceCategories.Keys.OrderBy(value => value, StringComparer.Ordinal))
            {
                string referenceHash = referenceCategories[name];
                string catalogHash = catalogCategories[name];
                bool equivalent = string.Equals(referenceHash, catalogHash, StringComparison.Ordinal);
                AnnotationDifferenceClass classification = equivalent
                    ? AnnotationDifferenceClass.Exact
                    : reviewedDifferences != null && reviewedDifferences.TryGetValue(name, out AnnotationDifferenceClass reviewed)
                        ? reviewed
                        : AnnotationDifferenceClass.Unresolved;
                categories.Add(new AnnotationCategoryComparison(name, referenceHash, catalogHash, equivalent, classification));
            }
            return new AnnotationSemanticComparison(
                Format,
                FogAnnotationFingerprint.Compute(reference),
                FogAnnotationFingerprint.Compute(catalog.ToFogModAnnotations()),
                Array.AsReadOnly(categories.ToArray()));
        }
    }

    internal static class AnnotationSemanticComparerSelfTests
    {
        public static void Run()
        {
            FogAnnotationCatalog catalog = Fixture();
            AnnotationData.Annotations exact = catalog.ToFogModAnnotations();
            AnnotationSemanticComparison same = AnnotationSemanticComparer.Compare(exact, catalog);
            if (same.Categories.Count != 9 || same.Categories.Any(category => !category.Equivalent || category.Classification != AnnotationDifferenceClass.Exact))
                throw new InvalidOperationException("The annotation semantic comparer did not recognize an exact consumed-model match.");

            exact.DefaultCost["home"] = 0.75f;
            AnnotationSemanticComparison different = AnnotationSemanticComparer.Compare(exact, catalog);
            AnnotationCategoryComparison cost = different.Categories.Single(category => category.Category == "DefaultCost");
            if (cost.Equivalent || cost.Classification != AnnotationDifferenceClass.Unresolved || !different.HasUnresolvedDifferences)
                throw new InvalidOperationException("The annotation semantic comparer must report unreviewed category differences as unresolved.");

            AnnotationSemanticComparison reviewed = AnnotationSemanticComparer.Compare(exact, catalog,
                new ReadOnlyDictionary<string, AnnotationDifferenceClass>(new Dictionary<string, AnnotationDifferenceClass>
                {
                    ["DefaultCost"] = AnnotationDifferenceClass.IntentionalBehavioralEquivalent,
                }));
            cost = reviewed.Categories.Single(category => category.Category == "DefaultCost");
            if (cost.Classification != AnnotationDifferenceClass.IntentionalBehavioralEquivalent || reviewed.HasUnresolvedDifferences)
                throw new InvalidOperationException("A reviewed semantic difference must retain its explicit category classification.");

            using JsonDocument report = JsonDocument.Parse(reviewed.ToJson());
            if (!report.RootElement.GetProperty("categories").EnumerateArray().Any())
                throw new InvalidOperationException("The annotation semantic report did not include per-category results.");
        }

        private static FogAnnotationCatalog Fixture() => new FogAnnotationCatalog(
            new[] { new FogArea(new FogAreaId("home"), new FogModAreaName("home"), new[] { new FogMapId("asylum") }, FogAreaTags.None, null, Array.Empty<FogAreaLink>()) },
            Array.Empty<FogKeyItem>(), Array.Empty<FogWarp>(), Array.Empty<FogEntrance>(), Array.Empty<FogCustomStart>(),
            new[] { new FogDefaultCost(new FogAreaId("home"), 1.0f) }, Array.Empty<FogEnemyScalingRule>(),
            Array.Empty<FogLotLocation>(), Array.Empty<FogDefaultFlagCollision>());
    }
}
