using System.Text.Json;
using Sts2LanConnect.Scripts;

namespace Sts2LanConnect.Tests.Lobby.ModSync;

public sealed class LanConnectPendingModSyncJoinStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"lan-connect-pending-{Guid.NewGuid():N}");
    private readonly DateTimeOffset _now = new(2026, 7, 17, 9, 30, 0, TimeSpan.Zero);

    [Fact]
    public void Pending_store_writes_atomically_without_password_or_tokens()
    {
        Directory.CreateDirectory(_directory);
        string path = Path.Combine(_directory, "pending-mod-sync-join.json");
        LanConnectPendingModSyncJoinStore store = new(path, () => _now);

        LanConnectPendingModSyncJoin saved = store.Save(
            "https://lobby.example/path?ignored=1",
            "room-1",
            "Public room",
            "player-slot-2");

        string json = File.ReadAllText(path);
        using JsonDocument document = JsonDocument.Parse(json);
        Assert.Equal(1, document.RootElement.GetProperty("version").GetInt32());
        Assert.Equal("https://lobby.example/", document.RootElement.GetProperty("serverBaseUrl").GetString());
        Assert.Equal("room-1", document.RootElement.GetProperty("roomId").GetString());
        Assert.DoesNotContain("password", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("token", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Directory.EnumerateFiles(_directory), candidate => candidate.EndsWith(".tmp", StringComparison.Ordinal));
        Assert.Equal(_now.AddMinutes(15), saved.ExpiresAtUtc);
    }

    [Fact]
    public void Pending_store_clears_expired_and_unknown_versions()
    {
        Directory.CreateDirectory(_directory);
        string path = Path.Combine(_directory, "pending-mod-sync-join.json");
        DateTimeOffset current = _now;
        LanConnectPendingModSyncJoinStore store = new(path, () => current);
        store.Save("https://lobby.example", "room-1", "Room", null);

        current = _now.AddMinutes(16);
        Assert.Null(store.Load());
        Assert.False(File.Exists(path));

        File.WriteAllText(path, "{\"version\":999,\"serverBaseUrl\":\"https://lobby.example/\",\"roomId\":\"room-1\",\"roomName\":\"Room\",\"createdAtUtc\":\"2026-07-17T09:30:00Z\",\"expiresAtUtc\":\"2026-07-17T09:45:00Z\"}");
        current = _now;
        Assert.Null(store.Load());
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void Pending_store_only_clears_the_generation_that_was_resumed()
    {
        string path = Path.Combine(_directory, "pending-mod-sync-join.json");
        LanConnectPendingModSyncJoinStore store = new(path, () => _now);
        LanConnectPendingModSyncJoin original = store.Save("https://one.example", "room-1", "One", null);
        LanConnectPendingModSyncJoin replacement = original with
        {
            ServerBaseUrl = "https://two.example/",
            RoomId = "room-2",
            CreatedAtUtc = original.CreatedAtUtc.AddSeconds(1),
            ExpiresAtUtc = original.ExpiresAtUtc.AddSeconds(1)
        };
        store.Save(replacement);

        Assert.False(store.TryClear(original));
        Assert.Equal("room-2", store.Load()?.RoomId);
        Assert.True(store.TryClear(replacement));
        Assert.Null(store.Load());
    }

    [Fact]
    public void Pending_store_clears_when_user_changes_server()
    {
        string path = Path.Combine(_directory, "pending-mod-sync-join.json");
        LanConnectPendingModSyncJoinStore store = new(path, () => _now);
        store.Save("https://one.example", "room-1", "One", null);

        Assert.True(store.ClearIfServerChanged("https://two.example"));
        Assert.Null(store.Load());
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
