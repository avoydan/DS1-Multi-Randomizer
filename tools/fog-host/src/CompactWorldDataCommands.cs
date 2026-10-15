using System;
using System.Globalization;
using System.IO;
using System.Text;
using FogReconstruction;

namespace FogHost
{
    /// <summary>Creates the pinned FogMod runtime inputs from a verified local DSR snapshot without network access.</summary>
    public static class CompactWorldDataCommands
    {
        public static bool Handles(string[] args) => args.Length > 0 && args[0] == "reconstruct-compact-data";

        public static int Run(string[] args)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
            if (args.Length != 5 || args[1] != "--pristine-root" || args[3] != "--output-root")
            {
                Console.Error.WriteLine("Usage: FogHost reconstruct-compact-data --pristine-root <dir> --output-root <dir>");
                return 2;
            }

            try
            {
                string pristineRoot = Path.GetFullPath(args[2]);
                string outputRoot = Path.GetFullPath(args[4]);
                if (!Directory.Exists(outputRoot)) throw new DirectoryNotFoundException("Output root must already exist.");
                string ds1r = Path.Combine(outputRoot, "dist", "DS1R");
                Directory.CreateDirectory(ds1r);

                string eventWrapper = Path.Combine(outputRoot, ".fog-event-output");
                string talkWrapper = Path.Combine(outputRoot, ".fog-talk-output");
                string layoutWrapper = Path.Combine(outputRoot, ".fog-layout-output");
                OfflineEventReconstructor.BuildFromPristine(pristineRoot, eventWrapper);
                Directory.Move(Path.Combine(eventWrapper, "event"), Path.Combine(ds1r, "event"));
                Directory.Delete(eventWrapper);
                OfflineTalkReconstructor.BuildFromPristine(pristineRoot, talkWrapper);
                Directory.CreateDirectory(Path.Combine(ds1r, "script"));
                Directory.Move(Path.Combine(talkWrapper, "script", "talk"), Path.Combine(ds1r, "script", "talk"));
                Directory.Delete(talkWrapper, recursive: true);
                OfflineLayoutReconstructor.BuildFromPristine(pristineRoot, layoutWrapper);
                Directory.Move(Path.Combine(layoutWrapper, "Layouts"), Path.Combine(ds1r, "Layouts"));
                Directory.Delete(layoutWrapper);
                Console.WriteLine("Compact Fog runtime inputs reconstructed locally; no FogMod dist files were read or downloaded.");
                return 0;
            }
            catch (Exception error)
            {
                Console.Error.WriteLine($"COMPACT_WORLD_DATA_RECONSTRUCTION_FAILED: {error.Message}");
                return 5;
            }
        }
    }
}
