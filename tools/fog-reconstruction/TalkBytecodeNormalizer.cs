using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SoulsFormats;

namespace FogReconstruction
{
    /// <summary>
    /// Recreates the ESDLang text round-trip used by the pinned Fog Talk corpus:
    /// ordinary integer literals become int32 tokens, compact function IDs stay
    /// compact, and ContinueIfFalse annotations are omitted by the text form.
    /// </summary>
    internal static class TalkBytecodeNormalizer
    {
        private static readonly Dictionary<byte, string> BinaryOperators = new Dictionary<byte, string>
        {
            [0x8C] = "+", [0x8E] = "-", [0x8F] = "*", [0x90] = "/",
            [0x91] = "<=", [0x92] = ">=", [0x93] = "<", [0x94] = ">",
            [0x95] = "==", [0x96] = "!=", [0x98] = "&&", [0x99] = "||",
        };
        internal static int Normalize(ESD esd)
        {
            int rewritten = 0;
            foreach (KeyValuePair<long, Dictionary<long, ESD.State>> group in esd.StateGroups)
            foreach (KeyValuePair<long, ESD.State> stateEntry in group.Value)
            {
                ESD.State state = stateEntry.Value;
                var registers = new Dictionary<int, Expr>();
                rewritten += NormalizeCommands(state.EntryCommands, registers, esd.Name, group.Key, stateEntry.Key);
                foreach (ESD.Condition condition in state.Conditions ?? new List<ESD.Condition>())
                    rewritten += NormalizeCondition(condition, registers, esd.Name, group.Key, stateEntry.Key);
                rewritten += NormalizeCommands(state.WhileCommands, registers, esd.Name, group.Key, stateEntry.Key);
                rewritten += NormalizeCommands(state.ExitCommands, registers, esd.Name, group.Key, stateEntry.Key);
            }
            rewritten += ApplyRepeatedNpcConversationPatch(esd);
            return rewritten;
        }

        private static int ApplyRepeatedNpcConversationPatch(ESD esd)
        {
            if (!esd.StateGroups.TryGetValue(1, out Dictionary<long, ESD.State> group)
                || !group.TryGetValue(4, out ESD.State branchState)
                || !group.TryGetValue(17, out ESD.State resultState))
                return 0;

            byte[] expectedBranch = { 0x57, 0x84, 0x82, 0x00, 0x00, 0x00, 0x00, 0x95, 0xA1 };
            bool matchesVanillaShape = branchState.Conditions.Count == 17
                && branchState.Conditions[3].TargetState == 22
                && branchState.Conditions[3].Evaluator.SequenceEqual(expectedBranch)
                && resultState.Conditions.Count == 2
                && resultState.Conditions[0].TargetState == 21
                && resultState.Conditions[1].TargetState == 18
                && branchState.EntryCommands.Count == 15
                && branchState.EntryCommands.Skip(3).All(c => c.CommandBank == 1 && c.CommandID == 19)
                && ReadInteger(branchState.EntryCommands[4].Arguments[2]) == unchecked((int)0x00B434D0)
                && ReadInteger(branchState.EntryCommands[11].Arguments[0]) == 12
                && ReadInteger(branchState.EntryCommands[11].Arguments[1]) == unchecked((int)0x00E4E256)
                && ReadInteger(branchState.EntryCommands[11].Arguments[2]) == 0x02CD
                && ReadInteger(branchState.EntryCommands[13].Arguments[2]) == unchecked((int)0x00B434D0)
                && ReadInteger(branchState.EntryCommands[14].Arguments[2]) == unchecked((int)0x00B434D0)
                && resultState.EntryCommands.Count == 1
                && resultState.EntryCommands[0].CommandBank == 1
                && resultState.EntryCommands[0].CommandID == 11;
            if (!matchesVanillaShape) return 0;

            // The pinned Fog Talk base has a shared branch for the special talk result
            // in 73 NPC script copies. Keep the rule structural so game-derived ESD
            // bodies are never stored in the application.
            var inserted = new ESD.Condition(17, new byte[] { 0x57, 0x84, 0x82, 0x0F, 0x00, 0x00, 0x00, 0x95, 0xA1 })
            {
                PassCommands = new List<ESD.CommandCall>(),
                Subconditions = new List<ESD.Condition>(),
            };
            branchState.Conditions.Insert(4, inserted);

            SetInteger(branchState.EntryCommands[4].Arguments, 2, -1);
            SetInteger(branchState.EntryCommands[11].Arguments, 0, 15);
            SetInteger(branchState.EntryCommands[11].Arguments, 1, unchecked((int)0x00E4E2D8));
            SetInteger(branchState.EntryCommands[11].Arguments, 2, 0x001AFE);
            SetInteger(branchState.EntryCommands[13].Arguments, 2, -1);
            SetInteger(branchState.EntryCommands[14].Arguments, 2, -1);

            resultState.EntryCommands[0].Arguments = new List<byte[]>
            {
                Int32Expression(0x14B4),
                Int32Expression(1),
            };

            resultState.Conditions.Clear();
            resultState.Conditions.Add(new ESD.Condition(22, new byte[] { 0x41, 0xA1 })
            {
                PassCommands = new List<ESD.CommandCall>(),
                Subconditions = new List<ESD.Condition>(),
            });
            return 2;
        }

