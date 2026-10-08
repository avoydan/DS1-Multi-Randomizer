using System.Text.Json;
using SoulsFormats;

// SmokeVerify <package mod dir> <paramdef.paramdefbnd.dcx> <expectations.json> [<original game dir>]
// expectations.json: [{ "id", "map", "slot", "model", "kind", "souls"?, "disableRespawn"? }]
// Prints one JSON object per expectation: { id, ok, checks: [{ name, ok, detail }] }.
// SmokeVerify audit <package mod dir> <randomizer.json> <original game dir> <sampling|classic>
// For every generated placement that needs a custom event (a wake-up, a passive or deferred activation, a disabled entity, a static bridge),
// the event owner of its map is resolved, the owner's event file must exist in the package, and a NEW event (an ID that the clean game file
// does not have) must name the placement's entity. Event IDs must be unique in the owner file. In "classic" mode the classic permutation keeps
// the behavior it was frozen with (the Darkroot Basin has no events): those placements are counted, not failed.
if (args.Length > 0 && args[0] == "audit")
{
    var packageDir = args[1];
    var planDoc = JsonDocument.Parse(File.ReadAllText(args[2]));
    var cleanDir = args[3];
    var sampling = args.Length > 4 && args[4] == "sampling";
    string OwnerOf(string map) => sampling && map == "m12_00_00_01" ? "m12_00_00_00" : map;
    var cache = new Dictionary<string, (EMEVD? now, HashSet<int> originalIds)>();
    (EMEVD? now, HashSet<int> originalIds) Load(string owner)
    {
        if (cache.TryGetValue(owner, out var hit)) return hit;
        var nowPath = Path.Combine(packageDir, "event", owner + ".emevd.dcx");
        var originalPath = Path.Combine(cleanDir, "event", owner + ".emevd.dcx");
        var original = File.Exists(originalPath) ? EMEVD.Read(originalPath).Events.Select(entry => (int)entry.ID).ToHashSet() : new HashSet<int>();
        return cache[owner] = (File.Exists(nowPath) ? EMEVD.Read(nowPath) : null, original);
    }
    var missing = new List<string>();
    var frozen = 0;
    var checkedCount = 0;
    var duplicateOwners = new List<string>();
    foreach (var group in new[] { "enemies", "bosses" })
    {
        if (!planDoc.RootElement.GetProperty("placements").TryGetProperty(group, out var list)) continue;
        foreach (var placement in list.EnumerateArray())
        {
            bool Flag(string name) => placement.TryGetProperty(name, out var value) && (value.ValueKind == JsonValueKind.True || (value.ValueKind == JsonValueKind.Number));
            var needsEvent = Flag("forceCombatActivation") || Flag("passiveUntilAttacked") || Flag("disableEntity") || Flag("activationRegionId") || Flag("combatRegionId") || Flag("staticBridgeDragon");
            if (!needsEvent) continue;
            var entity = placement.GetProperty("entityId").GetInt32();
            if (entity < 0) continue;
            var map = placement.GetProperty("map").GetString()!;
            var slotName = placement.GetProperty("slot").GetString()!;
            var owner = OwnerOf(map);
            if (!sampling && map == "m12_00_00_01") { frozen++; continue; }
            checkedCount++;
            var (now, originalIds) = Load(owner);
            if (now == null) { missing.Add($"{slotName}: owner {owner} has no event file in the package"); continue; }
            bool Names(EMEVD.Event entry) => entry.Instructions.Any(instruction =>
            {
                var data = instruction.ArgData;
                for (var offset = 0; offset + 4 <= data.Length; offset += 4) if (BitConverter.ToInt32(data, offset) == entity) return true;
                return false;
            });
            if (!now.Events.Any(entry => !originalIds.Contains((int)entry.ID) && Names(entry)))
                missing.Add($"{slotName}: no new event of {owner} names entity {entity}");
        }
    }
    foreach (var (owner, (now, _)) in cache)
    {
        if (now == null) continue;
        var duplicates = now.Events.GroupBy(entry => entry.ID).Where(entry => entry.Count() > 1).Select(entry => entry.Key).ToList();
        if (duplicates.Count > 0) duplicateOwners.Add($"{owner}: {string.Join(",", duplicates)}");
    }
    Console.WriteLine(JsonSerializer.Serialize(new { checkedCount, frozenWithoutEvents = frozen, missing, duplicateOwners, owners = cache.Keys.OrderBy(entry => entry).ToArray() }));
    return missing.Count == 0 && duplicateOwners.Count == 0 ? 0 : 1;
}

