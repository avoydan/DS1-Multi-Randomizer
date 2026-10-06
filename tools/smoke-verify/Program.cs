using System.Text.Json;
using SoulsFormats;

// SmokeVerify <package mod dir> <paramdef.paramdefbnd.dcx> <expectations.json> [<original game dir>]
// expectations.json: [{ "id", "map", "slot", "model", "kind", "souls"?, "disableRespawn"? }]
// Prints one JSON object per expectation: { id, ok, checks: [{ name, ok, detail }] }.
var modDir = args[0];
var paramdefs = BND3.Read(args[1]).Files.Select(file => PARAMDEF.Read(file.Bytes)).ToList();
var expectations = JsonSerializer.Deserialize<List<Expectation>>(File.ReadAllText(args[2]), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
var originalDir = args.Length > 3 ? args[3] : null;

PARAM ReadParam(BND3 bnd, string suffix)
{
    var file = bnd.Files.Single(entry => entry.Name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
    var param = PARAM.Read(file.Bytes);
    param.ApplyParamdef(paramdefs.First(definition => definition.ParamType == param.ParamType));
    return param;
}
int Cell(PARAM.Row row, string startsWith) =>
    Convert.ToInt32(row.Cells.Single(cell => cell.Def.InternalName.StartsWith(startsWith, StringComparison.Ordinal)).Value);

var gameParam = BND3.Read(Path.Combine(modDir, "param", "GameParam", "GameParam.parambnd.dcx"));
var npc = ReadParam(gameParam, "NpcParam.param").Rows.ToDictionary(row => row.ID);
var think = ReadParam(gameParam, "NpcThinkParam.param").Rows.ToDictionary(row => row.ID);
var results = new List<object>();
foreach (var expectation in expectations)
{
    var checks = new List<object>();
    void Check(string name, bool ok, string detail) => checks.Add(new { name, ok, detail });
    var msbPath = Path.Combine(modDir, "map", "MapStudio", expectation.Map + ".msb");
    var msb = MSB1.Read(msbPath);
    var part = msb.Parts.Enemies.SingleOrDefault(entry => entry.Name == expectation.Slot);
    Check("part exists", part != null, expectation.Slot);
    if (part != null)
    {
        Check("model", part.ModelName == expectation.Model, $"{part.ModelName} (expected {expectation.Model})");
        Check("model is loaded by the map", msb.Models.Enemies.Any(model => model.Name == part.ModelName), part.ModelName);
        Check("NpcParam row exists", npc.TryGetValue(part.NPCParamID, out var npcRow), $"row {part.NPCParamID}");
        Check("NpcThinkParam row exists", think.ContainsKey(part.ThinkParamID), $"row {part.ThinkParamID}");
        if (npc.TryGetValue(part.NPCParamID, out var row))
        {
            if (expectation.Souls.HasValue) Check("souls", Cell(row, "getSoul") == expectation.Souls.Value, $"{Cell(row, "getSoul")} (expected {expectation.Souls})");
            if (expectation.DisableRespawn.HasValue) Check("respawn bit", (Cell(row, "disableRespawn") == 1) == expectation.DisableRespawn.Value, $"disableRespawn={Cell(row, "disableRespawn")} (expected {(expectation.DisableRespawn.Value ? 1 : 0)})");
        }
        var emevd = EMEVD.Read(Path.Combine(modDir, "event", expectation.Map + ".emevd.dcx"));
        var duplicates = emevd.Events.GroupBy(entry => entry.ID).Where(group => group.Count() > 1).Select(group => group.Key).ToList();
        Check("event IDs are unique", duplicates.Count == 0, duplicates.Count == 0 ? $"{emevd.Events.Count} events" : $"duplicate IDs: {string.Join(",", duplicates)}");
        int References(EMEVD file) => file.Events.Count(entry => entry.Instructions.Any(instruction =>
        {
            var data = instruction.ArgData;
            for (var offset = 0; offset + 4 <= data.Length; offset += 4) if (BitConverter.ToInt32(data, offset) == part.EntityID) return true;
            return false;
        }));
        if (expectation.WakeUpEvent == true && originalDir != null)
        {
            var original = EMEVD.Read(Path.Combine(originalDir, "event", expectation.Map + ".emevd.dcx"));
            Check("a wake-up event was added for the entity", References(emevd) > References(original), $"events naming entity {part.EntityID}: {References(emevd)} now, {References(original)} in the clean game");
        }
        if (expectation.Kind == "encounter-body" && originalDir != null)
        {
            // Part E2: the encounter stays the boss's. Entity, rewards and completion bits come from the boss row; the body never respawns.
            var originalMsb = MSB1.Read(Path.Combine(originalDir, "map", "MapStudio", expectation.Map + ".msb"));
            var originalPart = originalMsb.Parts.Enemies.SingleOrDefault(entry => entry.Name == expectation.Slot);
            Check("the boss part exists in the clean game", originalPart != null, expectation.Slot);
            if (originalPart != null)
            {
                Check("entity id is the boss's", part.EntityID == originalPart.EntityID, $"{part.EntityID} (clean game: {originalPart.EntityID})");
                Check("the boss model was replaced", originalPart.ModelName != part.ModelName, $"{originalPart.ModelName} -> {part.ModelName}");
                var originalParam = BND3.Read(Path.Combine(originalDir, "param", "GameParam", "GameParam.parambnd.dcx"));
                var originalNpc = ReadParam(originalParam, "NpcParam.param").Rows.ToDictionary(entry => entry.ID);
                if (originalNpc.TryGetValue(originalPart.NPCParamID, out var bossRow) && npc.TryGetValue(part.NPCParamID, out var bodyRow))
                {
                    Check("body row is not the boss row", part.NPCParamID != originalPart.NPCParamID, $"row {part.NPCParamID} (boss row {originalPart.NPCParamID})");
                    foreach (var cellName in new[] { "dropType", "getSoul", "disableInitializeDead" })
                        Check($"{cellName} is the encounter's", Cell(bodyRow, cellName) == Cell(bossRow, cellName), $"{Cell(bodyRow, cellName)} (boss row: {Cell(bossRow, cellName)})");
                    foreach (var cell in bossRow.Cells.Where(cell => cell.Def.InternalName.StartsWith("itemLotId", StringComparison.Ordinal)))
                    {
                        var name = cell.Def.InternalName;
                        Check($"{name} is the encounter's", Convert.ToInt32(bodyRow.Cells.Single(entry => entry.Def.InternalName == name).Value) == Convert.ToInt32(cell.Value), $"{bodyRow.Cells.Single(entry => entry.Def.InternalName == name).Value} (boss row: {cell.Value})");
                    }
                    Check("the body never respawns", Cell(bodyRow, "disableRespawn") == 1, $"disableRespawn={Cell(bodyRow, "disableRespawn")}");
                }
                else Check("NpcParam rows readable", false, $"rows {originalPart.NPCParamID} / {part.NPCParamID}");
                // Every event of the clean game that names the entity still exists (the death, the defeated flag, the fog, the exit all hang on them).
                var originalEmevd = EMEVD.Read(Path.Combine(originalDir, "event", expectation.Map + ".emevd.dcx"));
                bool Names(EMEVD.Event entry) => entry.Instructions.Any(instruction =>
                {
                    var data = instruction.ArgData;
                    for (var offset = 0; offset + 4 <= data.Length; offset += 4) if (BitConverter.ToInt32(data, offset) == part.EntityID) return true;
                    return false;
                });
                var kept = emevd.Events.Select(entry => entry.ID).ToHashSet();
                var lost = originalEmevd.Events.Where(Names).Select(entry => entry.ID).Where(id => !kept.Contains(id)).ToList();
                Check("events naming the entity are kept", lost.Count == 0, lost.Count == 0 ? $"{originalEmevd.Events.Count(Names)} events" : $"missing: {string.Join(",", lost)}");
                // Conditions (bank 3), flag writes and event initializers (banks 2003, 2000) are what completion rests on and must not disappear.
                // The one allowed removal is the model-specific 2003[18] aimed at the encounter's own entity (a forced animation of the boss model:
                // the Asylum Demon drop, the Taurus Demon entrance); it is the same sanitization boss sampling applies to the same slot.
                var weakened = new List<string>();
                int FirstArg(EMEVD.Instruction instruction) => instruction.ArgData.Length >= 4 ? BitConverter.ToInt32(instruction.ArgData, 0) : int.MinValue;
                string Signature(EMEVD.Instruction instruction) => $"{instruction.Bank}.{instruction.ID}:{Convert.ToHexString(instruction.ArgData)}";
                bool Monitored(EMEVD.Instruction instruction) =>
                    (instruction.Bank == 3 || instruction.Bank == 2003 || instruction.Bank == 2000) &&
                    !(instruction.Bank == 2003 && instruction.ID == 18 && FirstArg(instruction) == part.EntityID);
                foreach (var originalEvent in originalEmevd.Events.Where(Names))
                {
                    var current = emevd.Events.FirstOrDefault(entry => entry.ID == originalEvent.ID);
                    if (current == null) continue;
                    var after = current.Instructions.Select(Signature).GroupBy(entry => entry).ToDictionary(group => group.Key, group => group.Count());
                    foreach (var group in originalEvent.Instructions.Where(Monitored).Select(Signature).GroupBy(entry => entry))
                        if (!after.TryGetValue(group.Key, out var present) || present < group.Count()) weakened.Add($"{originalEvent.ID}:{group.Key.Split(':')[0]}");
                }
                Check("completion conditions are kept", weakened.Count == 0, weakened.Count == 0 ? "no condition or flag instruction was removed from an event naming the entity" : $"weakened: {string.Join(",", weakened)}");
            }
        }
        int Models(MSB1 file) => file.Parts.Enemies.Where(entry => entry.ModelName.StartsWith('c')).Select(entry => entry.ModelName).Distinct().Count();
        var now = Models(msb);
        var vanilla = originalDir != null ? Models(MSB1.Read(Path.Combine(originalDir, "map", "MapStudio", expectation.Map + ".msb"))) : 0;
        Check("model count of the map", now <= Math.Max(30, vanilla), $"{now} distinct enemy models (clean game: {vanilla}; the limit is the larger of 30 and the clean game)");
    }
    results.Add(new { id = expectation.Id, ok = checks.All(check => (bool)check.GetType().GetProperty("ok")!.GetValue(check)!), checks });
}
Console.WriteLine(JsonSerializer.Serialize(results));
return results.All(result => (bool)result.GetType().GetProperty("ok")!.GetValue(result)!) ? 0 : 1;

record Expectation(string Id, string Map, string Slot, string Model, string Kind, int? Souls, bool? DisableRespawn, bool? WakeUpEvent);
