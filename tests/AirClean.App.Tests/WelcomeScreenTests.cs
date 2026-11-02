using System.Text;
using System.Reflection;
using AirClean.App.Services;
using AirClean.App.ViewModels;
using AirClean.App.Views;
using AirClean.Core.Deleting;
using AirClean.Core.Models;
using AirClean.Core.Rules;
using AirClean.Core.Scanning;
using AirClean.Core.Platform;
using AirClean.Core.Trash;
using AirClean.Core.Volumes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Xunit;

[assembly: AvaloniaTestApplication(typeof(AirClean.App.Tests.TestAppBuilder))]

namespace AirClean.App.Tests;

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
        .UseSkia()
        .WithInterFont();
}

/// <summary>
/// Renders the real windows off-screen. Renderer output is written to
/// <c>%TEMP%/airclean-shots</c> so the visuals can be inspected while iterating.
/// </summary>
public sealed class FlowTests
{
    [AvaloniaFact]
    public void Welcome_screen_renders_in_both_languages_and_themes()
    {
        var viewModel = CreateViewModel("zh-CN", null);
        var window = Show(viewModel);

        Save(window.CaptureRenderedFrame()!, "welcome-zh-light.png");

        viewModel.SetThemeCommand.Execute(ThemeService.DarkMode);
        Pump();
        window.InvalidateVisual();
        Save(window.CaptureRenderedFrame()!, "welcome-zh-dark.png");

        viewModel.SetThemeCommand.Execute(ThemeService.LightMode);
        viewModel.ToggleLanguageCommand.Execute(null);
        Pump();
        window.InvalidateVisual();
        Save(window.CaptureRenderedFrame()!, "welcome-en-light.png");
    }

