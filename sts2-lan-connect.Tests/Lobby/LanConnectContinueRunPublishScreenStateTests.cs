using Sts2LanConnect.Scripts;
using Xunit;

namespace Sts2LanConnect.Tests.Lobby;

public sealed class LanConnectContinueRunPublishScreenStateTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Retry = TimeSpan.FromSeconds(5);

    [Fact]
    public void Canceled_prompt_keeps_the_cached_screen_attemptable_on_every_later_visit()
    {
        LanConnectContinueRunPublishScreenState state = new(Retry);
        DateTimeOffset now = T0;

        // NMainMenuSubmenuStack caches the multiplayer load screen, so every visit reuses the
        // same instance id. Canceling the channel prompt must not disable the restore path.
        for (int visit = 0; visit < 3; visit++)
        {
            Assert.True(
                state.TryBeginAttempt(7, now, out LanConnectContinueRunPublishAttempt attempt),
                $"visit {visit} must be allowed to attempt a restore");
            state.MarkRetryable(attempt);
            state.EndAttempt(attempt);
            state.NotifyScreenClosed(7);
            now += TimeSpan.FromSeconds(1);
        }

        Assert.True(state.TryBeginAttempt(7, now, out _));
    }

    [Fact]
    public void Successful_restore_is_settled_for_the_visit_but_released_when_the_screen_closes()
    {
        LanConnectContinueRunPublishScreenState state = new(Retry);

        Assert.True(state.TryBeginAttempt(7, T0, out LanConnectContinueRunPublishAttempt first));
        state.MarkSettled(first);
        state.EndAttempt(first);

        // Same visit: no duplicate publish even after the retry interval elapses.
        Assert.False(state.TryBeginAttempt(7, T0 + TimeSpan.FromMinutes(10), out _));

        state.NotifyScreenClosed(7);

        Assert.True(state.TryBeginAttempt(7, T0 + TimeSpan.FromMinutes(10), out _));
    }

    [Fact]
    public void Retryable_outcome_is_throttled_inside_one_visit_and_cleared_by_closing()
    {
        LanConnectContinueRunPublishScreenState state = new(Retry);

        Assert.True(state.TryBeginAttempt(7, T0, out LanConnectContinueRunPublishAttempt attempt));
        state.MarkRetryable(attempt);
        state.EndAttempt(attempt);

        Assert.False(state.TryBeginAttempt(7, T0 + TimeSpan.FromSeconds(1), out _));
        Assert.True(state.TryBeginAttempt(7, T0 + Retry, out LanConnectContinueRunPublishAttempt throttled));
        state.EndAttempt(throttled);

        state.NotifyScreenClosed(7);
        Assert.True(state.TryBeginAttempt(7, T0 + TimeSpan.FromSeconds(1), out _));
    }

    [Fact]
    public void Only_one_attempt_can_be_in_flight_per_screen()
    {
        LanConnectContinueRunPublishScreenState state = new(Retry);

        Assert.True(state.TryBeginAttempt(7, T0, out LanConnectContinueRunPublishAttempt inFlight));
        Assert.False(state.TryBeginAttempt(7, T0 + TimeSpan.FromMinutes(1), out _));

        state.EndAttempt(inFlight);
        Assert.True(state.TryBeginAttempt(7, T0 + TimeSpan.FromMinutes(1), out _));
    }

    [Fact]
    public void Attempt_released_after_a_failure_leaves_the_screen_usable()
    {
        LanConnectContinueRunPublishScreenState state = new(Retry);
        Assert.True(state.TryBeginAttempt(7, T0, out LanConnectContinueRunPublishAttempt attempt));

        try
        {
            throw new InvalidOperationException("publish blew up before the lobby call");
        }
        catch (InvalidOperationException)
        {
            state.EndAttempt(attempt);
        }

        Assert.True(state.TryBeginAttempt(7, T0 + Retry, out _));
    }

    [Fact]
    public void Late_outcome_from_an_abandoned_visit_cannot_settle_the_reopened_screen()
    {
        LanConnectContinueRunPublishScreenState state = new(Retry);
        Assert.True(state.TryBeginAttempt(7, T0, out LanConnectContinueRunPublishAttempt stale));

        state.NotifyScreenClosed(7);
        state.MarkSettled(stale);
        state.EndAttempt(stale);

        Assert.True(state.TryBeginAttempt(7, T0 + TimeSpan.FromSeconds(1), out _));
    }

    [Fact]
    public void Closing_one_screen_does_not_reset_another()
    {
        LanConnectContinueRunPublishScreenState state = new(Retry);
        Assert.True(state.TryBeginAttempt(7, T0, out LanConnectContinueRunPublishAttempt seven));
        state.MarkSettled(seven);
        state.EndAttempt(seven);

        Assert.True(state.TryBeginAttempt(8, T0, out LanConnectContinueRunPublishAttempt eight));
        state.MarkSettled(eight);
        state.EndAttempt(eight);

        state.NotifyScreenClosed(8);

        Assert.False(state.TryBeginAttempt(7, T0 + TimeSpan.FromMinutes(1), out _));
        Assert.True(state.TryBeginAttempt(8, T0 + TimeSpan.FromMinutes(1), out _));
    }

    [Fact]
    public void Forgetting_a_screen_drops_every_latch()
    {
        LanConnectContinueRunPublishScreenState state = new(Retry);
        Assert.True(state.TryBeginAttempt(7, T0, out LanConnectContinueRunPublishAttempt attempt));
        state.MarkSettled(attempt);

        state.ForgetScreen(7);

        Assert.True(state.TryBeginAttempt(7, T0, out _));
    }

    [Fact]
    public void Concurrent_triggers_start_exactly_one_attempt()
    {
        LanConnectContinueRunPublishScreenState state = new(Retry);
        int started = 0;

        Parallel.For(0, 64, iteration =>
        {
            if (state.TryBeginAttempt(7, T0, out _))
            {
                Interlocked.Increment(ref started);
            }
        });

        Assert.Equal(1, started);
    }

    [Fact]
    public void An_attempt_is_stale_only_after_its_visit_ended()
    {
        LanConnectContinueRunPublishScreenState state = new(Retry);
        Assert.True(state.TryBeginAttempt(7, T0, out LanConnectContinueRunPublishAttempt attempt));

        Assert.False(state.IsAttemptStale(attempt));

        state.NotifyScreenClosed(7);
        Assert.True(state.IsAttemptStale(attempt));

        state.EndAttempt(attempt);
        state.ForgetScreen(7);
        Assert.True(state.IsAttemptStale(attempt));
    }

    [Fact]
    public void Reported_field_sequence_restore_cancel_restore_stays_alive()
    {
        // 用户现场序列：存档退出 -> 载入页恢复一次成功 -> 取消 -> 再次恢复。
        // 修复前第一次终态就把缓存的载入页永久锁死，第二次点击毫无反应，只能重启游戏。
        LanConnectContinueRunPublishScreenState state = new(Retry);
        DateTimeOffset now = T0;

        Assert.True(state.TryBeginAttempt(42, now, out LanConnectContinueRunPublishAttempt firstVisit));
        state.MarkSettled(firstVisit);
        state.EndAttempt(firstVisit);
        state.NotifyScreenClosed(42);

        now += TimeSpan.FromSeconds(2);
        Assert.True(state.TryBeginAttempt(42, now, out LanConnectContinueRunPublishAttempt secondVisit));
        state.MarkRetryable(secondVisit); // 联机方式选择框被取消
        state.EndAttempt(secondVisit);
        state.NotifyScreenClosed(42);

        now += TimeSpan.FromSeconds(2);
        Assert.True(state.TryBeginAttempt(42, now, out LanConnectContinueRunPublishAttempt thirdVisit));
        state.MarkSettled(thirdVisit);
        state.EndAttempt(thirdVisit);
    }
    [Fact]
    public void Rapid_reopen_waits_for_old_request_then_retries_without_throttle_or_duplicate_release()
    {
        LanConnectContinueRunPublishScreenState state = new(Retry);
        Assert.True(state.TryBeginAttempt(7, T0, out var oldAttempt));
        state.NotifyScreenClosed(7);

        Assert.False(state.TryBeginAttempt(7, T0, out _));
        Assert.True(state.IsAttemptStale(oldAttempt));
        state.MarkSettled(oldAttempt);
        state.EndAttempt(oldAttempt);

        Assert.True(state.TryBeginAttempt(7, T0, out var newAttempt));
        state.EndAttempt(oldAttempt);
        state.MarkRetryable(oldAttempt);
        Assert.False(state.TryBeginAttempt(7, T0 + Retry, out _));
        state.MarkSettled(newAttempt);
        state.EndAttempt(newAttempt);
        state.MarkRetryable(oldAttempt);
        Assert.False(state.TryBeginAttempt(7, T0 + Retry, out _));
    }

}
