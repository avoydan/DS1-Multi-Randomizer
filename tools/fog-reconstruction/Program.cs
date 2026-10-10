using System;
using System.IO;
using System.Linq;
using System.Reflection;
using SoulsFormats;

namespace FogReconstruction
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            try
            {
                if (args.Length == 1 && args[0] == "inspect-api")
                {
                    InspectApi();
                    return 0;
                }
                if (args.Length == 1 && args[0] == "self-test")
                {
                    SemanticComparerSelfTests.Run();
                    TalkBytecodeNormalizerSelfTests.Run();
                    return 0;
                }
                if (args.Length == 3 && args[0] == "verify-talk-normalization")
                {
                    TalkNormalizationVerifier.Verify(args[1], args[2]);
                    return 0;
                }
                if (args.Length == 2 && args[0] == "inspect-esd-expression")
                {
                    string hex = args[1].Replace(" ", "").Replace("-", "");
                    if (hex.Length % 2 != 0) throw new ArgumentException("Bytecode hex must have an even number of characters.");
                    byte[] bytes = Enumerable.Range(0, hex.Length / 2).Select(i => Convert.ToByte(hex.Substring(i * 2, 2), 16)).ToArray();
                    Console.WriteLine(TalkBytecodeNormalizer.DescribeExpression(bytes));
                    Console.WriteLine(BitConverter.ToString(TalkBytecodeNormalizer.NormalizeExpression(bytes)));
                    return 0;
                }
                if (args.Length == 3 && args[0] == "inspect-event")
                {
                    InspectEvent(args[1], long.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture));
                    return 0;
                }
                if (args.Length == 2 && args[0] == "inspect-paramdefs")
                {
                    ParamDefInspector.Inspect(args[1]);
                    return 0;
                }
                if (args.Length == 2 && args[0] == "verify-reference")
                {
                    FogHost.DataRoot.Verify(args[1]);
                    Console.WriteLine($"Verified all {FogHost.DataRoot.Manifest.Count} pinned Fog reference files and directory inventories.");
                    return 0;
                }
                if (args.Length >= 4 && args[0] == "extract")
                {
                    string emedf = args.Length > 4 ? args[4] : null;
                    RecipeExtractor.Extract(args[1], args[2], args[3], emedf);
                    return 0;
                }
                if (args.Length == 3 && args[0] == "verify-inputs")
                {
                    RecipeExtractor.VerifyInputs(args[1], args[2]);
                    return 0;
                }

                Console.Error.WriteLine("Usage:");
                Console.Error.WriteLine("  FogReconstruction inspect-api");
                Console.Error.WriteLine("  FogReconstruction self-test");
                Console.Error.WriteLine("  FogReconstruction verify-talk-normalization <pristine-game-root> <fog-reference-root>");
                Console.Error.WriteLine("  FogReconstruction inspect-esd-expression <bytecode-hex>");
                Console.Error.WriteLine("  FogReconstruction inspect-event <emevd-path> <event-id>");
                Console.Error.WriteLine("  FogReconstruction inspect-paramdefs <pristine-game-root>");
                Console.Error.WriteLine("  FogReconstruction verify-reference <fog-reference-root>");
                Console.Error.WriteLine("  FogReconstruction verify-inputs <pristine-game-root> <fog-reference-root>");
                Console.Error.WriteLine("  FogReconstruction extract <pristine-game-root> <fog-reference-root> <report-json> [ds1-common.emedf.json]");
                return 2;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                return 1;
            }
        }

        private static void InspectApi()
        {
            foreach (Type type in new[]
            {
                typeof(ESD), typeof(ESD.State), typeof(ESD.Condition), typeof(ESD.CommandCall),
                typeof(EMEVD), typeof(EMEVD.Event), typeof(EMEVD.Instruction), typeof(EMEVD.Parameter),
                typeof(BND4), typeof(BND3), typeof(BinderFile), typeof(BinaryReaderEx), typeof(PARAMDEF), typeof(PARAM.Layout),
            })
            {
                Console.WriteLine($"=== {type.FullName} ===");
                foreach (MemberInfo member in type.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                             .Where(m => m.MemberType is MemberTypes.Field or MemberTypes.Property or MemberTypes.Constructor)
                             .OrderBy(m => m.Name, StringComparer.Ordinal))
                    Console.WriteLine($"{member.MemberType}: {member}");
            }
        }

        private static void InspectEvent(string path, long eventId)
        {
            byte[] bytes = File.ReadAllBytes(path);
            if (DCX.Is(bytes)) bytes = DCX.Decompress(bytes);
            EMEVD file = EMEVD.Read(bytes);
            EMEVD.Event evt = file.Events.SingleOrDefault(e => e.ID == eventId);
            if (evt == null) throw new InvalidDataException($"Event {eventId} not found in {path}.");
            Console.WriteLine($"event={evt.ID} name={evt.Name} rest={evt.RestBehavior} instructions={evt.Instructions.Count} parameters={evt.Parameters.Count}");
            for (int i = 0; i < evt.Instructions.Count; i++)
            {
                EMEVD.Instruction instruction = evt.Instructions[i];
                Console.WriteLine($"{i:D3}: {instruction.Bank}[{instruction.ID}] layer={instruction.Layer?.ToString() ?? "null"} args={Convert.ToHexString(instruction.ArgData)}");
            }
            foreach (EMEVD.Parameter parameter in evt.Parameters.OrderBy(p => p.InstructionIndex).ThenBy(p => p.TargetStartByte))
                Console.WriteLine($"param: instruction={parameter.InstructionIndex} target={parameter.TargetStartByte} source={parameter.SourceStartByte} bytes={parameter.ByteCount} id={parameter.UnkID}");
        }
    }
}