    [AvaloniaFact]
    public void Scan_then_confirm_then_clean_produces_results_and_completion()
    {
        var root = CreateFixture();

        try
        {
            var real = Environment.GetEnvironmentVariable("AIRCLEAN_REAL_SCAN") == "1";
            var rules = real ? RuleSet.ForCurrentPlatform() : LoadRules(root);
            var viewModel = real
                ? CreateViewModel("zh-CN", rules)
                : CreateViewModel("zh-CN", rules, volumes: [FixtureVolume(root)], duplicateRoots: [root]);
            var window = Show(viewModel);

            viewModel.ScanCommand.Execute(null);
            PumpUntil(() => viewModel.IsScanComplete);
            Pump(8);

            Assert.Equal(AppStage.Results, viewModel.Stage);
            Assert.True(viewModel.Categories.Sum(category => category.FileCount) > 0);
            Assert.StartsWith("扫描用时", viewModel.ScanElapsedText);
            window.InvalidateVisual();
            Save(window.CaptureRenderedFrame()!, "results-zh-light.png");

            viewModel.SetThemeCommand.Execute(ThemeService.DarkMode);
            Pump();
            window.InvalidateVisual();
            Save(window.CaptureRenderedFrame()!, "results-zh-dark.png");
            viewModel.SetThemeCommand.Execute(ThemeService.LightMode);
            Pump();

            var first = viewModel.Categories.First(category => category.FileCount > 0);
            first.ToggleExpandCommand.Execute(null);
            Pump();
            Assert.True(first.IsExpanded);
            Assert.NotEmpty(first.Details);
            window.InvalidateVisual();
            Save(window.CaptureRenderedFrame()!, "results-expanded.png");

            first.Details[0].IsSelected = false;
            Pump();
            Assert.True(first.HasPartialSelection);
            Assert.Null(first.VisibleSelectionState);
            Assert.True(first.IsFileView);

            window.InvalidateVisual();
            Save(window.CaptureRenderedFrame()!, "results-partial.png");

            Pump(4);
            Assert.Null(first.VisibleSelectionState);

            Assert.Equal(AppStage.Results, viewModel.Stage);

            // Partial selection offers "select all"; a full selection offers "clear".
            Assert.Equal("全选", first.SelectAllLabel);

            first.Details[0].IsSelected = true;
            Pump();
            Assert.Equal("全不选", first.SelectAllLabel);

            first.ToggleVisibleSelectionCommand.Execute(null);
            Pump();
            Assert.Equal(0, first.VisibleDetails.Count(row => row.IsSelected));
            Assert.Equal("全选", first.SelectAllLabel);

            first.ToggleVisibleSelectionCommand.Execute(null);
            Pump();
            Assert.All(first.VisibleDetails, row => Assert.True(row.IsSelected));

            first.Details[1].IsSelected = false;
            Pump();

            first.ShowDirectoriesCommand.Execute(null);
            Pump();
            window.InvalidateVisual();
            Save(window.CaptureRenderedFrame()!, "results-directories.png");

            if (first.DirectoryGroups.Count > 0)
            {
                first.DirectoryGroups[0].SelectCommand.Execute(null);
                Pump();
                Assert.True(first.HasFilter);
                window.InvalidateVisual();
                Save(window.CaptureRenderedFrame()!, "results-filtered.png");
            }

            first.ClearFilterCommand.Execute(null);
            first.ShowFilesCommand.Execute(null);
            Pump();

            first.ToggleExpandCommand.Execute(null);
            Pump();

            viewModel.OpenConfirmCommand.Execute(null);
            Pump();
            Assert.True(viewModel.IsConfirmVisible);
            window.InvalidateVisual();
            Save(window.CaptureRenderedFrame()!, "confirm-zh-light.png");

            viewModel.ConfirmCleanCommand.Execute(null);
            Pump(1);

            if (viewModel.IsDeleting)
            {
                Assert.True(viewModel.CanCancelClean);
                window.InvalidateVisual();
                Save(window.CaptureRenderedFrame()!, "cleaning.png");
            }

            PumpUntil(() => viewModel.Stage == AppStage.Completed);

            Assert.Equal(AppStage.Completed, viewModel.Stage);
            Assert.False(string.IsNullOrEmpty(viewModel.CompletedTitle));
            window.InvalidateVisual();
            Save(window.CaptureRenderedFrame()!, "completed-zh-light.png");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [AvaloniaFact]
    public void Welcome_page_lets_the_user_pick_a_drive_before_scanning()
    {
        var system = new VolumeInfo("C:\\", "System", "NTFS", 500_000_000_000, 40_000_000_000, IsSystem: true, IsRemovable: false);
        var data = new VolumeInfo("D:\\", "Data", "NTFS", 1_000_000_000_000, 120_000_000_000, IsSystem: false, IsRemovable: false);

        var viewModel = CreateViewModel("zh-CN", null, [system, data]);
        var window = Show(viewModel);

        // Defaults to the system drive.
        Assert.Equal(2, viewModel.Volumes.Count);
        Assert.True(viewModel.Volumes[0].IsSelected);
        Assert.False(viewModel.Volumes[1].IsSelected);
        Assert.Equal(AppStage.Welcome, viewModel.Stage);

        window.InvalidateVisual();
        Save(window.CaptureRenderedFrame()!, "welcome-volumes.png");

        // Choosing a drive must not start a scan by itself.
        viewModel.Volumes[1].ChooseCommand.Execute(null);
        Pump();

        Assert.Equal(AppStage.Welcome, viewModel.Stage);
        Assert.False(viewModel.Volumes[0].IsSelected);
        Assert.True(viewModel.Volumes[1].IsSelected);
        Assert.True(viewModel.IsVolumeMode);
        Assert.Contains("D:", viewModel.WelcomeScanHint);

        window.InvalidateVisual();
        Save(window.CaptureRenderedFrame()!, "welcome-volumes-data.png");
    }

    [AvaloniaFact]
    public void The_status_bar_reports_the_build_version()
    {
        var viewModel = CreateViewModel("en", null);
        var window = Show(viewModel);

        // The label must come from the assembly, so a release build reports its own tag.
        var informational = typeof(MainWindowViewModel).Assembly
            .GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        var expected = informational is null
            ? typeof(MainWindowViewModel).Assembly.GetName().Version!.ToString(3)
            : informational.Split('+')[0];

        Assert.Equal("AirClean " + expected, viewModel.VersionText);
        Assert.Equal(expected, MainWindowViewModel.AppVersion);

        window.Close();
    }

    [AvaloniaFact]
    public void Compact_window_and_settings_menu_do_not_break()
    {
        var viewModel = CreateViewModel("en", null);
        var window = Show(viewModel, width: 920, height: 620);

        Save(window.CaptureRenderedFrame()!, "welcome-compact.png");

        var settingsButton = window.FindControl<Button>("SettingsButton");
        Assert.NotNull(settingsButton);
        Assert.NotNull(settingsButton!.Flyout);

        settingsButton.Flyout!.ShowAt(settingsButton);
        Pump();
        settingsButton.Flyout.Hide();
    }

    [AvaloniaFact]
    public void Volume_mode_lists_large_files_and_requires_explicit_selection()
    {
        var root = CreateFixture();
        var realVolume = Environment.GetEnvironmentVariable("AIRCLEAN_REAL_VOLUME");

        try
        {
            var volumes = string.IsNullOrEmpty(realVolume)
                ? new[]
                {
                    new VolumeInfo(
                        Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar,
                        "Test drive",
                        "NTFS",
                        1_000_000,
                        500_000,
                        IsSystem: false,
                        IsRemovable: false)
                }
                : VolumeService.List();

            var threshold = string.IsNullOrEmpty(realVolume) ? 1_000L : 100L * 1024 * 1024;

            var viewModel = CreateViewModel("zh-CN", null, volumes, threshold);
            var window = Show(viewModel);

            var target = string.IsNullOrEmpty(realVolume)
                ? viewModel.Volumes[0]
                : viewModel.Volumes.First(volume => volume.Root.StartsWith(realVolume, StringComparison.OrdinalIgnoreCase));

            viewModel.SelectVolumeCommand.Execute(target);
            PumpUntil(() => viewModel.IsScanComplete, 180_000);
            Pump(8);

            Assert.Equal(AppStage.Results, viewModel.Stage);
            Assert.True(viewModel.IsVolumeMode);

            var large = viewModel.Categories.Single(category => category.Category == CleanCategory.LargeFiles);
            Assert.True(large.IsExpanded);
            Assert.True(large.FileCount >= 2);

            // Large files are never pre-selected: the clean button stays disabled.
            Assert.False(viewModel.CanClean);

            var caches = viewModel.Categories.Single(category => category.Category == CleanCategory.AppCache);
            Assert.False(caches.IsApplicable);

            large.Details[0].IsSelected = true;
            Pump();

            Assert.True(viewModel.CanClean);

            window.InvalidateVisual();
            Save(window.CaptureRenderedFrame()!, "volume-mode.png");

            viewModel.OpenConfirmCommand.Execute(null);
            Pump();
            Assert.True(viewModel.IsConfirmVisible);
            Assert.Single(viewModel.ConfirmItems);
            viewModel.CancelConfirmCommand.Execute(null);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [AvaloniaFact]
    public void Duplicate_scan_groups_identical_files_and_requires_explicit_selection()
    {
        var root = CreateDuplicateFixture();

        try
        {
            var viewModel = CreateViewModel(
                "zh-CN",
                null,
                volumes: [FixtureVolume(root)],
                duplicateRoots: [root]);
            var window = Show(viewModel);

            viewModel.ScanCommand.Execute(null);
            PumpUntil(() => viewModel.IsScanComplete);
            Pump(8);

            Assert.Equal(AppStage.Results, viewModel.Stage);

            var duplicates = viewModel.Categories.Single(category => category.Category == CleanCategory.Duplicates);
            Assert.True(duplicates.IsExpanded);
            Assert.Single(duplicates.DuplicateGroups);

            // The duplicate category starts with nothing selected, even though the cache
            // categories on the same scan are pre-selected.
            Assert.Equal(0, duplicates.SelectedFileCount);
            Assert.False(duplicates.DuplicateGroups[0].IsSelected);

            var beforeBytes = duplicates.SelectedBytes;

            duplicates.DuplicateGroups[0].IsSelected = true;
            Pump();
            Assert.True(duplicates.SelectedFileCount > 0);
            Assert.True(duplicates.SelectedBytes > beforeBytes);

            window.InvalidateVisual();
            Save(window.CaptureRenderedFrame()!, "duplicates.png");

            viewModel.OpenConfirmCommand.Execute(null);
            Pump();
            Assert.True(viewModel.IsConfirmVisible);
            Assert.Contains(viewModel.ConfirmItems, item => item.Category == CleanCategory.Duplicates);
            viewModel.CancelConfirmCommand.Execute(null);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateDuplicateFixture()
    {
        var root = Path.Combine(Path.GetTempPath(), "airclean-dups-ui", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "a"));
        Directory.CreateDirectory(Path.Combine(root, "b"));

        var content = new byte[1_200_000];
        for (var index = 0; index < content.Length; index++)
        {
            content[index] = (byte)((index * 17) % 251);
        }

        var older = Path.Combine(root, "a", "movie.bin");
        File.WriteAllBytes(older, content);
        File.SetLastWriteTimeUtc(older, DateTime.UtcNow.AddDays(-10));

        var newer = Path.Combine(root, "b", "movie-copy.bin");
        File.WriteAllBytes(newer, content);

        var other = Path.Combine(root, "b", "other.bin");
        File.WriteAllBytes(other, new byte[1_200_000]);

        return root;
    }

    private static MainWindow Show(MainWindowViewModel viewModel, double width = 1240, double height = 860)
    {
        var window = new MainWindow
        {
            DataContext = viewModel,
            Width = width,
            Height = height
        };

        window.Show();
        Pump();
        return window;
    }

    private static MainWindowViewModel CreateViewModel(
        string language,
        RuleSet? rules,
        IReadOnlyList<VolumeInfo>? volumes = null,
        long threshold = LargeFileScanner.DefaultThresholdBytes,
        IReadOnlyList<string>? duplicateRoots = null)
    {
        var settings = new AppSettings { Language = language };
        var localization = new LocalizationService();
        localization.Apply(language);

        var theme = new ThemeService(settings, () => { });
        var trash = new FakeTrash();
        var reveal = new FakeReveal();
        var volumeScan = new VolumeScanService(rules ?? RuleSet.ForCurrentPlatform(), trash);
        var journal = Path.Combine(Path.GetTempPath(), "airclean-shots", "journal");

        return new MainWindowViewModel(
            settings,
            localization,
            theme,
            () => { },
            volumeScan,
            new DeletionService(trash, journal),
            trash,
            reveal,
            threshold,
            volumes,
            duplicateRoots);
    }

    /// <summary>
    /// A stand-in for the system drive so tests never walk the real disk: same rules, same
    /// category set, but every root is inside the fixture.
    /// </summary>
    private static VolumeInfo FixtureVolume(string root) => new(
        Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar,
        "Fixture",
        "TEST",
        1_000_000_000,
        500_000_000,
        IsSystem: true,
        IsRemovable: false);

    private static string CreateFixture()
    {
        var root = Path.Combine(Path.GetTempPath(), "airclean-ui", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "cache", "level2"));
        Directory.CreateDirectory(Path.Combine(root, "logs"));

        Write(Path.Combine(root, "cache", "a.bin"), 120_000);
        Write(Path.Combine(root, "cache", "level2", "b.bin"), 240_000);
        Write(Path.Combine(root, "logs", "crash.log"), 32_000);

        return root;
    }

    private static void Write(string path, int size)
    {
        File.WriteAllBytes(path, new byte[size]);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-10));
    }

    private static RuleSet LoadRules(string root)
    {
        var escaped = root.Replace("\\", "\\\\");
        var json = $$"""
        {
          "schemaVersion": 1,
          "platform": "test",
          "rules": [
            {
              "id": "ui.temp",
              "category": "SystemTemp",
              "safety": "Safe",
              "roots": ["{{escaped}}"],
              "maxDepth": 4,
              "minAgeDays": 1,
              "source": "UI test fixture"
            }
          ]
        }
        """;

        return RuleSet.Load(new MemoryStream(Encoding.UTF8.GetBytes(json)));
    }

    private static void Pump(int rounds = 6)
    {
        for (var index = 0; index < rounds; index++)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(5);
        }
    }

    private static void PumpUntil(Func<bool> condition, int timeoutMilliseconds = 15_000)
    {
        var deadline = Environment.TickCount64 + timeoutMilliseconds;

        while (!condition() && Environment.TickCount64 < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(5);
        }

        Dispatcher.UIThread.RunJobs();
    }

    private static void Save(Avalonia.Media.Imaging.WriteableBitmap frame, string fileName)
    {
        var directory = Path.Combine(Path.GetTempPath(), "airclean-shots");
        Directory.CreateDirectory(directory);
        frame.Save(Path.Combine(directory, fileName));
    }

    private sealed class FakeTrash : ITrashService
    {
        public TrashSample? Sample { get; set; }

        public bool IsSupported => true;

        public TrashSample? Probe(string? rootPath = null) => Sample;

        public bool MoveToTrash(string path, out string? error)
        {
            error = null;
            return true;
        }

        public long Empty(string? rootPath = null) => 0;

        public void OpenTrash()
        {
        }
    }

    private sealed class FakeReveal : IRevealService
    {
        public List<string> Revealed { get; } = [];

        public bool IsSupported => true;

        public void Reveal(string path) => Revealed.Add(path);
    }
}
