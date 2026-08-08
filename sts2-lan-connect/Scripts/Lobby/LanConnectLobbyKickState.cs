using System;
using System.Collections.Generic;

namespace Sts2LanConnect.Scripts;

internal readonly record struct LanConnectLobbyKickTarget(
    string PlayerNetId,
    string? BindingId,
    string OccupantName,
    long ConnectionGeneration)
{
    internal string Fingerprint =>
        $"{PlayerNetId}\n{BindingId ?? "<legacy>"}\n{OccupantName}\n{ConnectionGeneration}";
}

internal readonly record struct LanConnectLobbyKickResult(
    bool Accepted,
    string Reason,
    string Message)
{
    internal bool ShouldScheduleDisconnect => Accepted;

    internal static LanConnectLobbyKickResult AcceptedByLegacyService() =>
        new(true, "legacy_service", string.Empty);

    internal static LanConnectLobbyKickResult FromResponse(
        LanConnectLobbyKickTarget target,
        bool accepted,
        string? playerNetId,
        string? bindingId,
        string? reason,
        string? message)
    {
        if (!accepted)
        {
            return new(
                false,
                string.IsNullOrWhiteSpace(reason) ? "rejected" : reason.Trim(),
                string.IsNullOrWhiteSpace(message)
                    ? "目标玩家已变化，请刷新列表后重试。"
                    : message.Trim());
        }

        bool slotMatches = string.Equals(playerNetId?.Trim(), target.PlayerNetId, StringComparison.Ordinal);
        bool bindingMatches = target.BindingId == null ||
            string.Equals(bindingId?.Trim(), target.BindingId, StringComparison.Ordinal);
        return slotMatches && bindingMatches
            ? new(true, "accepted", string.Empty)
            : new(false, "mismatched_response", "移出请求的确认信息不匹配，请刷新列表后重试。");
    }
}

internal sealed class LanConnectLobbyKickTargetDirectory
{
    internal const int Capacity = 256;

    private readonly object _sync = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private long _nextConnectionGeneration;
    private long _nextSequence;

    internal int Count
    {
        get
        {
            lock (_sync)
            {
                return _entries.Count;
            }
        }
    }

    internal bool ObserveConnected(string playerNetId)
    {
        string normalizedNetId = playerNetId.Trim();
        lock (_sync)
        {
            if (!_entries.TryGetValue(normalizedNetId, out Entry? entry))
            {
                if (!MakeRoomFor(normalizedNetId))
                {
                    return false;
                }
                entry = new Entry { Sequence = ++_nextSequence };
                _entries.Add(normalizedNetId, entry);
            }

            entry.Connected = true;
            entry.ConnectionGeneration = ++_nextConnectionGeneration;
            return true;
        }
    }

    internal void ObserveDisconnected(string playerNetId)
    {
        lock (_sync)
        {
            _entries.Remove(playerNetId.Trim());
        }
    }

    internal bool RememberBinding(string playerNetId, string bindingId)
    {
        string normalizedNetId = playerNetId.Trim();
        string normalizedBindingId = bindingId.Trim();
        lock (_sync)
        {
            if (!_entries.TryGetValue(normalizedNetId, out Entry? entry))
            {
                if (!MakeRoomFor(normalizedNetId))
                {
                    return false;
                }
                entry = new Entry { Sequence = ++_nextSequence };
                _entries.Add(normalizedNetId, entry);
            }

            entry.BindingId = normalizedBindingId;
            return true;
        }
    }

    internal LanConnectLobbyKickTarget Capture(
        string playerNetId,
        string occupantName)
    {
        string normalizedNetId = playerNetId.Trim();
        lock (_sync)
        {
            _entries.TryGetValue(normalizedNetId, out Entry? entry);
            return new LanConnectLobbyKickTarget(
                normalizedNetId,
                entry?.BindingId,
                occupantName,
                entry?.ConnectionGeneration ?? 0);
        }
    }

    internal bool IsCurrent(LanConnectLobbyKickTarget target)
    {
        lock (_sync)
        {
            return IsCurrentUnsafe(target);
        }
    }

    internal bool TryRunIfCurrent(LanConnectLobbyKickTarget target, Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        lock (_sync)
        {
            if (!IsCurrentUnsafe(target))
            {
                return false;
            }

            action();
            return true;
        }
    }

    private bool IsCurrentUnsafe(LanConnectLobbyKickTarget target) =>
        _entries.TryGetValue(target.PlayerNetId, out Entry? entry)
        && entry.Connected
        && entry.ConnectionGeneration == target.ConnectionGeneration
        && string.Equals(entry.BindingId, target.BindingId, StringComparison.Ordinal);

    private bool MakeRoomFor(string incomingPlayerNetId)
    {
        if (_entries.ContainsKey(incomingPlayerNetId) || _entries.Count < Capacity)
        {
            return true;
        }

        string? obsoleteNetId = null;
        long oldestSequence = long.MaxValue;
        foreach ((string playerNetId, Entry entry) in _entries)
        {
            if (!entry.Connected && entry.Sequence < oldestSequence)
            {
                obsoleteNetId = playerNetId;
                oldestSequence = entry.Sequence;
            }
        }

        return obsoleteNetId != null && _entries.Remove(obsoleteNetId);
    }

    private sealed class Entry
    {
        public string? BindingId { get; set; }

        public long ConnectionGeneration { get; set; }

        public long Sequence { get; init; }

        public bool Connected { get; set; }
    }
}
