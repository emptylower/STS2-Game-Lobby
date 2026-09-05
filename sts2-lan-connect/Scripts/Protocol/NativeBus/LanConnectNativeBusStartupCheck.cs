using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;

namespace Sts2LanConnect.Scripts;

/// <summary>
/// native_bus_v1 启动自检（spec §3.4）：注册表 ≤256 项且全表 id→byte 映射唯一，
/// 本类型 ID 不与 BaseLib 消息（128/129）冲突。异常 ⇒ 拒启用 native 载体并输出诊断
/// （明确报错，不崩溃）。
///
/// 时机（0.111.0 真机实测）：游戏在 OneTimeInitialization.ExecuteEssential()（主菜单显示前）
/// 才调用 MessageTypes.Initialize() 与 AssemblyInfo.Init()，而 mod 初始化器在它之前运行——
/// Entry 阶段两者必然未就绪；第三方 mod 也可能提前建好注册表，但 AssemblyInfo 仍未就绪
/// （注册表可用 ≠ AssemblyInfo 可用）。因此 Run() 在注册表或 AssemblyInfo 未初始化时返回
/// Pending（不缓存、不禁用），由 EnsureReadyOrThrow() 在首次 tail 绑定（两者必然已就绪）
/// 时补跑并缓存最终裁决。
/// </summary>
internal static class LanConnectNativeBusStartupCheck
{
    /// <summary>已知 BaseLib 生产消息 ID（v0.111.0 运行日志实证）。</summary>
    internal static readonly int[] KnownBaseLibMessageIds = [128, 129];

    private static readonly object Sync = new();
    private static Result? _cachedVerdict;

    internal sealed record Result(bool Ok, bool Pending, string? Reason, int? LocalTypeId, string? RegistryFingerprint)
    {
        public static Result OkResult(int localTypeId, string fingerprint) =>
            new(true, false, null, localTypeId, fingerprint);

        public static Result Fail(string reason) => new(false, false, reason, null, null);

        public static Result RegistryPending() =>
            new(false, true, "message registry not yet initialized (deferred to first tail session)", null, null);

        /// <summary>注册表已被其他 mod 提前建好，但 AssemblyInfo 仍未就绪：同样挂起，理由单独标明便于日志定位。</summary>
        public static Result AssemblyInfoPending() =>
            new(
                false,
                true,
                "AssemblyInfo not yet initialized while the message registry was pre-initialized by another mod (deferred to first tail session)",
                null,
                null);
    }

    /// <summary>注册表是否已初始化（未初始化时 TryGetMessageType 抛 InvalidOperationException）。</summary>
    private static bool IsRegistryAvailable()
    {
        try
        {
            _ = MessageTypes.TryGetMessageType(0, out _);
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>
    /// 首次 tail 绑定前的强制门禁：未裁决则补跑；终局失败 ⇒ 进入降级模式并抛结构化异常。
    /// 该方法只在用户发起建房/加入时触达（主菜单已显示，注册表必然已初始化）。
    /// </summary>
    internal static void EnsureReadyOrThrow()
    {
        lock (Sync)
        {
            if (_cachedVerdict == null || _cachedVerdict.Pending)
            {
                Result result = Run();
                if (!result.Pending)
                {
                    _cachedVerdict = result;
                }
                else
                {
                    // 用户已发起 tail 会话而注册表仍不可用：视为终局失败（Do not guess）。
                    _cachedVerdict = Result.Fail("message registry is unavailable at tail session start.");
                }
            }

            if (_cachedVerdict.Ok)
            {
                return;
            }

            string fingerprint = $"native_bus_self_check:{_cachedVerdict.Reason}";
            if (!LanConnectDegradedMode.IsActive)
            {
                LanConnectDegradedMode.Enter(LanConnectDegradedMode.ProtocolPatchConflictCode, fingerprint);
            }

            throw LanConnectProtocolFailureMapper.FromLocalException(
                LanConnectDegradedMode.ProtocolPatchConflictCode,
                _cachedVerdict.Reason);
        }
    }

    internal static Result CachedVerdict
    {
        get
        {
            lock (Sync)
            {
                return _cachedVerdict ?? Result.RegistryPending();
            }
        }
    }

    internal static void ResetForTesting()
    {
        lock (Sync)
        {
            _cachedVerdict = null;
        }
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
        if (!IsRegistryAvailable())
        {
            return Result.RegistryPending();
        }

        // 注册表可能被第三方 mod 提前建好，但 AssemblyInfo.Init() 要到 ExecuteEssential 才运行：
        // ModForType 届时会抛无消息的 InvalidOperationException——属未就绪而非补丁冲突，同样挂起。
        // 0.107.1 没有 AssemblyInfo 类型（适配器不可用，IsInitialized 恒 false）：同样挂起；
        // 0.107.1 没有 tail runtime，永远不会走到 EnsureReadyOrThrow，挂起是安全的。
        if (!LanConnectAssemblyInfoAdapter.IsInitialized)
        {
            return Result.AssemblyInfoPending();
        }

        try
        {
            // 不依赖 MessageTypes.Count（0.107.1 无该属性）：从 0 起枚举到首个空洞。
            List<int> ids = [];
            while (MessageTypes.TryGetMessageType(ids.Count, out Type? type) && type != null)
            {
                ids.Add(ids.Count);
            }

            if (ids.Count == 0)
            {
                // 空表指纹格式合法但内容错误，宁可挂起也不产出可 silently 毒化创建门禁的指纹。
                return Result.RegistryPending();
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

    /// <summary>输出 native_bus 诊断行（就绪：typeId + registry fingerprint；挂起/失败：明确原因）。</summary>
    internal static void LogDiagnostics(Result result, string patchStackOrder)
    {
        if (result.Ok)
        {
            Log.Info(
                $"sts2_lan_connect native_bus: ready local_type_id={result.LocalTypeId} " +
                $"registry_fingerprint={result.RegistryFingerprint} " +
                $"baselib_conflict=false patch_stack={patchStackOrder}");
        }
        else if (result.Pending)
        {
            Log.Info(
                $"sts2_lan_connect native_bus: pending reason=\"{result.Reason}\" " +
                "self-check defers to the first tail session.");
        }
        else
        {
            Log.Error(
                $"sts2_lan_connect native_bus: DISABLED reason=\"{result.Reason}\" " +
                "tail rooms cannot be hosted/joined until the mod set is consistent.");
        }
    }
}
