using System;

namespace FogHost
{
    /// <summary>
    /// Internal QA switch (docs/INTERNAL_QA.md). The solver route (the witness sidecar) and FogMod's own connectivity listing (the area-by-area "From X to Y" predecessor lines it prints to its log)
    /// show a complete route to Gwyn. Public builds must not leave that in any user-accessible file, so the host writes neither unless the backend explicitly enables internal QA for this run.
    /// The backend is the only party that decides: it sets <c>DS1TR_INTERNAL_QA</c> for the host process from the build channel and its own environment, so a variable set by a player
    /// of a public build never reaches the host. Nothing here changes what is generated: the same search runs, the same files are written; only diagnostics differ.
    /// </summary>
    public static class InternalQa
    {
        public const string Variable = "DS1TR_INTERNAL_QA";

        /// <summary>Read at the moment of use, so a test can switch it per run.</summary>
        public static bool Enabled => string.Equals(Environment.GetEnvironmentVariable(Variable), "1", StringComparison.Ordinal);
    }
}
