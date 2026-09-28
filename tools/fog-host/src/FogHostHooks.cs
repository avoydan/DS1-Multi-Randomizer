using System;
using System.Collections.Generic;
using SoulsFormats;

namespace FogHost
{
    /// <summary>
    /// Connection points between the patched FogMod <c>GameDataWriter</c> and the host.
    /// The writer never touches the file system directly: every write is deferred here, so
    /// a dry run writes nothing and a merge conflict is raised before any file changes.
    /// </summary>
    public sealed class FogHostHooks
    {
        private readonly List<(string Path, Action Write)> deferred = new List<(string, Action)>();

        /// <summary>NpcParam rows owned by the combined engine (never restored, deleted, or scaled).</summary>
        public HashSet<int> PreservedNpcParams { get; } = new HashSet<int>();

        /// <summary>Called for each FogMod event file after FogMod's own edits, before it is written.</summary>
        public Action<string, EMEVD> EventMerger { get; set; }

        /// <summary>Final in-memory MSBs keyed by game-relative path (backslashes).</summary>
        public Dictionary<string, MSB1> Msbs { get; } = new Dictionary<string, MSB1>(StringComparer.Ordinal);

        /// <summary>Final in-memory params that FogMod loaded with layouts.</summary>
        public Dictionary<string, PARAM> Params { get; set; }

        public IReadOnlyList<(string Path, Action Write)> Deferred => deferred;

        public void MergeEvents(string fileName, EMEVD output) => EventMerger?.Invoke(fileName, output);

        public void Defer(string relativePath, Action write) => deferred.Add((relativePath, write));
    }
}
