using System.Globalization;
using Avalonia;
using Avalonia.Controls;

namespace AirClean.App.Services;

/// <summary>
/// Lightweight runtime localization. Static XAML text uses DynamicResource keys such as
/// <c>L.StartScan</c>; text built in code goes through <see cref="T(string)"/>.
/// Applying a language swaps a runtime dictionary in the application resources, so the
/// window updates without being recreated.
/// </summary>
public sealed class LocalizationService
{
    public const string English = "en";
    public const string ChineseSimplified = "zh-CN";

    private static readonly IReadOnlyDictionary<string, (string En, string Zh)> Catalog =
        new Dictionary<string, (string En, string Zh)>(StringComparer.Ordinal)
        {
            ["L.Scan"] = ("Scan", "扫描"),
            ["L.ScanTooltip"] = ("Scan now (Ctrl+R)", "开始扫描（Ctrl+R）"),
            ["L.StartScan"] = ("Start scan", "开始扫描"),
            ["L.Tagline"] = ("A FEATHER-LIGHT DISK CLEANER", "轻盈磁盘清理"),
            ["L.WelcomeTitle1"] = ("Your disk,", "你的磁盘，"),
            ["L.WelcomeTitle2"] = ("a little lighter.", "轻一点。"),
            ["L.WelcomeSubtitle"] = (
                "Scan, preview, clean — three steps. No account, no uploads, no drivers, and nothing permanently deleted behind your back.",
                "扫描、预览、清理，三步完成。不需要账号，不上传文件名，不安装驱动，也不会偷偷永久删除任何东西。"),
            ["L.WelcomeMeta"] = ("Runs locally  ·  Free  ·  MIT licensed", "本地运行 · 免费 · MIT 许可"),
            ["L.Language"] = ("Language", "语言"),
            ["L.Appearance"] = ("Appearance", "外观"),
            ["L.Settings"] = ("Settings", "设置"),
            ["L.SettingsTitle"] = ("Settings", "设置"),
            ["L.SectionAppearance"] = ("APPEARANCE", "外观"),
            ["L.SectionScan"] = ("SCANNING", "扫描"),
            ["L.SectionBehaviour"] = ("BEHAVIOUR", "行为"),
            ["L.Theme"] = ("Theme", "主题"),
            ["L.QuickScan"] = ("Quick scan", "快速扫描"),
            ["L.QuickScanHint"] = ("Caches, logs and the Trash only.", "只扫描缓存、日志与废纸篓。"),
            ["L.FullScan"] = ("Full scan", "全盘扫描"),
            ["L.FullScanHint"] = ("Also finds large files and duplicates; slower on cold drives.", "同时查找大文件与重复文件；冷缓存下更慢。"),
            ["L.Exclusions"] = ("Excluded folders", "排除目录"),
            ["L.ExclusionsHint"] = ("These folders are never scanned or deleted.", "这些目录不会被扫描，也不会被删除。"),
            ["L.AddFolder"] = ("Add folder", "添加目录"),
            ["L.RemoveExclusion"] = ("Remove", "移除"),
            ["L.NoExclusions"] = ("No excluded folders.", "没有排除目录。"),
            ["L.Thresholds"] = ("Thresholds", "阈值"),
            ["L.LargeFileThreshold"] = ("Large file threshold (MB)", "大文件阈值（MB）"),
            ["L.LargeFileLimit"] = ("Large files listed", "大文件列表上限"),
            ["L.DuplicateMinSize"] = ("Smallest duplicate (MB)", "重复文件最小体积（MB）"),
            ["L.DuplicateGroupLimit"] = ("Duplicate groups listed", "重复文件组上限"),
            ["L.HashBudget"] = ("Hashing budget (GB)", "哈希预算（GB）"),
            ["L.CheckUpdates"] = ("Check for updates on startup", "启动时检查更新"),
            ["L.CloseToTray"] = ("Close to the tray", "关闭到托盘"),
            ["L.MinimizeToTray"] = ("Minimize to the tray", "最小化到托盘"),
            ["L.RunAtLogin"] = ("Start at login", "登录时启动"),
            ["L.ReminderEnabled"] = ("Remind me to clean up", "提醒我清理"),
            ["L.ReminderInterval"] = ("Reminder interval (days)", "提醒间隔（天）"),
            ["L.ReminderHint"] = ("A reminder only; nothing is scanned or deleted automatically.", "只提醒，不会自动扫描或删除。"),
            ["L.Save"] = ("Save", "保存"),
            ["L.UseAdminHelper"] = (
                "Use administrator rights (faster scans and system cleanup)",
                "使用管理员权限（加速扫描与系统清理）"),
            ["L.ThemeSystem"] = ("Follow system", "跟随系统"),
            ["L.ThemeLight"] = ("Light", "浅色"),
            ["L.ThemeDark"] = ("Dark", "深色"),
            ["L.ChineseSimplified"] = ("简体中文", "简体中文"),
            ["L.MoreSettingsLater"] = ("More settings in a later version", "更多设置将在后续版本提供"),
            ["L.Ready"] = ("Ready", "就绪"),
            ["L.RestartToUpdateFormat"] = ("Restart to update {0}", "重启以更新 {0}"),
            ["L.CheckForUpdates"] = ("Check for updates", "检查更新"),
            ["L.LocalOnly"] = ("runs locally, nothing uploaded", "本地运行，不上传任何文件"),
            ["L.ScanNotReady"] = (
                "Scan engine lands in M1 — this build is the interface shell.",
                "扫描引擎将在 M1 接入，这一版是界面外壳。"),
            ["L.DiskUsed"] = ("Used", "已用"),
            ["L.DiskFree"] = ("Free", "可用"),
            ["L.DiskTotal"] = ("Total", "总计"),
            ["L.DiskUsage"] = ("Disk usage", "磁盘占用"),
            ["L.UsedOfFormat"] = ("used of {0}", "已用 / {0}"),
            ["L.RecentCleanups"] = ("RECENT CLEANUPS", "最近清理"),
            ["L.NoRecentCleanups"] = ("No cleanups yet", "还没有清理记录"),
            ["L.VersionFormat"] = ("AirClean {0}", "AirClean {0}"),

            ["L.Scanning"] = ("Scanning…", "正在扫描…"),
            ["L.ScanCanceled"] = ("Scan canceled", "扫描已取消"),
            ["L.ScanCanceledNote"] = (
                "The scan stopped early; these results only cover the categories that finished.",
                "扫描提前结束，当前结果只包含已完成的分类。"),
            ["L.CategoryCanceled"] = ("Not finished", "未完成"),
            ["L.ScanningLarge"] = ("Looking for large files…", "正在查找大文件…"),
            ["L.ScanningDuplicates"] = ("Comparing duplicates…", "正在比对重复文件…"),
            ["L.Pending"] = ("Waiting…", "等待中…"),
            ["L.ScanLargeFormat"] = ("{0:N0} files walked · {1:N0} large", "已遍历 {0:N0} 个文件 · {1:N0} 个大文件"),
            ["L.ScanDuplicatesFormat"] = ("{0:N0} files checked · {1:N0} groups", "已检查 {0:N0} 个文件 · 找到 {1:N0} 组"),
            ["L.FoundVolumeFormat"] = ("{0} · {1:N0} listed of {2:N0} found", "{0} · 已列出 {1:N0} / 共 {2:N0} 个"),
                        ["L.ScanFound"] = ("Reclaimable so far", "已找到可清理"),
            ["L.ScanFilesFormat"] = ("{0:N0} files scanned", "已扫描 {0:N0} 个文件"),
            ["L.ItemsCountFormat"] = ("{0:N0} files", "{0:N0} 个文件"),
            ["L.ItemCountOneFormat"] = ("1 file", "1 个文件"),
            ["L.FoundSummaryFormat"] = ("Found {0} · {1:N0} files", "共发现 {0} · {1:N0} 个文件"),
            ["L.FoundLargeFormat"] = ("{0} · {1:N0} large files, top {2} listed", "{0} · {1:N0} 个大文件，已列出最大的 {2} 个"),
            ["L.SystemVolume"] = ("System drive", "系统盘"),
            ["L.ScanTarget"] = ("SCAN TARGET", "扫描目标"),
            ["L.VolumeFreeFormat"] = ("{0} free", "可用 {0}"),
            ["L.WelcomeScopeSystem"] = ("Caches, logs, large files and duplicates on {0}.", "扫描 {0} 上的缓存、日志、大文件与重复文件。"),
            ["L.WelcomeScopeQuick"] = ("Caches, logs and the Trash on {0}.", "扫描 {0} 上的缓存、日志与废纸篓。"),
            ["L.WelcomeScopeVolume"] = ("Large files and duplicates on {0} ({1}).", "扫描 {0} 上的大文件与重复文件（{1}）。"),
            ["L.WelcomeScopeVolumeQuick"] = ("Large files on {0} ({1}).", "扫描 {0} 上的大文件（{1}）。"),
            ["L.ScanElapsedFormat"] = ("scanned in {0}s", "扫描用时 {0} 秒"),
            ["L.SelectionSummaryFormat"] = ("Selected {0:N0} files · {1}", "已选 {0:N0} 个文件 · {1}"),
            ["L.CleanSelected"] = ("Clean selected", "清理所选"),
            ["L.CleanSelectedFormat"] = ("Clean {0}", "清理所选 {0}"),
            ["L.Reclaimable"] = ("Reclaimable", "可清理"),
            ["L.NothingFound"] = ("Nothing to clean was found.", "没有发现可清理的内容。"),
            ["L.ScanComplete"] = ("Scan complete", "扫描完成"),
            ["L.ScanFailedFormat"] = ("Scan failed: {0}", "扫描失败：{0}"),
            ["L.CleanComplete"] = ("Cleanup complete", "清理完成"),
            ["L.CleanCanceled"] = ("Cleanup stopped partway", "清理已中止"),
            ["L.CleanPercentFormat"] = ("{0}%", "{0}%"),
            ["L.CleanCancel"] = ("Stop", "停止"),
            ["L.CleaningTitle"] = ("Cleaning…", "正在清理…"),
            ["L.CleanFailedFormat"] = ("Cleanup failed: {0}", "清理失败：{0}"),
            ["L.AbortFailureRate"] = (
                "Cleanup stopped early: too many files failed. The rest were left untouched.",
                "清理提前中止：失败文件过多，其余文件未处理。"),
            ["L.AbortDiskFull"] = (
                "There is not enough free space to move files to the Trash. Switch to permanent deletion and try again.",
                "磁盘或废纸篓空间不足，无法移到废纸篓。请改用永久删除后重试。"),

            ["L.CategorySystemTemp"] = ("System temp", "系统临时文件"),
            ["L.CategorySystemTempDesc"] = ("Temporary files, update caches and crash dumps", "临时目录、更新缓存、崩溃转储与旧安装包"),
            ["L.CategoryAppCache"] = ("App caches", "应用缓存"),
            ["L.CategoryAppCacheDesc"] = ("Browser and app caches that rebuild themselves", "浏览器与应用缓存，可自行重建"),
            ["L.CategoryLogs"] = ("Logs & diagnostics", "日志与诊断"),
            ["L.CategoryLogsDesc"] = ("Crash reports and diagnostic archives", "崩溃报告与诊断归档"),
            ["L.CategorySystemUpdates"] = ("Update & package cache", "系统更新与包缓存"),
            ["L.CategorySystemUpdatesDesc"] = (
                "Downloaded updates and package archives; needs administrator rights",
                "已下载的系统更新与软件包缓存；需要管理员权限"),
            ["L.CategorySystemLogs"] = ("System journal", "系统日志"),
            ["L.CategorySystemLogsDesc"] = (
                "systemd journal; keeps the last 14 days and at most 500 MB",
                "systemd journal；清理后保留最近 14 天且不超过 500 MB"),
            ["L.ActionCount"] = ("1 cleanup", "1 项清理"),
            ["L.CategoryTrash"] = ("Trash", "废纸篓"),
            ["L.CategoryTrashDesc"] = ("Emptied immediately, cannot be undone", "清空后立即释放空间，无法恢复"),
            ["L.CategoryDev"] = ("Developer caches", "开发缓存"),
            ["L.CategoryDevDesc"] = ("Package manager and build caches; regenerated on demand", "包管理器与构建缓存，需要时重新生成"),
            ["L.CategoryLargeFiles"] = ("Large files", "大文件"),
            ["L.CategoryLargeFilesDesc"] = ("Large files on this drive; pick files yourself", "此盘上的大文件，逐条手动挑选"),
            ["L.CategoryDuplicates"] = ("Duplicates", "重复文件"),
            ["L.CategoryDuplicatesDesc"] = ("Identical copies; one of each group is kept", "内容完全相同的副本，每组保留一份"),
            ["L.DuplicateSummaryFormat"] = ("{0} copies · {1} each · {2} reclaimable", "{0} 份 · 各 {1} · 可释放 {2}"),
            ["L.DuplicateKeepFormat"] = ("keep {0}", "保留 {0}"),
            ["L.DuplicateSummaryLineFormat"] = ("{0:N0} groups · {1} reclaimable", "共 {0:N0} 组重复 · 可释放 {1}"),
            ["L.SelectedNoteFormat"] = ("{0} selected · {1}", "已选 {0} 项 · {1}"),
            ["L.LocalVolume"] = ("Local disk", "本地磁盘"),
            ["L.VolumeItemFormat"] = ("{0} · {1} free", "{0} · 可用 {1}"),
            ["L.ScanScopeVolume"] = ("Only the files you tick are removed. System folders are skipped.", "只删除你勾选的文件，系统目录不会被扫描。"),
            ["L.CategoryCountFormat"] = ("{0:N0} files", "{0:N0} 个文件"),
            ["L.DetailsSummaryFormat"] = ("Showing {0:N0} of {1:N0} files · largest first", "显示 {0:N0} / {1:N0} 个文件 · 按大小排序"),
            ["L.DirectorySummaryFormat"] = ("{0:N0} folders · aggregated from the top {1:N0} files", "共 {0:N0} 个目录 · 由前 {1:N0} 个文件聚合"),
            ["L.FilteredCountFormat"] = ("{0:N0} files in this folder", "此目录下 {0:N0} 个文件"),
            ["L.PartialNoteFormat"] = ("{0:N0} of {1:N0} selected", "已选 {0:N0} / {1:N0} 个"),
            ["L.FilesTab"] = ("Files", "文件"),
            ["L.DirectoriesTab"] = ("Folders", "目录"),
            ["L.UnknownFolder"] = ("Unknown folder", "未知目录"),
            ["L.RevealInFolder"] = ("Show in folder", "在文件夹中显示"),
            ["L.SelectAll"] = ("Select all", "全选"),
            ["L.SelectNone"] = ("Clear", "全不选"),
            ["L.ClearFilter"] = ("Clear filter", "清除筛选"),
            ["L.ShowDetails"] = ("Show files", "查看文件"),
            ["L.HideDetails"] = ("Hide files", "收起文件"),
            ["L.PermanentBadge"] = ("PERM", "永久"),
            ["L.Storage"] = ("STORAGE", "存储空间"),
            ["L.ThisScan"] = ("THIS SCAN", "本次扫描"),
            ["L.Categories"] = ("CATEGORIES", "分类"),
            ["L.CleanupDetails"] = ("CLEANUP DETAILS", "清理明细"),
            ["L.ProtectedNote"] = ("Protected items are never listed", "受保护项目不会被列出"),
            ["L.ScanScope"] = ("Only caches, logs and the Trash are scanned. Documents are never touched.", "只扫描缓存、日志与废纸篓，不会碰你的文档。"),
            ["L.ScanScopeQuick"] = ("Quick scan: caches, logs and the Trash. Documents are never touched.", "快速扫描：缓存、日志与废纸篓，不会碰你的文档。"),
            ["L.ScanScopeFull"] = ("Full scan: also looks for large files and duplicate copies.", "全盘扫描：同时查找大文件与重复文件。"),
            ["L.LimitedNote"] = ("Duplicate hashing hit its cap — clean, then scan again for more.", "重复文件比对已达到上限，清理后可再次扫描。"),
            ["L.LimitedGroupsNoteFormat"] = (
                "Only the first {0} duplicate groups are listed; {1} more were found.",
                "只列出前 {0} 组重复文件；另有 {1} 组未列出。"),

            ["L.ConfirmTitleFormat"] = ("Clean {0}?", "清理 {0}？"),
            ["L.ConfirmNoteTrash"] = ("{0} moves to the Trash and can be restored.", "{0} 将移到废纸篓，可以恢复。"),
            ["L.ConfirmNotePermanentTrash"] = ("{0} in the Trash is deleted permanently.", "废纸篓中的 {0} 将被永久删除。"),
            ["L.ConfirmNotePermanentAdmin"] = ("{0} of system files will be deleted permanently.", "{0} 的系统文件将被永久删除。"),
            ["L.ConfirmPermanentOption"] = ("Skip the Trash and delete permanently", "不经过废纸篓，直接永久删除"),
            ["L.ConfirmClean"] = ("Clean now", "开始清理"),
            ["L.Cancel"] = ("Cancel", "取消"),
            ["L.Deleting"] = ("Cleaning…", "正在清理…"),
            ["L.DeletingProgressFormat"] = ("{0:N0} / {1:N0}", "{0:N0} / {1:N0}"),

            ["L.DoneTitleFormat"] = ("Cleaned {0}", "已清理 {0}"),
            ["L.UndoneTitleFormat"] = ("Restored {0}", "已恢复 {0}"),
            ["L.DoneSubTrashFormat"] = ("{0} moved to the Trash", "{0} 已移到废纸篓"),
            ["L.DoneSubFreedFormat"] = ("{0} deleted permanently", "{0} 已永久删除"),
            ["L.DoneNoteTrashFormat"] = ("{0} is still in the Trash — it frees up when you empty it.", "还有 {0} 在废纸篓里，清空后才会释放。"),
            ["L.Done"] = ("Done", "完成"),
            ["L.UndoLastClean"] = ("Undo this cleanup", "撤销本次清理"),
            ["L.UndoShort"] = ("Undo", "撤销"),
            ["L.Undoing"] = ("Undoing…", "正在撤销…"),
            ["L.UndoResultFormat"] = ("Restored {0:N0} files · {1}", "已恢复 {0:N0} 个文件 · {1}"),
            ["L.UndoPartialFormat"] = (
                "Restored {0:N0} of {1:N0} files · {2}",
                "已恢复 {0:N0} / {1:N0} 个文件 · {2}"),
            ["L.OpenTrash"] = ("Open Trash", "打开回收站"),
            ["L.EmptyTrashNow"] = ("Empty Trash", "清空废纸篓"),
            ["L.EmptyingTrash"] = ("Emptying…", "正在清空…"),
            ["L.EmptyTrashTitle"] = ("Empty the Trash?", "清空废纸篓？"),
            ["L.EmptyTrashNote"] = (
                "This permanently deletes everything in this drive's Trash, including files from other apps and earlier cleanups. It cannot be undone.",
                "将永久删除该磁盘废纸篓中的全部内容，包括其他应用和更早清理的文件。此操作无法恢复。"),
            ["L.EmptyTrashConfirm"] = ("Empty Trash", "清空"),
            ["L.EmptyTrashDoneFormat"] = ("Freed {0} from the Trash", "已从废纸篓释放 {0}"),
            ["L.EmptyTrashFailedFormat"] = ("Could not empty the Trash: {0}", "清空废纸篓失败：{0}"),
            ["L.Rescan"] = ("Scan again", "重新扫描"),
            ["L.SkippedFormat"] = ("Skipped {0:N0} files", "已跳过 {0:N0} 个文件"),
            ["L.SkippedOne"] = ("Skipped 1 file", "已跳过 1 个文件"),
            ["L.SkippedItemFormat"] = ("{0} {1}", "{0} {1}"),
            ["L.SkippedInUse"] = ("in use", "使用中"),
            ["L.SkippedPermission"] = ("no permission", "权限不足"),
            ["L.SkippedChanged"] = ("changed since scan", "扫描后已变更"),
            ["L.SkippedNotFound"] = ("no longer exists", "已不存在"),
            ["L.SkippedProtected"] = ("protected path", "受保护路径"),
            ["L.SkippedReparse"] = ("link or reparse point", "链接或重解析点"),
            ["L.SkippedDeleteFailed"] = ("delete failed", "删除失败"),
            ["L.SkippedExcluded"] = ("excluded folder", "已排除目录"),
            ["L.SkippedTruncated"] = ("Only the first 500 skipped files are listed.", "只列出前 500 个跳过的文件。"),
            ["L.ExportCsv"] = ("Export CSV", "导出 CSV"),
            ["L.Exporting"] = ("Exporting…", "正在导出…"),
            ["L.ExportSaved"] = ("Report saved", "报告已保存"),
            ["L.ExportCanceled"] = ("Export canceled", "已取消导出")
        };

