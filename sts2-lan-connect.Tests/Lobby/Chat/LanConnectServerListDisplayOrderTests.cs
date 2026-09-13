using Sts2LanConnect.Scripts;

namespace Sts2LanConnect.Tests.Lobby.Chat;

public sealed class LanConnectServerListDisplayOrderTests
{
    private static ServerListEntry Entry(
        string address,
        ServerVersionInfo? version = null,
        ServerProbeState probeState = ServerProbeState.Pending,
        int? pingMs = null,
        bool pinned = false) => new()
    {
        Address = address,
        Version = version ?? ServerVersionInfo.Unknown,
        ProbeState = probeState,
        PingMs = pingMs,
        IsPinned = pinned,
    };

    [Fact]
    public void No_resort_keeps_previous_order_appends_new_entries_and_drops_gone_entries()
    {
        List<ServerListEntry> current =
        [
            Entry("https://c.example", ServerVersionInfo.Unknown, ServerProbeState.Unreachable),
            Entry("https://a.example", new ServerVersionInfo(ServerVersionSource.Reported, 0, 6, "0.6.1"), ServerProbeState.Reachable, 42),
            Entry("https://new-2.example", new ServerVersionInfo(ServerVersionSource.Reported, 0, 7, "0.7.0"), ServerProbeState.Reachable, 5),
            Entry("https://new-1.example", new ServerVersionInfo(ServerVersionSource.Reported, 0, 7, "0.7.0"), ServerProbeState.Reachable, 9),
        ];

        List<string> previousOrder =
        [
            "https://gone.example",
            "https://c.example",
            "https://a.example",
        ];

        List<string> applied = LanConnectServerListDisplayOrder.Apply(previousOrder, current, resort: false)
            .Select(entry => entry.Address)
            .ToList();

        // Previous survivors keep relative order (c before a regardless of
        // rank); new entries are §3-ranked and appended at the end (new-2 has
        // the lower ping within the 0.7 tier); the gone entry is dropped.
        Assert.Equal(
        [
            "https://c.example",
            "https://a.example",
            "https://new-2.example",
            "https://new-1.example",
        ], applied);
    }

    [Fact]
    public void No_resort_matches_previous_order_with_trailing_slash_and_case_differences()
    {
        List<ServerListEntry> current =
        [
            Entry("http://A.example/"),
            Entry("http://b.example"),
        ];
        List<string> previousOrder = ["http://a.example"];

        List<string> applied = LanConnectServerListDisplayOrder.Apply(previousOrder, current, resort: false)
            .Select(entry => entry.Address)
            .ToList();

        Assert.Equal(["http://A.example/", "http://b.example"], applied);
    }

    [Fact]
    public void Consecutive_no_resort_renders_stay_stable_when_entry_data_flips()
    {
        List<ServerListEntry> entries =
        [
            Entry("https://anchor.example", new ServerVersionInfo(ServerVersionSource.Reported, 0, 6, "0.6.1"), ServerProbeState.Reachable, 30),
        ];
        List<string> displayOrder = LanConnectServerListDisplayOrder.Apply([], entries, resort: true)
            .Select(entry => LanConnectServerListBootstrap.NormalizeAddress(entry.Address))
            .ToList();

        entries.AddRange(
        [
            Entry("https://b.example", new ServerVersionInfo(ServerVersionSource.Reported, 0, 5, "0.5.1"), ServerProbeState.Reachable, 20),
            Entry("https://c.example", new ServerVersionInfo(ServerVersionSource.Reported, 0, 6, "0.6.1"), ServerProbeState.Reachable, 10),
        ]);
        displayOrder = LanConnectServerListDisplayOrder.Apply(displayOrder, entries, resort: false)
            .Select(entry => LanConnectServerListBootstrap.NormalizeAddress(entry.Address))
            .ToList();
        // New entries append §3-ranked: c (0.6 tier) before b (0.5 tier).
        Assert.Equal(
        [
            "https://anchor.example",
            "https://c.example",
            "https://b.example",
        ], displayOrder);

        // Flip latency, version tier, and reachability of the appended pair —
        // a resort=false render must not move anything.
        ServerListEntry b = entries.Single(entry => entry.Address == "https://b.example");
        b.PingMs = 5;
        b.ProbeState = ServerProbeState.Reachable;
        ServerListEntry c = entries.Single(entry => entry.Address == "https://c.example");
        c.Version = new ServerVersionInfo(ServerVersionSource.Reported, 0, 9, "0.9.0");
        c.PingMs = 400;
        c.ProbeState = ServerProbeState.Unreachable;

        displayOrder = LanConnectServerListDisplayOrder.Apply(displayOrder, entries, resort: false)
            .Select(entry => LanConnectServerListBootstrap.NormalizeAddress(entry.Address))
            .ToList();

        // Stability: despite b now being the fastest reachable and c being an
        // unreachable 0.9, the on-screen order does not move on resort=false.
        Assert.Equal(
        [
            "https://anchor.example",
            "https://c.example",
            "https://b.example",
        ], displayOrder);
    }

