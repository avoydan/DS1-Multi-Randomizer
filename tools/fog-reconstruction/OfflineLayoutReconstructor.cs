using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using FogHost;
using SoulsFormats;

namespace FogReconstruction
{
    /// <summary>Derives FogMod Layout XML from the user's local DSR ParamDef and checks the real SoulsFormats consumer.</summary>
    internal static class OfflineLayoutReconstructor
    {
        private const string ParamDefFile = "paramdef.paramdefbnd.dcx";
        private const string ParamDefSha256 = "647bd58afc8f4f30c6d007626bf50fc0e66d9a93f6bf56666e29c0d767e0f6e6";
        private const string GameParamRelativePath = "param/GameParam/GameParam.parambnd.dcx";
        private static readonly string[] ParamTypes =
        {
            "GAME_AREA_PARAM_ST", "ITEMLOT_PARAM_ST", "NPC_PARAM_ST", "SP_EFFECT_PARAM_ST",
        };

        internal static void Build(string pristineRoot, string referenceRoot, string outputRoot)
        {
            string sourceRoot = Path.GetFullPath(pristineRoot);
            string oracleRoot = Path.GetFullPath(referenceRoot);
            string finalRoot = Path.GetFullPath(outputRoot);
            string repositoryRoot = FindRepositoryRoot(Environment.CurrentDirectory);
            EnsureOutsideRepository(finalRoot, repositoryRoot);
            EnsureDistinctRoots(sourceRoot, oracleRoot, finalRoot);
            if (Directory.Exists(finalRoot) || File.Exists(finalRoot))
                throw new IOException("Offline Layout output already exists; select a new output path: " + finalRoot);

            RecipeExtractor.VerifyPristineBaseFiles(sourceRoot);
            DataRoot.Verify(oracleRoot);
            Dictionary<string, PARAMDEF> definitions = ReadDefinitions(sourceRoot);
            ValidateParamDefHash(sourceRoot);

            string stagingRoot = finalRoot + ".staging-" + Guid.NewGuid().ToString("N");
            string stagingLayouts = Path.Combine(stagingRoot, "Layouts");
            Directory.CreateDirectory(stagingLayouts);
            var generatedLayouts = new Dictionary<string, PARAM.Layout>(StringComparer.Ordinal);
            foreach (string paramType in ParamTypes)
            {
                if (!definitions.TryGetValue(paramType, out PARAMDEF definition))
                    throw new InvalidDataException("The clean ParamDef binder is missing " + paramType + ".");
                string outputPath = Path.Combine(stagingLayouts, paramType + ".xml");
                WriteLayout(definition, outputPath);

                PARAM.Layout layout = PARAM.Layout.ReadXMLFile(outputPath);
                PARAMDEF generatedDefinition = layout.ToParamdef(paramType, out List<PARAMTDF> ignoredEnums);
                if (layout.Size != definition.GetRowSize() || generatedDefinition.GetRowSize() != definition.GetRowSize())
                    throw new InvalidDataException($"Generated Layout size for {paramType} is {layout.Size}/{generatedDefinition.GetRowSize()}, expected ParamDef row size {definition.GetRowSize()}.");

                string oraclePath = Path.Combine(oracleRoot, "dist", "DS1R", "Layouts", paramType + ".xml");
                CompareLayoutStructure(paramType, outputPath, oraclePath, definition.GetRowSize());
                generatedLayouts.Add(paramType, layout);
                Console.WriteLine($"PASS ParamDef/Layout structure {paramType}: {definition.Fields.Count} ordered fields, row size {definition.GetRowSize()}.");
            }

            ValidateGameParamDecodeEncode(sourceRoot, definitions, generatedLayouts);
            Directory.Move(stagingRoot, finalRoot);
            Console.WriteLine("Offline Layout reconstruction passed all four ParamDef-derived layouts and GameParam decode/re-encode checks.");
            Console.WriteLine("Output contains only generated Layout XML and is a development artifact, not a complete Fog data root.");
        }

