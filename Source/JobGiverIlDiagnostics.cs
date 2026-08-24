using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;

namespace BetterRimAI
{
    /// <summary>
    /// One-shot startup diagnostic. Dumps readable IL for the two RimWorld 1.6 JobGiver_Work methods
    /// we need to understand. It does not patch or execute on the game tick path.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class JobGiverIlDiagnostics
    {
        static JobGiverIlDiagnostics()
        {
            try
            {
                Dump(AccessTools.DeclaredMethod(typeof(JobGiver_Work), "TryIssueJobPackage"));
                Dump(AccessTools.DeclaredMethod(typeof(JobGiver_Work), "GiverTryGiveJobPrioritized"));
            }
            catch (Exception ex)
            {
                Log.Error("[BetterRimAI][IL] dump failed: " + ex);
            }
        }

        private static void Dump(MethodBase method)
        {
            if (method == null)
            {
                Log.Error("[BetterRimAI][IL] method not found.");
                return;
            }

            MethodBody body = method.GetMethodBody();
            byte[] il = body?.GetILAsByteArray();
            if (il == null)
            {
                Log.Warning($"[BetterRimAI][IL] {Describe(method)} has no IL body.");
                return;
            }

            Module module = method.Module;
            Type[] typeArgs = method.DeclaringType?.GetGenericArguments();
            Type[] methodArgs = method.IsGenericMethod ? method.GetGenericArguments() : null;
            List<string> lines = new List<string>();
            int p = 0;

            while (p < il.Length)
            {
                int offset = p;
                OpCode op = ReadOpCode(il, ref p);
                object operand = ReadOperand(op, il, ref p, module, typeArgs, methodArgs);
                lines.Add($"IL_{offset:X4}: {op.Name}{(operand == null ? string.Empty : " " + operand)}");
            }

            Log.Message($"[BetterRimAI][IL-BEGIN] {Describe(method)}\n" +
                        string.Join("\n", lines) +
                        $"\n[BetterRimAI][IL-END] {Describe(method)}");
        }

        private static OpCode ReadOpCode(byte[] il, ref int p)
        {
            byte first = il[p++];
            if (first != 0xFE)
                return SingleByteOpCodes[first];
            return MultiByteOpCodes[il[p++]];
        }

        private static object ReadOperand(OpCode op, byte[] il, ref int p, Module module, Type[] typeArgs, Type[] methodArgs)
        {
            try
            {
                switch (op.OperandType)
                {
                    case OperandType.InlineNone:
                        return null;
                    case OperandType.ShortInlineI:
                        return (sbyte)il[p++];
                    case OperandType.InlineI:
                        return ReadInt32(il, ref p);
                    case OperandType.InlineI8:
                        long l = BitConverter.ToInt64(il, p); p += 8; return l;
                    case OperandType.ShortInlineR:
                        float f = BitConverter.ToSingle(il, p); p += 4; return f;
                    case OperandType.InlineR:
                        double d = BitConverter.ToDouble(il, p); p += 8; return d;
                    case OperandType.ShortInlineVar:
                        return "V_" + il[p++];
                    case OperandType.InlineVar:
                        ushort us = BitConverter.ToUInt16(il, p); p += 2; return "V_" + us;
                    case OperandType.ShortInlineBrTarget:
                        sbyte delta8 = (sbyte)il[p++]; return $"IL_{p + delta8:X4}";
                    case OperandType.InlineBrTarget:
                        int delta32 = ReadInt32(il, ref p); return $"IL_{p + delta32:X4}";
                    case OperandType.InlineSwitch:
                        int count = ReadInt32(il, ref p);
                        int basePos = p + count * 4;
                        string[] targets = new string[count];
                        for (int i = 0; i < count; i++) targets[i] = $"IL_{basePos + ReadInt32(il, ref p):X4}";
                        return "(" + string.Join(", ", targets) + ")";
                    case OperandType.InlineString:
                        return "\"" + module.ResolveString(ReadInt32(il, ref p)) + "\"";
                    case OperandType.InlineMethod:
                        return Describe(module.ResolveMethod(ReadInt32(il, ref p), typeArgs, methodArgs));
                    case OperandType.InlineField:
                        return Describe(module.ResolveField(ReadInt32(il, ref p), typeArgs, methodArgs));
                    case OperandType.InlineType:
                        return module.ResolveType(ReadInt32(il, ref p), typeArgs, methodArgs)?.FullName;
                    case OperandType.InlineTok:
                        return Describe(module.ResolveMember(ReadInt32(il, ref p), typeArgs, methodArgs));
                    case OperandType.InlineSig:
                        return "sig:0x" + ReadInt32(il, ref p).ToString("X8");
                    default:
                        return "<operand " + op.OperandType + ">";
                }
            }
            catch (Exception ex)
            {
                return "<resolve failed: " + ex.GetType().Name + ">";
            }
        }

        private static int ReadInt32(byte[] il, ref int p)
        {
            int value = BitConverter.ToInt32(il, p);
            p += 4;
            return value;
        }

        private static string Describe(MemberInfo member)
        {
            if (member == null) return "null";
            if (member is MethodBase mb)
            {
                string args = string.Join(",", mb.GetParameters().Select(x => x.ParameterType.Name));
                return $"{mb.DeclaringType?.FullName}.{mb.Name}({args})";
            }
            return $"{member.DeclaringType?.FullName}.{member.Name}";
        }

        private static readonly OpCode[] SingleByteOpCodes = BuildSingle();
        private static readonly OpCode[] MultiByteOpCodes = BuildMulti();

        private static OpCode[] BuildSingle()
        {
            OpCode[] result = new OpCode[256];
            foreach (FieldInfo field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (!(field.GetValue(null) is OpCode op)) continue;
                ushort value = unchecked((ushort)op.Value);
                if (value < 0x100) result[value] = op;
            }
            return result;
        }

        private static OpCode[] BuildMulti()
        {
            OpCode[] result = new OpCode[256];
            foreach (FieldInfo field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (!(field.GetValue(null) is OpCode op)) continue;
                ushort value = unchecked((ushort)op.Value);
                if ((value & 0xFF00) == 0xFE00) result[value & 0xFF] = op;
            }
            return result;
        }
    }
}
