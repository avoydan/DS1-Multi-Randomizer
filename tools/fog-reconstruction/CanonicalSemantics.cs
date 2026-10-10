using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SoulsFormats;
using SoulsIds;

namespace FogReconstruction
{
    /// <summary>Stable semantic representation of a SoulsFormats EMEVD or ESD object.</summary>
    internal static class CanonicalSemantics
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false,
        };

        public static string Serialize(object value) => JsonSerializer.Serialize(value, JsonOptions);

        public static string Fingerprint(object value) => Sha256(Encoding.UTF8.GetBytes(Serialize(value)));

        public static EsdFileModel EsdSemanticModel(EsdFileModel model) => new EsdFileModel
        {
            // The archive entry name is container identity, not ESD program semantics.
            LongFormat = model.LongFormat,
            DarkSoulsCount = model.DarkSoulsCount,
            Unk70 = model.Unk70,
            Unk74 = model.Unk74,
            Unk78 = model.Unk78,
            Unk7C = model.Unk7C,
            StateGroups = model.StateGroups,
        };

        public static string EsdFingerprint(EsdFileModel model) => Fingerprint(EsdSemanticModel(model));

        public static string Sha256(byte[] value) => Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();

        public static EmevdFileModel Emevd(EMEVD file, EMEDF emedf = null)
        {
            return new EmevdFileModel
            {
                Format = file.Format.ToString(),
                LinkedFileOffsets = file.LinkedFileOffsets.OrderBy(v => v).ToList(),
                StringDataHex = Hex(file.StringData),
                Events = file.Events.OrderBy(e => e.ID).Select(e => new EmevdEventModel
                {
                    Id = e.ID,
                    Name = e.Name,
                    RestBehavior = e.RestBehavior.ToString(),
                    Instructions = e.Instructions.Select(i => Instruction(i, emedf)).ToList(),
                    Parameters = e.Parameters
                        .OrderBy(p => p.InstructionIndex)
                        .ThenBy(p => p.TargetStartByte)
                        .ThenBy(p => p.SourceStartByte)
                        .ThenBy(p => p.ByteCount)
                        .ThenBy(p => p.UnkID)
                        .Select(p => new EmevdParameterModel
                        {
                            InstructionIndex = p.InstructionIndex,
                            TargetStartByte = p.TargetStartByte,
                            SourceStartByte = p.SourceStartByte,
                            ByteCount = p.ByteCount,
                            UnkId = p.UnkID,
                        }).ToList(),
                }).ToList(),
            };
        }

        public static EsdFileModel Esd(ESD file, string entryName, bool normalizeBytecode = true)
        {
            return new EsdFileModel
            {
                EntryName = entryName,
                LongFormat = file.LongFormat,
                DarkSoulsCount = file.DarkSoulsCount,
                Name = file.Name,
                Unk70 = file.Unk70,
                Unk74 = file.Unk74,
                Unk78 = file.Unk78,
                Unk7C = file.Unk7C,
                StateGroups = file.StateGroups.OrderBy(g => g.Key).Select(group => new EsdStateGroupModel
                {
                    Id = group.Key,
                    States = group.Value.OrderBy(s => s.Key).Select(state => new EsdStateModel
                    {
                        Id = state.Key,
                        Conditions = (state.Value.Conditions ?? new List<ESD.Condition>()).Select(c => Condition(c, 0, normalizeBytecode)).ToList(),
                        EntryCommands = Commands(state.Value.EntryCommands, normalizeBytecode),
                        ExitCommands = Commands(state.Value.ExitCommands, normalizeBytecode),
                        WhileCommands = Commands(state.Value.WhileCommands, normalizeBytecode),
                    }).ToList(),
                }).ToList(),
            };
        }

        public static string InstructionSignature(EMEVD.Instruction instruction) =>
            string.Join("|", instruction.Bank.ToString(CultureInfo.InvariantCulture),
                instruction.ID.ToString(CultureInfo.InvariantCulture),
                instruction.Layer.HasValue ? instruction.Layer.Value.ToString(CultureInfo.InvariantCulture) : "null",
                Hex(instruction.ArgData));

        public static string InstructionAnchor(EMEVD.Instruction instruction) =>
            string.Join("|", instruction.Bank.ToString(CultureInfo.InvariantCulture),
                instruction.ID.ToString(CultureInfo.InvariantCulture),
                instruction.Layer.HasValue ? instruction.Layer.Value.ToString(CultureInfo.InvariantCulture) : "null",
                Sha256(instruction.ArgData ?? Array.Empty<byte>()).Substring(0, 12));

        public static bool SameInstruction(EMEVD.Instruction a, EMEVD.Instruction b) =>
            a.Bank == b.Bank && a.ID == b.ID && a.Layer == b.Layer
            && (a.ArgData ?? Array.Empty<byte>()).AsSpan().SequenceEqual(b.ArgData ?? Array.Empty<byte>());

        internal static EmevdInstructionModel Instruction(EMEVD.Instruction instruction, EMEDF emedf)
        {
            var model = new EmevdInstructionModel
            {
                Bank = instruction.Bank,
                Id = instruction.ID,
                Layer = instruction.Layer,
                RawArgsHex = Hex(instruction.ArgData),
                Anchor = InstructionAnchor(instruction),
            };

            List<EMEVD.Instruction.ArgType> types = null;
            List<string> names = null;
            if (instruction.Bank == 2000 && (instruction.ID == 0 || instruction.ID == 6))
            {
                types = Enumerable.Repeat(EMEVD.Instruction.ArgType.Int32, instruction.ArgData.Length / 4).ToList();
                names = Enumerable.Range(0, types.Count).Select(i => "arg" + i.ToString(CultureInfo.InvariantCulture)).ToList();
            }
            else if (emedf != null)
            {
                EMEDF.ClassDoc bank = emedf[instruction.Bank];
                EMEDF.InstrDoc definition = bank == null ? null : bank[instruction.ID];
                if (definition != null && definition.Arguments != null)
                {
                    types = definition.Arguments.Select(a => ArgType(a.Type)).ToList();
                    names = definition.Arguments.Select(a => a.Name ?? "").ToList();
                }
            }

            if (types != null)
            {
                try
                {
                    List<object> values = instruction.UnpackArgs(types);
                    model.TypedArgs = values.Select((value, index) => new EmevdTypedArgumentModel
                    {
                        Name = index < names.Count ? names[index] : "arg" + index.ToString(CultureInfo.InvariantCulture),
                        Type = types[index].ToString(),
                        Value = value,
                    }).ToList();
                }
                catch (Exception ex)
                {
                    model.ArgumentDecodeError = ex.Message;
                }
            }
            return model;
        }

        private static EMEVD.Instruction.ArgType ArgType(long type)
        {
            // DarkScript EMEDF type 8 is the documented 32-bit unsigned event initializer argument.
            if (type == 8) return EMEVD.Instruction.ArgType.UInt32;
            if (type < 0 || type > 6) throw new InvalidOperationException("Unsupported EMEDF argument type " + type.ToString(CultureInfo.InvariantCulture));
            return (EMEVD.Instruction.ArgType)type;
        }

        private static EsdConditionModel Condition(ESD.Condition condition, int depth, bool normalizeBytecode)
        {
            if (depth > 256) throw new InvalidOperationException("ESD condition nesting exceeds the safety limit.");
            return new EsdConditionModel
            {
                TargetState = condition.TargetState,
                EvaluatorHex = BytecodeHex(condition.Evaluator, normalizeBytecode),
                PassCommands = Commands(condition.PassCommands, normalizeBytecode),
                Subconditions = (condition.Subconditions ?? new List<ESD.Condition>()).Select(c => Condition(c, depth + 1, normalizeBytecode)).ToList(),
            };
        }

        private static List<EsdCommandModel> Commands(IEnumerable<ESD.CommandCall> commands, bool normalizeBytecode = true) =>
            (commands ?? Enumerable.Empty<ESD.CommandCall>()).Select(c => new EsdCommandModel
            {
                Bank = c.CommandBank,
                Id = c.CommandID,
                Arguments = (c.Arguments ?? new List<byte[]>()).Select(a => BytecodeHex(a, normalizeBytecode)).ToList(),
            }).ToList();

        private static string BytecodeHex(byte[] bytes, bool normalizeBytecode) =>
            Hex(normalizeBytecode ? TalkBytecodeNormalizer.NormalizeExpression(bytes) : bytes);

        private static string SemanticBytecodeHex(byte[] bytes) => Hex(TalkBytecodeNormalizer.NormalizeExpression(bytes));

        private static string Hex(byte[] bytes) => Convert.ToHexString(bytes ?? Array.Empty<byte>());
    }

    internal sealed class EmevdFileModel
    {
        public string Format { get; set; }
        public List<long> LinkedFileOffsets { get; set; }
        public string StringDataHex { get; set; }
        public List<EmevdEventModel> Events { get; set; }
    }

    internal sealed class EmevdEventModel
    {
        public long Id { get; set; }
        public string Name { get; set; }
        public string RestBehavior { get; set; }
        public List<EmevdInstructionModel> Instructions { get; set; }
        public List<EmevdParameterModel> Parameters { get; set; }
    }

    internal sealed class EmevdInstructionModel
    {
        public int Bank { get; set; }
        public int Id { get; set; }
        public uint? Layer { get; set; }
        public string RawArgsHex { get; set; }
        public string Anchor { get; set; }
        public List<EmevdTypedArgumentModel> TypedArgs { get; set; }
        public string ArgumentDecodeError { get; set; }
    }

    internal sealed class EmevdTypedArgumentModel
    {
        public string Name { get; set; }
        public string Type { get; set; }
        public object Value { get; set; }
    }

    internal sealed class EmevdParameterModel
    {
        public long InstructionIndex { get; set; }
        public long TargetStartByte { get; set; }
        public long SourceStartByte { get; set; }
        public int ByteCount { get; set; }
        public int UnkId { get; set; }
    }

    internal sealed class EsdFileModel
    {
        public string EntryName { get; set; }
        public bool LongFormat { get; set; }
        public int DarkSoulsCount { get; set; }
        public string Name { get; set; }
        public int Unk70 { get; set; }
        public int Unk74 { get; set; }
        public int Unk78 { get; set; }
        public int Unk7C { get; set; }
        public List<EsdStateGroupModel> StateGroups { get; set; }
    }

    internal sealed class EsdStateGroupModel
    {
        public long Id { get; set; }
        public List<EsdStateModel> States { get; set; }
    }

    internal sealed class EsdStateModel
    {
        public long Id { get; set; }
        public List<EsdConditionModel> Conditions { get; set; }
        public List<EsdCommandModel> EntryCommands { get; set; }
        public List<EsdCommandModel> ExitCommands { get; set; }
        public List<EsdCommandModel> WhileCommands { get; set; }
    }

    internal sealed class EsdConditionModel
    {
        public long? TargetState { get; set; }
        public string EvaluatorHex { get; set; }
        public List<EsdCommandModel> PassCommands { get; set; }
        public List<EsdConditionModel> Subconditions { get; set; }
    }

    internal sealed class EsdCommandModel
    {
        public int Bank { get; set; }
        public int Id { get; set; }
        public List<string> Arguments { get; set; }
    }
}
