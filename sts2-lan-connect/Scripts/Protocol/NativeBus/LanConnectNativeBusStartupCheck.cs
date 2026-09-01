using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;

namespace Sts2LanConnect.Scripts;

/// <summary>
/// native_bus_v1 启动自检（spec §3.4）：MessageTypes.Count ≤ 256 且全表 id→byte 映射唯一，
/// 本类型 ID 不与 BaseLib 消息（128/129）冲突。异常 ⇒ 拒启用 native 载体并输出诊断
/// （明确报错，不崩溃）。
/// </summary>
internal static class LanConnectNativeBusStartupCheck
{
    /// <summary>已知 BaseLib 生产消息 ID（v0.111.0 运行日志实证）。</summary>
    internal static readonly int[] KnownBaseLibMessageIds = [128, 129];

    internal sealed record Result(bool Ok, string? Reason, int? LocalTypeId, string? RegistryFingerprint)
    {
        public static Result OkResult(int localTypeId, string fingerprint) =>
            new(true, null, localTypeId, fingerprint);

        public static Result Fail(string reason) => new(false, reason, null, null);
    }

    /// <summary>纯函数部分：注册表规模与 byte 映射唯一性。</summary>
    internal static string? ValidateTable(int count, IReadOnlyList<int> ids)
    {
        if (count > 256)
        {
            return $"MessageTypes table size {count} exceeds 256; vanilla WriteByte((byte)id) would alias.";
        }

        HashSet<byte> seen = [];
        foreach (int id in ids)
        {
            if (!seen.Add(checked((byte)id)))
            {
                return $"Message id {id} aliases byte {(byte)id} with another registry entry.";
            }
        }

        return null;
    }

    internal static Result Run()
    {
        try
        {
            // 不依赖 MessageTypes.Count（0.107.1 无该属性）：从 0 起枚举到首个空洞。
            List<int> ids = [];
            while (MessageTypes.TryGetMessageType(ids.Count, out Type? type) && type != null)
            {
                ids.Add(ids.Count);
            }

            string? tableError = ValidateTable(ids.Count, ids);
            if (tableError != null)
            {
                return Result.Fail(tableError);
            }

            int localTypeId = LanConnectNativeBusSender.ResolveTypeId();
            if (KnownBaseLibMessageIds.Contains(localTypeId))
            {
                return Result.Fail($"native bus message id {localTypeId} collides with a known BaseLib message id.");
            }

            if (localTypeId > 255)
            {
                return Result.Fail($"native bus message id {localTypeId} does not fit the vanilla wire byte.");
            }

            string fingerprint = LanConnectRegistryFingerprint.Compute();
            return Result.OkResult(localTypeId, fingerprint);
        }
        catch (Exception exception)
        {
            return Result.Fail($"{exception.GetType().Name}: {exception.Message}");
        }
    }

    /// <summary>输出 native_bus 就绪诊断行（ typeId、registry fingerprint、BaseLib 冲突状态）。</summary>
    internal static void LogDiagnostics(Result result, string patchStackOrder)
    {
        if (result.Ok)
        {
            Log.Info(
                $"sts2_lan_connect native_bus: ready local_type_id={result.LocalTypeId} " +
                $"registry_fingerprint={result.RegistryFingerprint} " +
                $"baselib_conflict=false patch_stack={patchStackOrder}");
        }
        else
        {
            Log.Error(
                $"sts2_lan_connect native_bus: DISABLED reason=\"{result.Reason}\" " +
                "tail rooms cannot be hosted/joined until the mod set is consistent.");
        }
    }
}
