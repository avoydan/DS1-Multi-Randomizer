using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using SoulsFormats;
using SoulsIds;

namespace FogReconstruction
{
    /// <summary>Reusable semantic equality and mismatch reporting for parsed EMEVD and ESD content.</summary>
    internal static class SemanticComparer
    {
        public static SemanticComparison Compare(EmevdFileModel left, EmevdFileModel right)
        {
            return CompareCanonical(left, right);
        }

        public static SemanticComparison Compare(EsdFileModel left, EsdFileModel right)
        {
            return CompareCanonical(CanonicalSemantics.EsdSemanticModel(left), CanonicalSemantics.EsdSemanticModel(right));
        }

        public static SemanticComparison Compare(EMEVD left, EMEVD right, EMEDF emedf = null)
        {
            return Compare(CanonicalSemantics.Emevd(left, emedf), CanonicalSemantics.Emevd(right, emedf));
        }

        public static SemanticComparison Compare(ESD left, ESD right)
        {
            return Compare(CanonicalSemantics.Esd(left, null), CanonicalSemantics.Esd(right, null));
        }

        private static SemanticComparison CompareCanonical(object left, object right)
        {
            string leftJson = CanonicalSemantics.Serialize(left);
            string rightJson = CanonicalSemantics.Serialize(right);
            string leftFingerprint = CanonicalSemantics.Sha256(System.Text.Encoding.UTF8.GetBytes(leftJson));
            string rightFingerprint = CanonicalSemantics.Sha256(System.Text.Encoding.UTF8.GetBytes(rightJson));
            if (leftJson == rightJson)
                return new SemanticComparison(true, leftFingerprint, rightFingerprint, null);

            using (JsonDocument a = JsonDocument.Parse(leftJson))
            using (JsonDocument b = JsonDocument.Parse(rightJson))
                return new SemanticComparison(false, leftFingerprint, rightFingerprint,
                    FirstDifference(a.RootElement, b.RootElement, "$"));
        }

        private static string FirstDifference(JsonElement left, JsonElement right, string path)
        {
            if (left.ValueKind != right.ValueKind) return path + " (value kind)";
            if (left.ValueKind == JsonValueKind.Object)
            {
                Dictionary<string, JsonElement> leftProperties = left.EnumerateObject().ToDictionary(p => p.Name, p => p.Value, StringComparer.Ordinal);
                Dictionary<string, JsonElement> rightProperties = right.EnumerateObject().ToDictionary(p => p.Name, p => p.Value, StringComparer.Ordinal);
                foreach (string key in leftProperties.Keys.Union(rightProperties.Keys).OrderBy(k => k, StringComparer.Ordinal))
                {
                    if (!leftProperties.ContainsKey(key) || !rightProperties.ContainsKey(key)) return path + "." + key + " (field presence)";
                    string nested = FirstDifference(leftProperties[key], rightProperties[key], path + "." + key);
                    if (nested != null) return nested;
                }
                return null;
            }
            if (left.ValueKind == JsonValueKind.Array)
            {
                JsonElement[] leftItems = left.EnumerateArray().ToArray();
                JsonElement[] rightItems = right.EnumerateArray().ToArray();
                int common = Math.Min(leftItems.Length, rightItems.Length);
                for (int i = 0; i < common; i++)
                {
                    string nested = FirstDifference(leftItems[i], rightItems[i], path + "[" + i + "]");
                    if (nested != null) return nested;
                }
                return leftItems.Length == rightItems.Length ? null : path + " (length)";
            }
            return left.GetRawText() == right.GetRawText() ? null : path;
        }
    }

    internal sealed class SemanticComparison
    {
        public SemanticComparison(bool equal, string leftFingerprint, string rightFingerprint, string firstDifference)
        {
            Equal = equal;
            LeftFingerprint = leftFingerprint;
            RightFingerprint = rightFingerprint;
            FirstDifference = firstDifference;
        }

        public bool Equal { get; }
        public string LeftFingerprint { get; }
        public string RightFingerprint { get; }
        public string FirstDifference { get; }
    }
}
