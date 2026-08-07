using System.Threading.Tasks;
using Godot;

namespace Sts2LanConnect.Scripts;

internal static class LanConnectContinueRunHostChannelPrompt
{
    public static async Task<string> PromptAsync(Control owner, string roomName)
    {
        ConfirmationDialog confirmation = new()
        {
            Name = "LanConnectContinueRunHostChannelConfirmation",
            Title = "选择续局联机方式",
            DialogText =
                $"无法确认多人存档“{roomName}”上次使用的是大厅还是 LAN。\n\n" +
                "恢复大厅房间会将房间重新发布到当前绑定的公共大厅；选择“仅 LAN”则不会发布。",
            OkButtonText = "恢复大厅房间",
            CancelButtonText = "仅 LAN",
            Exclusive = true,
            Unresizable = false,
            MinSize = new Vector2I(560, 300)
        };
        TaskCompletionSource<string> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        confirmation.Confirmed += () => completion.TrySetResult(LanConnectHostChannels.Lobby);
        confirmation.Canceled += () => completion.TrySetResult(LanConnectHostChannels.Lan);
        confirmation.CloseRequested += () => completion.TrySetResult(LanConnectHostChannels.Lan);
        owner.AddChild(confirmation);
        try
        {
            confirmation.PopupCenteredClamped(new Vector2I(680, 380), 0.9f);
            confirmation.GetCancelButton().GrabFocus();
            return await completion.Task;
        }
        finally
        {
            if (GodotObject.IsInstanceValid(confirmation))
            {
                confirmation.QueueFree();
            }
        }
    }
}
