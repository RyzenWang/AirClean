using AirClean.App.Services;
using AirClean.Core.Models;
using Xunit;

namespace AirClean.App.Tests;

public sealed class ReminderServiceTests
{
    [Fact]
    public void A_reminder_is_due_only_after_the_interval_and_when_enabled()
    {
        var settings = new AppSettings
        {
            ReminderEnabled = true,
            ReminderIntervalDays = 7
        };
        var localization = new LocalizationService();
        var messages = new List<string>();
        var saves = 0;

        var service = new ReminderService(settings, localization, messages.Add, () => saves++);
        var now = new DateTimeOffset(2026, 11, 11, 10, 0, 0, TimeSpan.Zero);

        Assert.True(service.Check(now));
        Assert.Single(messages);
        Assert.Equal(now, settings.LastReminderAt);
        Assert.Equal(1, saves);

        Assert.False(service.Check(now.AddDays(1)));
        Assert.Single(messages);

        Assert.True(service.Check(now.AddDays(7)));
        Assert.Equal(2, messages.Count);

        settings.ReminderEnabled = false;
        Assert.False(service.Check(now.AddDays(30)));
    }
}