        private static int ReadInteger(byte[] expression)
        {
            if (expression == null || expression.Length != 6 || expression[0] != 0x82 || expression[5] != 0xA1)
                throw new InvalidDataException("Expected a normalized Int32 ESD expression.");
            return BitConverter.ToInt32(expression, 1);
        }

        private static void SetInteger(List<byte[]> arguments, int index, int value) => arguments[index] = Int32Expression(value);

        private static byte[] Int32Expression(int value)
        {
            byte[] result = new byte[6];
            result[0] = 0x82;
            BitConverter.GetBytes(value).CopyTo(result, 1);
            result[5] = 0xA1;
            return result;
        }

        internal static byte[] NormalizeExpression(byte[] expression)
        {
            if (expression == null || expression.Length == 0) return expression;
            Expr root = Parse(expression);
            return WriteExpression(root);
        }

        internal static string DescribeExpression(byte[] expression) => Describe(Parse(expression));

        private static int NormalizeCondition(ESD.Condition condition, Dictionary<int, Expr> registers, string esdName, long groupId, long stateId)
        {
            int rewritten = Normalize(ref condition.Evaluator, registers, esdName, groupId, stateId);
            rewritten += NormalizeCommands(condition.PassCommands, registers, esdName, groupId, stateId);
            foreach (ESD.Condition child in condition.Subconditions ?? new List<ESD.Condition>())
                rewritten += NormalizeCondition(child, registers, esdName, groupId, stateId);
            return rewritten;
        }

        private static int NormalizeCommands(List<ESD.CommandCall> commands, Dictionary<int, Expr> registers, string esdName, long groupId, long stateId)
        {
            int rewritten = 0;
            foreach (ESD.CommandCall command in commands ?? new List<ESD.CommandCall>())
            for (int i = 0; i < command.Arguments.Count; i++)
            {
                byte[] argument = command.Arguments[i];
                int changed = Normalize(ref argument, registers, esdName, groupId, stateId);
                if (changed != 0) command.Arguments[i] = argument;
                rewritten += changed;
            }
            return rewritten;
        }

        private static int Normalize(ref byte[] bytes, Dictionary<int, Expr> registers, string esdName, long groupId, long stateId)
        {
            if (bytes == null || bytes.Length == 0) return 0;
            Expr root = ResolveRegisters(Parse(bytes), registers, esdName, groupId, stateId);
            byte[] normalized = WriteExpression(root);
            if (bytes.SequenceEqual(normalized)) return 0;
            bytes = normalized;
            return 1;
        }

        private static Expr ResolveRegisters(Expr expr, Dictionary<int, Expr> registers, string esdName, long groupId, long stateId)
        {
            if (expr.Kind == ExprKind.RegisterSet)
            {
                Expr value = ResolveRegisters(expr.Left, registers, esdName, groupId, stateId);
                registers[expr.RegisterIndex] = Clone(value);
                return value;
            }
            if (expr.Kind == ExprKind.RegisterGet)
            {
                if (!registers.TryGetValue(expr.RegisterIndex, out Expr value))
                    throw new InvalidDataException($"ESD register {expr.RegisterIndex} is read before assignment in {esdName}, group {groupId}, state {stateId}.");
                return Clone(value);
            }

            if (expr.Left != null) expr.Left = ResolveRegisters(expr.Left, registers, esdName, groupId, stateId);
            if (expr.Right != null) expr.Right = ResolveRegisters(expr.Right, registers, esdName, groupId, stateId);
            for (int i = 0; i < expr.Arguments.Count; i++)
                expr.Arguments[i] = ResolveRegisters(expr.Arguments[i], registers, esdName, groupId, stateId);
            return expr;
        }

