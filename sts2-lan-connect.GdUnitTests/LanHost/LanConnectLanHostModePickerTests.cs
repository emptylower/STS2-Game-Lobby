using Godot;
using GdUnit4;
using Sts2LanConnect.Scripts;
using static GdUnit4.Assertions;

namespace Sts2LanConnect.GdUnitTests.LanHost;

[TestSuite]
[RequireGodotRuntime]
public sealed class LanConnectLanHostModePickerTests
{
    [TestCase]
    public async Task Picker_has_three_stable_mode_items()
    {
        using PickerFixture fixture = await PickerFixture.Create();

        AssertThat(fixture.Picker.ItemCount).IsEqual(3);
        AssertThat(Items(fixture.Picker)).ContainsExactly(
            new MenuItem(0, "标准模式"),
            new MenuItem(1, "多人每日挑战"),
            new MenuItem(2, "自定义模式"));
    }

    [TestCase]
    public async Task Open_refreshes_native_mode_availability_each_time()
    {
        LanConnectLanHostModeAvailability availability = new(
            Standard: true,
            Daily: false,
            Custom: false);
        using PickerFixture fixture = await PickerFixture.Create(
            availability: () => availability);

        fixture.Picker.Open();
        AssertThat(fixture.Picker.IsItemDisabled(0)).IsFalse();
        AssertThat(fixture.Picker.IsItemDisabled(1)).IsTrue();
        AssertThat(fixture.Picker.IsItemDisabled(2)).IsTrue();

        fixture.Picker.Hide();
        availability = new(
            Standard: false,
            Daily: true,
            Custom: true);
        fixture.Picker.Open();

        AssertThat(fixture.Picker.IsItemDisabled(0)).IsTrue();
        AssertThat(fixture.Picker.IsItemDisabled(1)).IsFalse();
        AssertThat(fixture.Picker.IsItemDisabled(2)).IsFalse();
        AssertThat(fixture.Picker.GetFocusedItem()).IsEqual(1);
    }

    [TestCase]
    public async Task Custom_selection_starts_custom_mode_once_and_closes_picker()
    {
        List<LanConnectLanHostMode> selected = [];
        using PickerFixture fixture = await PickerFixture.Create(
            onSelected: mode =>
            {
                selected.Add(mode);
                return Task.CompletedTask;
            });
        fixture.Picker.Open();

        fixture.Picker.EmitSignal(PopupMenu.SignalName.IdPressed, 2L);
        await fixture.Runner.AwaitIdleFrame();

        AssertThat(selected).ContainsExactly(LanConnectLanHostMode.Custom);
        AssertThat(fixture.Picker.Visible).IsFalse();
    }

    [TestCase]
    public async Task Duplicate_selection_is_suppressed_until_start_finishes()
    {
        TaskCompletionSource completion = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        int starts = 0;
        using PickerFixture fixture = await PickerFixture.Create(
            onSelected: _ =>
            {
                starts++;
                return completion.Task;
            });
        fixture.Picker.Open();

        fixture.Picker.EmitSignal(PopupMenu.SignalName.IdPressed, 2L);
        fixture.Picker.EmitSignal(PopupMenu.SignalName.IdPressed, 2L);
        await fixture.Runner.AwaitIdleFrame();

        AssertThat(starts).IsEqual(1);

        completion.SetResult();
        await fixture.Runner.AwaitIdleFrame();
        fixture.Picker.Open();
        fixture.Picker.EmitSignal(PopupMenu.SignalName.IdPressed, 0L);
        await fixture.Runner.AwaitIdleFrame();

        AssertThat(starts).IsEqual(2);
    }

    [TestCase]
    public async Task Disabled_and_unknown_ids_are_rejected_even_when_signaled_directly()
    {
        int starts = 0;
        using PickerFixture fixture = await PickerFixture.Create(
            availability: () => new(
                Standard: true,
                Daily: true,
                Custom: false),
            onSelected: _ =>
            {
                starts++;
                return Task.CompletedTask;
            });
        fixture.Picker.Open();

        fixture.Picker.EmitSignal(PopupMenu.SignalName.IdPressed, 2L);
        fixture.Picker.EmitSignal(PopupMenu.SignalName.IdPressed, 99L);
        await fixture.Runner.AwaitIdleFrame();

        AssertThat(starts).IsEqual(0);
        AssertThat(fixture.Picker.Visible).IsTrue();
    }

