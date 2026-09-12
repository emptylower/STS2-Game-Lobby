using System;
using System.Collections.Generic;

namespace Sts2LanConnect.Scripts;

/// <summary>
/// 一次续局恢复尝试的令牌。仅由 <see cref="LanConnectContinueRunPublishScreenState"/> 创建，
/// 用引用相等标识 in-flight 尝试，用 <see cref="Generation"/> 识别属于哪一次进入载入页。
/// </summary>
internal sealed class LanConnectContinueRunPublishAttempt
{
    internal LanConnectContinueRunPublishAttempt(ulong screenId, long generation)
    {
        ScreenId = screenId;
        Generation = generation;
    }

    public ulong ScreenId { get; }

    public long Generation { get; }
}

/// <summary>
/// 续局自动恢复的每屏节流/去重状态。
///
/// 关键约束：STS2 的 <c>NMainMenuSubmenuStack</c> 把多人载入页缓存成常驻子节点，
/// 只切换 <c>Visible</c>，节点不会离开场景树。因此“已完成”不能是节点生命周期级别的永久闩锁，
/// 否则任何一次终态（取消选择联机方式、发布成功、缺少大厅地址）都会让本次游戏进程内
/// 再也无法恢复房间。这里把终态收敛到“本次进入载入页”，关闭载入页即释放。
/// </summary>
internal sealed class LanConnectContinueRunPublishScreenState
{
    private readonly object _sync = new();
    private readonly Dictionary<ulong, ScreenEntry> _entries = new();
    private readonly TimeSpan _retryInterval;

    public LanConnectContinueRunPublishScreenState(TimeSpan retryInterval)
    {
        _retryInterval = retryInterval;
    }

    public bool TryBeginAttempt(ulong screenId, DateTimeOffset nowUtc, out LanConnectContinueRunPublishAttempt attempt)
    {
        lock (_sync)
        {
            if (!_entries.TryGetValue(screenId, out ScreenEntry? entry))
            {
                entry = new ScreenEntry();
                _entries.Add(screenId, entry);
            }

            if (entry.InFlight != null || entry.Settled)
            {
                attempt = null!;
                return false;
            }

            if (entry.LastAttemptAt is DateTimeOffset lastAttempt && nowUtc - lastAttempt < _retryInterval)
            {
                attempt = null!;
                return false;
            }

            attempt = new LanConnectContinueRunPublishAttempt(screenId, entry.Generation);
            entry.InFlight = attempt;
            entry.LastAttemptAt = nowUtc;
            return true;
        }
    }

    /// <summary>结束一次尝试；无论成功、失败还是抛异常都必须调用。</summary>
    public void EndAttempt(LanConnectContinueRunPublishAttempt attempt)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        lock (_sync)
        {
            if (_entries.TryGetValue(attempt.ScreenId, out ScreenEntry? entry)
                && ReferenceEquals(entry.InFlight, attempt))
            {
                entry.InFlight = null;
            }
        }
    }

    /// <summary>本次进入载入页已得到最终结果（已发布、用户选择仅 LAN、非房主等），不再重复尝试。</summary>
    public void MarkSettled(LanConnectContinueRunPublishAttempt attempt)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        lock (_sync)
        {
            if (_entries.TryGetValue(attempt.ScreenId, out ScreenEntry? entry)
                && entry.Generation == attempt.Generation)
            {
                entry.Settled = true;
            }
        }
    }

    /// <summary>本次尝试没有结论（取消选择、写入失败、发布失败），保持可重试。</summary>
    public void MarkRetryable(LanConnectContinueRunPublishAttempt attempt)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        lock (_sync)
        {
            if (_entries.TryGetValue(attempt.ScreenId, out ScreenEntry? entry)
                && entry.Generation == attempt.Generation)
            {
                entry.Settled = false;
            }
        }
    }

    /// <summary>
    /// 这次尝试所属的那次“进入载入页”是否已经结束。用于判断尝试结束时是否需要为
    /// 当前这次新的进入补发一次恢复尝试。
    /// </summary>
    public bool IsAttemptStale(LanConnectContinueRunPublishAttempt attempt)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        lock (_sync)
        {
            return !_entries.TryGetValue(attempt.ScreenId, out ScreenEntry? entry)
                || entry.Generation != attempt.Generation;
        }
    }

    /// <summary>载入页隐藏（Pop）时调用：开启新一代，释放终态与节流，让下次进入重新评估。</summary>
    public void NotifyScreenClosed(ulong screenId)
    {
        lock (_sync)
        {
            if (!_entries.TryGetValue(screenId, out ScreenEntry? entry))
            {
                return;
            }

            entry.Generation++;
            entry.Settled = false;
            entry.LastAttemptAt = null;
        }
    }

    /// <summary>节点真正离开场景树时调用。</summary>
    public void ForgetScreen(ulong screenId)
    {
        lock (_sync)
        {
            _entries.Remove(screenId);
        }
    }

    private sealed class ScreenEntry
    {
        public LanConnectContinueRunPublishAttempt? InFlight { get; set; }

        public bool Settled { get; set; }

        public DateTimeOffset? LastAttemptAt { get; set; }

        public long Generation { get; set; }
    }
}
