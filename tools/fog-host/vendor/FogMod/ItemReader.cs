using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using static FogMod.AnnotationData;
using SoulsIds;
using SoulsFormats;
using static SoulsIds.GameSpec;

namespace FogMod
{
    public class ItemReader
    {
        public Result FindItems(RandomizerOptions opt, Annotations ann, Graph g, string gameDir, FromGame game)
        {
            GameEditor editor = new GameEditor(game);
            editor.Spec.GameDir = gameDir;
            Dictionary<string, PARAM.Layout> layouts = editor.LoadLayouts();
            Dictionary<string, PARAM> Params = editor.LoadParams(layouts);

            Dictionary<int, PARAM.Row> lots = Params["ItemLotParam"].Rows.ToDictionary(r => (int)r.ID, r => r);
            Dictionary<string, Item> itemsById = ann.KeyItems.ToDictionary(item => item.ID, item => item);
            Dictionary<string, string> itemAreas = g.itemAreas;

            void ProcessLot(int annotationLot, int lotId, string area, PARAM.Row row)
            {
                for (int i = 1; i <= 8; i++)
                {
                    int item = (int)row[$"lotItemId0{i}"].Value;
                    if (item == 0) continue;
                    int category = (int)row[$"lotItemCategory0{i}"].Value;
                    category = Universe.LotTypes.TryGetValue((uint)category, out int value) ? value : -1;
                    if (category == -1) continue;
                    string id = $"{category}:{item}";
                    if (opt["debuglots"]) Console.WriteLine($"{annotationLot} (row {lotId}) in {area} has {id}");
                    if (itemsById.TryGetValue(id, out Item keyItem))
                    {
                        string name = keyItem.Name;
                        if (itemAreas[name] != null && itemAreas[name] != area)
                        {
                            // Compact's exact source rows can contain multiple vanilla copies of an
                            // optional key item. Keep the first (lowest-ID) source as its graph
                            // representative; these items are not shuffled, and every physical lot
                            // remains untouched. Required key items still reject ambiguous owners.
                            if (ann.CompactLotLocations != null && keyItem.HasTag("randomonly")) continue;
                            throw new Exception($"Item {name} found in both {itemAreas[name]} and {area}");
                        }
                        itemAreas[name] = area;
                    }
                }
            }

            if (ann.CompactLotLocations != null)
            {
                foreach (KeyValuePair<int, string> entry in ann.CompactLotLocations.OrderBy(entry => entry.Key))
                {
                    if (!g.areas.ContainsKey(entry.Value)) throw new Exception($"Internal error in exact Compact lot config for {entry.Key}: {entry.Value} does not exist");
                    // Compact ownership is backed by exact pristine references. Never expand it into adjacent IDs.
                    if (lots.TryGetValue(entry.Key, out PARAM.Row row)) ProcessLot(entry.Key, entry.Key, entry.Value, row);
                }
            }
            else
            {
                foreach (KeyValuePair<int, string> entry in ann.LotLocations)
                {
                    int lot = entry.Key;
                    if (!g.areas.ContainsKey(entry.Value)) throw new Exception($"Internal error in lot config for {entry.Key}: {entry.Value} does not exist");
                    while (true)
                    {
                        // It's also fine to not have a lot defined as long as all key items are found
                        if (!lots.TryGetValue(lot, out PARAM.Row row)) break;
                        ProcessLot(entry.Key, lot, entry.Value, row);
                        lot++;
                    }
                }
            }
            if (opt["explain"])
            {
                foreach (Item item in ann.KeyItems)
                {
                    Console.WriteLine($"{item.Name} {item.ID}: default {item.Area}, found {itemAreas[item.Name] ?? "?????"}");
                }
            }
            // Collect items in graph
            SortedSet<string> itemRecord = new SortedSet<string>();
            bool randomized = false;
            foreach (Item item in ann.KeyItems)
            {
                if (itemAreas[item.Name] == null)
                {
                    if (item.HasTag("randomonly"))
                    {
                        itemAreas[item.Name] = item.Area;
                    }
                    else if (item.HasTag("hard") && !opt["hard"])
                    {
                        continue;
                    }
                    else throw new Exception($"Couldn't find {item.Name} in item lots");
                }
                string area = itemAreas[item.Name];
                g.graph[area].Items.Add(item.Name);
                if (!item.HasTag("randomonly"))
                {
                    if (area != item.Area) randomized = true;
                    itemRecord.Add($"{item.Name}={area}");
                }
            }
            return new Result
            {
                Randomized = randomized,
                ItemHash = (RandomizerOptions.JavaStringHash($"{string.Join(",", itemRecord)}") % 99999).ToString().PadLeft(5, '0')
            };
        }

        public class Result
        {
            public bool Randomized { get; set; }
            public string ItemHash { get; set; }
        }
    }
}