// SmokeVerify laundromat <package mod dir> <paramdef.paramdefbnd.dcx> <randomizer.json> <original game dir>
// The Laundromat Mixup (docs/LAUNDROMAT_MIXUP.md) in the package against the plan and the clean game: every planned CharaInitParam row wears the planned armor, every
// planned piece is a piece some NPC already wears in that slot in the clean game, no hollowed row (piece ids from 2,000,000) changed, and no other cell of any NPC row
// (id from 6000) differs from the clean game.
if (args.Length > 0 && args[0] == "laundromat")
{
    var defs = BND3.Read(args[2]).Files.Select(file => PARAMDEF.Read(file.Bytes)).ToList();
    PARAM Read(BND3 bnd)
    {
        var file = bnd.Files.Single(entry => entry.Name.EndsWith("CharaInitParam.param", StringComparison.OrdinalIgnoreCase));
        var param = PARAM.Read(file.Bytes);
        param.ApplyParamdef(defs.First(definition => definition.ParamType == param.ParamType));
        return param;
    }
    var nowRows = Read(BND3.Read(Path.Combine(args[1], "param", "GameParam", "GameParam.parambnd.dcx"))).Rows.ToDictionary(row => row.ID);
    var thenRows = Read(BND3.Read(Path.Combine(args[4], "param", "GameParam", "GameParam.parambnd.dcx"))).Rows.ToDictionary(row => row.ID);
    var plan = JsonDocument.Parse(File.ReadAllText(args[3])).RootElement.GetProperty("placements");
    var slots = new[] { ("head", "equip_Helm"), ("chest", "equip_Armer"), ("hands", "equip_Gaunt"), ("legs", "equip_Leg") };
    var problems = new List<string>();
    var planned = new Dictionary<int, JsonElement>();
    if (plan.TryGetProperty("laundromat", out var list)) foreach (var row in list.EnumerateArray()) planned[row.GetProperty("id").GetInt32()] = row;
    var worn = slots.ToDictionary(slot => slot.Item2, slot => thenRows.Values.Where(row => row.ID >= 6000).Select(row => Convert.ToInt32(row[slot.Item2].Value)).Where(id => id > 0).ToHashSet());
    var armorCells = slots.Select(slot => slot.Item2).ToHashSet();
    foreach (var (id, row) in planned)
    {
        if (id < 6000 || !nowRows.TryGetValue(id, out var written)) { problems.Add($"row {id} is not a present NPC row"); continue; }
        foreach (var (name, cell) in slots)
        {
            var wanted = row.GetProperty(name).GetInt32();
            var actual = Convert.ToInt32(written[cell].Value);
            if (actual != wanted) problems.Add($"row {id} {cell}: package {actual}, plan {wanted}");
            var original = Convert.ToInt32(thenRows[id][cell].Value);
            if (original <= 0 && wanted != original) problems.Add($"row {id} {cell}: an empty slot was filled");
            if (wanted > 0 && !worn[cell].Contains(wanted)) problems.Add($"row {id} {cell}: {wanted} is worn by no NPC in the clean game");
            if (wanted >= 2_000_000 || original >= 2_000_000) problems.Add($"row {id} {cell}: a hollowed piece");
        }
    }
    var changedOutside = new List<int>();
    foreach (var (id, row) in nowRows.Where(entry => entry.Key >= 6000))
    {
        if (!thenRows.TryGetValue(id, out var original)) { changedOutside.Add(id); continue; }
        foreach (var cell in row.Cells)
        {
            var name = cell.Def.InternalName;
            if (planned.ContainsKey(id) && armorCells.Contains(name)) continue;
            static string Text(object? value) => value is byte[] bytes ? Convert.ToHexString(bytes) : Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "";
            if (Text(cell.Value) != Text(original[name].Value)) { changedOutside.Add(id); break; }
        }
    }
    if (changedOutside.Count > 0) problems.Add($"{changedOutside.Count} NPC rows changed outside the plan: {string.Join(",", changedOutside.Take(5))}");
    Console.WriteLine(JsonSerializer.Serialize(new { plannedRows = planned.Count, problems }));
    return problems.Count == 0 ? 0 : 1;
}

