using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SoulsFormats;
using SoulsIds;
using static SoulsIds.GameSpec;

namespace FogHost
{
    /// <summary>Game params as FogMod sees them (layout-applied) plus raw bytes of every BND entry.</summary>
    public sealed class ParamSnapshot
    {
        public Dictionary<string, PARAM> Params { get; private set; }
        public Dictionary<string, byte[]> Raw { get; private set; }

        /// <summary>Reads a GameParam BND. The working directory must be the FogMod data root.</summary>
        public static ParamSnapshot Read(string gameParamPath, Dictionary<string, PARAM.Layout> layouts)
        {
            var editor = new GameEditor(FromGame.DS1R);
            return new ParamSnapshot
            {
                Params = editor.LoadParams(gameParamPath, layouts),
                Raw = editor.LoadBnd(gameParamPath, (data, name) => data),
            };
        }
    }

    /// <summary>
    /// Post-write compatibility invariants (docs/FOGHOST_INTEGRATION.md, amended by the v1 scaling
    /// rule in docs/DECISIONS.md). Any violation is FOG_COMPATIBILITY_VIOLATED.
    /// </summary>
    public static class Invariants
    {
        /// <summary>Params the combined engine patches and FogMod must leave alone, compared cell by cell.</summary>
        private static readonly string[] LayoutParams = { "ItemLotParam" };

        /// <summary>Params FogMod never loads (no layout); their BND entries must be byte-identical.</summary>
        private static readonly string[] RawParams = { "ShopLineupParam", "CharaInitParam", "NpcThinkParam" };

        public static void CheckMsbs(
            IReadOnlyDictionary<string, MSB1> input,
            IReadOnlyDictionary<string, MSB1> output,
            PARAM inputNpcs,
            PARAM outputNpcs,
            ISet<int> preserved,
            List<string> problems)
        {
            var inputIds = new HashSet<int>(inputNpcs.Rows.Select(r => (int)r.ID));
            var outputRows = outputNpcs.Rows.ToDictionary(r => (int)r.ID);
            foreach ((string path, MSB1 before) in input.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                if (!output.TryGetValue(path, out MSB1 after)) continue; // FogMod does not touch this map.
                var declared = new HashSet<string>(after.Models.Enemies.Select(m => m.Name), StringComparer.Ordinal);
                var parts = after.Parts.Enemies.GroupBy(e => e.Name, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
                foreach (MSB1.Part.Enemy enemy in before.Parts.Enemies)
                {
                    string where = $"{path} enemy {enemy.Name}";
                    if (!parts.TryGetValue(enemy.Name, out MSB1.Part.Enemy written)) { problems.Add($"{where} was removed"); continue; }
                    if (written.ModelName != enemy.ModelName) problems.Add($"{where} model {enemy.ModelName} -> {written.ModelName}");
                    if (written.ThinkParamID != enemy.ThinkParamID) problems.Add($"{where} ThinkParamID {enemy.ThinkParamID} -> {written.ThinkParamID}");
                    if (!declared.Contains(written.ModelName)) problems.Add($"{where} model {written.ModelName} is not declared");
                    if (written.NPCParamID == enemy.NPCParamID) continue;
                    // v1 rule: parts on preserved rows keep them. Other parts may move only to a FogMod
                    // scale clone: a new row whose pcAttr hint encodes the part's input row.
                    bool clone = !preserved.Contains(enemy.NPCParamID)
                        && !inputIds.Contains(written.NPCParamID)
                        && outputRows.TryGetValue(written.NPCParamID, out PARAM.Row row)
                        && CloneHint(row) == enemy.NPCParamID;
                    if (!clone) problems.Add($"{where} NPCParamID {enemy.NPCParamID} -> {written.NPCParamID}");
                }
            }
        }

        public static void CheckParams(
            IReadOnlyDictionary<string, PARAM> input,
            IReadOnlyDictionary<string, PARAM> output,
            IEnumerable<int> preserved,
            List<string> problems)
        {
            foreach (string name in LayoutParams)
            {
                if (!input.TryGetValue(name, out PARAM before) || !output.TryGetValue(name, out PARAM after))
                {
                    problems.Add($"{name} could not be compared");
                    continue;
                }
                if (before.Rows.Count != after.Rows.Count || !before.Rows.Zip(after.Rows).All(p => SameRow(p.First, p.Second)))
                    problems.Add($"{name} differs from the staging input");
            }
            var beforeNpcs = input["NpcParam"].Rows.ToDictionary(r => (int)r.ID);
            var afterNpcs = output["NpcParam"].Rows.ToDictionary(r => (int)r.ID);
            foreach (int id in preserved)
            {
                if (!afterNpcs.TryGetValue(id, out PARAM.Row after)) problems.Add($"preserved NpcParam {id} was deleted");
                else if (!SameRow(beforeNpcs[id], after)) problems.Add($"preserved NpcParam {id} was changed");
            }
        }

        public static void CheckRaw(IReadOnlyDictionary<string, byte[]> input, IReadOnlyDictionary<string, byte[]> output, List<string> problems)
        {
            foreach (string name in RawParams)
            {
                if (!input.TryGetValue(name, out byte[] before) || !output.TryGetValue(name, out byte[] after) || !before.AsSpan().SequenceEqual(after))
                    problems.Add($"{name} differs from the staging input");
            }
        }

        public static void CheckEvents(string stagingDirectory, EmevdMerge merge, List<string> problems)
        {
            foreach ((string fileName, List<EMEVD.Event> expected) in merge.ExpectedEvents.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                EMEVD written = EMEVD.Read(Path.Combine(stagingDirectory, "event", fileName));
                var byId = written.Events.GroupBy(e => e.ID).ToDictionary(g => g.Key, g => g.ToList());
                foreach (EMEVD.Event evt in expected)
                {
                    if (!byId.TryGetValue(evt.ID, out var found) || found.Count != 1 || !EmevdMerge.SameEvent(found[0], evt))
                        problems.Add($"event/{fileName} merged event {evt.ID} is missing or changed");
                }
                EMEVD.Event event0 = written.Events.FirstOrDefault(e => e.ID == 0);
                foreach (EMEVD.Instruction init in merge.ExpectedInits[fileName])
                {
                    if (event0 == null || !event0.Instructions.Any(i => EmevdMerge.SameInstruction(i, init)))
                        problems.Add($"event/{fileName} event-0 initialization is missing");
                }
            }
        }

        public static FogHostException Violation(List<string> problems) =>
            new FogHostException(HostErrors.CompatibilityViolated,
                "Fog output violates combined-engine compatibility: " + string.Join("; ", problems.Take(20))
                + (problems.Count > 20 ? $"; and {problems.Count - 20} more" : "") + ".")
            { Stage = "write-game-data" };

        /// <summary>The original NpcParam ID FogMod stores in pcAttr B/W/L/R when it clones a row for scaling.</summary>
        public static int CloneHint(PARAM.Row row) =>
            BitConverter.ToInt32("BWLR".Select(c => (byte)row[$"pcAttr{c}"].Value).ToArray(), 0);

        public static bool SameRow(PARAM.Row a, PARAM.Row b) =>
            a.ID == b.ID && a.Cells.Count == b.Cells.Count
            && a.Cells.Zip(b.Cells).All(p => p.First.Def.InternalName == p.Second.Def.InternalName && SameValue(p.First.Value, p.Second.Value));

        private static bool SameValue(object a, object b) =>
            a is byte[] x && b is byte[] y ? x.AsSpan().SequenceEqual(y) : Equals(a, b);
    }
}
