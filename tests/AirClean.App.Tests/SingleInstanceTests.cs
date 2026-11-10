using AirClean.App.Services;
using Xunit;

namespace AirClean.App.Tests;

public sealed class SingleInstanceTests
{
    [Fact]
    public void A_second_instance_signals_the_first_and_does_not_become_primary()
    {
        var key = Guid.NewGuid().ToString("N")[..16];

        using var first = SingleInstance.Acquire(key);
        using var signaled = new ManualResetEventSlim();

        Assert.True(first.IsFirst);
        first.Activated += (_, _) => signaled.Set();

        using var second = SingleInstance.Acquire(key);
        Assert.False(second.IsFirst);

        second.SignalExisting();

        Assert.True(signaled.Wait(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void A_third_launch_after_the_first_is_disposed_becomes_primary()
    {
        var key = Guid.NewGuid().ToString("N")[..16];

        using (var first = SingleInstance.Acquire(key))
        {
            Assert.True(first.IsFirst);
        }

        using var next = SingleInstance.Acquire(key);
        Assert.True(next.IsFirst);
    }
}
