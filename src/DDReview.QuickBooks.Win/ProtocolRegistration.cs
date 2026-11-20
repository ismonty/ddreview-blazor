using DDReview.QuickBooks.Launch;
using Microsoft.Win32;

namespace DDReview.QuickBooks.Win;

/// <summary>
/// Tells Windows that <c>ddposter:</c> links and <c>.ddposter</c> / <c>.ddpackage</c> files open this program. Written under HKCU\Software\Classes, so it is per Windows user and needs no administrator rights.
/// The registered command holds this program's full path; if the program is moved or a different copy is run, <see cref="GetState"/> says so and the program registers itself again.
/// </summary>
static class ProtocolRegistration
{
    const string KeyPath = @"Software\Classes\" + PosterLinkRules.Scheme;
    const string FileExtKey = @"Software\Classes\" + PosterLaunchFile.Extension;
    const string FileProgId = "DDReview.QuickBooksPoster.LaunchFile";
    const string FileProgKey = @"Software\Classes\" + FileProgId;
    const string PackageExtKey = @"Software\Classes\" + PosterPackage.Extension;
    const string PackageProgId = "DDReview.QuickBooksPoster.Package";
    const string PackageProgKey = @"Software\Classes\" + PackageProgId;

    public enum State { NotRegistered, RegisteredHere, RegisteredElsewhere }

    public static State GetState(string exePath)
    {
        var want = ProtocolCommand.Build(exePath);
        string? link = Command(KeyPath), file = Command(FileProgKey), package = Command(PackageProgKey);
        if (link is null && file is null && package is null) return State.NotRegistered;
        using (var proto = Registry.CurrentUser.OpenSubKey(KeyPath))
            if (proto?.GetValue("URL Protocol") is null) return State.RegisteredElsewhere;   // an installer that left this out is repaired the next time the program starts
        bool linkHere = string.Equals(link, want, StringComparison.OrdinalIgnoreCase);
        bool fileHere = string.Equals(file, want, StringComparison.OrdinalIgnoreCase) && string.Equals(package, want, StringComparison.OrdinalIgnoreCase);
        return linkHere && fileHere ? State.RegisteredHere : State.RegisteredElsewhere;   // a half-registered copy (links but not files) is set up again
    }

    static void RemoveExtensionIfOurs(string key, string progId)
    {
        using (var ext = Registry.CurrentUser.OpenSubKey(key))
            if (!string.Equals(ext?.GetValue(null) as string, progId, StringComparison.Ordinal)) return;
        Registry.CurrentUser.DeleteSubKeyTree(key, throwOnMissingSubKey: false);
    }

    static string? Command(string key)
    {
        using var k = Registry.CurrentUser.OpenSubKey(key + @"\shell\open\command");
        return k?.GetValue(null) is string c && c.Length > 0 ? c : null;
    }

    public static void Register(string exePath)
    {
        string command = ProtocolCommand.Build(exePath);   // refuses a path that is not a full path or has quote marks
        using var root = Registry.CurrentUser.CreateSubKey(KeyPath);
        root.SetValue(null, "URL:DD Review QuickBooks Poster");
        root.SetValue("URL Protocol", "");
        using (var icon = root.CreateSubKey("DefaultIcon")) icon.SetValue(null, "\"" + exePath + "\",0");
        using (var cmd = root.CreateSubKey(@"shell\open\command")) cmd.SetValue(null, command);

        // .ddposter files: the extension points at a program id whose open command is the same command.
        using (var ext = Registry.CurrentUser.CreateSubKey(FileExtKey)) ext.SetValue(null, FileProgId);
        using (var prog = Registry.CurrentUser.CreateSubKey(FileProgKey))
        {
            prog.SetValue(null, "DD Review QuickBooks launch file");
            using (var icon = prog.CreateSubKey("DefaultIcon")) icon.SetValue(null, "\"" + exePath + "\",0");
            using (var cmd = prog.CreateSubKey(@"shell\open\command")) cmd.SetValue(null, command);
        }

        // .ddpackage files (the signed package the website makes) open the same way.
        using (var ext = Registry.CurrentUser.CreateSubKey(PackageExtKey)) ext.SetValue(null, PackageProgId);
        using (var prog = Registry.CurrentUser.CreateSubKey(PackageProgKey))
        {
            prog.SetValue(null, "DD Review QuickBooks package");
            using (var icon = prog.CreateSubKey("DefaultIcon")) icon.SetValue(null, "\"" + exePath + "\",0");
            using (var cmd = prog.CreateSubKey(@"shell\open\command")) cmd.SetValue(null, command);
        }
    }

    public static void Unregister()
    {
        Registry.CurrentUser.DeleteSubKeyTree(KeyPath, throwOnMissingSubKey: false);
        Registry.CurrentUser.DeleteSubKeyTree(FileProgKey, throwOnMissingSubKey: false);
        Registry.CurrentUser.DeleteSubKeyTree(PackageProgKey, throwOnMissingSubKey: false);
        // An extension key is removed only if it is still ours (another program may have taken it since).
        RemoveExtensionIfOurs(FileExtKey, FileProgId);
        RemoveExtensionIfOurs(PackageExtKey, PackageProgId);
    }
}