        private static Expr Clone(Expr expr) => new Expr
        {
            Kind = expr.Kind,
            IntegerValue = expr.IntegerValue,
            FunctionId = expr.FunctionId,
            OperatorText = expr.OperatorText,
            RegisterIndex = expr.RegisterIndex,
            ContinueIfFalse = expr.ContinueIfFalse,
            AbortIfFalse = expr.AbortIfFalse,
            Left = expr.Left == null ? null : Clone(expr.Left),
            Right = expr.Right == null ? null : Clone(expr.Right),
            Arguments = expr.Arguments.Select(Clone).ToList(),
            RawBytes = expr.RawBytes == null ? null : (byte[])expr.RawBytes.Clone(),
        };

        private static byte[] WriteExpression(Expr root)
        {
            using (var output = new MemoryStream())
            {
                Write(root, output, isCallId: false);
                output.WriteByte(0xA1);
                return output.ToArray();
            }
        }

        private static Expr Parse(byte[] bytes)
        {
            var stack = new Stack<Expr>();
            int index = 0;
            bool ended = false;
            while (index < bytes.Length)
            {
                byte token = bytes[index++];
                if (token == 0xA1)
                {
                    ended = true;
                    break;
                }
                if (token <= 0x7F)
                {
                    stack.Push(Expr.Integer(token - 64));
                }
                else if (token == 0x80)
                {
                    stack.Push(Expr.Raw(ExprKind.Float, bytes, index - 1, 5));
                    index = CheckedAdvance(index, 4, bytes.Length);
                }
                else if (token == 0x81)
                {
                    stack.Push(Expr.Raw(ExprKind.Double, bytes, index - 1, 9));
                    index = CheckedAdvance(index, 8, bytes.Length);
                }
                else if (token == 0x82)
                {
                    Ensure(bytes, index, 4);
                    int value = BitConverter.ToInt32(bytes, index);
                    stack.Push(Expr.Integer(value));
                    index += 4;
                }
                else if (token == 0xA5)
                {
                    int end = index;
                    while (end + 1 < bytes.Length && (bytes[end] != 0 || bytes[end + 1] != 0)) end += 2;
                    if (end + 1 >= bytes.Length) throw Invalid(bytes, index - 1, "unterminated UTF-16 string");
                    stack.Push(Expr.Raw(ExprKind.String, bytes, index - 1, end + 2 - (index - 1)));
                    index = end + 2;
                }
                else if (token >= 0x84 && token <= 0x8A)
                {
                    int argCount = token - 0x84;
                    var args = PopMany(stack, argCount, bytes, index - 1);
                    Expr idExpr = Pop(stack, bytes, index - 1);
                    if (idExpr.Kind != ExprKind.Integer) throw Invalid(bytes, index - 1, "call ID is not an integer literal");
                    stack.Push(Expr.Call(idExpr.IntegerValue, args));
                }
                else if (BinaryOperators.TryGetValue(token, out string binaryOperator))
                {
                    Expr right = Pop(stack, bytes, index - 1);
                    Expr left = Pop(stack, bytes, index - 1);
                    stack.Push(Expr.Operator(binaryOperator, left, right));
                }
                else if (token == 0x8D || token == 0x9A)
                {
                    Expr operand = Pop(stack, bytes, index - 1);
                    stack.Push(Expr.Operator(token == 0x8D ? "N" : "!", operand, null));
                }
                else if (token == 0xA6 || token == 0xB7)
                {
                    Expr top = Peek(stack, bytes, index - 1);
                    if (token == 0xA6) top.ContinueIfFalse = true;
                    else top.AbortIfFalse = true;
                }
                else if (token >= 0xA7 && token <= 0xAE)
                {
                    stack.Push(Expr.RegisterSet(token - 0xA7, Pop(stack, bytes, index - 1)));
                }
                else if (token >= 0xAF && token <= 0xB6)
                {
                    stack.Push(Expr.RegisterGet(token - 0xAF));
                }
                else if (token == 0xB8)
                {
                    stack.Push(Expr.StateGroup(Pop(stack, bytes, index - 1)));
                }
                else if (token == 0xB9 || token == 0xBA)
                {
                    stack.Push(Expr.Raw(token == 0xB9 ? ExprKind.CallResult : ExprKind.CallOngoing, new[] { token }, 0, 1));
                }
                else
                {
                    stack.Push(Expr.Raw(ExprKind.Unknown, bytes, index - 1, 1));
                }
            }
            if (!ended) throw Invalid(bytes, bytes.Length, "missing expression terminator 0xA1");
            if (index != bytes.Length) throw Invalid(bytes, index, "trailing bytes after expression terminator");
            if (stack.Count != 1) throw Invalid(bytes, index, $"expected one expression, found {stack.Count}");
            return stack.Pop();
        }

