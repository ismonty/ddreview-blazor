using DDReview.QuickBooks.Launch;

namespace DDReview.QuickBooks.Win;

static class Program
{
    /// <summary>
    /// Normal start, or start from a "Post to QuickBooks" link on the website (<c>ddposter:</c> link or a <c>.ddposter</c> file as the one argument), or <c>--register</c> / <c>--unregister</c>
    /// to set up the links without opening a window. Only one window runs per Windows session: a second start passes its link to the first and exits.
    /// </summary>
    [STAThread]
    static int Main(string[] args)
    {
        var command = PosterCommandLine.Parse(args);
        var exe = Environment.ProcessPath ?? "";

        if (command.Action != PosterAction.Run)
        {
            try
            {
                if (command.Action == PosterAction.Register) ProtocolRegistration.Register(exe); else ProtocolRegistration.Unregister();
                return 0;
            }
            catch (Exception) { return 1; }
        }

        // A .ddposter file that was double-clicked: its link is read here and then handled exactly like a link from the website.
        string? link = command.Link;
        if (command.File is not null)
        {
            if (PosterLaunchFile.TryRead(command.File, out var fromFile, out var why)) link = fromFile;
            else
            {
                MessageBox.Show(why + "\r\n\r\nNothing was changed.", "Post to QuickBooks", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return 0;
            }
        }

        string? package = command.Package is null ? null : Path.GetFullPath(command.Package);

        using var mutex = new Mutex(true, InstanceChannel.MutexName, out bool first);
        if (!first)
        {
            if (!(package is not null ? InstanceChannel.SendPackage(package) : InstanceChannel.Send(link)))
                MessageBox.Show("The program is already open but did not answer. Close it (Task Manager, if it is stuck) and try again.", "Post to QuickBooks", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return 0;
        }

        try
        {
            ApplicationConfiguration.Initialize();
            var form = new MainForm(link, package);
            using var server = new InstanceServer();
            // Load, not Shown: the pipe must already be listening while the first-run question is on screen, or a click then would be told "no answer".
            form.Load += (_, _) => server.Start(
                link => { try { form.BeginInvoke(() => form.ApplyLink(link)); } catch (InvalidOperationException) { /* window is closing */ } },
                () => { try { form.BeginInvoke(form.BringForward); } catch (InvalidOperationException) { } },
                path => { try { form.BeginInvoke(() => form.ApplyPackage(path)); } catch (InvalidOperationException) { /* window is closing */ } });
            Application.Run(form);
        }
        finally { mutex.ReleaseMutex(); }
        return 0;
    }
}