// SmokeVerify lots <package mod dir> <paramdef.paramdefbnd.dcx> <randomizer.json> <original game dir>
// Item lot rows of the package against the plan: every item placement that targets a lot row (world lots, gifts, enemy drops, boss rewards) must
// be in the row; no other ItemLotParam row may differ from the clean game (except the guaranteed-drop normalization, which only touches rates);
// no NpcParam row of the package (the scaled copies included) may name a boss reward chain; and the boss reward event of the common event file
// (event 970 and every initialisation of it) must be exactly the clean game's: a reward belongs to its trigger flag, never to a boss body.
if (args.Length > 0 && args[0] == "lots")
{
    var defs = BND3.Read(args[2]).Files.Select(file => PARAMDEF.Read(file.Bytes)).ToList();
    PARAM Read(BND3 bnd, string suffix)
    {
        var file = bnd.Files.Single(entry => entry.Name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
        var param = PARAM.Read(file.Bytes);
        param.ApplyParamdef(defs.First(definition => definition.ParamType == param.ParamType));
        return param;
    }
    var nowBnd = BND3.Read(Path.Combine(args[1], "param", "GameParam", "GameParam.parambnd.dcx"));
    var thenBnd = BND3.Read(Path.Combine(args[4], "param", "GameParam", "GameParam.parambnd.dcx"));
    var nowLots = Read(nowBnd, "ItemLotParam.param").Rows.ToDictionary(row => row.ID);
    var thenLots = Read(thenBnd, "ItemLotParam.param").Rows.ToDictionary(row => row.ID);
    var plan = JsonDocument.Parse(File.ReadAllText(args[3])).RootElement.GetProperty("placements");
    int LotCell(PARAM.Row row, string name) => Convert.ToInt32(row[name].Value);
    var problems = new List<string>();
    var targeted = new HashSet<int>();
    var checkedTargets = 0;
    var bossRows = new HashSet<int>();
    foreach (var group in new[] { "items", "gifts", "enemyDrops", "bossRewards" })
    {
        if (!plan.TryGetProperty(group, out var list)) continue;
        foreach (var placement in list.EnumerateArray())
        {
            if (!placement.TryGetProperty("targetKind", out var kind) || kind.GetString() != "lot") continue;
            var rowId = placement.GetProperty("rowId").GetInt32();
            var slot = placement.GetProperty("targetSlot").GetInt32();
            targeted.Add(rowId);
            if (group == "bossRewards") bossRows.Add(rowId);
            checkedTargets++;
            if (!nowLots.TryGetValue(rowId, out var row)) { problems.Add($"{group}: row {rowId} is missing from the package"); continue; }
            var suffix = slot.ToString("00");
            if (LotCell(row, "lotItemId" + suffix) != placement.GetProperty("itemId").GetInt32() ||
                LotCell(row, "lotItemCategory" + suffix) != placement.GetProperty("itemCategory").GetInt32() ||
                LotCell(row, "lotItemNum" + suffix) != placement.GetProperty("itemQuantity").GetInt32())
                problems.Add($"{group}: row {rowId} slot {slot} does not hold the planned item");
        }
    }
    var changedOutside = new List<int>();
    foreach (var (id, row) in nowLots)
    {
        if (targeted.Contains(id)) continue;
        if (!thenLots.TryGetValue(id, out var original)) { changedOutside.Add(id); continue; }
        foreach (var cell in row.Cells)
        {
            var name = cell.Def.InternalName;
            if (!name.StartsWith("lotItem", StringComparison.Ordinal)) continue;
            if (name.StartsWith("lotItemBasePoint", StringComparison.Ordinal)) continue;   // the guaranteed-drop normalization only changes rates
            if (!Equals(cell.Value, original[name].Value)) { changedOutside.Add(id); break; }
        }
    }
    if (changedOutside.Count > 0) problems.Add($"ItemLotParam rows changed without a placement: {string.Join(",", changedOutside.Take(20))}");
    // A boss reward chain is every row of an awarded lot; the scan recorded them in the plan only for the placed rows, so the reference check
    // uses the rows of the placements and their consecutive neighbours.
    var chainRows = new HashSet<int>();
    foreach (var id in bossRows) { for (var n = id; thenLots.ContainsKey(n); n++) chainRows.Add(n); }
    var npcNow = Read(nowBnd, "NpcParam.param");
    foreach (var row in npcNow.Rows)
        foreach (var cell in row.Cells.Where(cell => cell.Def.InternalName.StartsWith("itemLotId_", StringComparison.OrdinalIgnoreCase)))
            if (chainRows.Contains(Convert.ToInt32(cell.Value))) problems.Add($"NpcParam {row.ID} names the boss reward row {cell.Value}");
    var rewardEventChecks = 0;
    var packageCommon = Path.Combine(args[1], "event", "common.emevd.dcx");
    if (File.Exists(packageCommon))
    {
        List<string> Inits(string path) => EMEVD.Read(path).Events.SelectMany(entry => entry.Instructions)
            .Where(instruction => instruction.Bank == 2000 && (instruction.ID == 0 || instruction.ID == 6) && instruction.ArgData.Length >= 8 && BitConverter.ToUInt32(instruction.ArgData, 4) == 970)
            .Select(instruction => Convert.ToHexString(instruction.ArgData)).ToList();
        string Body(string path) => string.Join("|", EMEVD.Read(path).Events.Where(entry => entry.ID == 970)
            .SelectMany(entry => entry.Instructions.Select(instruction => instruction.Bank + ":" + instruction.ID + ":" + Convert.ToHexString(instruction.ArgData))));
        var clean = Path.Combine(args[4], "event", "common.emevd.dcx");
        rewardEventChecks = 1;
        if (!Inits(packageCommon).SequenceEqual(Inits(clean)) || Body(packageCommon) != Body(clean))
            problems.Add("the boss reward event (970) or one of its initialisations differs from the clean game");
    }
    Console.WriteLine(JsonSerializer.Serialize(new { checkedTargets, bossRewardTargets = bossRows.Count, rewardEventChecked = rewardEventChecks, problems }));
    return problems.Count == 0 ? 0 : 1;
}

// SmokeVerify dlc-untouched <package game tree> <paramdef.paramdefbnd.dcx> <catalog.json> <original game dir> [<rows.json>]
// rows.json ({"shopRows":[...],"giftLotRows":[...]}) adds the ShopLineupParam rows of the DLC merchants and the item lot rows of the DLC NPCs' gifts.
// A world made with the DLC left out (docs/DLC_AUDIT.md) must leave the DLC exactly as the clean game has it: the map and event files of the DLC map are absent from
// the package or byte-identical, every NpcParam row that only DLC enemies use, every item lot row of the DLC (its world lots and the drop lots only DLC enemies award)
// and every DLC boss reward row are cell for cell the clean game's.
if (args.Length > 0 && args[0] == "dlc-untouched")
{
    const string dlcMap = "m12_01_00_00";
    var defs = BND3.Read(args[2]).Files.Select(file => PARAMDEF.Read(file.Bytes)).ToList();
    PARAM Read(BND3 bnd, string suffix)
    {
        var file = bnd.Files.Single(entry => entry.Name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
        var param = PARAM.Read(file.Bytes);
        param.ApplyParamdef(defs.First(definition => definition.ParamType == param.ParamType));
        return param;
    }
    var catalog = JsonDocument.Parse(File.ReadAllText(args[3])).RootElement;
    var problems = new List<string>();
    var filesChecked = 0;
    foreach (var relative in new[] { "map/MapStudio/" + dlcMap + ".msb.dcx", "map/MapStudio/" + dlcMap + ".msb", "event/" + dlcMap + ".emevd.dcx" })
    {
        var packaged = Path.Combine(args[1], relative.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(packaged)) continue;
        filesChecked++;
        var clean = Path.Combine(args[4], relative.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(clean) || !File.ReadAllBytes(packaged).AsSpan().SequenceEqual(File.ReadAllBytes(clean))) problems.Add($"{relative} differs from the clean game");
    }
    var nowBnd = BND3.Read(Path.Combine(args[1], "param", "GameParam", "GameParam.parambnd.dcx"));
    var thenBnd = BND3.Read(Path.Combine(args[4], "param", "GameParam", "GameParam.parambnd.dcx"));
    string Text(object? value) => value is byte[] bytes ? Convert.ToHexString(bytes) : Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "";
    int CompareRows(string suffix, IEnumerable<int> ids, string label)
    {
        var now = Read(nowBnd, suffix).Rows.ToDictionary(row => row.ID);
        var then = Read(thenBnd, suffix).Rows.ToDictionary(row => row.ID);
        var count = 0;
        foreach (var id in ids.Distinct())
        {
            if (!then.TryGetValue(id, out var original)) continue;
            count++;
            if (!now.TryGetValue(id, out var row)) { problems.Add($"{label} row {id} is missing from the package"); continue; }
            foreach (var cell in row.Cells)
            {
                if (Text(cell.Value) != Text(original[cell.Def.InternalName].Value))
                { problems.Add($"{label} row {id} cell {cell.Def.InternalName} was changed"); break; }
            }
        }
        return count;
    }
    // NpcParam rows used only by enemies of the DLC map.
    var slotsByNpc = new Dictionary<int, bool>();
    foreach (var slot in catalog.GetProperty("enemySlots").EnumerateArray())
    {
        var npcId = slot.GetProperty("npcParamId").GetInt32();
        if (npcId < 0) continue;
        var inDlc = slot.GetProperty("mapId").GetString() == dlcMap;
        slotsByNpc[npcId] = slotsByNpc.TryGetValue(npcId, out var all) ? all && inDlc : inDlc;
    }
    var dlcNpcRows = slotsByNpc.Where(entry => entry.Value).Select(entry => entry.Key).ToList();
    var npcChecked = CompareRows("NpcParam.param", dlcNpcRows, "NpcParam");
    var lotIds = new List<int>();
    foreach (var lot in catalog.GetProperty("worldItemLots").EnumerateArray())
        if (lot.GetProperty("mapId").GetString() == dlcMap) lotIds.Add(lot.GetProperty("rowId").GetInt32());
    foreach (var lot in catalog.GetProperty("enemyDropLots").EnumerateArray())
        if (lot.TryGetProperty("dlcOnly", out var tagged) && tagged.ValueKind == JsonValueKind.True) lotIds.Add(lot.GetProperty("rowId").GetInt32());
    foreach (var reward in catalog.GetProperty("bossRewards").EnumerateArray())
        if (reward.GetProperty("dlc").GetBoolean())
            foreach (var lot in reward.GetProperty("lots").EnumerateArray())
                foreach (var row in lot.GetProperty("chain").EnumerateArray()) lotIds.Add(row.GetProperty("rowId").GetInt32());
    // The shops of the DLC merchants and the gift lots of the DLC NPCs: the row lists come from the caller (the same function the exclusion uses), as {"shopRows":[...],"giftLotRows":[...]}.
    var shopsChecked = 0;
    if (args.Length > 5)
    {
        var rows = JsonDocument.Parse(File.ReadAllText(args[5])).RootElement;
        foreach (var row in rows.GetProperty("giftLotRows").EnumerateArray()) lotIds.Add(row.GetInt32());
        shopsChecked = CompareRows("ShopLineupParam.param", rows.GetProperty("shopRows").EnumerateArray().Select(row => row.GetInt32()).ToList(), "ShopLineupParam");
    }
    var lotsChecked = CompareRows("ItemLotParam.param", lotIds, "ItemLotParam");
    Console.WriteLine(JsonSerializer.Serialize(new { filesChecked, npcRowsChecked = npcChecked, lotRowsChecked = lotsChecked, shopRowsChecked = shopsChecked, problems }));
    return problems.Count == 0 ? 0 : 1;
}

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
// The Darkroot Basin shares the event file and the AI bundle of the Darkroot Garden (the data tool normalizes it the same way).
// An enemy part and a dummy enemy part carry the same fields in two classes; the checks read them through one view.
PartView? FindPart(MSB1 file, string name, bool dummy)
{
    if (dummy)
    {
        var d = file.Parts.DummyEnemies.SingleOrDefault(entry => entry.Name == name);
        return d == null ? null : new PartView(d.Name, d.ModelName, d.EntityID, d.NPCParamID, d.ThinkParamID, d.TalkID);
    }
    var e = file.Parts.Enemies.SingleOrDefault(entry => entry.Name == name);
    return e == null ? null : new PartView(e.Name, e.ModelName, e.EntityID, e.NPCParamID, e.ThinkParamID, e.TalkID);
}
string AiMap(string map) => map == "m12_00_00_01" ? "m12_00_00_00" : map;
var results = new List<object>();
foreach (var expectation in expectations)
{
    var checks = new List<object>();
    void Check(string name, bool ok, string detail) => checks.Add(new { name, ok, detail });
    if (expectation.Kind == "item-lot")
    {
        // Part H: an item lot row of the package holds the item the world promises (a transposed boss weapon where a pickup lies).
        var lotRows = ReadParam(gameParam, "ItemLotParam.param").Rows.ToDictionary(entry => entry.ID);
        var found = lotRows.TryGetValue(expectation.LotRow ?? -1, out var lotRow);
        Check("the item lot row exists", found, $"row {expectation.LotRow}");
        if (found)
        {
            var holds = Enumerable.Range(1, 8).Any(slot => Cell(lotRow!, "lotItemId" + slot.ToString("00")) == expectation.ItemId && Cell(lotRow!, "lotItemCategory" + slot.ToString("00")) == expectation.ItemCategory);
            Check("the row holds the item", holds, $"item {expectation.ItemId} category {expectation.ItemCategory}");
        }
        results.Add(new { id = expectation.Id, ok = checks.All(check => (bool)check.GetType().GetProperty("ok")!.GetValue(check)!), checks });
        continue;
    }
    var msbPath = Path.Combine(modDir, "map", "MapStudio", expectation.Map + ".msb");
    var msb = MSB1.Read(msbPath);
    var part = FindPart(msb, expectation.Slot, expectation.Dummy);
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
        var emevd = EMEVD.Read(Path.Combine(modDir, "event", AiMap(expectation.Map) + ".emevd.dcx"));
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
            var original = EMEVD.Read(Path.Combine(originalDir, "event", AiMap(expectation.Map) + ".emevd.dcx"));
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
                var originalEmevd = EMEVD.Read(Path.Combine(originalDir, "event", AiMap(expectation.Map) + ".emevd.dcx"));
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
        if (expectation.Kind == "hostile-npc" && originalDir != null)
        {
            // Part F1: a hostile NPC body in a regular slot. The body, its animations and its AI are the NPC's; what makes it an actor of the
            // slot's kind (allegiance class, respawn, rewards, drops, covenant mark) is the slot's. No talk script, no shop, no quest binding.
            var originalMsb = MSB1.Read(Path.Combine(originalDir, "map", "MapStudio", expectation.Map + ".msb"));
            var originalPart = FindPart(originalMsb, expectation.Slot, expectation.Dummy);
            Check("the slot exists in the clean game", originalPart != null, expectation.Slot);
            if (originalPart != null)
            {
                Check("the slot's own enemy was replaced", originalPart.ModelName != part.ModelName, $"{originalPart.ModelName} -> {part.ModelName}");
                Check("the part has no talk script", part.TalkID == originalPart.TalkID && part.TalkID <= 0, $"talk {part.TalkID} (clean game {originalPart.TalkID})");
                Check("the entity id is kept or assigned", originalPart.EntityID < 0 ? (part.EntityID >= 0 || expectation.Dummy) : part.EntityID == originalPart.EntityID, $"{part.EntityID} (clean game {originalPart.EntityID})");
                var originalParam = BND3.Read(Path.Combine(originalDir, "param", "GameParam", "GameParam.parambnd.dcx"));
                var originalNpc = ReadParam(originalParam, "NpcParam.param").Rows.ToDictionary(entry => entry.ID);
                if (originalNpc.TryGetValue(originalPart.NPCParamID, out var slotRow) && npc.TryGetValue(part.NPCParamID, out var bodyRow))
                {
                    Check("the body has its own scaled row", part.NPCParamID != originalPart.NPCParamID, $"row {part.NPCParamID} (slot row {originalPart.NPCParamID})");
                    foreach (var cellName in new[] { "teamType", "npcType", "disableRespawn", "dropType", "getSoul", "humanityLotId", "vowType" })
                        Check($"{cellName} is the slot's", Cell(bodyRow, cellName) == Cell(slotRow, cellName) || (expectation.Passive && cellName == "teamType" && Cell(bodyRow, cellName) == 2), $"{Cell(bodyRow, cellName)} (slot row: {Cell(slotRow, cellName)})");
                    foreach (var cell in slotRow.Cells.Where(cell => cell.Def.InternalName.StartsWith("itemLotId", StringComparison.Ordinal)))
                    {
                        var name = cell.Def.InternalName;
                        var value = bodyRow.Cells.Single(entry => entry.Def.InternalName == name).Value;
                        Check($"{name} is the slot's", Convert.ToInt32(value) == Convert.ToInt32(cell.Value), $"{value} (slot row: {cell.Value})");
                    }
                    Check("no ghost transition", Cell(bodyRow, "isGhost") == 0 && Cell(bodyRow, "isChangeWanderGhost") == 0 && Cell(bodyRow, "ghostModelId") == -1, $"isGhost {Cell(bodyRow, "isGhost")}, isChangeWanderGhost {Cell(bodyRow, "isChangeWanderGhost")}, ghostModelId {Cell(bodyRow, "ghostModelId")}");
                }
                else Check("NpcParam rows readable", false, $"rows {originalPart.NPCParamID} / {part.NPCParamID}");
                // The think row exists, and the map's AI bundle holds the scripts of the goals it names (battle and logic) for the variant that was placed.
                if (think.TryGetValue(part.ThinkParamID, out var thinkRow))
                {
                    var battle = Cell(thinkRow, "battleGoalID");
                    var logic = Cell(thinkRow, "logicId");
                    var scriptPath = Path.Combine(modDir, "script", AiMap(expectation.Map) + ".luabnd.dcx");
                    if (!File.Exists(scriptPath)) scriptPath = Path.Combine(originalDir, "script", AiMap(expectation.Map) + ".luabnd.dcx");
                    var scripts = BND3.Read(scriptPath).Files.Select(file => Path.GetFileName(file.Name)).ToHashSet(StringComparer.OrdinalIgnoreCase);
                    var common = BND3.Read(Path.Combine(originalDir, "script", "aiCommon.luabnd.dcx")).Files.Select(file => Path.GetFileName(file.Name)).ToHashSet(StringComparer.OrdinalIgnoreCase);
                    bool Has(string file) => scripts.Contains(file) || common.Contains(file);
                    Check("the battle script of the placed AI is in the map's AI bundle", Has($"{battle}_battle.lua"), $"{battle}_battle.lua");
                    Check("the logic script of the placed AI is in the map's AI bundle", logic == 10000 || Has($"{logic}_logic.lua"), logic == 10000 ? "common logic 10000" : $"{logic}_logic.lua");
                }
                if (!expectation.Dummy && !expectation.Passive)
                {
                    var originalEmevd = EMEVD.Read(Path.Combine(originalDir, "event", AiMap(expectation.Map) + ".emevd.dcx"));
                    int Named(EMEVD file) => file.Events.Count(entry => entry.Instructions.Any(instruction =>
                    {
                        var data = instruction.ArgData;
                        for (var offset = 0; offset + 4 <= data.Length; offset += 4) if (BitConverter.ToInt32(data, offset) == part.EntityID) return true;
                        return false;
                    }));
                    Check("a wake-up event names the entity", Named(emevd) > Named(originalEmevd), $"events naming entity {part.EntityID}: {Named(emevd)} now, {Named(originalEmevd)} in the clean game");
                }
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

record PartView(string Name, string ModelName, int EntityID, int NPCParamID, int ThinkParamID, int TalkID);
record Expectation(string Id, string Map, string Slot, string Model, string Kind, int? Souls, bool? DisableRespawn, bool? WakeUpEvent, bool Dummy = false, bool Passive = false, int? LotRow = null, int? ItemId = null, int? ItemCategory = null);
