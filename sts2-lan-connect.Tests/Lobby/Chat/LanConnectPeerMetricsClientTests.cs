using System.Net;
using System.Net.Http;
using System.Text;
using Sts2LanConnect.Scripts;

namespace Sts2LanConnect.Tests.Lobby.Chat;

public sealed class LanConnectPeerMetricsClientTests
{
    [Fact]
    public async Task Missing_metrics_capability_falls_back_to_probe()
    {
        QueueHandler handler = new(
            """{"address":"http://101.35.217.99:8788","displayName":"魔仙堡","rooms":1}""",
            """{"ok":true,"capabilities":{"modSyncProtocolVersion":1,"modSyncEnabled":true}}""");

        PeerMetricsResponse? metrics = await LanConnectPeerMetricsClient.FetchAsync(
            "http://101.35.217.99:8788/",
            handler);

        Assert.NotNull(metrics);
        Assert.Equal(1, metrics.ModSyncProtocolVersion);
        Assert.True(metrics.ModSyncEnabled);
        Assert.Equal(
            [
                "http://101.35.217.99:8788/peers/metrics",
                "http://101.35.217.99:8788/probe"
            ],
            handler.RequestUris);
    }

    [Fact]
    public async Task Snapshot_with_reported_version_and_mod_sync_skips_probe_entirely()
    {
        ScriptedHandler handler = new(Sync(_ => Json(
            """{"address":"http://a.example","serviceVersion":"0.6.1","modSyncProtocolVersion":1,"modSyncEnabled":true,"rooms":2}""")));

        LanConnectPeerMetricsClient.PeerDiscoverySnapshot snapshot =
            await LanConnectPeerMetricsClient.FetchSnapshotAsync("http://a.example", handler);

        Assert.NotNull(snapshot.Metrics);
        Assert.Equal(2, snapshot.Metrics.Rooms);
        Assert.Equal(ServerVersionSource.Reported, snapshot.Version.Source);
        Assert.Equal("0.6.1", snapshot.Version.Display);
        Assert.Equal(0, handler.RequestCount("/probe"));
        Assert.Equal(1, handler.RequestCount("/peers/metrics"));
    }

    [Fact]
    public async Task Snapshot_with_reported_version_but_missing_mod_sync_probes_once_and_keeps_reported()
    {
        ScriptedHandler handler = new(Sync(uri => uri.EndsWith("/probe")
            ? Json("""{"ok":true,"capabilities":{"modSyncProtocolVersion":1,"modSyncEnabled":true,"modSyncMinimumClientVersion":"0.5.1"}}""")
            : Json("""{"address":"http://a.example","serviceVersion":"0.6.1","modSyncProtocolVersion":0}""")));

        LanConnectPeerMetricsClient.PeerDiscoverySnapshot snapshot =
            await LanConnectPeerMetricsClient.FetchSnapshotAsync("http://a.example", handler);

        Assert.Equal(1, handler.RequestCount("/probe"));
        Assert.Equal(ServerVersionSource.Reported, snapshot.Version.Source);
        Assert.Equal("0.6.1", snapshot.Version.Display);
        Assert.NotNull(snapshot.Metrics);
        Assert.Equal(1, snapshot.Metrics.ModSyncProtocolVersion);
    }

    [Fact]
    public async Task Snapshot_with_reported_version_survives_failing_or_legacy_probe()
    {
        ScriptedHandler failing = new(Sync(uri => uri.EndsWith("/probe") ? new HttpResponseMessage(HttpStatusCode.InternalServerError) : Json(
            """{"address":"http://a.example","serviceVersion":"0.6.1","modSyncProtocolVersion":0}""")));
        LanConnectPeerMetricsClient.PeerDiscoverySnapshot failed =
            await LanConnectPeerMetricsClient.FetchSnapshotAsync("http://a.example", failing);
        Assert.Equal(1, failing.RequestCount("/probe"));
        Assert.Equal(ServerVersionSource.Reported, failed.Version.Source);

        // A bare {"ok":true} would infer 0.4 — it must not override the
        // reported metrics version either.
        ScriptedHandler legacy = new(Sync(uri => uri.EndsWith("/probe") ? Json("""{"ok":true}""") : Json(
            """{"address":"http://a.example","serviceVersion":"0.6.1","modSyncProtocolVersion":0}""")));
        LanConnectPeerMetricsClient.PeerDiscoverySnapshot legacySnapshot =
            await LanConnectPeerMetricsClient.FetchSnapshotAsync("http://a.example", legacy);
        Assert.Equal(ServerVersionSource.Reported, legacySnapshot.Version.Source);
        Assert.Equal("0.6.1", legacySnapshot.Version.Display);
    }

    [Fact]
    public async Task Snapshot_without_reported_version_probes_exactly_once()
    {
        ScriptedHandler handler = new(Sync(uri => uri.EndsWith("/probe")
            ? Json("""{"ok":true,"capabilities":{"dualProtocolApiVersion":1}}""")
            : Json("""{"address":"http://a.example","modSyncProtocolVersion":1,"modSyncEnabled":true}""")));

        LanConnectPeerMetricsClient.PeerDiscoverySnapshot snapshot =
            await LanConnectPeerMetricsClient.FetchSnapshotAsync("http://a.example", handler);

        Assert.Equal(1, handler.RequestCount("/probe"));
        Assert.Equal(ServerVersionSource.Inferred, snapshot.Version.Source);
        Assert.Equal((0, 6), (snapshot.Version.Major, snapshot.Version.Minor));
    }

