using Godot;

namespace Sts2LanConnect.Scripts;

internal static class LanConnectModPreflightDecisionPrompt
{
    public static async Task<LanConnectModPreflightDecision> ShowAsync(
        Node parent,
        LobbyRoomSummary room,
        LobbyModPreflightResponse response,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(room);
        ArgumentNullException.ThrowIfNull(response);
        cancellationToken.ThrowIfCancellationRequested();

        string summary = BuildDifferenceSummary(response);
        if (!response.CanContinueRelaxed)
        {
            return LanConnectModPreflightDecision.Synchronize;
        }

        ConfirmationDialog dialog = new()
        {
            Name = "LanConnectModPreflightRelaxedConfirmation",
            Title = "发现 gameplay MOD 差异",
            DialogText =
                $"加入 {room.RoomName} 前检测到以下差异：\n\n{summary}\n\n" +
                "继续加入可能失败或导致联机状态异常。是否仍然尝试？",
            OkButtonText = "仍然尝试加入（可能失败）",
            CancelButtonText = "取消",
            Exclusive = true,
            Unresizable = true
        };
        TaskCompletionSource<LanConnectModPreflightDecision> completion = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        Callable confirm = Callable.From(() =>
            completion.TrySetResult(LanConnectModPreflightDecision.ContinueRelaxed));
        Callable cancel = Callable.From(() =>
            completion.TrySetResult(LanConnectModPreflightDecision.Cancel));
        dialog.Connect(AcceptDialog.SignalName.Confirmed, confirm);
        dialog.Connect(AcceptDialog.SignalName.Canceled, cancel);
        dialog.Connect(Window.SignalName.CloseRequested, cancel);
        parent.AddChild(dialog);

        using CancellationTokenRegistration registration = cancellationToken.Register(() =>
            completion.TrySetCanceled(cancellationToken));
        try
        {
            dialog.PopupCentered(new Vector2I(620, 360));
            dialog.GetCancelButton().GrabFocus();
            return await completion.Task;
        }
        finally
        {
            if (GodotObject.IsInstanceValid(dialog))
            {
                dialog.QueueFree();
            }
        }
    }

    internal static string BuildDifferenceSummary(LobbyModPreflightResponse response)
    {
        List<string> parts = [];
        AddCount(parts, "可从 Steam Workshop 获取的缺失项", response.MissingWorkshopMods.Count);
        AddCount(parts, "需要手动处理的缺失项", response.MissingManualMods.Count);
        AddCount(parts, "本机多出的 gameplay MOD", response.ExtraGameplayMods.Count);
        AddCount(parts, "版本不一致项", response.VersionMismatches.Count);
        return parts.Count == 0 ? "未发现 gameplay MOD 差异。" : string.Join("\n", parts);
    }

    private static void AddCount(List<string> parts, string label, int count)
    {
        if (count > 0)
        {
            parts.Add($"{label}：{count}");
        }
    }
}
