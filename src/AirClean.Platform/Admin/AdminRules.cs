using AirClean.Core.Scanning;

namespace AirClean.Platform.Admin;

internal enum AdminRuleKind
{
    Directory,
    Journal
}

internal sealed record AdminRule(string Id, AdminRuleKind Kind, string Root);

internal static class AdminRules
{
    public static AdminRule? Find(string ruleId)
    {
        if (OperatingSystem.IsWindows() &&
            string.Equals(ruleId, AdminRuleIds.WindowsUpdateCache, StringComparison.Ordinal))
        {
            var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

            return string.IsNullOrWhiteSpace(windows)
                ? null
                : new AdminRule(
                    AdminRuleIds.WindowsUpdateCache,
                    AdminRuleKind.Directory,
                    Path.Combine(windows, "SoftwareDistribution", "Download"));
        }

        if (!OperatingSystem.IsLinux())
        {
            return null;
        }

        return ruleId switch
        {
            AdminRuleIds.LinuxAptCache => new AdminRule(
                AdminRuleIds.LinuxAptCache,
                AdminRuleKind.Directory,
                "/var/cache/apt/archives"),
            AdminRuleIds.LinuxJournald => new AdminRule(
                AdminRuleIds.LinuxJournald,
                AdminRuleKind.Journal,
                string.Empty),
            _ => null
        };
    }
}
