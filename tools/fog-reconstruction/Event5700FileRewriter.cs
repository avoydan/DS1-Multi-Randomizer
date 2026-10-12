using System;
using System.IO;
using System.Linq;
using SoulsFormats;

namespace FogReconstruction
{
    /// <summary>Development-only file adapter for the production-owned Event 5700 builder.</summary>
    internal static class Event5700FileRewriter
    {
        public static void ReplaceInCommonFile(string sourcePath, string outputPath)
        {
            string output = Path.GetFullPath(outputPath);
            byte[] sourceBytes = File.ReadAllBytes(sourcePath);
            DCX.Type compression = DCX.Type.None;
            byte[] uncompressed = DCX.Is(sourceBytes) ? DCX.Decompress(sourceBytes, out compression) : sourceBytes;
            EMEVD common = EMEVD.Read(uncompressed);
            FogHost.Event5700Builder.ReplaceExisting(common);

            byte[] serialized = common.Write();
            byte[] outputBytes = compression == DCX.Type.None ? serialized : DCX.Compress(serialized, compression);
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            File.WriteAllBytes(output, outputBytes);

            byte[] verifyBytes = File.ReadAllBytes(output);
            if (DCX.Is(verifyBytes)) verifyBytes = DCX.Decompress(verifyBytes);
            EMEVD verifyFile = EMEVD.Read(verifyBytes);
            EMEVD.Event roundTrip = verifyFile.Events.SingleOrDefault(evt => evt.ID == 5700)
                ?? throw new InvalidDataException("The written common EMEVD lost event 5700.");
            if (!FogHost.EmevdMerge.SameEvent(FogHost.Event5700Builder.Build(), roundTrip))
                throw new InvalidDataException("Event 5700 changed during EMEVD/DSC round-trip serialization.");

            var sourceCalls = WarpEventCallMatrix.Read(sourcePath);
            var outputCalls = WarpEventCallMatrix.Read(output);
            if (!sourceCalls.SequenceEqual(outputCalls))
                throw new InvalidDataException("Replacing event 5700 altered the existing initializer call contract.");

            Console.WriteLine($"Wrote project-authored event 5700 and round-trip verified {outputCalls.Count} unchanged initializers: {output}");
        }
    }
}
