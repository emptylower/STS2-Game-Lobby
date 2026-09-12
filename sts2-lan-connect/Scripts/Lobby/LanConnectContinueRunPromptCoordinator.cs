using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Sts2LanConnect.Scripts;

internal sealed class LanConnectContinueRunPromptCoordinator
{
    private readonly object _sync = new();
    private readonly Dictionary<ulong, PromptLease> _leasesByScreen = new();
    private readonly HashSet<string> _promptingSaveKeys = new(StringComparer.Ordinal);

    public bool TryBegin(ulong screenId, string saveKey, out PromptLease lease)
    {
        lock (_sync)
        {
            if (_leasesByScreen.ContainsKey(screenId) || !_promptingSaveKeys.Add(saveKey))
            {
                lease = null!;
                return false;
            }

            lease = new PromptLease(screenId, saveKey);
            _leasesByScreen.Add(screenId, lease);
            return true;
        }
    }

    public async Task<PromptResolution> ResolveAsync(
        PromptLease lease,
        Func<CancellationToken, Task<string?>> prompt,
        Func<string, bool> persistChoice)
    {
        try
        {
            CancellationToken cancellationToken;
            lock (_sync)
            {
                if (!_leasesByScreen.TryGetValue(lease.ScreenId, out PromptLease? active)
                    || !ReferenceEquals(active, lease))
                {
                    return new PromptResolution(null, false);
                }

                cancellationToken = lease.Cancellation.Token;
            }

            string? choice = await prompt(cancellationToken);
            lock (_sync)
            {
                // A confirmed dialog can finish after its owner was closed. Never persist
                // that abandoned visit's choice or overwrite the newly opened prompt.
                if (string.IsNullOrWhiteSpace(choice)
                    || !_leasesByScreen.TryGetValue(lease.ScreenId, out PromptLease? active)
                    || !ReferenceEquals(active, lease))
                {
                    return new PromptResolution(null, false);
                }

                return new PromptResolution(choice, persistChoice(choice));
            }
        }
        finally
        {
            Release(lease);
        }
    }

    public void ClearScreen(ulong screenId)
    {
        PromptLease? lease;
        lock (_sync)
        {
            if (!_leasesByScreen.Remove(screenId, out lease))
            {
                return;
            }

            _promptingSaveKeys.Remove(lease.SaveKey);
        }

        lease.Cancellation.Cancel();
        lease.Cancellation.Dispose();
    }

    private void Release(PromptLease lease)
    {
        lock (_sync)
        {
            if (_leasesByScreen.TryGetValue(lease.ScreenId, out PromptLease? active)
                && ReferenceEquals(active, lease))
            {
                _leasesByScreen.Remove(lease.ScreenId);
                _promptingSaveKeys.Remove(lease.SaveKey);
                lease.Cancellation.Dispose();
            }
        }
    }

    internal sealed record PromptResolution(string? Choice, bool Persisted);

    internal sealed class PromptLease
    {
        public PromptLease(ulong screenId, string saveKey)
        {
            ScreenId = screenId;
            SaveKey = saveKey;
        }

        public ulong ScreenId { get; }

        public string SaveKey { get; }

        public CancellationTokenSource Cancellation { get; } = new();
    }
}