    [Fact]
    public async Task Snapshot_with_failing_metrics_still_probes_and_reports_the_version()
    {
        ScriptedHandler handler = new(Sync(uri => uri.EndsWith("/probe")
            ? Json("""{"ok":true,"capabilities":{"dualProtocolApiVersion":1,"modSyncProtocolVersion":1,"modSyncEnabled":true}}""")
            : new HttpResponseMessage(HttpStatusCode.NotFound)));

        LanConnectPeerMetricsClient.PeerDiscoverySnapshot snapshot =
            await LanConnectPeerMetricsClient.FetchSnapshotAsync("http://a.example", handler);

        Assert.Null(snapshot.Metrics);
        Assert.Equal(ServerVersionSource.Inferred, snapshot.Version.Source);
        Assert.Equal((0, 6), (snapshot.Version.Major, snapshot.Version.Minor));
        Assert.Equal(1, handler.RequestCount("/peers/metrics"));
        Assert.Equal(1, handler.RequestCount("/probe"));
    }

    [Fact]
    public async Task Snapshot_without_reported_version_and_failing_probe_is_unknown_but_keeps_metrics()
    {
        ScriptedHandler handler = new(Sync(uri => uri.EndsWith("/probe")
            ? new HttpResponseMessage(HttpStatusCode.InternalServerError)
            : Json("""{"address":"http://a.example","modSyncProtocolVersion":1,"modSyncEnabled":true,"rooms":7}""")));

        LanConnectPeerMetricsClient.PeerDiscoverySnapshot snapshot =
            await LanConnectPeerMetricsClient.FetchSnapshotAsync("http://a.example", handler);

        Assert.Equal(ServerVersionSource.Unknown, snapshot.Version.Source);
        Assert.NotNull(snapshot.Metrics);
        Assert.Equal(7, snapshot.Metrics.Rooms);
        Assert.Equal(1, snapshot.Metrics.ModSyncProtocolVersion);
    }

    [Fact]
    public async Task Snapshot_does_not_backfill_mod_sync_from_explicit_null_capabilities()
    {
        ScriptedHandler handler = new(Sync(uri => uri.EndsWith("/probe")
            ? Json("""{"ok":true,"capabilities":null}""")
            : Json("""{"address":"http://a.example","serviceVersion":"0.6.1","modSyncProtocolVersion":0}""")));

        LanConnectPeerMetricsClient.PeerDiscoverySnapshot snapshot =
            await LanConnectPeerMetricsClient.FetchSnapshotAsync("http://a.example", handler);

        Assert.Equal(1, handler.RequestCount("/probe"));
        // No backfill: the metrics payload keeps its original mod-sync state,
        // and the reported version survives untouched.
        Assert.NotNull(snapshot.Metrics);
        Assert.Equal(0, snapshot.Metrics.ModSyncProtocolVersion);
        Assert.False(snapshot.Metrics.ModSyncEnabled);
        Assert.Equal(ServerVersionSource.Reported, snapshot.Version.Source);
        Assert.Equal("0.6.1", snapshot.Version.Display);
    }

    [Fact]
    public async Task Snapshot_canceled_during_metrics_never_requests_the_probe()
    {
        TaskCompletionSource metricsArrived = NewSource();
        TaskCompletionSource releaseMetrics = NewSource();
        ScriptedHandler handler = new(async uri =>
        {
            if (uri.EndsWith("/peers/metrics", StringComparison.Ordinal))
            {
                metricsArrived.SetResult();
                await releaseMetrics.Task;
                return Json("""{"address":"http://a.example","serviceVersion":"0.6.1","modSyncProtocolVersion":0}""");
            }

            return Json("""{"ok":true,"capabilities":{"dualProtocolApiVersion":1}}""");
        });

        using CancellationTokenSource cts = new();
        Task<LanConnectPeerMetricsClient.PeerDiscoverySnapshot> snapshotTask =
            LanConnectPeerMetricsClient.FetchSnapshotAsync("http://a.example", handler, cts.Token);
        await metricsArrived.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cts.Cancel();
        releaseMetrics.SetResult();

        LanConnectPeerMetricsClient.PeerDiscoverySnapshot snapshot =
            await snapshotTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(1, handler.RequestCount("/peers/metrics"));
        Assert.Equal(0, handler.RequestCount("/probe"));
        Assert.Equal(ServerVersionSource.Unknown, snapshot.Version.Source);
    }

    private static TaskCompletionSource NewSource() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };

    private static Func<string, Task<HttpResponseMessage>> Sync(Func<string, HttpResponseMessage> respond) =>
        uri => Task.FromResult(respond(uri));

    private sealed class ScriptedHandler(Func<string, Task<HttpResponseMessage>> responder) : HttpMessageHandler
    {
        private readonly List<string> _requests = [];

        public int RequestCount(string pathSuffix) => _requests.Count(uri => uri.EndsWith(pathSuffix, StringComparison.Ordinal));

        // Yields first so responder gates can never run on the caller's
        // synchronous path (mirrors the ping-test deadlock lesson).
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            await Task.Yield();
            string uri = request.RequestUri!.AbsoluteUri;
            _requests.Add(uri);
            return await responder(uri);
        }
    }

    private sealed class QueueHandler(params string[] responses) : HttpMessageHandler
    {
        private int _index;

        public List<string> RequestUris { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUris.Add(request.RequestUri!.AbsoluteUri);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responses[_index++], Encoding.UTF8, "application/json")
            });
        }
    }
}
