using System.Globalization;
using System.Management;
using Microsoft.Win32;
using System.Runtime.Versioning;

namespace ExteraMonitor.Services;

/// <summary>Collects read-only Windows security and virtualization settings that can prevent a kernel driver from loading.</summary>
[SupportedOSPlatform("windows")]
internal static class DriverSecurityDiagnostics
{
    private const string DeviceGuardNamespace = @"root\Microsoft\Windows\DeviceGuard";
    private const string DeviceGuardClass = "Win32_DeviceGuard";

    public static string Collect()
    {
        if (!OperatingSystem.IsWindows()) return "Windows security diagnostics are only available on Windows.";

        var deviceGuard = ReadDeviceGuard();
        var lines = new List<string>
        {
            $"HVCI / Memory Integrity: configured={HasCode(deviceGuard.ConfiguredServices, 2)}; running={HasCode(deviceGuard.RunningServices, 2)}; registryEnabled={ReadRegistryDword(@"SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity", "Enabled")}; policy={ReadRegistryDword(@"SOFTWARE\Policies\Microsoft\Windows\DeviceGuard", "HypervisorEnforcedCodeIntegrity")}",
            $"VBS: {DescribeStatus(deviceGuard.VbsStatus, new Dictionary<uint, string> { [0] = "Disabled", [1] = "EnabledNotRunning", [2] = "Running" })}; registryEnabled={ReadRegistryDword(@"SYSTEM\CurrentControlSet\Control\DeviceGuard", "EnableVirtualizationBasedSecurity")}; policy={ReadRegistryDword(@"SOFTWARE\Policies\Microsoft\Windows\DeviceGuard", "EnableVirtualizationBasedSecurity")}",
            $"HypervisorPresent: {ReadHypervisorPresent()}",
            $"Hyper-V feature: {ReadHyperVFeatures()}",
            $"Vulnerable Driver Blocklist: registryEnabled={ReadRegistryDword(@"SYSTEM\CurrentControlSet\Control\CI\Config", "VulnerableDriverBlocklistEnable")}",
            $"WDAC / Kernel CI: kernelPolicy={DescribeStatus(deviceGuard.KernelCiStatus, EnforcementNames)}; userModePolicy={DescribeStatus(deviceGuard.UserModeCiStatus, EnforcementNames)}; activePolicyFiles={ReadActivePolicyFiles()}"
        };

        return string.Join(Environment.NewLine, lines);
    }

    public static bool? IsHvciRunning()
    {
        if (!OperatingSystem.IsWindows()) return null;
        return HasCode(ReadDeviceGuard().RunningServices, 2);
    }

    private static readonly Dictionary<uint, string> EnforcementNames = new()
    {
        [0] = "Off",
        [1] = "Audit",
        [2] = "Enforced"
    };

    private static DeviceGuardState ReadDeviceGuard()
    {
        try
        {
            var scope = new ManagementScope($@"\\.\{DeviceGuardNamespace}");
            var options = new System.Management.EnumerationOptions { ReturnImmediately = false, Timeout = TimeSpan.FromSeconds(2) };
            using var searcher = new ManagementObjectSearcher(
                scope,
                new ObjectQuery($"SELECT * FROM {DeviceGuardClass}"), options);
            using var results = searcher.Get();
            var row = results.Cast<ManagementObject>().FirstOrDefault();
            if (row is null) return new DeviceGuardState(null, [], [], null, null);

            return new DeviceGuardState(
                ReadUInt(row["VirtualizationBasedSecurityStatus"]),
                ReadUIntArray(row["SecurityServicesConfigured"]),
                ReadUIntArray(row["SecurityServicesRunning"]),
                ReadUInt(row["CodeIntegrityPolicyEnforcementStatus"]),
                ReadUInt(row["UsermodeCodeIntegrityPolicyEnforcementStatus"]));
        }
        catch (Exception ex)
        {
            DriverDiagnostics.Write("driver.security.deviceguard.failed", $"WMI query failed: {ex.GetType().Name}: {ex.Message}");
            return new DeviceGuardState(null, [], [], null, null);
        }
    }

