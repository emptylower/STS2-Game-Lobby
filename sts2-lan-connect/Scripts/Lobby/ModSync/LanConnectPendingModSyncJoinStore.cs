using System.Text.Json;
using Godot;

namespace Sts2LanConnect.Scripts;

internal sealed class LanConnectPendingModSyncJoinStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly object _sync = new();
    private readonly string _path;
    private readonly Func<DateTimeOffset> _utcNow;

    public LanConnectPendingModSyncJoinStore(string path, Func<DateTimeOffset>? utcNow = null)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Pending join path is required.", nameof(path));
        }
        _path = Path.GetFullPath(path);
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
    }

    public static LanConnectPendingModSyncJoinStore CreateDefault() => new(
        ProjectSettings.GlobalizePath("user://sts2_lan_connect/pending-mod-sync-join.json"));

    public LanConnectPendingModSyncJoin Save(
        string serverBaseUrl,
        string roomId,
        string roomName,
        string? desiredSavePlayerNetId)
    {
        DateTimeOffset now = _utcNow();
        LanConnectPendingModSyncJoin pending = new()
        {
            ServerBaseUrl = NormalizeServer(serverBaseUrl),
            RoomId = RequireValue(roomId, nameof(roomId)),
            RoomName = RequireValue(roomName, nameof(roomName)),
            DesiredSavePlayerNetId = NormalizeOptional(desiredSavePlayerNetId),
            CreatedAtUtc = now,
            ExpiresAtUtc = now.Add(LanConnectPendingModSyncJoin.TimeToLive)
        };
        Save(pending);
        return pending;
    }

    public void Save(LanConnectPendingModSyncJoin pending)
    {
        ArgumentNullException.ThrowIfNull(pending);
        Validate(pending);
        lock (_sync)
        {
            string? directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }
            string temporaryPath = _path + ".tmp";
            try
            {
                File.WriteAllText(temporaryPath, JsonSerializer.Serialize(pending, JsonOptions));
                File.Move(temporaryPath, _path, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }
    }

    public LanConnectPendingModSyncJoin? Load()
    {
        lock (_sync)
        {
            if (!File.Exists(_path))
            {
                return null;
            }
            try
            {
                LanConnectPendingModSyncJoin? pending = JsonSerializer.Deserialize<LanConnectPendingModSyncJoin>(
                    File.ReadAllText(_path),
                    JsonOptions);
                if (pending == null ||
                    pending.Version != LanConnectPendingModSyncJoin.CurrentVersion ||
                    pending.ExpiresAtUtc <= _utcNow())
                {
                    ClearUnsafe();
                    return null;
                }
                Validate(pending);
                return pending;
            }
            catch (Exception exception) when (exception is JsonException or IOException or ArgumentException)
            {
                ClearUnsafe();
                return null;
            }
        }
    }

    public bool TryClear(LanConnectPendingModSyncJoin expected)
    {
        ArgumentNullException.ThrowIfNull(expected);
        lock (_sync)
        {
            LanConnectPendingModSyncJoin? current = Load();
            if (current == null || !SameGeneration(current, expected))
            {
                return false;
            }
            ClearUnsafe();
            return true;
        }
    }

    public bool ClearIfServerChanged(string serverBaseUrl)
    {
        string normalized = NormalizeServer(serverBaseUrl);
        lock (_sync)
        {
            LanConnectPendingModSyncJoin? current = Load();
            if (current == null || string.Equals(current.ServerBaseUrl, normalized, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            ClearUnsafe();
            return true;
        }
    }

    public void Clear()
    {
        lock (_sync)
        {
            ClearUnsafe();
        }
    }

    private void ClearUnsafe()
    {
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
        string temporaryPath = _path + ".tmp";
        if (File.Exists(temporaryPath))
        {
            File.Delete(temporaryPath);
        }
    }

    private static bool SameGeneration(
        LanConnectPendingModSyncJoin left,
        LanConnectPendingModSyncJoin right) =>
        left.Version == right.Version &&
        string.Equals(left.ServerBaseUrl, right.ServerBaseUrl, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(left.RoomId, right.RoomId, StringComparison.Ordinal) &&
        left.CreatedAtUtc == right.CreatedAtUtc &&
        left.ExpiresAtUtc == right.ExpiresAtUtc;

    private static void Validate(LanConnectPendingModSyncJoin pending)
    {
        if (pending.Version != LanConnectPendingModSyncJoin.CurrentVersion ||
            string.IsNullOrWhiteSpace(pending.RoomId) ||
            string.IsNullOrWhiteSpace(pending.RoomName) ||
            pending.CreatedAtUtc == default ||
            pending.ExpiresAtUtc <= pending.CreatedAtUtc)
        {
            throw new ArgumentException("Pending MOD sync join is invalid.", nameof(pending));
        }
        NormalizeServer(pending.ServerBaseUrl);
    }

    private static string NormalizeServer(string value) =>
        LanConnectLobbyServerAddress.NormalizeUri(value, nameof(value)).AbsoluteUri;

    private static string RequireValue(string value, string parameterName) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Value is required.", parameterName)
            : value.Trim();

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
