using System.Net;
using System.Net.Http;
using System.Text;
using Sts2LanConnect.Scripts;

namespace Sts2LanConnect.Tests.Lobby.Chat;

public sealed class LanConnectPeerPingTests
{
    [Fact]
    public async Task Successful_health_probe_sends_two_samples_and_reports_reachable()
    {
        ProbeHandler handler = new(Ok("""{"address":"http://a.example","displayName":"魔仙堡","challenge":"c","signature":"s"}"""));

        PeerProbeResult result = await LanConnectPeerPing.ProbeAsync("http://a.example", handler, CancellationToken.None);

        Assert.Equal(2, handler.HealthRequests);
        Assert.Equal(0, handler.ProbeRequests);
        Assert.True(result.Ms >= 0);
        Assert.Equal(PingBucket.Low, result.Bucket);
        Assert.Equal("魔仙堡", result.DisplayName);
    }

    [Fact]
    public async Task Failing_first_health_sample_skips_the_second_and_falls_back_to_probe()
    {
        ProbeHandler handler = new(
            healthResponder: Status(HttpStatusCode.NotFound),
            probeResponder: Ok("""{"ok":true}"""));

        PeerProbeResult result = await LanConnectPeerPing.ProbeAsync("http://a.example", handler, CancellationToken.None);

        Assert.Equal(1, handler.HealthRequests);
        Assert.Equal(2, handler.ProbeRequests);
        Assert.True(result.Ms >= 0);
        Assert.Null(result.DisplayName);
    }

    [Fact]
    public async Task Failing_second_health_sample_keeps_the_first_sample_and_stays_reachable()
    {
        int healthCalls = 0;
        ProbeHandler handler = new(
            healthResponder: () => Task.FromResult(++healthCalls == 1
                ? Json("""{"address":"http://a.example","displayName":"魔仙堡"}""")
                : new HttpResponseMessage(HttpStatusCode.InternalServerError)),
            probeResponder: Ok("""{"ok":true}"""));

        PeerProbeResult result = await LanConnectPeerPing.ProbeAsync("http://a.example", handler, CancellationToken.None);

        Assert.Equal(2, handler.HealthRequests);
        Assert.Equal(0, handler.ProbeRequests);
        Assert.True(result.Ms >= 0);
        Assert.Equal("魔仙堡", result.DisplayName);
    }

    [Fact]
    public async Task Successful_health_probe_reports_the_minimum_of_both_samples()
    {
        int healthCalls = 0;
        ProbeHandler handler = new(async () =>
        {
            if (Interlocked.Increment(ref healthCalls) == 1)
            {
                await Task.Delay(150);
            }

            return Json("""{"displayName":"魔仙堡"}""");
        });

        PeerProbeResult result = await LanConnectPeerPing.ProbeAsync("http://a.example", handler, CancellationToken.None);

        Assert.Equal(2, handler.HealthRequests);
        Assert.Equal(0, handler.ProbeRequests);
        Assert.True(result.Ms >= 0);
        Assert.True(result.Ms < 150, $"expected the fast second sample to win, got {result.Ms}ms");
        Assert.Equal(PingBucket.Low, result.Bucket);
    }

    [Fact]
    public async Task Both_tiers_failing_reports_unreachable()
    {
        ProbeHandler handler = new(
            healthResponder: Status(HttpStatusCode.ServiceUnavailable),
            probeResponder: Status(HttpStatusCode.ServiceUnavailable));

        PeerProbeResult result = await LanConnectPeerPing.ProbeAsync("http://a.example", handler, CancellationToken.None);

        Assert.Equal((-1, PingBucket.Unreachable, null), (result.Ms, result.Bucket, result.DisplayName));
        Assert.Equal(1, handler.HealthRequests);
        Assert.Equal(1, handler.ProbeRequests);
    }

    [Fact]
    public async Task Cancellation_during_the_first_sample_sends_no_further_requests()
    {
        TaskCompletionSource firstHealthArrived = NewSource();
        TaskCompletionSource releaseFirstHealth = NewSource();
        ProbeHandler handler = new(async () =>
        {
            firstHealthArrived.SetResult();
            await releaseFirstHealth.Task;
            return Json("""{"displayName":"魔仙堡"}""");
        });

        using CancellationTokenSource cts = new();
        Task<PeerProbeResult> probe = LanConnectPeerPing.ProbeAsync("http://a.example", handler, cts.Token);
        await firstHealthArrived.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cts.Cancel();
        releaseFirstHealth.SetResult();

        PeerProbeResult result = await probe.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal((-1, PingBucket.Unreachable, null), (result.Ms, result.Bucket, result.DisplayName));
        Assert.Equal(1, handler.HealthRequests);
        Assert.Equal(0, handler.ProbeRequests);
    }

    [Fact]
    public async Task Pre_canceled_token_sends_no_requests_at_all()
    {
        ProbeHandler handler = new(Ok("""{"ok":true}"""));

        using CancellationTokenSource cts = new();
        cts.Cancel();

        PeerProbeResult result = await LanConnectPeerPing.ProbeAsync("http://a.example", handler, cts.Token);

        Assert.Equal((-1, PingBucket.Unreachable, null), (result.Ms, result.Bucket, result.DisplayName));
        Assert.Equal(0, handler.HealthRequests);
        Assert.Equal(0, handler.ProbeRequests);
    }

    [Fact]
    public async Task Per_request_timeout_without_caller_cancellation_still_falls_back()
    {
        ProbeHandler handler = new(
            healthResponder: () => Task.FromException<HttpResponseMessage>(new TaskCanceledException("simulated HttpClient timeout")),
            probeResponder: Ok("""{"ok":true}"""));

        using CancellationTokenSource cts = new();
        PeerProbeResult result = await LanConnectPeerPing.ProbeAsync("http://a.example", handler, cts.Token);

        Assert.False(cts.IsCancellationRequested);
        Assert.True(result.Ms >= 0);
        Assert.Equal(1, handler.HealthRequests);
        Assert.Equal(2, handler.ProbeRequests);
    }

    private static TaskCompletionSource NewSource() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };

    private static Func<Task<HttpResponseMessage>> Ok(string body) =>
        () => Task.FromResult(Json(body));

    private static Func<Task<HttpResponseMessage>> Status(HttpStatusCode statusCode) =>
        () => Task.FromResult(new HttpResponseMessage(statusCode));

    // SendAsync always yields first so the responder body can never run on the
    // caller's synchronous path — ProbeAsync must hand control back to the
    // test before any responder gate blocks.
    private sealed class ProbeHandler(
        Func<Task<HttpResponseMessage>> healthResponder,
        Func<Task<HttpResponseMessage>>? probeResponder = null) : HttpMessageHandler
    {
        private int _healthRequests;
        private int _probeRequests;

        public int HealthRequests => _healthRequests;
        public int ProbeRequests => _probeRequests;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            await Task.Yield();
            if (request.RequestUri!.AbsolutePath.EndsWith("/peers/health", StringComparison.Ordinal))
            {
                Interlocked.Increment(ref _healthRequests);
                return await healthResponder();
            }

            if (request.RequestUri!.AbsolutePath.EndsWith("/probe", StringComparison.Ordinal))
            {
                Interlocked.Increment(ref _probeRequests);
                return await (probeResponder ?? healthResponder)();
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }
    }
}
