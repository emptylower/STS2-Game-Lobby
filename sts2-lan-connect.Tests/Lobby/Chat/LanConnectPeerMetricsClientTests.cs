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