        private static void ValidateParamDefHash(string pristineRoot)
        {
            string path = Path.Combine(pristineRoot, "paramdef", ParamDefFile);
            string actual = DataRoot.Hash(path, text: false);
            if (!string.Equals(actual, ParamDefSha256, StringComparison.Ordinal))
                throw new InvalidDataException($"Pristine ParamDef input hash mismatch: expected {ParamDefSha256}, got {actual}.");
        }

        private static Dictionary<string, PARAMDEF> ReadDefinitions(string pristineRoot)
        {
            string path = Path.Combine(pristineRoot, "paramdef", ParamDefFile);
            byte[] bytes = File.ReadAllBytes(path);
            if (DCX.Is(bytes)) bytes = DCX.Decompress(bytes);
            IBinder binder = BND4.Is(bytes) ? (IBinder)BND4.Read(bytes)
                : BND3.Is(bytes) ? (IBinder)BND3.Read(bytes)
                : throw new InvalidDataException("ParamDef binder is not BND3/BND4 after decompression.");
            var definitions = new Dictionary<string, PARAMDEF>(StringComparer.Ordinal);
            foreach (BinderFile entry in binder.Files)
            {
                PARAMDEF definition = PARAMDEF.Read(entry.Bytes);
                if (!definitions.TryAdd(definition.ParamType, definition))
                    throw new InvalidDataException("Duplicate ParamDef type: " + definition.ParamType);
            }
            return definitions;
        }

