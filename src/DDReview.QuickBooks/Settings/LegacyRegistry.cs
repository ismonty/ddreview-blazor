using System.Runtime.Versioning;
using Microsoft.Win32;

namespace DDReview.QuickBooks.Settings;

/// <summary>
/// Reads (never writes) the settings DDAdminCS kept in the registry under
/// HKCU\SOFTWARE\Integrated Restaurant Systems\DDEOD\CurrentVersion, so a computer already set up for it behaves the same.
/// </summary>
public static class LegacyRegistry
{
    public const string Key = @"SOFTWARE\Integrated Restaurant Systems\DDEOD\CurrentVersion";

    [SupportedOSPlatform("windows")]
    public static string? Get(string name)
    {
        using var k = Registry.CurrentUser.OpenSubKey(Key);
        return k?.GetValue(name) as string;
    }

    /// <summary>Folder to write QBGeneralLedgerTransaction.xml, QBLogFile.txt and QBError.xml to; null = debugging off.</summary>
    public static string? DebugDirectory()
    {
        if (!OperatingSystem.IsWindows()) return null;
        var v = Get("QuickBooks Debug Directory");
        return string.IsNullOrWhiteSpace(v) ? null : v.Trim();
    }

    /// <summary>"QuickBooks QBXML Max Version": a cap on the QBXML version sent.</summary>
    public static string? MaxVersion() => OperatingSystem.IsWindows() ? Get("QuickBooks QBXML Max Version") : null;
}
