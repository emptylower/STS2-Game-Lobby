using System;
using System.Threading;
using System.Threading.Tasks;
using Godot;

namespace Sts2LanConnect.Scripts;

internal static class LanConnectContinueRunHostChannelPrompt
{
    public static async Task<string?> PromptAsync(Control owner, string roomName, CancellationToken cancellationToken)
    {
        if (!GodotObject.IsInstanceValid(owner) || owner.IsQueuedForDeletion())
        {
            return null;
        }

        ConfirmationDialog confirmation = new()
        {
            Name = "LanConnectContinueRunHostChannelConfirmation",
            Title = "选择续局联机方式",
            DialogText =
                $"无法确认多人存档“{roomName}”上次使用的是大厅还是 LAN。\n\n" +
                "恢复大厅房间会将房间重新发布到当前绑定的公共大厅；选择“仅 LAN”则不会发布。",
            OkButtonText = "恢复大厅房间",
            Exclusive = true,
            Unresizable = false,
            MinSize = new Vector2I(560, 300)
        };
        TaskCompletionSource<string?> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Action ownerExiting = () => completion.TrySetResult(null);
        confirmation.Confirmed += () => completion.TrySetResult(LanConnectHostChannels.Lobby);
        confirmation.Canceled += () => completion.TrySetResult(null);
        confirmation.CloseRequested += () => completion.TrySetResult(null);
        confirmation.TreeExiting += () => completion.TrySetResult(null);
        owner.TreeExiting += ownerExiting;
        owner.AddChild(confirmation);
        Button lanButton = confirmation.AddButton("仅 LAN", right: true, action: "lan");
        confirmation.CustomAction += action =>
        {
            if (action == "lan")
            {
                completion.TrySetResult(LanConnectHostChannels.Lan);
            }
        };
        confirmation.GetCancelButton().Hide();
        using CancellationTokenRegistration cancellationRegistration =
            cancellationToken.Register(() => completion.TrySetResult(null));
        try
        {
            confirmation.PopupCenteredClamped(new Vector2I(680, 380), 0.9f);
            lanButton.GrabFocus();
            return await completion.Task;
        }
        finally
        {
            if (GodotObject.IsInstanceValid(owner))
            {
                owner.TreeExiting -= ownerExiting;
            }
            if (GodotObject.IsInstanceValid(confirmation))
            {
                confirmation.QueueFree();
            }
        }
    }
}