    private static string ReadHypervisorPresent()
    {
        try
        {
            var options = new System.Management.EnumerationOptions { ReturnImmediately = false, Timeout = TimeSpan.FromSeconds(2) };
            using var searcher = new ManagementObjectSearcher(
                new ManagementScope(@"\\.\root\CIMV2"),
                new ObjectQuery("SELECT HypervisorPresent FROM Win32_ComputerSystem"),
                options);
            using var results = searcher.Get();
            var row = results.Cast<ManagementObject>().FirstOrDefault();
            return row is null ? "Unknown (no WMI result)" : FormatValue(row["HypervisorPresent"]);
        }
        catch (Exception ex)
        {
            return $"Unknown ({ex.GetType().Name})";
        }
    }

    private static string ReadHyperVFeatures()
    {
        try
        {
            var options = new System.Management.EnumerationOptions { ReturnImmediately = false, Timeout = TimeSpan.FromSeconds(2) };
            using var searcher = new ManagementObjectSearcher(
                new ManagementScope(@"\\.\root\CIMV2"),
                new ObjectQuery("SELECT Name, InstallState FROM Win32_OptionalFeature WHERE Name LIKE 'Microsoft-Hyper-V%'"),
                options);
            using var results = searcher.Get();
            var features = results.Cast<ManagementObject>()
                .Select(row => $"{FormatValue(row["Name"])}={DescribeStatus(ReadUInt(row["InstallState"]), OptionalFeatureNames)}")
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .Take(12)
                .ToArray();
            return features.Length == 0 ? "not reported by Win32_OptionalFeature" : string.Join(", ", features);
        }
        catch (Exception ex)
        {
            return $"Unknown ({ex.GetType().Name})";
        }
    }

    private static string ReadActivePolicyFiles()
    {
        try
        {
            var policyDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "CodeIntegrity", "CiPolicies", "Active");
            var activePolicies = Directory.Exists(policyDirectory)
                ? Directory.EnumerateFiles(policyDirectory, "*.cip", SearchOption.TopDirectoryOnly)
                    .Select(Path.GetFileName)
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                    .ToArray()
                : [];
            var legacyPolicy = File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "CodeIntegrity", "SiPolicy.p7b"));
            const int maxNames = 8;
            var listedPolicies = activePolicies.Take(maxNames).ToArray();
            var details = listedPolicies.Length == 0 ? "none" : string.Join(",", listedPolicies);
            var remainder = activePolicies.Length > maxNames ? $",...+{activePolicies.Length - maxNames} more" : string.Empty;
            return $"cipCount={activePolicies.Length}; cip=[{details}{remainder}]; SiPolicy.p7b={legacyPolicy}";
        }
        catch (Exception ex)
        {
            return $"Unknown ({ex.GetType().Name})";
        }
    }

    private static string ReadRegistryDword(string subKey, string valueName)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(subKey, writable: false);
            var value = key?.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            return value is null ? "not set" : Convert.ToString(value, CultureInfo.InvariantCulture) ?? "not set";
        }
        catch (Exception ex)
        {
            return $"Unknown ({ex.GetType().Name})";
        }
    }

    private static uint? ReadUInt(object? value)
    {
        if (value is null) return null;
        try { return Convert.ToUInt32(value, CultureInfo.InvariantCulture); }
        catch { return null; }
    }

    private static uint[] ReadUIntArray(object? value)
    {
        if (value is not Array values) return [];
        return values.Cast<object?>().Select(ReadUInt).Where(number => number.HasValue).Select(number => number!.Value).ToArray();
    }

    private static bool? HasCode(IReadOnlyCollection<uint> values, uint code) =>
        values.Count == 0 ? null : values.Contains(code);

    private static string DescribeStatus(uint? value, IReadOnlyDictionary<uint, string> descriptions) =>
        value is null ? "Unknown" : descriptions.TryGetValue(value.Value, out var description) ? $"{description} ({value.Value})" : $"Value={value.Value}";

    private static string FormatValue(object? value) =>
        Convert.ToString(value, CultureInfo.InvariantCulture) ?? "Unknown";

    private static readonly IReadOnlyDictionary<uint, string> OptionalFeatureNames = new Dictionary<uint, string>
    {
        [1] = "Enabled",
        [2] = "Disabled",
        [3] = "Absent",
        [4] = "Unknown"
    };

    private sealed record DeviceGuardState(
        uint? VbsStatus,
        uint[] ConfiguredServices,
        uint[] RunningServices,
        uint? KernelCiStatus,
        uint? UserModeCiStatus);
}
