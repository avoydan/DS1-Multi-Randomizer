using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using SoulsFormats;

namespace FogReconstruction
{
    internal static class ParamDefInspector
    {
        public static void Inspect(string pristineRoot)
        {
            string path = Path.Combine(pristineRoot, "paramdef", "paramdef.paramdefbnd.dcx");
            if (!File.Exists(path)) throw new FileNotFoundException("The pristine snapshot does not contain the DSR ParamDef binder.", path);
            byte[] bytes = File.ReadAllBytes(path);
            if (DCX.Is(bytes)) bytes = DCX.Decompress(bytes);
            IBinder binder = BND4.Is(bytes) ? (IBinder)BND4.Read(bytes)
                : BND3.Is(bytes) ? (IBinder)BND3.Read(bytes)
                : throw new InvalidDataException("ParamDef binder is not BND3/BND4 after decompression.");

            foreach (BinderFile entry in binder.Files)
            {
                PARAMDEF definition = PARAMDEF.Read(entry.Bytes);
                Console.WriteLine($"{Path.GetFileNameWithoutExtension(entry.Name)}\t{definition.ParamType}\trowSize={definition.GetRowSize()}\tfields={definition.Fields.Count}");
            }
        }

        public static void AnalyzeLayouts(string pristineRoot, string referenceRoot)
        {
            string path = Path.Combine(pristineRoot, "paramdef", "paramdef.paramdefbnd.dcx");
            byte[] bytes = File.ReadAllBytes(path);
            if (DCX.Is(bytes)) bytes = DCX.Decompress(bytes);
            IBinder binder = BND4.Is(bytes) ? (IBinder)BND4.Read(bytes)
                : BND3.Is(bytes) ? (IBinder)BND3.Read(bytes)
                : throw new InvalidDataException("ParamDef binder is not BND3/BND4 after decompression.");
            string layoutRoot = Path.Combine(referenceRoot, "dist", "DS1R", "Layouts");
            string[] names = { "GAME_AREA_PARAM_ST", "ITEMLOT_PARAM_ST", "NPC_PARAM_ST", "SP_EFFECT_PARAM_ST" };

            foreach (string name in names)
            {
                string xmlPath = Path.Combine(layoutRoot, name + ".xml");
                XDocument xml = XDocument.Load(xmlPath);
                var xmlFields = xml.Root.Elements("entry").Select(entry => new
                {
                    Name = (string)entry.Element("name"),
                    Type = (string)entry.Element("type"),
                    Default = (string)entry.Element("default"),
                    Size = (string)entry.Element("size"),
                }).ToArray();
                PARAMDEF definition = binder.Files.Select(file => PARAMDEF.Read(file.Bytes))
                    .Single(candidate => string.Equals(candidate.ParamType, name, StringComparison.OrdinalIgnoreCase));
                if (definition.Fields.Count != xmlFields.Length)
                    throw new InvalidDataException($"ParamDef field count does not match oracle layout for {name}: {definition.Fields.Count} vs {xmlFields.Length}.");

                int nameMatches = 0;
                int typeMatches = 0;
                int mappedNameMatches = 0;
                int mappedTypeMatches = 0;
                int defaultTextMatches = 0;
                int paddingSizeMatches = 0;
                var typePairs = new System.Collections.Generic.Dictionary<string, int>(StringComparer.Ordinal);
                var displayPairs = new System.Collections.Generic.Dictionary<string, int>(StringComparer.Ordinal);
                var bitProfiles = new System.Collections.Generic.Dictionary<string, int>(StringComparer.Ordinal);
                var mappingMismatches = new System.Collections.Generic.List<string>();
                int arrays = 0;
                int bitSizes = 0;
                for (int i = 0; i < definition.Fields.Count; i++)
                {
                    PARAMDEF.Field field = definition.Fields[i];
                    if (string.Equals(field.InternalName, xmlFields[i].Name, StringComparison.Ordinal)) nameMatches++;
                    if (string.Equals(field.InternalType, xmlFields[i].Type, StringComparison.Ordinal)) typeMatches++;
                    string mappedName = field.InternalName;
                    string mappedType = field.DisplayType.ToString();
                    if (field.BitSize > 0)
                    {
                        mappedName += ":" + field.BitSize.ToString(System.Globalization.CultureInfo.InvariantCulture);
                        if (mappedType != "dummy8") mappedType = "b" + mappedType.Substring(mappedType.Length == 2 ? 1 : mappedType.Length - 2);
                    }
                    if (field.DisplayType == PARAMDEF.DefType.dummy8 && field.ArrayLength > 1)
                        mappedName = field.InternalName + "[" + (field.ArrayLength - 1).ToString(System.Globalization.CultureInfo.InvariantCulture) + "]";
                    if (string.Equals(mappedName, xmlFields[i].Name, StringComparison.Ordinal)) mappedNameMatches++;
                    if (string.Equals(mappedType, xmlFields[i].Type, StringComparison.Ordinal)) mappedTypeMatches++;
                    if (!string.Equals(mappedName, xmlFields[i].Name, StringComparison.Ordinal)
                        || !string.Equals(mappedType, xmlFields[i].Type, StringComparison.Ordinal))
                        mappingMismatches.Add($"index={i} mapped={mappedName}/{mappedType} oracle={xmlFields[i].Name}/{xmlFields[i].Type} display={field.DisplayType} array={field.ArrayLength} bitSize={field.BitSize}");
                    if (field.BitSize <= 0 && Convert.ToString(field.Default, System.Globalization.CultureInfo.InvariantCulture) == xmlFields[i].Default)
                        defaultTextMatches++;
                    if (field.DisplayType == PARAMDEF.DefType.dummy8
                        && (field.ArrayLength - 1).ToString(System.Globalization.CultureInfo.InvariantCulture) == xmlFields[i].Size)
                        paddingSizeMatches++;
                    string pair = field.InternalType + " -> " + xmlFields[i].Type;
                    typePairs[pair] = typePairs.TryGetValue(pair, out int count) ? count + 1 : 1;
                    string displayPair = field.DisplayType + " -> " + xmlFields[i].Type;
                    displayPairs[displayPair] = displayPairs.TryGetValue(displayPair, out int displayCount) ? displayCount + 1 : 1;
                    string bitProfile = field.DisplayType + "/" + field.BitSize + " -> " + xmlFields[i].Type;
                    bitProfiles[bitProfile] = bitProfiles.TryGetValue(bitProfile, out int bitCount) ? bitCount + 1 : 1;
                    if (field.ArrayLength > 1) arrays++;
                    if (field.BitSize > 0) bitSizes++;
                }
                int consumerSize = PARAM.Layout.ReadXMLFile(xmlPath).Size;
                Console.WriteLine($"{name}: fields={definition.Fields.Count}, names={nameMatches}/{xmlFields.Length}, mapped-names={mappedNameMatches}/{xmlFields.Length}, exact-internal-types={typeMatches}/{xmlFields.Length}, mapped-types={mappedTypeMatches}/{xmlFields.Length}, default-text={defaultTextMatches}, padding-size={paddingSizeMatches}, row-size={definition.GetRowSize()}, consumer-layout-size={consumerSize}, arrays={arrays}, bit-sizes={bitSizes}");
                Console.WriteLine("  type pairs: " + string.Join(", ", typePairs.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => pair.Key + "=" + pair.Value)));
                Console.WriteLine("  display pairs: " + string.Join(", ", displayPairs.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => pair.Key + "=" + pair.Value)));
                Console.WriteLine("  bit profiles: " + string.Join(", ", bitProfiles.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => pair.Key + "=" + pair.Value)));
                foreach (string mismatch in mappingMismatches) Console.WriteLine("  mapping mismatch: " + mismatch);
                foreach ((PARAMDEF.Field field, int index) in definition.Fields.Select((field, index) => (field, index))
                             .Where(pair => pair.field.ArrayLength > 1))
                    Console.WriteLine($"  array[{index}]: name={field.InternalName}, internal={field.InternalType}, display={field.DisplayType}, array={field.ArrayLength}, bitSize={field.BitSize}, default={field.Default}");
                foreach ((PARAMDEF.Field field, int index) in definition.Fields.Select((field, index) => (field, index))
                             .Where(pair => pair.field.DisplayType != PARAMDEF.DefType.dummy8
                                 && (pair.field.InternalName + (pair.field.BitSize > 0 ? ":" + pair.field.BitSize.ToString(System.Globalization.CultureInfo.InvariantCulture) : "") != xmlFields[pair.index].Name
                                     || (pair.field.BitSize > 0
                                         ? "b" + pair.field.DisplayType.ToString().Substring(pair.field.DisplayType.ToString().Length == 2 ? 1 : pair.field.DisplayType.ToString().Length - 2)
                                         : pair.field.DisplayType.ToString()) != xmlFields[pair.index].Type
                                     || Convert.ToString(pair.field.Default, System.Globalization.CultureInfo.InvariantCulture) != xmlFields[pair.index].Default))
                             .Take(10))
                {
                    string mappedName = field.InternalName + (field.BitSize > 0 ? ":" + field.BitSize.ToString(System.Globalization.CultureInfo.InvariantCulture) : "");
                    string mappedType = field.DisplayType.ToString();
                    if (field.BitSize > 0) mappedType = "b" + mappedType.Substring(mappedType.Length == 2 ? 1 : mappedType.Length - 2);
                    Console.WriteLine($"  compare[{index}]: mapped={mappedName}/{mappedType}, oracle={xmlFields[index].Name}/{xmlFields[index].Type}, default={Convert.ToString(field.Default, System.Globalization.CultureInfo.InvariantCulture)}/{xmlFields[index].Default}, internal={field.InternalType}, display={field.DisplayType}, array={field.ArrayLength}, bitSize={field.BitSize}");
                }
            }
        }
    }
}