    private ResourceDictionary? _dictionary;

    public string Language { get; private set; } = English;

    public bool IsChinese => string.Equals(Language, ChineseSimplified, StringComparison.OrdinalIgnoreCase);

    public string LanguageShortLabel => IsChinese ? "中" : "EN";

    public CultureInfo Culture => IsChinese
        ? CultureInfo.GetCultureInfo("zh-CN")
        : CultureInfo.GetCultureInfo("en-US");

    public event EventHandler? LanguageChanged;

    public void Apply(string? language)
    {
        var normalized = string.Equals(language, ChineseSimplified, StringComparison.OrdinalIgnoreCase) ||
                         (language?.StartsWith("zh", StringComparison.OrdinalIgnoreCase) ?? false)
            ? ChineseSimplified
            : English;

        var changed = !string.Equals(Language, normalized, StringComparison.Ordinal) || _dictionary is null;
        Language = normalized;

        if (Application.Current is { } application)
        {
            var dictionary = new ResourceDictionary();
            foreach (var pair in Catalog)
            {
                dictionary[pair.Key] = IsChinese ? pair.Value.Zh : pair.Value.En;
            }

            if (_dictionary is not null)
            {
                application.Resources.MergedDictionaries.Remove(_dictionary);
            }

            application.Resources.MergedDictionaries.Add(dictionary);
            _dictionary = dictionary;
        }

        if (changed)
        {
            LanguageChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public string T(string key) =>
        Catalog.TryGetValue(key, out var value)
            ? (IsChinese ? value.Zh : value.En)
            : key;

    public string Format(string key, params object[] args) =>
        string.Format(Culture, T(key), args);

    /// <summary>Removes this instance's resource dictionary from the application.</summary>
    public void Dispose()
    {
        if (_dictionary is not null && Application.Current is { } application)
        {
            application.Resources.MergedDictionaries.Remove(_dictionary);
        }

        _dictionary = null;
    }
}