    [Fact]
    public void Resort_true_is_equivalent_to_full_ranking()
    {
        List<ServerListEntry> entries =
        [
            Entry("https://a.example", new ServerVersionInfo(ServerVersionSource.Reported, 0, 5, "0.5.1"), ServerProbeState.Reachable, 99),
            Entry("https://z.example", new ServerVersionInfo(ServerVersionSource.Reported, 0, 6, "0.6.1"), ServerProbeState.Reachable, 12),
            Entry("https://featured", ServerVersionInfo.Unknown, ServerProbeState.Unreachable, null, pinned: true),
        ];
        List<string> previousOrder = ["https://a.example"];

        List<string> applied = LanConnectServerListDisplayOrder.Apply(previousOrder, entries, resort: true)
            .Select(entry => entry.Address)
            .ToList();

        Assert.Equal(
            LanConnectServerListBootstrap.OrderForDisplay(entries).Select(entry => entry.Address).ToList(),
            applied);
    }

    [Fact]
    public void Refresh_render_order_and_closed_log_lines_match_the_e2e_contract()
    {
        Assert.Equal(
            "sts2_lan_connect server picker refresh: dialog=3 generation=2 state=started",
            LanConnectServerPickerLogLines.Refresh(3, 2, "started"));
        Assert.Equal(
            "sts2_lan_connect server picker refresh: dialog=12 generation=1 state=canceled",
            LanConnectServerPickerLogLines.Refresh(12, 1, "canceled"));

        List<ServerListEntry> entries =
        [
            Entry("http://101.35.217.99:8788", new ServerVersionInfo(ServerVersionSource.Reported, 0, 6, "0.6.2-alpha.1"), ServerProbeState.Reachable, 38, pinned: true),
            Entry("http://10.0.0.2:8787", new ServerVersionInfo(ServerVersionSource.Inferred, 0, 5, "0.5.x（推断）"), ServerProbeState.Unreachable),
            Entry("http://10.0.0.3:8787", ServerVersionInfo.Unknown, ServerProbeState.Pending),
        ];

        Assert.Equal(
            "sts2_lan_connect server picker render: dialog=7 generation=4 resort=false count=3 order=http://101.35.217.99:8788,http://10.0.0.2:8787,http://10.0.0.3:8787",
            LanConnectServerPickerLogLines.Render(7, 4, resort: false, entries));

        Assert.Equal(
            "sts2_lan_connect server picker order: dialog=7 generation=4 count=3 items=" +
            "1=http://101.35.217.99:8788|pinned|0.6.2-alpha.1|reachable|38ms; " +
            "2=http://10.0.0.2:8787|-|0.5.x（推断）|unreachable|-; " +
            "3=http://10.0.0.3:8787|-|未知|pending|-",
            LanConnectServerPickerLogLines.OrderSnapshot(7, 4, entries));

        Assert.Equal(
            "sts2_lan_connect server picker closed: dialog=9",
            LanConnectServerPickerLogLines.Closed(9));
    }

    [Fact]
    public void Order_snapshot_log_line_never_truncates_large_lists()
    {
        List<ServerListEntry> entries = Enumerable.Range(1, 50)
            .Select(i => Entry($"http://10.0.0.{i}:8787", new ServerVersionInfo(ServerVersionSource.Reported, 0, 6, "0.6.1"), ServerProbeState.Reachable, i))
            .ToList();

        string line = LanConnectServerPickerLogLines.OrderSnapshot(1, 1, entries);

        Assert.StartsWith("sts2_lan_connect server picker order: dialog=1 generation=1 count=50 items=", line);
        Assert.EndsWith("; 50=http://10.0.0.50:8787|-|0.6.1|reachable|50ms", line);
        Assert.Equal(50, line.Split("; ", StringSplitOptions.None).Length);
    }

    [Fact]
    public void Log_lines_percent_encode_special_characters_but_keep_normal_addresses_verbatim()
    {
        List<ServerListEntry> entries =
        [
            Entry("http://101.35.217.99:8788", new ServerVersionInfo(ServerVersionSource.Reported, 0, 6, "0.6.1"), ServerProbeState.Reachable, 38),
            Entry("http://a.example/x|y,z;w q\nr%2"),
        ];

        string render = LanConnectServerPickerLogLines.Render(1, 1, resort: true, entries);
        Assert.EndsWith(
            "order=http://101.35.217.99:8788,http://a.example/x%7Cy%2Cz%3Bw%20q%0Ar%252",
            render);
        Assert.DoesNotContain('\n', render);

        string order = LanConnectServerPickerLogLines.OrderSnapshot(1, 1, entries);
        Assert.Contains(
            "1=http://101.35.217.99:8788|-|0.6.1|reachable|38ms; ",
            order);
        Assert.Contains(
            "2=http://a.example/x%7Cy%2Cz%3Bw%20q%0Ar%252|-|未知|pending|-",
            order);
        Assert.DoesNotContain('\n', order);
        Assert.DoesNotContain("http://a.example/x|", order);
    }

    [Fact]
    public async Task AwaitRefreshTasks_waits_for_the_ping_task_before_surfacing_the_cf_failure()
    {
        Task cloudflareTask = Task.FromException(new InvalidOperationException("cf discovery failed"));
        Task pingTask = Task.Run(async () =>
        {
            await Task.Delay(150);
            return 42;
        });
        Task tracking = Task.Run(async () => await pingTask);

        InvalidOperationException thrown = await Assert.ThrowsAsync<InvalidOperationException>(
            () => LanConnectServerListDisplayOrder.AwaitRefreshTasksAsync(cloudflareTask, pingTask));

        Assert.Equal("cf discovery failed", thrown.Message);
        Assert.True(pingTask.IsCompletedSuccessfully);
        await tracking;
    }
}
