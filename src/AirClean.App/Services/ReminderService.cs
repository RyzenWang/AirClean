using AirClean.Core.Models;
using Avalonia.Threading;

namespace AirClean.App.Services;

/// <summary>
/// Periodic, opt-in reminder. It never scans or deletes anything: it only updates the
/// status bar and the tray tooltip. <see cref="Check"/> is pure enough to unit-test with
/// an explicit clock; the timer simply calls it every six hours.
/// </summary>
internal sealed class ReminderService : IDisposable
{
    private readonly AppSettings _settings;
    private readonly LocalizationService _localization;
    private readonly Action<string> _notify;
    private readonly Action _persist;
    private DispatcherTimer? _timer;

    public ReminderService(
        AppSettings settings,
        LocalizationService localization,
        Action<string> notify,
        Action persist)
    {
        _settings = settings;
        _localization = localization;
        _notify = notify;
        _persist = persist;
    }

    public void Start()
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromHours(6) };
        _timer.Tick += (_, _) => Check(DateTimeOffset.Now);
        _timer.Start();

        // Give the first window time to settle before the first reminder can appear.
        DispatcherTimer.RunOnce(() => Check(DateTimeOffset.Now), TimeSpan.FromSeconds(15));
    }

    /// <summary>Returns true when a reminder was shown and the timestamp was saved.</summary>
    public bool Check(DateTimeOffset now)
    {
        if (!_settings.ReminderEnabled)
        {
            return false;
        }

        var interval = TimeSpan.FromDays(Math.Max(1, _settings.ReminderIntervalDays));

        if (_settings.LastReminderAt is { } last && now - last < interval)
        {
            return false;
        }

        _settings.LastReminderAt = now;
        _persist();
        _notify(_localization.Format("L.ReminderMessageFormat", Math.Max(1, _settings.ReminderIntervalDays)));
        return true;
    }

    public void Dispose()
    {
        _timer?.Stop();
        _timer = null;
    }
}