        private static void WriteLayout(PARAMDEF definition, string path)
        {
            var settings = new XmlWriterSettings
            {
                Encoding = new UTF8Encoding(false),
                Indent = true,
                IndentChars = "  ",
                NewLineChars = "\n",
                NewLineHandling = NewLineHandling.Replace,
                OmitXmlDeclaration = false,
            };
            using (XmlWriter writer = XmlWriter.Create(path, settings))
            {
                writer.WriteStartDocument();
                writer.WriteStartElement("layout");
                foreach (PARAMDEF.Field field in definition.Fields)
                {
                    writer.WriteStartElement("entry");
                    writer.WriteElementString("name", LayoutName(field));
                    writer.WriteElementString("type", LayoutType(field));
                    if (field.DisplayType == PARAMDEF.DefType.dummy8)
                    {
                        int size = field.BitSize > 0 ? 0 : Math.Max(0, field.ArrayLength);
                        writer.WriteElementString("size", size.ToString(CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        writer.WriteElementString("default", LayoutDefault(field));
                    }
                    writer.WriteEndElement();
                }
                writer.WriteEndElement();
                writer.WriteEndDocument();
            }
            AdjustDummyPaddingToRowSize(definition, path);
        }

        private static void AdjustDummyPaddingToRowSize(PARAMDEF definition, string path)
        {
            int expectedSize = definition.GetRowSize();
            int actualSize = PARAM.Layout.ReadXMLFile(path).Size;
            int remaining = expectedSize - actualSize;
            if (remaining == 0) return;

            XDocument document = XDocument.Load(path);
            List<XElement> entries = document.Root.Elements("entry").ToList();
            var paddingEntries = definition.Fields.Select((field, index) => new { Field = field, Index = index })
                .Where(item => item.Field.DisplayType == PARAMDEF.DefType.dummy8
                    && item.Field.BitSize <= 0
                    && entries[item.Index].Element("size") != null)
                .OrderByDescending(item => item.Index)
                .ToList();

            foreach (var item in paddingEntries)
            {
                if (remaining == 0) break;
                XElement entry = entries[item.Index];
                int size = (int)entry.Element("size");
                int adjustedSize;
                if (remaining < 0)
                {
                    int reduction = Math.Min(size, -remaining);
                    adjustedSize = size - reduction;
                    remaining += reduction;
                }
                else
                {
                    adjustedSize = checked(size + remaining);
                    remaining = 0;
                }

                entry.Element("size").Value = adjustedSize.ToString(CultureInfo.InvariantCulture);
                entry.Element("name").Value = PaddingName(item.Field, adjustedSize);
            }

            if (remaining != 0)
                throw new InvalidDataException($"ParamDef/Layout row size differs by {remaining} byte(s) for {definition.ParamType}, but no dummy8 padding can reconcile it.");

            SaveLayout(document, path);
            int adjustedLayoutSize = PARAM.Layout.ReadXMLFile(path).Size;
            if (adjustedLayoutSize != expectedSize)
                throw new InvalidDataException($"Adjusted Layout size for {definition.ParamType} is {adjustedLayoutSize}; ParamDef row size is {expectedSize}.");
        }

        private static string PaddingName(PARAMDEF.Field field, int size)
        {
            return size > 1 ? field.InternalName + "[" + size.ToString(CultureInfo.InvariantCulture) + "]" : field.InternalName;
        }

        private static void SaveLayout(XDocument document, string path)
        {
            var settings = new XmlWriterSettings
            {
                Encoding = new UTF8Encoding(false),
                Indent = true,
                IndentChars = "  ",
                NewLineChars = "\n",
                NewLineHandling = NewLineHandling.Replace,
                OmitXmlDeclaration = false,
            };
            using (XmlWriter writer = XmlWriter.Create(path, settings))
                document.Save(writer);
        }

        private static string LayoutName(PARAMDEF.Field field)
        {
            if (field.DisplayType == PARAMDEF.DefType.dummy8 && field.ArrayLength > 1)
                return field.InternalName + "[" + field.ArrayLength.ToString(CultureInfo.InvariantCulture) + "]";
            if (field.BitSize > 0)
                return field.InternalName + ":" + field.BitSize.ToString(CultureInfo.InvariantCulture);
            return field.InternalName;
        }

        private static string LayoutType(PARAMDEF.Field field)
        {
            string displayType = field.DisplayType.ToString();
            if (field.BitSize != 1 || displayType == "dummy8") return displayType;
            string digits = displayType.Length == 2 ? displayType.Substring(1) : displayType.Substring(displayType.Length - 2);
            return "b" + digits;
        }

        private static string LayoutDefault(PARAMDEF.Field field)
        {
            // PARAMDEF's Default values are metadata sentinels and often do not
            // match Layout's default semantics (for example integer -1 maps to
            // Layout's zero default). The shipped Fog layouts use zero values.
            if (field.BitSize == 1)
                return "false";
            return field.DisplayType == PARAMDEF.DefType.f32 ? "0.0" : "0";
        }

        private static void CompareLayoutStructure(string paramType, string generatedPath, string oraclePath, int rowSize)
        {
            XDocument generated = XDocument.Load(generatedPath);
            XDocument oracle = XDocument.Load(oraclePath);
            var actualFields = ReadXmlFields(generated);
            var expectedFields = ReadXmlFields(oracle);
            if (actualFields.Count != expectedFields.Count)
                throw new InvalidDataException($"Generated Layout field count for {paramType} differs from the development oracle: {actualFields.Count} vs {expectedFields.Count}.");
            int typeMatches = 0;
            int normalizedNameMatches = 0;
            for (int i = 0; i < actualFields.Count; i++)
            {
                if (string.Equals(actualFields[i].Type, expectedFields[i].Type, StringComparison.Ordinal)) typeMatches++;
                if (string.Equals(NormalizeLayoutName(actualFields[i].Name), NormalizeLayoutName(expectedFields[i].Name), StringComparison.Ordinal)) normalizedNameMatches++;
            }
            PARAM.Layout generatedLayout = PARAM.Layout.ReadXMLFile(generatedPath);
            PARAM.Layout oracleLayout = PARAM.Layout.ReadXMLFile(oraclePath);
            if (typeMatches != actualFields.Count || normalizedNameMatches != actualFields.Count
                || generatedLayout.Size != rowSize || oracleLayout.Size != rowSize)
                throw new InvalidDataException($"Layout consumer mismatch for {paramType}: fields={actualFields.Count}, type parity={typeMatches}, name parity={normalizedNameMatches}, generated size={generatedLayout.Size}, oracle size={oracleLayout.Size}, ParamDef size={rowSize}.");
        }

        private static List<LayoutField> ReadXmlFields(XDocument document) => document.Root.Elements("entry")
            .Select(entry => new LayoutField
            {
                Name = (string)entry.Element("name"),
                Type = (string)entry.Element("type"),
                Size = (int?)entry.Element("size"),
                Default = (string)entry.Element("default"),
            }).ToList();

        private static string NormalizeLayoutName(string name) =>
            new string((name ?? "").Where(character => !char.IsWhiteSpace(character)).ToArray());

        private static void ValidateGameParamDecodeEncode(string pristineRoot,
            IReadOnlyDictionary<string, PARAMDEF> definitions, IReadOnlyDictionary<string, PARAM.Layout> layouts)
        {
            string path = Path.Combine(pristineRoot, GameParamRelativePath.Replace('/', Path.DirectorySeparatorChar));
            byte[] bytes = File.ReadAllBytes(path);
            if (DCX.Is(bytes)) bytes = DCX.Decompress(bytes);
            IBinder binder = BND4.Is(bytes) ? (IBinder)BND4.Read(bytes)
                : BND3.Is(bytes) ? (IBinder)BND3.Read(bytes)
                : throw new InvalidDataException("GameParam binder is not BND3/BND4 after decompression.");
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (BinderFile entry in binder.Files)
            {
                PARAM raw = PARAM.Read(entry.Bytes);
                if (!ParamTypes.Contains(raw.ParamType, StringComparer.Ordinal)) continue;
                if (!seen.Add(raw.ParamType)) throw new InvalidDataException("Duplicate GameParam table " + raw.ParamType);

                PARAMDEF sourceDefinition = definitions[raw.ParamType];
                PARAMDEF generatedDefinition = layouts[raw.ParamType].ToParamdef(raw.ParamType, out List<PARAMTDF> ignoredEnums);
                if (raw.DetectedSize != sourceDefinition.GetRowSize() || raw.DetectedSize != generatedDefinition.GetRowSize())
                    throw new InvalidDataException($"GameParam table {raw.ParamType} has row size {raw.DetectedSize}; source and generated layouts are {sourceDefinition.GetRowSize()} and {generatedDefinition.GetRowSize()}.");

                PARAM sourceDecoded = PARAM.Read(entry.Bytes);
                sourceDecoded.ApplyParamdef(sourceDefinition);
                PARAM generatedDecoded = PARAM.Read(entry.Bytes);
                generatedDecoded.ApplyParamdef(generatedDefinition);

                byte[] reencoded = generatedDecoded.Write();
                byte[] sourceReencoded = sourceDecoded.Write();
                if (!reencoded.SequenceEqual(sourceReencoded))
                {
                    int mismatch = Enumerable.Range(0, Math.Min(reencoded.Length, sourceReencoded.Length))
                        .FirstOrDefault(index => reencoded[index] != sourceReencoded[index]);
                    throw new InvalidDataException($"GameParam {raw.ParamType} serialized bytes differ between the pristine ParamDef and generated Layout at offset {mismatch} (length {sourceReencoded.Length}/{reencoded.Length}).");
                }
                CompareParamRows(raw.ParamType, sourceDecoded, generatedDecoded, "source ParamDef", "generated Layout", compareValues: false);
                PARAM roundTripped = PARAM.Read(reencoded);
                roundTripped.ApplyParamdef(generatedDefinition);
                CompareParamRows(raw.ParamType, generatedDecoded, roundTripped, "decoded GameParam", "re-encoded GameParam", compareValues: true);
                Console.WriteLine($"PASS GameParam decode/re-encode {raw.ParamType}: {generatedDecoded.Rows.Count} rows, {generatedDecoded.Rows.Sum(row => row.Cells.Count)} cells.");
            }
            if (seen.Count != ParamTypes.Length)
                throw new InvalidDataException($"Expected {ParamTypes.Length} target GameParam tables, decoded {seen.Count}: {string.Join(",", seen.OrderBy(name => name, StringComparer.Ordinal))}.");
        }

        private static void CompareParamRows(string paramType, PARAM expected, PARAM actual, string expectedLabel, string actualLabel, bool compareValues)
        {
            if (expected.ParamType != actual.ParamType || expected.Rows.Count != actual.Rows.Count)
                throw new InvalidDataException($"GameParam {paramType} {expectedLabel}/{actualLabel} table or row count differs.");
            for (int rowIndex = 0; rowIndex < expected.Rows.Count; rowIndex++)
            {
                PARAM.Row expectedRow = expected.Rows[rowIndex];
                PARAM.Row actualRow = actual.Rows[rowIndex];
                if (expectedRow.ID != actualRow.ID || expectedRow.Cells.Count != actualRow.Cells.Count)
                    throw new InvalidDataException($"GameParam {paramType} {expectedLabel}/{actualLabel} row index {rowIndex} metadata differs.");
                for (int cellIndex = 0; cellIndex < expectedRow.Cells.Count; cellIndex++)
                {
                    PARAM.Cell expectedCell = expectedRow.Cells[cellIndex];
                    PARAM.Cell actualCell = actualRow.Cells[cellIndex];
                    if (!compareValues) continue;
                    if (expectedCell.Def?.DisplayType == PARAMDEF.DefType.dummy8
                        || actualCell.Def?.DisplayType == PARAMDEF.DefType.dummy8)
                        continue;
                    object expectedValue = expectedCell.Value;
                    object actualValue = actualCell.Value;
                    if (!ValuesEqual(expectedValue, actualValue))
                    {
                        string fieldName = expectedCell.Def?.InternalName ?? cellIndex.ToString(CultureInfo.InvariantCulture);
                        throw new InvalidDataException($"GameParam {paramType} row {expectedRow.ID} field {fieldName} differs after {actualLabel}: {expectedValue ?? "<null>"} vs {actualValue ?? "<null>"}.");
                    }
                }
            }
        }

        private static bool ValuesEqual(object expected, object actual)
        {
            if (ReferenceEquals(expected, actual)) return true;
            if (expected == null || actual == null) return false;
            if (expected is Array expectedArray && actual is Array actualArray)
            {
                if (expectedArray.Length != actualArray.Length || expectedArray.Rank != actualArray.Rank) return false;
                for (int index = 0; index < expectedArray.Length; index++)
                    if (!ValuesEqual(expectedArray.GetValue(index), actualArray.GetValue(index))) return false;
                return true;
            }
            return Equals(expected, actual);
        }

        private static void EnsureDistinctRoots(string source, string oracle, string output)
        {
            string[] roots = { source, oracle, output };
            for (int i = 0; i < roots.Length; i++)
                for (int j = i + 1; j < roots.Length; j++)
                    if (IsSameOrChild(roots[i], roots[j]) || IsSameOrChild(roots[j], roots[i]))
                        throw new InvalidOperationException("Pristine source, reference oracle and output paths must be separate trees.");
        }

        private static void EnsureOutsideRepository(string path, string repositoryRoot)
        {
            if (IsSameOrChild(repositoryRoot, path))
                throw new InvalidOperationException("Reconstructed Layout output must be outside the repository.");
        }

        private static bool IsSameOrChild(string parent, string candidate)
        {
            string normalizedParent = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string normalizedCandidate = Path.GetFullPath(candidate).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return string.Equals(normalizedParent, normalizedCandidate, StringComparison.OrdinalIgnoreCase)
                || normalizedCandidate.StartsWith(normalizedParent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
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

        private sealed class LayoutField
        {
            public string Name { get; set; }
            public string Type { get; set; }
            public int? Size { get; set; }
            public string Default { get; set; }
        }
    }
}