        private static void Write(Expr expr, Stream output, bool isCallId)
        {
            switch (expr.Kind)
            {
                case ExprKind.Integer:
                    if (isCallId && expr.IntegerValue >= -64 && expr.IntegerValue <= 63)
                        output.WriteByte((byte)(expr.IntegerValue + 64));
                    else
                    {
                        output.WriteByte(0x82);
                        WriteInt32(output, expr.IntegerValue);
                    }
                    break;
                case ExprKind.Float:
                case ExprKind.Double:
                case ExprKind.String:
                case ExprKind.Unknown:
                case ExprKind.CallResult:
                case ExprKind.CallOngoing:
                    output.Write(expr.RawBytes, 0, expr.RawBytes.Length);
                    break;
                case ExprKind.Call:
                    Write(Expr.Integer(expr.FunctionId), output, isCallId: true);
                    foreach (Expr arg in expr.Arguments) Write(arg, output, isCallId: false);
                    output.WriteByte((byte)(0x84 + expr.Arguments.Count));
                    break;
                case ExprKind.Unary:
                    Write(expr.Left, output, isCallId: false);
                    output.WriteByte(expr.OperatorText == "N" ? (byte)0x8D : (byte)0x9A);
                    break;
                case ExprKind.Binary:
                    Write(expr.Left, output, isCallId: false);
                    Write(expr.Right, output, isCallId: false);
                    output.WriteByte(BinaryOperators.Single(kv => kv.Value == expr.OperatorText).Key);
                    break;
                case ExprKind.RegisterSet:
                    Write(expr.Left, output, isCallId: false);
                    // The Fog Talk text round-trip strips temporary register stores;
                    // corresponding reads are rewritten to their source functions.
                    break;
                case ExprKind.RegisterGet:
                    output.WriteByte((byte)(0xAF + expr.RegisterIndex));
                    break;
                case ExprKind.StateGroup:
                    Write(expr.Left, output, isCallId: false);
                    output.WriteByte(0xB8);
                    break;
                default:
                    throw new InvalidDataException($"Unsupported ESD expression node {expr.Kind}.");
            }
            // The Fog Talk compiler input is the structured condition tree, so
            // both postfix false-path annotations are represented by tree flow
            // and are omitted from the emitted evaluator expression.
        }

        private static List<Expr> PopMany(Stack<Expr> stack, int count, byte[] bytes, int offset)
        {
            var args = new List<Expr>(count);
            for (int i = 0; i < count; i++) args.Add(Pop(stack, bytes, offset));
            args.Reverse();
            return args;
        }

        private static Expr Pop(Stack<Expr> stack, byte[] bytes, int offset)
        {
            if (stack.Count == 0) throw Invalid(bytes, offset, "expression stack underflow");
            return stack.Pop();
        }

        private static Expr Peek(Stack<Expr> stack, byte[] bytes, int offset)
        {
            if (stack.Count == 0) throw Invalid(bytes, offset, "expression stack underflow");
            return stack.Peek();
        }

        private static int CheckedAdvance(int current, int count, int length)
        {
            EnsureLength(current, count, length);
            return current + count;
        }

