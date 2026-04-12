using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;

namespace Sts2LanConnect.Scripts;

internal static class LanConnectTranspilerUtils
{
    private static readonly int LdcI4MinOpcodeValue = OpCodes.Ldc_I4_M1.Value;
    private static readonly int LdcI4MaxOpcodeValue = OpCodes.Ldc_I4_8.Value;
    private static readonly int LdcI4SOpcodeValue = OpCodes.Ldc_I4_S.Value;
    private static readonly int LdcI4OpcodeValue = OpCodes.Ldc_I4.Value;

    internal static IEnumerable<CodeInstruction> ReplaceBitWidthBeforeCall(
        IEnumerable<CodeInstruction> instructions,
        MethodInfo? targetMethod,
        int sourceBitWidth,
        int targetBitWidth,
        string patchName)
    {
        MethodInfo resolvedTargetMethod = targetMethod
            ?? throw new InvalidOperationException($"{patchName}: target method is null.");

        List<CodeInstruction> list = new(instructions);
        int count = 0;

        for (int i = 0; i < list.Count; i++)
        {
            if (!IsCallToMethod(list[i], resolvedTargetMethod))
            {
                continue;
            }

            int loadIndex = FindBitWidthLoadIndex(list, i, sourceBitWidth);
            if (loadIndex < 0)
            {
                continue;
            }

            list[loadIndex] = CloneWithNewIntOperand(list[loadIndex], targetBitWidth);
            count++;
        }

        if (count == 0)
        {
            Log.Warn($"sts2_lan_connect transpiler [{patchName}]: no bit-width operand replaced for method {resolvedTargetMethod.Name}");
        }
        else
        {
            Log.Info($"sts2_lan_connect transpiler [{patchName}]: replaced {count} bit-width operand(s) {sourceBitWidth} -> {targetBitWidth} for {resolvedTargetMethod.Name}");
        }

        return list;
    }

    internal static IEnumerable<CodeInstruction> ReplaceBitWidthBeforeCallWithProvider(
        IEnumerable<CodeInstruction> instructions,
        MethodInfo? targetMethod,
        int expectedBitWidth,
        MethodInfo? providerMethod,
        string patchName)
    {
        MethodInfo resolvedTargetMethod = targetMethod
            ?? throw new InvalidOperationException($"{patchName}: target method is null.");
        MethodInfo resolvedProviderMethod = providerMethod
            ?? throw new InvalidOperationException($"{patchName}: provider method is null.");

        List<CodeInstruction> list = new(instructions);
        int count = 0;

        for (int i = 0; i < list.Count; i++)
        {
            if (!IsCallToMethod(list[i], resolvedTargetMethod))
            {
                continue;
            }

            int loadIndex = FindBitWidthLoadIndex(list, i, expectedBitWidth);
            if (loadIndex < 0)
            {
                continue;
            }

            list[loadIndex] = CloneWithMethodCall(list[loadIndex], resolvedProviderMethod);
            count++;
        }

        if (count == 0)
        {
            Log.Warn($"sts2_lan_connect transpiler [{patchName}]: no bit-width operand replaced for method {resolvedTargetMethod.Name}");
        }
        else
        {
            Log.Info(
                $"sts2_lan_connect transpiler [{patchName}]: replaced {count} bit-width operand(s) {expectedBitWidth} -> dynamic {resolvedProviderMethod.Name} for {resolvedTargetMethod.Name}");
        }

        return list;
    }

    private static int FindBitWidthLoadIndex(IReadOnlyList<CodeInstruction> instructions, int callIndex, int expectedValue)
    {
        int searchStart = Math.Max(0, callIndex - 8);
        for (int i = callIndex - 1; i >= searchStart; i--)
        {
            if (instructions[i].opcode == OpCodes.Nop)
            {
                continue;
            }

            int? ldcI4Value = ReadLdcI4Nullable(instructions[i]);
            if (ldcI4Value.HasValue)
            {
                return ldcI4Value.Value == expectedValue ? i : -1;
            }

            if (IsTerminatingOpcode(instructions[i].opcode))
            {
                return -1;
            }
        }

        return -1;
    }

    private static bool IsTerminatingOpcode(OpCode opcode)
    {
        FlowControl flowControl = opcode.FlowControl;
        return flowControl is FlowControl.Branch or FlowControl.Cond_Branch
            or FlowControl.Return or FlowControl.Throw or FlowControl.Call;
    }

    private static CodeInstruction CloneWithNewIntOperand(CodeInstruction source, int newValue)
    {
        CodeInstruction result = new(OpCodes.Ldc_I4, newValue);
        result.labels.AddRange(source.labels);
        result.blocks.AddRange(source.blocks);
        return result;
    }

    private static CodeInstruction CloneWithMethodCall(CodeInstruction source, MethodInfo providerMethod)
    {
        CodeInstruction result = new(OpCodes.Call, providerMethod);
        result.labels.AddRange(source.labels);
        result.blocks.AddRange(source.blocks);
        return result;
    }

    internal static bool IsCallToMethod(CodeInstruction instruction, MethodInfo targetMethod)
    {
        if ((instruction.opcode != OpCodes.Call && instruction.opcode != OpCodes.Callvirt)
            || instruction.operand is not MethodInfo methodInfo)
        {
            return false;
        }

        if (methodInfo == targetMethod)
        {
            return true;
        }

        MethodInfo resolvedMethod = methodInfo.IsGenericMethod ? methodInfo.GetGenericMethodDefinition() : methodInfo;
        MethodInfo resolvedTarget = targetMethod.IsGenericMethod ? targetMethod.GetGenericMethodDefinition() : targetMethod;
        return resolvedMethod == resolvedTarget;
    }

    internal static int? ReadLdcI4Nullable(CodeInstruction instruction)
    {
        int opcodeValue = instruction.opcode.Value;
        return opcodeValue switch
        {
            _ when opcodeValue >= LdcI4MinOpcodeValue && opcodeValue <= LdcI4MaxOpcodeValue
                => opcodeValue - (LdcI4MinOpcodeValue + 1),
            _ when opcodeValue == LdcI4SOpcodeValue && instruction.operand is sbyte sb
                => sb,
            _ when opcodeValue == LdcI4OpcodeValue && instruction.operand is int num
                => num,
            _ => null
        };
    }
}
