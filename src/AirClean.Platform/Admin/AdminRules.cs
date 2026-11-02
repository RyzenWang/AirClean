using AirClean.Core.Scanning;

namespace AirClean.Platform.Admin;

internal sealed record AdminRule(string Id, string Root);

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
                    Path.Combine(windows, "SoftwareDistribution", "Download"));
        }

        if (OperatingSystem.IsLinux() &&
            string.Equals(ruleId, AdminRuleIds.LinuxAptCache, StringComparison.Ordinal))
        {
            return new AdminRule(AdminRuleIds.LinuxAptCache, "/var/cache/apt/archives");
        }

        return null;
    }
}
