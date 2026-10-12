using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SoulsFormats;

namespace FogReconstruction
{
    /// <summary>
    /// Project-owned event membership changes for the pinned DS1R source.
    /// This plan contains event identifiers only; it does not store or copy event bodies.
    /// </summary>
    internal static class EventRemovalPlan
    {
        private static readonly IReadOnlyDictionary<string, long[]> Removals =
            new Dictionary<string, long[]>(StringComparer.Ordinal)
            {
                ["m10_00_00_00.emevd.dcx"] = new long[] { 11005029, 11005036, 11005333, 11005843, 11005844, 11005990 },
                ["m10_01_00_00.emevd.dcx"] = new long[] { 11015203, 11015843, 11015846, 11015900, 11015901, 11015990 },
                ["m11_00_00_00.emevd.dcx"] = new long[] { 11105843, 11105846 },
                ["m12_00_00_00.emevd.dcx"] = new long[] { 11205035, 11205843, 11205846, 11205990 },
                ["m12_01_00_00.emevd.dcx"] = new long[]
                {
                    11210688, 11210702, 11210717, 11210727, 11210777, 11210817, 11210826, 11210827,
                    11210828, 11210829, 11210832, 11210833, 11210860, 11210861, 11210871, 11210887,
                    11210888, 11210889, 11210891, 11210892, 11210893, 11210894, 11210895, 11210932,
                    11212222, 11212786, 11212787, 11213888, 11214435, 11215843, 11215846, 11219010,
                    11219100, 11219111, 11219112, 11219113, 11219114, 11219121, 11219122, 11219876,
                    11219877, 11219900, 11219901, 11219950, 11219999,
                },
                ["m13_00_00_00.emevd.dcx"] = new long[] { 11305029, 11305843, 11305846, 11305990 },
                ["m13_01_00_00.emevd.dcx"] = new long[] { 11314398, 11315843, 11315846 },
                ["m14_00_00_00.emevd.dcx"] = new long[] { 11405029, 11405843, 11405846, 11415170, 14005990 },
                ["m14_01_00_00.emevd.dcx"] = new long[] { 11415029, 11415843, 11415846, 14015990 },
                ["m15_00_00_00.emevd.dcx"] = new long[] { 11505029, 11505843, 11505846, 11505990 },
                ["m15_01_00_00.emevd.dcx"] = new long[] { 11515029, 11515843, 11515846, 11515990, 11516388 },
                ["m16_00_00_00.emevd.dcx"] = new long[] { 11605029, 11605843, 11605846, 11605990 },
                ["m17_00_00_00.emevd.dcx"] = new long[] { 11705843, 11705846 },
                ["m18_00_00_00.emevd.dcx"] = new long[] { 11805029, 11805990 },
            };

        internal static int TotalEventCount => Removals.Values.Sum(ids => ids.Length);

        internal static IReadOnlyDictionary<string, long[]> Entries => Removals;

        internal static int Apply(string archiveName, EMEVD archive)
        {
            if (archive == null) throw new ArgumentNullException(nameof(archive));
            if (!Removals.TryGetValue(archiveName, out long[] eventIds)) return 0;

            var indexes = new List<int>(eventIds.Length);
            foreach (long eventId in eventIds)
            {
                int[] matches = Enumerable.Range(0, archive.Events.Count)
                    .Where(index => archive.Events[index].ID == eventId)
                    .ToArray();
                if (matches.Length != 1)
                    throw new InvalidDataException($"Expected exactly one source event {eventId} in {archiveName}; found {matches.Length}.");
                indexes.Add(matches[0]);
            }

            foreach (int index in indexes.OrderByDescending(value => value)) archive.Events.RemoveAt(index);
            if (eventIds.Any(id => archive.Events.Any(evt => evt.ID == id)))
                throw new InvalidDataException($"An event removal remained after reconstruction in {archiveName}.");
            return indexes.Count;
        }
    }

    internal static class EventRemovalPlanSelfTests
    {
        internal static void Run()
        {
            Require(EventRemovalPlan.TotalEventCount == 96, "the pinned removal plan must contain 96 event identifiers");
            foreach (KeyValuePair<string, long[]> entry in EventRemovalPlan.Entries)
            {
                EMEVD archive = new EMEVD();
                foreach (long id in entry.Value) archive.Events.Add(new EMEVD.Event(id, EMEVD.Event.RestBehaviorType.Default));
                archive.Events.Add(new EMEVD.Event(987654321, EMEVD.Event.RestBehaviorType.Default));

                int removed = EventRemovalPlan.Apply(entry.Key, archive);
                Require(removed == entry.Value.Length, entry.Key + " must remove its complete declared event set");
                Require(archive.Events.Count == 1 && archive.Events[0].ID == 987654321,
                    entry.Key + " must preserve events outside the declared removal set");
            }

            string firstArchive = EventRemovalPlan.Entries.Keys.OrderBy(name => name, StringComparer.Ordinal).First();
            long firstId = EventRemovalPlan.Entries[firstArchive][0];
            EMEVD missing = new EMEVD();
            foreach (long id in EventRemovalPlan.Entries[firstArchive].Skip(1))
                missing.Events.Add(new EMEVD.Event(id, EMEVD.Event.RestBehaviorType.Default));
            ExpectInvalidData(() => EventRemovalPlan.Apply(firstArchive, missing), "missing source event must refuse reconstruction");

            EMEVD duplicate = new EMEVD();
            foreach (long id in EventRemovalPlan.Entries[firstArchive])
                duplicate.Events.Add(new EMEVD.Event(id, EMEVD.Event.RestBehaviorType.Default));
            duplicate.Events.Add(new EMEVD.Event(firstId, EMEVD.Event.RestBehaviorType.Default));
            ExpectInvalidData(() => EventRemovalPlan.Apply(firstArchive, duplicate), "duplicate source event must refuse reconstruction");
        }

        private static void ExpectInvalidData(Action action, string message)
        {
            try { action(); }
            catch (InvalidDataException) { return; }
            throw new InvalidOperationException(message);
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Event removal plan self-test failed: " + message + ".");
        }
    }
}
