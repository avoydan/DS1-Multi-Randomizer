using System;
using System.Collections.Generic;
using System.Linq;

namespace FogReconstruction
{
    /// <summary>Explicitly selects only the clean ESD entries that need project-owned bytecode normalization.</summary>
    internal static class TalkNormalizationPlan
    {
        private static readonly IReadOnlyDictionary<string, HashSet<string>> Entries =
            new Dictionary<string, HashSet<string>>(StringComparer.Ordinal)
            {
                ["m10_00_00_00.talkesdbnd.dcx"] = Set("t100000.esd", "t100001.esd", "t100002.esd", "t100010.esd"),
                ["m10_01_00_00.talkesdbnd.dcx"] = Set("t100100.esd", "t100101.esd", "t100102.esd", "t100103.esd", "t100104.esd", "t100105.esd", "t100106.esd", "t100110.esd"),
                ["m10_02_00_00.talkesdbnd.dcx"] = Set("t100200.esd", "t100201.esd", "t100202.esd", "t100210.esd"),
                ["m11_00_00_00.talkesdbnd.dcx"] = Set("t110000.esd", "t110001.esd", "t110002.esd", "t110010.esd"),
                ["m12_00_00_00.talkesdbnd.dcx"] = Set("t120000.esd", "t120001.esd", "t120002.esd", "t120010.esd"),
                ["m12_01_00_00.talkesdbnd.dcx"] = Set("t121000.esd", "t121001.esd", "t121002.esd", "t121003.esd", "t121004.esd"),
                ["m13_00_00_00.talkesdbnd.dcx"] = Set("t130000.esd", "t130001.esd", "t130002.esd", "t130010.esd"),
                ["m13_01_00_00.talkesdbnd.dcx"] = Set("t131000.esd", "t131001.esd", "t131002.esd", "t131010.esd"),
                ["m13_02_00_00.talkesdbnd.dcx"] = Set("t132000.esd", "t132001.esd", "t132002.esd", "t132010.esd"),
                ["m14_00_00_00.talkesdbnd.dcx"] = Set("t140000.esd", "t140001.esd", "t140002.esd", "t140010.esd"),
                ["m14_01_00_00.talkesdbnd.dcx"] = Set("t141000.esd", "t141001.esd", "t141002.esd", "t141003.esd", "t141004.esd", "t141010.esd"),
                ["m15_00_00_00.talkesdbnd.dcx"] = Set("t150000.esd", "t150001.esd", "t150002.esd", "t150010.esd"),
                ["m15_01_00_00.talkesdbnd.dcx"] = Set("t151000.esd", "t151001.esd", "t151002.esd", "t151010.esd"),
                ["m16_00_00_00.talkesdbnd.dcx"] = Set("t160000.esd", "t160001.esd", "t160002.esd", "t160010.esd"),
                ["m17_00_00_00.talkesdbnd.dcx"] = Set("t170000.esd", "t170001.esd", "t170002.esd", "t170010.esd"),
                ["m18_00_00_00.talkesdbnd.dcx"] = Set("t180000.esd", "t180001.esd", "t180002.esd", "t180010.esd"),
                ["m18_01_00_00.talkesdbnd.dcx"] = Set("t181000.esd", "t181001.esd"),
            };

        internal static int EntryCount => Entries.Values.Sum(set => set.Count);

        internal static bool ShouldNormalize(string archiveName, string entryName)
        {
            if (entryName == null) return false;
            string baseName = entryName.Replace('\\', '/').Split('/').Last();
            return Entries.TryGetValue(archiveName, out HashSet<string> entries) && entries.Contains(baseName);
        }

        private static HashSet<string> Set(params string[] names) => new HashSet<string>(names, StringComparer.Ordinal);
    }

    internal static class TalkNormalizationPlanSelfTests
    {
        internal static void Run()
        {
            Require(TalkNormalizationPlan.EntryCount == 73, "the normalization plan must select 73 changed ESD entries");
            Require(TalkNormalizationPlan.ShouldNormalize("m10_00_00_00.talkesdbnd.dcx", "N:\\FRPG\\data\\INTERROOT_x64\\script\\talk\\t100000.esd"),
                "a pinned script path should match its archive and basename");
            Require(!TalkNormalizationPlan.ShouldNormalize("m10_01_00_00.talkesdbnd.dcx", "t100000.esd"),
                "the normalization plan must remain scoped to its archive");
            Require(!TalkNormalizationPlan.ShouldNormalize("m10_00_00_00.talkesdbnd.dcx", "t100020.esd"),
                "an unlisted script must remain byte-identical");
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Talk normalization plan self-test failed: " + message + ".");
        }
    }
}
