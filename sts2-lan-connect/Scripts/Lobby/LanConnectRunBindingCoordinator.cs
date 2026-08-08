using System;
using System.Threading.Tasks;

namespace Sts2LanConnect.Scripts;

internal sealed class LanConnectRunBindingCoordinator<TRun>
    where TRun : class
{
    private readonly Func<LoadResult> _loadRun;
    private readonly Func<TRun, string> _buildSaveKey;
    private readonly Func<string, LanConnectSavedRoomBinding?> _readBinding;
    private readonly Func<TRun, BindingWrite, bool> _persistBinding;

    public LanConnectRunBindingCoordinator(
        Func<LoadResult> loadRun,
        Func<TRun, string> buildSaveKey,
        Func<string, LanConnectSavedRoomBinding?> readBinding,
        Func<TRun, BindingWrite, bool> persistBinding)
    {
        _loadRun = loadRun;
        _buildSaveKey = buildSaveKey;
        _readBinding = readBinding;
        _persistBinding = persistBinding;
    }

    public bool TryLoadForSafeLoad(out TRun? run, out string failureReason)
    {
        LoadResult result = _loadRun();
        run = result.Run;
        failureReason = result.FailureReason;
        return result.Success && run != null;
    }

    public RepairBindingInspection InspectRepairBinding()
    {
        LoadResult result = _loadRun();
        if (!result.Success || result.Run == null)
        {
            return new RepairBindingInspection(false, string.Empty, false, result.FailureReason);
        }

        string saveKey = _buildSaveKey(result.Run);
        return new RepairBindingInspection(
            true,
            saveKey,
            _readBinding(saveKey) != null,
            string.Empty);
    }

    public async Task ExecuteHostedRestartAsync(
        TRun run,
        string roomName,
        string? password,
        string gameMode,
        Action afterPersist,
        Func<Task> prepareReturn,
        Func<Task> returnToMainMenu)
    {
        BindingWrite write = new(
            _buildSaveKey(run),
            roomName,
            password,
            gameMode,
            LanConnectHostChannels.Lobby,
            LanConnectSavedRoomBinding.CurrentSchemaVersion,
            "host_restart_before_main_menu");
        if (!_persistBinding(run, write))
        {
            throw new InvalidOperationException(
                $"Hosted restart binding was not persisted for saveKey={write.SaveKey}.");
        }

        afterPersist();
        await prepareReturn();
        await returnToMainMenu();
    }

    internal sealed record LoadResult(bool Success, TRun? Run, string FailureReason);

    internal sealed record RepairBindingInspection(
        bool RunLoaded,
        string SaveKey,
        bool HasBinding,
        string FailureReason);

    internal sealed record BindingWrite(
        string SaveKey,
        string RoomName,
        string? Password,
        string GameMode,
        string HostChannel,
        int SchemaVersion,
        string Source);
}
