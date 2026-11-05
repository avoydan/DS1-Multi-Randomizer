using System;
using System.Collections.Generic;
using System.Linq;
using SoulsFormats;

namespace FogReconstruction
{
    /// <summary>
    /// Generation profile ds1tr-gen-v7 only: lays the locally rebuilt event archives out exactly like FogMod's own <c>dist</c> archives, so that FogMod's writer receives the same in-memory
    /// model and writes the same bytes as it does for the original data (docs/V7_UPSTREAM_PARITY.md, "Fog runtime parity").
    ///
    /// Only packaging is involved, never behavior: the instructions of every event are already identical to upstream's (all 1281 events, measured against the original files). What differs is
    ///  - where the author's tool placed the events it added (and the one pristine event it moved): the rebuilt archives append them, upstream puts them next to a neighbour; and
    ///  - the order of an event's parameter records: the author's tool writes them by (source byte, instruction), the project builders by instruction.
    /// The table below is event identifiers only (an event and the event that precedes it); it contains no event body and no game data.
    /// </summary>
    internal static class UpstreamEventLayout
    {
        // (archive, event, the event that precedes it upstream; -1 = first in the archive; 0 is the real event 0). Applied in order, so an anchor may name an earlier anchor.
        private static readonly (string Archive, long Id, long After)[] Anchors =
        {
            ("common.emevd.dcx", 8900, 0),
            ("common.emevd.dcx", 8901, 8900),
            ("common.emevd.dcx", 8950, 8901),
            ("common.emevd.dcx", 8960, 8950),
            ("common.emevd.dcx", 5690, 8960),
            ("common.emevd.dcx", 5700, 5690),
            ("m10_02_00_00.emevd.dcx", 11020303, 11020301),
            ("m12_01_00_00.emevd.dcx", 11210708, 11210700),
            ("m12_01_00_00.emevd.dcx", 11210710, 11210708),
            ("m12_01_00_00.emevd.dcx", 11210730, 11210710),
            ("m12_01_00_00.emevd.dcx", 11210875, 11215398),
            ("m12_01_00_00.emevd.dcx", 11210780, 11210876),
            ("m12_01_00_00.emevd.dcx", 11210410, 11210780),
            ("m12_01_00_00.emevd.dcx", 11210760, 11210836),
            ("m12_01_00_00.emevd.dcx", 11210880, 11210877),
            ("m12_01_00_00.emevd.dcx", 11210403, 11210402),
            ("m14_01_00_00.emevd.dcx", 11410220, 11410201),
            ("m14_01_00_00.emevd.dcx", 11410221, 11410220),
            ("m15_01_00_00.emevd.dcx", 11510231, 11510230),
            ("m17_00_00_00.emevd.dcx", 11708500, 0),
            ("m17_00_00_00.emevd.dcx", 11700005, 11700000),
        };

        internal static int AnchorCount => Anchors.Length;

        internal static void Apply(string archiveName, EMEVD archive, EMEVD pristine)
        {
            if (archive == null) throw new ArgumentNullException(nameof(archive));
            if (pristine == null) throw new ArgumentNullException(nameof(pristine));
            // Events the project replaced or added get the author's parameter order; every untouched event keeps the order it has in the game.
            Dictionary<long, string> pristineBodies = pristine.Events.ToDictionary(evt => evt.ID, Body);
            foreach (EMEVD.Event evt in archive.Events)
            {
                if (pristineBodies.TryGetValue(evt.ID, out string body) && body == Body(evt)) continue;
                evt.Parameters = evt.Parameters
                    .OrderBy(parameter => parameter.SourceStartByte).ThenBy(parameter => parameter.InstructionIndex).ThenBy(parameter => parameter.TargetStartByte).ToList();
            }

            foreach ((string archiveFile, long id, long after) in Anchors)
            {
                if (!string.Equals(archiveFile, archiveName, StringComparison.Ordinal)) continue;
                int from = archive.Events.FindIndex(evt => evt.ID == id);
                if (from < 0) throw new System.IO.InvalidDataException($"The upstream layout expects event {id} in {archiveName}.");
                EMEVD.Event moving = archive.Events[from];
                archive.Events.RemoveAt(from);
                int at = 0;
                if (after != -1)
                {
                    int predecessor = archive.Events.FindIndex(evt => evt.ID == after);
                    if (predecessor < 0) throw new System.IO.InvalidDataException($"The upstream layout expects event {after} before {id} in {archiveName}.");
                    at = predecessor + 1;
                }
                archive.Events.Insert(at, moving);
            }
        }

        private static string Body(EMEVD.Event evt) =>
            evt.RestBehavior + "|" + string.Join(";", evt.Instructions.Select(instruction => instruction.Bank + ":" + instruction.ID + ":" + Convert.ToHexString(instruction.ArgData)));
    }
}
