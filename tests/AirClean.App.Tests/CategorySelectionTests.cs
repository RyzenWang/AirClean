using AirClean.App.Services;
using AirClean.App.ViewModels;
using AirClean.Core.Models;
using Avalonia.Headless.XUnit;
using Xunit;

namespace AirClean.App.Tests;

public sealed class CategorySelectionTests
{
    private readonly LocalizationService _localization = new();
    private readonly FakeReveal _reveal = new();

    [AvaloniaFact]
    public void Administrator_action_categories_are_all_or_nothing()
    {
        var category = new CategoryViewModel(
            CleanCategory.SystemLogs,
            _localization,
            _reveal.Reveal,
            revealSupported: true);

        category.Apply(new CategoryScan
        {
            Category = CleanCategory.SystemLogs,
            Safety = Safety.Caution,
            IsPermanent = true,
            AdminRuleId = "linux.journald",
            IsAdminAction = true,
            Bytes = 1_024,
            FileCount = 1
        });

        Assert.False(category.CanExpand);
        Assert.Equal("1 cleanup", category.CountText);
        Assert.Equal(0, category.SelectedFileCount);

        category.SelectionState = true;

        Assert.Equal(1, category.SelectedFileCount);
        Assert.Equal(1_024, category.SelectedBytes);
        Assert.Same(category.Scan, category.BuildScan());
    }

    [AvaloniaFact]
    public void All_items_start_selected_for_safe_categories()
    {
        var category = Create();

        Assert.True(category.SelectionState);
        Assert.Equal(3, category.SelectedFileCount);
        Assert.Equal(300, category.SelectedBytes);
    }

    [AvaloniaFact]
    public void Unchecking_one_file_makes_the_category_indeterminate()
    {
        var category = Create();

        category.Details[0].IsSelected = false;

        Assert.Null(category.SelectionState);
        Assert.Equal(2, category.SelectedFileCount);
        Assert.Equal(200, category.SelectedBytes);
        Assert.True(category.HasPartialSelection);

        var scan = category.BuildScan();
        Assert.NotNull(scan);
        Assert.Equal(2, scan!.FileCount);
        Assert.DoesNotContain(scan.Items, item => item.Path == category.Details[0].FullPath);
    }

    [AvaloniaFact]
    public void Turning_the_category_off_clears_the_scan()
    {
        var category = Create();

        category.SelectionState = false;

        Assert.Equal(0, category.SelectedFileCount);
        Assert.Null(category.BuildScan());
        Assert.All(category.Details, row => Assert.False(row.IsSelected));
    }

    [AvaloniaFact]
    public void Turning_it_back_on_selects_everything_again()
    {
        var category = Create();
        category.Details[0].IsSelected = false;

        category.SelectionState = true;

        Assert.True(category.SelectionState);
        Assert.Equal(3, category.SelectedFileCount);
        Assert.All(category.Details, row => Assert.True(row.IsSelected));
    }

    [AvaloniaFact]
    public void Directories_are_grouped_and_filtering_drills_in()
    {
        var category = Create();

        Assert.Equal(2, category.DirectoryGroups.Count);

        var group = category.DirectoryGroups[0];
        group.SelectCommand.Execute(null);

        Assert.True(category.HasFilter);
        Assert.False(category.IsDirectoryView);
        Assert.Equal(group.FileCount, category.VisibleDetails.Count);

        category.ClearFilterCommand.Execute(null);
        Assert.False(category.HasFilter);
        Assert.Equal(3, category.VisibleDetails.Count);
    }

    [AvaloniaFact]
    public void Select_all_applies_to_the_filtered_list_only()
    {
        var category = Create();

        category.Details[0].IsSelected = false;
        Assert.Null(category.VisibleSelectionState);

        // Clearing the filter narrows the list to one folder, then select-all covers it.
        var folder = category.DirectoryGroups.First(group => group.FileCount == 1);
        folder.SelectCommand.Execute(null);
        Assert.Single(category.VisibleDetails);

        category.VisibleSelectionState = false;
        Assert.All(category.VisibleDetails, row => Assert.False(row.IsSelected));

        category.VisibleSelectionState = true;
        Assert.All(category.VisibleDetails, row => Assert.True(row.IsSelected));

        category.ClearFilterCommand.Execute(null);
    }

    [AvaloniaFact]
    public void A_folder_checkbox_selects_every_file_in_it()
    {
        var category = Create();
        category.SelectionState = false;
        Assert.Equal(0, category.SelectedFileCount);

        // The folder with two files.
        var folder = category.DirectoryGroups.First(group => group.FileCount == 2);
        folder.IsSelected = true;

        Assert.Equal(2, category.SelectedFileCount);
        Assert.True(category.HasPartialSelection);
        Assert.NotNull(category.BuildScan());
        Assert.Equal(2, category.BuildScan()!.FileCount);

        folder.IsSelected = false;
        Assert.Equal(0, category.SelectedFileCount);
        Assert.Null(category.BuildScan());
    }

    [AvaloniaFact]
    public void Reveal_invokes_the_platform_service()
    {
        var category = Create();

        category.Details[0].RevealCommand.Execute(null);

        Assert.Single(_reveal.Revealed);
        Assert.Equal(category.Details[0].FullPath, _reveal.Revealed[0]);
    }

    private CategoryViewModel Create()
    {
        var root = Path.Combine(Path.GetTempPath(), "airclean-select");
        var scan = new CategoryScan { Category = CleanCategory.SystemTemp, Safety = Safety.Safe };
        scan.Add(new CleanItem(Path.Combine(root, "a", "one.bin"), root, 100, DateTime.UtcNow.AddDays(-3)));
        scan.Add(new CleanItem(Path.Combine(root, "a", "two.bin"), root, 100, DateTime.UtcNow.AddDays(-3)));
        scan.Add(new CleanItem(Path.Combine(root, "b", "three.bin"), root, 100, DateTime.UtcNow.AddDays(-3)));

        var category = new CategoryViewModel(CleanCategory.SystemTemp, _localization, _reveal.Reveal, true);
        category.Apply(scan);
        return category;
    }

    private sealed class FakeReveal : Core.Platform.IRevealService
    {
        public List<string> Revealed { get; } = [];

        public bool IsSupported => true;

        public void Reveal(string path) => Revealed.Add(path);
    }
}
