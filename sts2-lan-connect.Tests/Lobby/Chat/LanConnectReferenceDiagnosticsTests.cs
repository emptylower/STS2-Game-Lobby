using System.Reflection;
using System.Text.Json;
using Sts2LanConnect.Scripts;

namespace Sts2LanConnect.Tests.Lobby.Chat;

public sealed class LanConnectReferenceDiagnosticsTests
{
    [Fact]
    public void Trace_keeps_allowed_metadata_and_excludes_chat_and_exception_messages()
    {
        List<string> lines = [];
        LanConnectReferenceDiagnostics diagnostics = new(lines.Add);

        diagnostics.Failure("preview", "visual_exception",
            new TargetInvocationException(CaptureFailure()),
            ("model_id", "CARD.DEFEND_IRONCLAD"), ("upgrade", 1), ("visible", true),
            ("text", "private-chat"), ("playerName", "private-player"),
            ("server", "private-server"), ("token", "private-token"));

        string line = Assert.Single(lines);
        using JsonDocument document = Parse(line);
        JsonElement root = document.RootElement;
        Assert.Equal("preview", root.GetProperty("stage").GetString());
        Assert.Equal("visual_exception", root.GetProperty("reason").GetString());
        Assert.Equal("CARD.DEFEND_IRONCLAD", root.GetProperty("model_id").GetString());
        Assert.Equal(1, root.GetProperty("upgrade").GetInt32());
        Assert.True(root.GetProperty("visible").GetBoolean());
        Assert.Equal(typeof(InvalidOperationException).FullName, root.GetProperty("exception").GetString());
        Assert.InRange(root.GetProperty("stack").GetArrayLength(), 1, 4);
        Assert.DoesNotContain("private-", line, StringComparison.Ordinal);
        Assert.DoesNotContain("/Users/", line, StringComparison.Ordinal);
        Assert.False(root.TryGetProperty("text", out _));
        Assert.False(root.TryGetProperty("playerName", out _));
        Assert.False(root.TryGetProperty("server", out _));
        Assert.False(root.TryGetProperty("token", out _));
    }

    [Fact]
    public void Repeated_hover_is_bounded_without_using_activation_budget()
    {
        List<string> lines = [];
        LanConnectReferenceDiagnostics diagnostics = new(lines.Add);
        for (int index = 0; index < 100; index++)
        {
            diagnostics.Write("input", "meta_hover_started", ("view_id", index));
        }
        diagnostics.Write("activate", "view_reference_pressed", ("view_id", 101));

        Assert.Equal(13, lines.Count);
        using JsonDocument last = Parse(lines[^1]);
        Assert.Equal("activate", last.RootElement.GetProperty("stage").GetString());
        Assert.Equal(13, last.RootElement.GetProperty("sequence").GetInt32());
    }

    [Fact]
    public void Total_budget_and_failing_sink_never_escape_into_ui()
    {
        List<string> lines = [];
        LanConnectReferenceDiagnostics bounded = new(lines.Add, budget: 2);
        bounded.Write("render", "first");
        bounded.Write("activate", "second");
        bounded.Write("preview", "third");

        Assert.Equal(2, lines.Count);
        LanConnectReferenceDiagnostics unavailable = new(_ => throw new InvalidOperationException("logging unavailable"));
        Exception? error = Record.Exception(() =>
        {
            unavailable.Write("render", "ready");
            unavailable.Failure("preview", "failure", CaptureFailure());
        });
        Assert.Null(error);
    }

    private static Exception CaptureFailure()
    {
        try
        {
            throw new InvalidOperationException("private-exception-message");
        }
        catch (InvalidOperationException exception)
        {
            return exception;
        }
    }

    private static JsonDocument Parse(string line)
    {
        const string prefix = "sts2_lan_connect reference_diag: ";
        Assert.StartsWith(prefix, line, StringComparison.Ordinal);
        return JsonDocument.Parse(line[prefix.Length..]);
    }
}