    [TestCase]
    public async Task Closing_without_selection_does_not_start_a_mode()
    {
        int starts = 0;
        using PickerFixture fixture = await PickerFixture.Create(
            onSelected: _ =>
            {
                starts++;
                return Task.CompletedTask;
            });
        fixture.Picker.Open();

        fixture.Picker.Hide();
        fixture.Picker.EmitSignal(PopupMenu.SignalName.IdPressed, 2L);
        await fixture.Runner.AwaitIdleFrame();

        AssertThat(starts).IsEqual(0);
        AssertThat(fixture.Picker.Visible).IsFalse();
    }

    [TestCase]
    public async Task Repeated_ensure_reuses_picker_and_rebinds_one_callback()
    {
        Control root = AutoFree(new Control())!;
        int firstCallbackStarts = 0;
        int secondCallbackStarts = 0;
        LanConnectLanHostModeAvailability Available() => new(
            Standard: true,
            Daily: true,
            Custom: true);

        LanConnectLanHostModePicker first = HostSubmenuPatches.EnsureLanHostModePicker(
            root,
            Available,
            _ =>
            {
                firstCallbackStarts++;
                return Task.CompletedTask;
            });
        LanConnectLanHostModePicker second = HostSubmenuPatches.EnsureLanHostModePicker(
            root,
            Available,
            _ =>
            {
                secondCallbackStarts++;
                return Task.CompletedTask;
            });
        using ISceneRunner runner = ISceneRunner.Load(root, autoFree: true);
        await runner.AwaitIdleFrame();

        AssertThat(ReferenceEquals(first, second)).IsTrue();
        AssertThat(root.FindChildren(
            LanConnectConstants.LanHostModePickerName,
            nameof(PopupMenu),
            recursive: true,
            owned: false).Count).IsEqual(1);

        second.Open();
        second.EmitSignal(PopupMenu.SignalName.IdPressed, 2L);
        await runner.AwaitIdleFrame();

        AssertThat(firstCallbackStarts).IsEqual(0);
        AssertThat(secondCallbackStarts).IsEqual(1);
    }

    private static MenuItem[] Items(PopupMenu picker) => Enumerable
        .Range(0, picker.ItemCount)
        .Select(index => new MenuItem(
            picker.GetItemId(index),
            picker.GetItemText(index)))
        .ToArray();

    private readonly record struct MenuItem(int Id, string Label);

    private sealed class PickerFixture : IDisposable
    {
        private PickerFixture(
            Control root,
            LanConnectLanHostModePicker picker,
            ISceneRunner runner)
        {
            Root = root;
            Picker = picker;
            Runner = runner;
        }

        internal Control Root { get; }
        internal LanConnectLanHostModePicker Picker { get; }
        internal ISceneRunner Runner { get; }

        internal static async Task<PickerFixture> Create(
            Func<LanConnectLanHostModeAvailability>? availability = null,
            Func<LanConnectLanHostMode, Task>? onSelected = null)
        {
            LanConnectLanHostModePicker picker = new();
            picker.Initialize(
                availability ?? (() => new(
                    Standard: true,
                    Daily: true,
                    Custom: true)),
                onSelected ?? (_ => Task.CompletedTask));
            Control root = AutoFree(new Control())!;
            root.AddChild(picker);
            ISceneRunner runner = ISceneRunner.Load(root, autoFree: true);
            await runner.AwaitIdleFrame();
            return new PickerFixture(root, picker, runner);
        }

        public void Dispose()
        {
            Runner.Dispose();
            if (GodotObject.IsInstanceValid(Root) && !Root.IsQueuedForDeletion())
            {
                Root.QueueFree();
            }
        }
    }
}
