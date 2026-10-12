using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using FogHost;
using SoulsFormats;
using SoulsIds;

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
                    AnnotationSemanticComparerSelfTests.Run();
                    TalkBytecodeNormalizerSelfTests.Run();
                    EventRemovalPlanSelfTests.Run();
                    TalkNormalizationPlanSelfTests.Run();
                    Event5700BehaviorHarness.VerifyBuiltBehavior();
                    FogDsrSpatialIndexSelfTests.Run();
                    AreaCatalogAuditSelfTests.Run();
                    CombinedTransitionGraphAuditSelfTests.Run();
                    AreaGraphBehaviorAuditSelfTests.Run();
                    TransitionEventAuditSelfTests.Run();
                    AreaGateEvidenceAudit.SelfTest();
                    WorldTransitionInventory.SelfTest();
                    return 0;
                }
                if (args.Length == 3 && args[0] == "verify-talk-normalization")
                {
                    TalkNormalizationVerifier.Verify(args[1], args[2]);
                    return 0;
                }
                if (args.Length == 4 && args[0] == "analyze-treasure-regions")
                {
                    TreasureRegionAnalyzer.Write(args[1], args[2], args[3]);
                    return 0;
                }
                if (args.Length == 2 && args[0] == "compare-start-area-shape")
                {
                    StartAreaComparisonAudit.Write(args[1]);
                    return 0;
                }
                if (args.Length == 4 && args[0] == "audit-area-catalog")
                {
                    AreaCatalogAudit.Write(args[1], args[2], args[3]);
                    return 0;
                }
                if (args.Length == 4 && args[0] == "audit-area-graph-behavior")
                {
                    AreaGraphBehaviorAudit.Write(args[1], args[2], args[3]);
                    return 0;
                }
                if (args.Length == 3 && args[0] == "inspect-reference-area-tag-ids")
                {
                    AreaCatalogAudit.WriteReferenceTagIdentityReport(args[1], args[2]);
                    return 0;
                }
                if (args.Length == 4 && args[0] == "audit-boss-defeat-facts")
                {
                    BossDefeatEvidenceAudit.Write(args[1], args[2], args[3]);
                    return 0;
                }
                if (args.Length == 4 && args[0] == "audit-transition-events")
                {
                    TransitionEventAudit.Write(args[1], args[2], args[3]);
                    return 0;
                }
                if (args.Length == 4 && args[0] == "audit-area-gates")
                {
                    AreaGateEvidenceAudit.Write(args[1], args[2], args[3]);
                    return 0;
                }
                if (args.Length == 4 && args[0] == "audit-world-transition-inventory")
                {
                    WorldTransitionInventory.Write(args[1], args[2], args[3]);
                    return 0;
                }
                if ((args.Length == 3 || args.Length == 4) && args[0] == "inspect-map-transition-candidates")
                {
                    MapTransitionCandidateAudit.Write(args[1], args[2], args.Length == 4 ? args[3] : null);
                    return 0;
                }
                if ((args.Length == 3 || args.Length == 4) && args[0] == "inspect-custom-start-boss-proximity")
                {
                    CustomStartSpatialAudit.Write(args[1], args[2], args.Length == 4 ? args[3] : null);
                    return 0;
                }
                if (args.Length == 5 && args[0] == "inspect-game-item-id")
                {
                    InspectGameItemId(args[1], args[2], int.Parse(args[3], System.Globalization.CultureInfo.InvariantCulture), int.Parse(args[4], System.Globalization.CultureInfo.InvariantCulture));
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
                if (args.Length == 4 && args[0] == "inspect-event")
                {
                    InspectEvent(args[1], long.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture), args[3]);
                    return 0;
                }
                if (args.Length == 3 && args[0] == "inspect-event-arg-references")
                {
                    InspectEventArgReferences(args[1], int.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture));
                    return 0;
                }
                if (args.Length == 2 && args[0] == "inspect-warp-calls")
                {
                    WarpEventCallMatrix.Inspect(args[1]);
                    return 0;
                }
                if (args.Length >= 3 && args[0] == "inspect-warp-call-audit")
                {
                    WarpEventCallMatrix.WriteArgumentAudit(args.Skip(2), args[1]);
                    return 0;
                }
                if (args.Length == 3 && args[0] == "inspect-event5700")
                {
                    Event5700Inspector.Write(args[1], args[2]);
                    return 0;
                }
                if (args.Length == 3 && args[0] == "replace-event5700")
                {
                    Event5700FileRewriter.ReplaceInCommonFile(args[1], args[2]);
                    return 0;
                }
                if (args.Length >= 4 && args[0] == "verify-event5700-behavior")
                {
                    Event5700BehaviorHarness.VerifyAndWriteMatrix(args[1], args.Skip(3), args[2]);
                    return 0;
                }
                if (args.Length >= 4 && args[0] == "inspect-event5700-history")
                {
                    Event5700HistoryAudit.Write(args.Skip(2), args[1]);
                    return 0;
                }
                if (args.Length == 2 && args[0] == "inspect-paramdefs")
                {
                    ParamDefInspector.Inspect(args[1]);
                    return 0;
                }
                if (args.Length == 3 && args[0] == "analyze-layout-defs")
                {
                    ParamDefInspector.AnalyzeLayouts(args[1], args[2]);
                    return 0;
                }
                if (args.Length == 2 && args[0] == "verify-reference")
                {
                    FogHost.DataRoot.Verify(args[1]);
                    Console.WriteLine($"Verified all {FogHost.DataRoot.Manifest.Count} pinned Fog reference files and directory inventories.");
                    return 0;
                }
                if (args.Length == 4 && args[0] == "reconstruct-events")
                {
                    OfflineEventReconstructor.Build(args[1], args[2], args[3]);
                    return 0;
                }
                if (args.Length == 4 && args[0] == "reconstruct-talk")
                {
                    OfflineTalkReconstructor.Build(args[1], args[2], args[3]);
                    return 0;
                }
                if (args.Length == 4 && args[0] == "reconstruct-layouts")
                {
                    OfflineLayoutReconstructor.Build(args[1], args[2], args[3]);
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
                if (args.Length == 4 && args[0] == "inspect-added-event-refs")
                {
                    AddedEventAudit.Write(args[1], args[2], args[3]);
                    return 0;
                }

                Console.Error.WriteLine("Usage:");
                Console.Error.WriteLine("  FogReconstruction inspect-api");
                Console.Error.WriteLine("  FogReconstruction self-test");
                Console.Error.WriteLine("  FogReconstruction verify-talk-normalization <pristine-game-root> <fog-reference-root>");
                Console.Error.WriteLine("  FogReconstruction analyze-treasure-regions <fog-data-root> <pristine-game-root> <output-json-outside-repo>");
                Console.Error.WriteLine("  FogReconstruction compare-start-area-shape <fog-reference-root>");
                Console.Error.WriteLine("  FogReconstruction audit-area-catalog <fog-data-root> <pristine-dsr-root> <report-json-outside-repo>");
                Console.Error.WriteLine("  FogReconstruction inspect-reference-area-tag-ids <fog-data-root> <comparison-json-outside-repo>");
                Console.Error.WriteLine("  FogReconstruction audit-boss-defeat-facts <fog-data-root> <pristine-dsr-root> <report-json-outside-repo>");
                Console.Error.WriteLine("  FogReconstruction audit-transition-events <fog-data-root> <pristine-dsr-root> <report-json-outside-repo>");
                Console.Error.WriteLine("  FogReconstruction inspect-map-transition-candidates <fog-data-root> <pristine-dsr-root> [map-key]");
                Console.Error.WriteLine("  FogReconstruction inspect-custom-start-boss-proximity <fog-data-root> <pristine-dsr-root> [custom-start-id]");
                Console.Error.WriteLine("  FogReconstruction inspect-game-item-id <fog-data-root> <pristine-dsr-root> <category-id> <item-id>");
                Console.Error.WriteLine("  FogReconstruction inspect-esd-expression <bytecode-hex>");
                Console.Error.WriteLine("  FogReconstruction inspect-event <emevd-path> <event-id> [emedf-json]");
                Console.Error.WriteLine("  FogReconstruction inspect-event-arg-references <emevd-path> <int32-value>");
                Console.Error.WriteLine("  FogReconstruction inspect-warp-calls <common.emevd.dcx-path>");
                Console.Error.WriteLine("  FogReconstruction inspect-warp-call-audit <output-path-outside-repo> <common.emevd.dcx> [...]");
                Console.Error.WriteLine("  FogReconstruction inspect-event5700 <common.emevd.dcx-path> <analysis-output-path-outside-repo>");
                Console.Error.WriteLine("  FogReconstruction replace-event5700 <generated-common.emevd.dcx> <output-common.emevd.dcx-outside-repo>");
                Console.Error.WriteLine("  FogReconstruction verify-event5700-behavior <reference-common.emevd.dcx> <matrix-output-outside-repo> <generated-common.emevd.dcx> [more-generated-common.emevd.dcx ...]");
                Console.Error.WriteLine("  FogReconstruction inspect-event5700-history <analysis-output-outside-repo> <label=common.emevd.dcx> <label=common.emevd.dcx> [...]");
                Console.Error.WriteLine("  FogReconstruction inspect-paramdefs <pristine-game-root>");
                Console.Error.WriteLine("  FogReconstruction analyze-layout-defs <pristine-game-root> <fog-reference-root>");
                Console.Error.WriteLine("  FogReconstruction verify-reference <fog-reference-root>");
                Console.Error.WriteLine("  FogReconstruction reconstruct-events <pristine-dsr-root> <fog-reference-root> <output-root-outside-repo>");
                Console.Error.WriteLine("  FogReconstruction reconstruct-talk <pristine-dsr-root> <fog-reference-root> <output-root-outside-repo>");
                Console.Error.WriteLine("  FogReconstruction reconstruct-layouts <pristine-dsr-root> <fog-reference-root> <output-root-outside-repo>");
                Console.Error.WriteLine("  FogReconstruction verify-inputs <pristine-game-root> <fog-reference-root>");
                Console.Error.WriteLine("  FogReconstruction extract <pristine-game-root> <fog-reference-root> <report-json> [ds1-common.emedf.json]");
                Console.Error.WriteLine("  FogReconstruction inspect-added-event-refs <pristine-game-root> <fog-reference-root> <analysis-output-outside-repo>");
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
                typeof(EMEVD), typeof(EMEVD.Event), typeof(EMEVD.Instruction), typeof(EMEVD.Instruction.ArgType), typeof(EMEVD.Parameter),
                typeof(BND4), typeof(BND3), typeof(BinderFile), typeof(BinaryReaderEx), typeof(PARAM), typeof(PARAM.Row), typeof(PARAM.CellType),
                typeof(PARAMDEF), typeof(PARAMDEF.Field), typeof(PARAM.Layout), typeof(DCX), typeof(DCX.Type),
            })
            {
                Console.WriteLine($"=== {type.FullName} ===");
                if (type.IsEnum)
                {
                    foreach (string name in Enum.GetNames(type))
                        Console.WriteLine($"EnumValue: {name}={(int)Enum.Parse(type, name)}");
                    continue;
                }
                foreach (MemberInfo member in type.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                             .Where(m => m.MemberType is MemberTypes.Field or MemberTypes.Property or MemberTypes.Constructor)
                             .OrderBy(m => m.Name, StringComparer.Ordinal))
                    Console.WriteLine($"{member.MemberType}: {member}");
                if (type == typeof(DCX) || type == typeof(PARAMDEF.Field) || type == typeof(PARAM.Layout))
                    foreach (MethodInfo method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly).OrderBy(m => m.Name, StringComparer.Ordinal))
                        Console.WriteLine($"Method: {method}");
                if (type == typeof(PARAMDEF.Field))
                {
                    foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                    {
                        if (!property.PropertyType.IsEnum) continue;
                        Console.WriteLine($"Enum property {property.Name}: {string.Join(",", Enum.GetNames(property.PropertyType))}");
                    }
                }
            }
        }

        private static void InspectEvent(string path, long eventId, string emedfPath = null)
        {
            byte[] bytes = File.ReadAllBytes(path);
            if (DCX.Is(bytes)) bytes = DCX.Decompress(bytes);
            EMEVD file = EMEVD.Read(bytes);
            EMEDF emedf = null;
            if (!string.IsNullOrWhiteSpace(emedfPath))
            {
                    string emedfJson = System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(emedfPath)).TrimStart('\uFEFF');
                    if (!emedfJson.TrimStart().StartsWith("{", StringComparison.Ordinal))
                        throw new InvalidDataException("The optional EMEDF input must be a DarkScript EMEDF JSON file; the game's common.emedf.dcx is a different binary format.");
                    emedf = EMEDF.ReadText(emedfJson);
            }
            EMEVD.Event evt = file.Events.SingleOrDefault(e => e.ID == eventId);
            if (evt == null) throw new InvalidDataException($"Event {eventId} not found in {path}.");
            Console.WriteLine($"event={evt.ID} name={evt.Name} rest={evt.RestBehavior} instructions={evt.Instructions.Count} parameters={evt.Parameters.Count}");
            for (int i = 0; i < evt.Instructions.Count; i++)
            {
                EMEVD.Instruction instruction = evt.Instructions[i];
                EMEDF.InstrDoc definition = emedf?[instruction.Bank]?[instruction.ID];
                if (definition == null)
                {
                    Console.WriteLine($"{i:D3}: {instruction.Bank}[{instruction.ID}] layer={instruction.Layer?.ToString() ?? "null"} args={Convert.ToHexString(instruction.ArgData)}");
                    continue;
                }
                EMEVD.Instruction.ArgType[] types = definition.Arguments
                    .Select(argument => (EMEVD.Instruction.ArgType)argument.Type).ToArray();
                object[] values = instruction.UnpackArgs(types).ToArray();
                string namedArgs = string.Join(", ", definition.Arguments.Zip(values,
                    (argument, value) => argument.Name + "=" + FormatEventArgument(value)));
                Console.WriteLine($"{i:D3}: {definition.Name} [{instruction.Bank}[{instruction.ID}]] layer={instruction.Layer?.ToString() ?? "null"} {namedArgs}");
            }
            foreach (EMEVD.Parameter parameter in evt.Parameters.OrderBy(p => p.InstructionIndex).ThenBy(p => p.TargetStartByte))
                Console.WriteLine($"param: instruction={parameter.InstructionIndex} target={parameter.TargetStartByte} source={parameter.SourceStartByte} bytes={parameter.ByteCount} id={parameter.UnkID}");
        }

        private static string FormatEventArgument(object value) => value switch
        {
            null => "null",
            IFormattable formatted => formatted.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
            _ => value.ToString(),
        };

        private static void InspectEventArgReferences(string path, int value)
        {
            byte[] bytes = File.ReadAllBytes(path);
            if (DCX.Is(bytes)) bytes = DCX.Decompress(bytes);
            EMEVD file = EMEVD.Read(bytes);
            int matches = 0;
            foreach (EMEVD.Event evt in file.Events)
            {
                for (int instructionIndex = 0; instructionIndex < evt.Instructions.Count; instructionIndex++)
                {
                    EMEVD.Instruction instruction = evt.Instructions[instructionIndex];
                    for (int offset = 0; offset <= instruction.ArgData.Length - sizeof(int); offset++)
                    {
                        if (BitConverter.ToInt32(instruction.ArgData, offset) != value) continue;
                        matches++;
                        Console.WriteLine($"event={evt.ID} instruction={instructionIndex} bank={instruction.Bank} id={instruction.ID} offset={offset}");
                    }
                }
            }
            Console.WriteLine($"Raw int32 argument byte matches for {value}: {matches}. Matches are candidate references; confirm the instruction's argument schema before assigning meaning.");
        }

        private static void InspectGameItemId(string dataRoot, string pristineGameRoot, int category, int itemId)
        {
            FogDsrFacts facts = FogDsrFacts.Read(dataRoot, pristineGameRoot);
            var id = new FogGameItemId(category, itemId);
            if (!facts.GameItemNames.TryGetValue(id, out string name))
                throw new KeyNotFoundException($"No pristine English FMG/GameParam item name resolves for {id}.");
            Console.WriteLine($"{id} | {name}");
        }






        private static string FindRepositoryRoot(string directory)
        {
            DirectoryInfo current = new DirectoryInfo(directory);
            while (current != null)
            {
                if (Directory.Exists(Path.Combine(current.FullName, ".git"))) return current.FullName;
                current = current.Parent;
            }
            throw new DirectoryNotFoundException("Could not locate the repository root from the current directory.");
        }

    }
}
