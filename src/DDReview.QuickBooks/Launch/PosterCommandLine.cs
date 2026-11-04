namespace DDReview.QuickBooks.Launch;

public enum PosterAction { Run, Register, Unregister }

/// <summary>What the poster was asked to do. <see cref="Link"/> is still untrusted text; it goes through <see cref="PosterLink.TryParse"/>. <see cref="File"/> is the path of a <c>.ddposter</c> file that was opened; its link is read with <see cref="PosterLaunchFile"/> and then checked the same way.</summary>
public sealed record PosterCommand(PosterAction Action, string? Link, string? File = null);

/// <summary>Reads the poster's command line. Only four things mean anything: <c>--register</c>, <c>--unregister</c>, one <c>ddposter:</c> link, and one <c>.ddposter</c> file. Everything else is ignored.</summary>
public static class PosterCommandLine
{
    public static PosterCommand Parse(IReadOnlyList<string> args)
    {
        string? link = null, file = null;
        foreach (var a in args)
        {
            if (string.Equals(a, "--register", StringComparison.OrdinalIgnoreCase)) return new(PosterAction.Register, null);
            if (string.Equals(a, "--unregister", StringComparison.OrdinalIgnoreCase)) return new(PosterAction.Unregister, null);
            if (link is null && a.StartsWith(PosterLinkRules.Scheme + ":", StringComparison.OrdinalIgnoreCase)) link = a;
            else if (file is null && a.EndsWith(PosterLaunchFile.Extension, StringComparison.OrdinalIgnoreCase)) file = a;
        }
        return link is not null ? new(PosterAction.Run, link) : new(PosterAction.Run, null, file);
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
