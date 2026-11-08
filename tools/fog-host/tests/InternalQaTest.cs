using System;
using System.IO;

namespace FogHost.Tests
{
    /// <summary>The solver route is internal QA material (docs/INTERNAL_QA.md): only the backend can enable it, and only exactly "1" does.</summary>
    public static class InternalQaTest
    {
        public static void VerifySwitchSemantics()
        {
            string previous = Environment.GetEnvironmentVariable(InternalQa.Variable);
            try
            {
                foreach (string off in new[] { null, "", "0", "true", "yes", "01", " 1", "1 " })
                {
                    Environment.SetEnvironmentVariable(InternalQa.Variable, off);
                    if (InternalQa.Enabled) throw new InvalidOperationException($"'{off ?? "(unset)"}' must not enable internal QA.");
                }
                Environment.SetEnvironmentVariable(InternalQa.Variable, "1");
                if (!InternalQa.Enabled) throw new InvalidOperationException("'1' must enable internal QA.");
            }
            finally { Environment.SetEnvironmentVariable(InternalQa.Variable, previous); }
        }

        /// <summary>The witness sidecar and FogMod's route listing are written only behind the switch; the search that decides the world is outside it.</summary>
        public static void VerifyRouteOutputIsBehindTheSwitch(string repositoryRoot)
        {
            string runner = File.ReadAllText(Path.Combine(repositoryRoot, "tools", "fog-host", "src", "HostRunner.cs"));
            if (!runner.Contains("if (InternalQa.Enabled) WriteSolverWitness(", StringComparison.Ordinal))
                throw new InvalidOperationException("The solver witness sidecar must be written only when internal QA is enabled.");
            int search = runner.IndexOf("StartReachabilityValidator.Validate(opt, graph, witnessObserver)", StringComparison.Ordinal);
            int write = runner.IndexOf("WriteSolverWitness(request", StringComparison.Ordinal);
            if (search < 0 || write < search) throw new InvalidOperationException("The accepting search must run unconditionally, before the gated witness write.");

            string connector = File.ReadAllText(Path.Combine(repositoryRoot, "tools", "fog-host", "vendor", "FogMod", "GraphConnector.cs"));
            int gate = connector.IndexOf("if (FogHost.InternalQa.Enabled)", StringComparison.Ordinal);
            int listing = connector.IndexOf("  From {e.From} ({e.Text}) to {rec.Area}", StringComparison.Ordinal);
            if (gate < 0 || listing < gate) throw new InvalidOperationException("FogMod's predecessor listing must sit inside the internal QA gate.");
        }
    }
}
