namespace DDReview.QuickBooks.Launch;

public enum PosterAction { Run, Register, Unregister }

/// <summary>What the poster was asked to do. <see cref="Link"/> is still untrusted text; it goes through <see cref="PosterLink.TryParse"/>. <see cref="File"/> is the path of a <c>.ddposter</c> file that was opened; its link is read with <see cref="PosterLaunchFile"/> and then checked the same way.</summary>
public sealed record PosterCommand(PosterAction Action, string? Link, string? File = null, string? Package = null);

/// <summary>Reads the poster's command line. Only five things mean anything: <c>--register</c>, <c>--unregister</c>, one <c>ddposter:</c> link, one <c>.ddposter</c> launch file, and one <c>.ddpackage</c> package file. Everything else is ignored.</summary>
public static class PosterCommandLine
{
    public static PosterCommand Parse(IReadOnlyList<string> args)
    {
        string? link = null, file = null, package = null;
        foreach (var a in args)
        {
            if (string.Equals(a, "--register", StringComparison.OrdinalIgnoreCase)) return new(PosterAction.Register, null);
            if (string.Equals(a, "--unregister", StringComparison.OrdinalIgnoreCase)) return new(PosterAction.Unregister, null);
            if (link is null && a.StartsWith(PosterLinkRules.Scheme + ":", StringComparison.OrdinalIgnoreCase)) link = a;
            else if (file is null && a.EndsWith(PosterLaunchFile.Extension, StringComparison.OrdinalIgnoreCase)) file = a;
            else if (package is null && a.EndsWith(PosterPackage.Extension, StringComparison.OrdinalIgnoreCase)) package = a;
        }
        return link is not null ? new(PosterAction.Run, link) : package is not null ? new(PosterAction.Run, null, null, package) : new(PosterAction.Run, null, file);
    }
}

/// <summary>The command Windows runs for <c>ddposter:</c> links: the program's full path, then the link as one quoted argument.</summary>
public static class ProtocolCommand
{
    public static string Build(string exePath)
    {
        if (!IsFullWindowsPath(exePath) || exePath.Contains('"'))
            throw new ArgumentException("The program path must be a full path without quote marks.", nameof(exePath));
        return "\"" + exePath + "\" \"%1\"";
    }

    /// <summary>A drive-letter path (C:\...) or a UNC path (\\server\...). Checked by hand so it means the same thing on every platform.</summary>
    static bool IsFullWindowsPath(string? p) =>
        !string.IsNullOrWhiteSpace(p) && p.Length >= 3 &&
        ((char.IsAsciiLetter(p[0]) && p[1] == ':' && p[2] is '\\' or '/') || p.StartsWith(@"\\", StringComparison.Ordinal));
}
