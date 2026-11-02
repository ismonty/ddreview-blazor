using DDReview.QuickBooks.Launch;
using Microsoft.Win32;

namespace DDReview.QuickBooks.Win;

/// <summary>
/// Tells Windows that <c>ddposter:</c> links open this program. Written under HKCU\Software\Classes, so it is per Windows user and needs no administrator rights.
/// The registered command holds this program's full path; if the program is moved or a different copy is run, <see cref="GetState"/> says so and the program registers itself again.
/// </summary>
static class ProtocolRegistration
{
    const string KeyPath = @"Software\Classes\" + PosterLinkRules.Scheme;

    public enum State { NotRegistered, RegisteredHere, RegisteredElsewhere }

    public static State GetState(string exePath)
    {
        using var k = Registry.CurrentUser.OpenSubKey(KeyPath + @"\shell\open\command");
        if (k?.GetValue(null) is not string cmd || cmd.Length == 0) return State.NotRegistered;
        return string.Equals(cmd, ProtocolCommand.Build(exePath), StringComparison.OrdinalIgnoreCase) ? State.RegisteredHere : State.RegisteredElsewhere;
    }

    public static void Register(string exePath)
    {
        string command = ProtocolCommand.Build(exePath);   // refuses a path that is not a full path or has quote marks
        using var root = Registry.CurrentUser.CreateSubKey(KeyPath);
        root.SetValue(null, "URL:DD Review QuickBooks Poster");
        root.SetValue("URL Protocol", "");
        using (var icon = root.CreateSubKey("DefaultIcon")) icon.SetValue(null, "\"" + exePath + "\",0");
        using (var cmd = root.CreateSubKey(@"shell\open\command")) cmd.SetValue(null, command);
    }

    public static void Unregister() => Registry.CurrentUser.DeleteSubKeyTree(KeyPath, throwOnMissingSubKey: false);
}
