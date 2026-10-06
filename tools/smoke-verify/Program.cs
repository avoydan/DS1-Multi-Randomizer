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