        private static void Ensure(byte[] bytes, int offset, int count) => EnsureLength(offset, count, bytes.Length);

        private static void EnsureLength(int offset, int count, int length)
        {
            if (offset < 0 || count < 0 || offset > length - count)
                throw new InvalidDataException("Truncated ESD evaluator bytecode.");
        }

        private static InvalidDataException Invalid(byte[] bytes, int offset, string reason) =>
            new InvalidDataException($"Invalid ESD bytecode at 0x{offset:X}: {reason}; data={BitConverter.ToString(bytes)}");

        private static void WriteInt32(Stream output, int value)
        {
            byte[] bytes = BitConverter.GetBytes(value);
            output.Write(bytes, 0, bytes.Length);
        }

        private static void WriteFunctionId(Stream output, int value)
        {
            if (value >= -64 && value <= 63) output.WriteByte((byte)(value + 64));
            else
            {
                output.WriteByte(0x82);
                WriteInt32(output, value);
            }
        }

        private static string Describe(Expr expr)
        {
            string text;
            switch (expr.Kind)
            {
                case ExprKind.Integer: text = expr.IntegerValue.ToString(System.Globalization.CultureInfo.InvariantCulture); break;
                case ExprKind.Call: text = $"f{expr.FunctionId}({string.Join(",", expr.Arguments.Select(Describe))})"; break;
                case ExprKind.Unary: text = $"{expr.OperatorText}({Describe(expr.Left)})"; break;
                case ExprKind.Binary: text = $"({Describe(expr.Left)} {expr.OperatorText} {Describe(expr.Right)})"; break;
                case ExprKind.RegisterSet: text = $"SetREG{expr.RegisterIndex}({Describe(expr.Left)})"; break;
                case ExprKind.RegisterGet: text = $"GetREG{expr.RegisterIndex}()"; break;
                case ExprKind.StateGroup: text = $"StateGroupArg({Describe(expr.Left)})"; break;
                case ExprKind.CallResult: text = "CallResult"; break;
                case ExprKind.CallOngoing: text = "CallOngoing"; break;
                case ExprKind.Float: text = $"float:{BitConverter.ToString(expr.RawBytes)}"; break;
                case ExprKind.Double: text = $"double:{BitConverter.ToString(expr.RawBytes)}"; break;
                case ExprKind.String: text = $"string:{BitConverter.ToString(expr.RawBytes)}"; break;
                default: text = $"unknown:{BitConverter.ToString(expr.RawBytes)}"; break;
            }
            if (expr.ContinueIfFalse) text += "~";
            if (expr.AbortIfFalse) text += ".";
            return text;
        }

        private enum ExprKind { Integer, Float, Double, String, Call, Unary, Binary, RegisterSet, RegisterGet, StateGroup, CallResult, CallOngoing, Unknown }

        private sealed class Expr
        {
            public ExprKind Kind;
            public int IntegerValue;
            public int FunctionId;
            public string OperatorText;
            public int RegisterIndex;
            public bool ContinueIfFalse;
            public bool AbortIfFalse;
            public Expr Left;
            public Expr Right;
            public List<Expr> Arguments = new List<Expr>();
            public byte[] RawBytes;

            public static Expr Integer(int value) => new Expr { Kind = ExprKind.Integer, IntegerValue = value };
            public static Expr Raw(ExprKind kind, byte[] bytes, int offset, int length) => new Expr { Kind = kind, RawBytes = bytes.Skip(offset).Take(length).ToArray() };
            public static Expr Call(int id, List<Expr> args) => new Expr { Kind = ExprKind.Call, FunctionId = id, Arguments = args };
            public static Expr Operator(string op, Expr left, Expr right) => new Expr { Kind = right == null ? ExprKind.Unary : ExprKind.Binary, OperatorText = op, Left = left, Right = right };
            public static Expr RegisterSet(int index, Expr value) => new Expr { Kind = ExprKind.RegisterSet, RegisterIndex = index, Left = value };
            public static Expr RegisterGet(int index) => new Expr { Kind = ExprKind.RegisterGet, RegisterIndex = index };
            public static Expr StateGroup(Expr arg) => new Expr { Kind = ExprKind.StateGroup, Left = arg };
        }
    }
}
