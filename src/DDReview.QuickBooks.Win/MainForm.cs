using System.Text;
using System.Xml.Linq;
using DDReview.QuickBooks;
using DDReview.QuickBooks.Launch;
using DDReview.QuickBooks.Model;
using DDReview.QuickBooks.Package;
using DDReview.QuickBooks.Planning;
using DDReview.QuickBooks.Posting;
using DDReview.QuickBooks.Qbxml;
using DDReview.QuickBooks.Settings;
using DDReview.QuickBooks.Sql;
using Microsoft.Data.SqlClient;

namespace DDReview.QuickBooks.Win;

/// <summary>
/// Posts general ledger and accounts receivable from the DD back-end database into QuickBooks Desktop.
/// Preview asks QuickBooks questions only; nothing is changed until Post to QuickBooks is chosen and confirmed.
/// </summary>
sealed class MainForm : Form
{
    readonly UserSettings _settings = UserSettings.Load();
    readonly TextBox _connection = new() { Dock = DockStyle.Fill, UseSystemPasswordChar = true };   // masked: it may hold a password
    readonly CheckBox _showConnection = new() { Text = "Show", AutoSize = true, Anchor = AnchorStyles.Left };
    readonly Label _recap = new() { AutoSize = true, Visible = false, Padding = new Padding(0, 0, 0, 8), ForeColor = Color.DarkBlue, MaximumSize = new Size(900, 0) };
    SplitContainer? _middle;
    Control[] _filters = [];
    readonly CheckedListBox _stores = new() { Dock = DockStyle.Fill, CheckOnClick = true, IntegralHeight = false };
    readonly DateTimePicker _start = new() { Format = DateTimePickerFormat.Short, Width = 110 };
    readonly DateTimePicker _end = new() { Format = DateTimePickerFormat.Short, Width = 110 };
    readonly CheckBox _gl = new() { Text = "General ledger (sales, cash, tax)", Checked = true, AutoSize = true };
    readonly CheckBox _ar = new() { Text = "Accounts receivable (house charges and payments)", Checked = true, AutoSize = true };
    readonly CheckBox _arAdj = new() { Text = "Accounts receivable adjustments", AutoSize = true };
    readonly CheckBox _debug = new() { Text = "Write debug files to:", AutoSize = true };
    readonly TextBox _debugDir = new() { Width = 320 };
    readonly Button _openDebug = new() { Text = "Open folder", AutoSize = true };
    readonly Button _loadStores = new() { Text = "Connect and load stores", AutoSize = true };
    readonly Button _preview = new() { Text = "Preview (changes nothing)", AutoSize = true, Enabled = false };
    readonly Button _post = new() { Text = "Post to QuickBooks...", AutoSize = true, Enabled = false };
    readonly Button _export = new() { Text = "Save import files...", AutoSize = true, Enabled = false };
    readonly Button _cancel = new() { Text = "Cancel", AutoSize = true, Enabled = false };
    readonly TextBox _log = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Font = new Font("Consolas", 9f) };
    readonly Label _status = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
    readonly Button _links = new() { AutoSize = true };
    readonly Label _linkNote = new() { Dock = DockStyle.Top, AutoSize = true, Visible = false, Padding = new Padding(0, 0, 0, 6), ForeColor = Color.DarkBlue, MaximumSize = new Size(410, 0) };

    // "Post to QuickBooks" links from the website (see docs/QUICKBOOKS.md). A link only fills in the form; it never starts a Post.
    readonly IReadOnlyList<TrustedKey> _trusted;
    readonly List<string> _keyProblems;
    readonly string? _launchLink;
    readonly string? _launchPackage;
    PackageContents? _package;             // a signed package file from the website: the rows come from it and no database connection is used
    Control? _top;
    VerifiedLaunch? _link;                 // the verified link this window was opened from; while set, only its stores can be used here
    HashSet<int> _linkStores = [];
    bool _running;

    CancellationTokenSource? _cts;
    PostingPlan? _plan;
    PostingRequest? _planFor;

    sealed record StoreItem(int Id, string Text) { public override string ToString() => Text; }

    public MainForm(string? launchLink = null, string? launchPackage = null)
    {
        _launchLink = launchLink;
        _launchPackage = launchPackage;
        _trusted = TrustedKeyFile.Load(Path.Combine(AppContext.BaseDirectory, TrustedKeyFile.FileName), out _keyProblems);

        Text = "Post to QuickBooks  " + VersionText();
        Width = 980; Height = 740; MinimumSize = new Size(820, 560);
        StartPosition = FormStartPosition.CenterScreen;

        // Day 0 of the DD databases: no date before it is accepted (a database may set a later day 0, which Preview and Post check as well).
        _start.MinDate = _end.MinDate = PosterLinkRules.EarliestDate;

        var today = DateTime.Today;
        var firstOfThisMonth = new DateTime(today.Year, today.Month, 1);
        _start.Value = firstOfThisMonth.AddMonths(-1);
        _end.Value = firstOfThisMonth.AddDays(-1);

        _connection.Text = _settings.ConnectionString;
        _debug.Checked = _settings.WriteDebugFiles || LegacyRegistry.DebugDirectory() != null;
        _debugDir.Text = !string.IsNullOrWhiteSpace(_settings.DebugDirectory) ? _settings.DebugDirectory : LegacyRegistry.DebugDirectory() ?? "";

        var top = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 3, Padding = new Padding(8) };
        top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        top.Controls.Add(new Label { Text = "Database connection:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        top.Controls.Add(_connection, 1, 0);
        top.Controls.Add(Row(_showConnection, _loadStores), 2, 0);
        _showConnection.CheckedChanged += (_, _) => _connection.UseSystemPasswordChar = !_showConnection.Checked;

        _top = top;
        var middle = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, SplitterDistance = 440 };
        _middle = middle;
        var storesBox = new GroupBox { Text = "Stores", Dock = DockStyle.Fill, Padding = new Padding(6) };
        var storeButtons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true };
        var all = new Button { Text = "All", AutoSize = true };
        var none = new Button { Text = "None", AutoSize = true };
        all.Click += (_, _) => SetAllStores(true);
        none.Click += (_, _) => SetAllStores(false);
        storeButtons.Controls.AddRange(new Control[] { all, none });
        storesBox.Controls.Add(_stores);
        storesBox.Controls.Add(storeButtons);
        storesBox.Controls.Add(_linkNote);
        middle.Panel1.Controls.Add(storesBox);

        var opts = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(8) };
        opts.Controls.Add(_recap);
        _filters = [Row(new Label { Text = "From", AutoSize = true, Anchor = AnchorStyles.Left }, _start, new Label { Text = "to", AutoSize = true, Anchor = AnchorStyles.Left }, _end), _gl, _ar, _arAdj];
        foreach (var c in _filters) opts.Controls.Add(c);
        opts.Controls.Add(Row(_debug, _debugDir, _openDebug));
        _openDebug.Click += (_, _) => OpenDebugFolder();
        middle.Panel2.Controls.Add(opts);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(8, 4, 8, 4) };
        buttons.Controls.AddRange(new Control[] { _preview, _post, _export, _cancel, _links });

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5 };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 230));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        root.Controls.Add(top, 0, 0);
        root.Controls.Add(middle, 0, 1);
        root.Controls.Add(buttons, 0, 2);
        root.Controls.Add(_log, 0, 3);
        root.Controls.Add(_status, 0, 4);
        Controls.Add(root);
        DdTheme.Apply(this, _preview, _post, _loadStores);
        _recap.ForeColor = DdTheme.Link; _linkNote.ForeColor = DdTheme.Link;
        _status.ForeColor = DdTheme.Muted;
        Controls.Add(DdTheme.Banner("Post to QuickBooks"));   // after the fill panel (so it docks across the top) and after Apply (which would repaint its labels)

        _loadStores.Click += async (_, _) => await LoadStoresAsync();
        _preview.Click += async (_, _) => await RunAsync(commit: false);
        _post.Click += async (_, _) => await PostAsync();
        _cancel.Click += (_, _) => _cts?.Cancel();
        _export.Click += (_, _) => ExportFiles();
        _links.Click += (_, _) => ToggleLinkRegistration();
        FormClosing += (_, _) => SaveSettings();
        Shown += (_, _) =>
        {
            CheckLinkRegistration();
            if (_launchPackage != null) ApplyPackage(_launchPackage);
            else if (_launchLink != null) ApplyLink(_launchLink);
        };

        Log("This program posts the general ledger and accounts receivable from your DD database into QuickBooks Desktop.");
        Log("1. Open your company file in QuickBooks.  2. Connect and load stores.  3. Choose stores and dates, then Preview.  4. Post.");
    }

    static FlowLayoutPanel Row(params Control[] c)
    {
        var p = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        p.Controls.AddRange(c);
        return p;
    }

    public void BringForward()
    {
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Activate();
    }

    /// <summary>
    /// A "Post to QuickBooks" link from the website, either at start or passed on by a second start. The link is untrusted until it checks out
    /// (signature from a trusted key, not expired, every parameter inside the limits). A link that does not check out changes nothing and says why.
    /// A good link only fills in the dates, the choice and the store list (limited to the link's stores); the person still presses Preview, and Post still needs a Preview and a Yes.
    /// </summary>
    public void ApplyLink(string text)
    {
        BringForward();
        if (_running)
        {
            Log("A link arrived while a run was in progress and was ignored.");
            MessageBox.Show(this, "A run is in progress. Wait for it to finish (or press Cancel), then click the link again.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (!PosterLink.TryParse(text, _trusted, DateTimeOffset.UtcNow, out var launch, out var problem))
        {
            Log("The link was ignored: " + problem.Message);
            if (problem.Problem == PosterLinkProblem.UnknownKey) foreach (var p in _keyProblems) Log("  " + p);
            MessageBox.Show(this, problem.Message + "\r\n\r\nNothing was changed.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _package = null; if (_top != null) _top.Visible = true;     // a link replaces a package: the connection is needed again
        var r = launch.Request;
        _link = launch;
        _linkStores = r.StoreIds.ToHashSet();
        _start.Value = r.Start;
        _end.Value = r.End;
        _gl.Checked = (r.Scope & 1) != 0;
        _ar.Checked = (r.Scope & 2) != 0;
        _arAdj.Checked = false;
        _plan = null; _planFor = null;     // a Preview from before no longer matches; Post needs a fresh one
        Busy(false);
        _linkNote.Text = $"Opened from DD Review (user {launch.WebUserId}). Only the {r.StoreIds.Count} store(s) in the link can be used in this window. To use other stores, close this window and open the program from its icon.";
        _linkNote.Visible = true;
        string what = r.Scope switch { 1 => "general ledger", 2 => "accounts receivable", _ => "general ledger and accounts receivable" };
        // The dates, choice and stores came from the website, so they are not shown as controls: just a plain recap (nothing here can be changed; run the report again on the website to change it).
        _recapBase = $"From DD Review: {what}, {r.Start:MMM d, yyyy} to {r.End:MMM d, yyyy}.";
        if (_middle != null) _middle.Panel1Collapsed = true;
        foreach (var c in _filters) c.Visible = false;
        UpdateRecap();
        Log($"Opened from DD Review: {r.Start:d} to {r.End:d}, {what}, {r.StoreIds.Count} store(s). Press Preview; nothing is changed in QuickBooks until you Post.");

        if (_connection.Text.Trim().Length == 0)
        {
            _stores.Items.Clear();
            Log("Enter the database connection, then press 'Connect and load stores'; only the link's stores will be listed.");
            return;
        }
        _ = LoadStoresAsync();
    }

    /// <summary>
    /// A package file from the website (a double-click, or passed on by a second start). It is untrusted until its signature, expiry and limits check out.
    /// A good one carries the rows and settings, so Preview and Post need no database connection; the person still presses Preview, and Post still needs a Preview and a Yes.
    /// </summary>
    public void ApplyPackage(string path)
    {
        BringForward();
        if (_running)
        {
            Log("A package arrived while a run was in progress and was ignored.");
            MessageBox.Show(this, "A run is in progress. Wait for it to finish (or press Cancel), then open the package again.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (!PosterPackage.TryOpenFile(path, _trusted, DateTimeOffset.UtcNow, out var contents, out var problem) || contents is null)
        {
            Log("The package was not used: " + problem.Message);
            if (problem.Problem == PosterLinkProblem.UnknownKey) foreach (var p in _keyProblems) Log("  " + p);
            MessageBox.Show(this, problem.Message + "\r\n\r\nNothing was changed.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var r = contents.Launch.Request;
        _package = contents;
        _link = contents.Launch;
        _linkStores = r.StoreIds.ToHashSet();
        _plan = null; _planFor = null;
        _stores.Items.Clear();
        for (int i = 0; i < r.StoreIds.Count; i++)
            _stores.Items.Add(new StoreItem(r.StoreIds[i], contents.StoreNames.Count == r.StoreIds.Count && contents.StoreNames[i].Length > 0 ? contents.StoreNames[i] : "Store " + r.StoreIds[i]), true);
        _linkNote.Visible = false;
        string what = r.Scope switch { 1 => "general ledger", 2 => "accounts receivable", _ => "general ledger and accounts receivable" };
        _recapBase = $"From a DD Review package made {contents.Created.ToLocalTime():MMM d, yyyy h:mm tt}: {what}, {r.Start:MMM d, yyyy} to {r.End:MMM d, yyyy}.";
        if (_top != null) _top.Visible = false;             // no database connection is used in this mode
        if (_middle != null) _middle.Panel1Collapsed = true;
        foreach (var c in _filters) c.Visible = false;
        UpdateRecap();
        Busy(false);
        _preview.Enabled = true;
        Log($"Opened a DD Review package: {r.Start:d} to {r.End:d}, {what}, {r.StoreIds.Count} store(s), {contents.Rows.Count:N0} rows. Press Preview; nothing is changed in QuickBooks until you Post. No database connection is needed.");
    }

    string _recapBase = "";

    /// <summary>The plain-words recap shown instead of the date, choice and store controls when the window was opened from the website.</summary>
    void UpdateRecap()
    {
        if (_link is null) { _recap.Visible = false; return; }
        var names = _stores.CheckedItems.Cast<StoreItem>().Select(i => i.Text).ToList();
        string stores = names.Count > 0 ? (names.Count == 1 ? "Store: " : "Stores: ") + string.Join("; ", names)
            : $"{_linkStores.Count} store(s) in the link (names appear after you connect).";
        _recap.Text = _recapBase + "\r\n" + stores + "\r\nTo change any of this, go back to DD Review, run the report you want and use Post to QuickBooks again. Press Preview; nothing is changed in QuickBooks until you Post.";
        _recap.Visible = true;
    }

    /// <summary>First start on a computer: offers to let website links open this program. If links point at another copy (moved folder, new build), points them here again.</summary>
    void CheckLinkRegistration()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe)) return;
            var state = ProtocolRegistration.GetState(exe);
            if (state == ProtocolRegistration.State.RegisteredElsewhere)
            {
                ProtocolRegistration.Register(exe);
                Log("DD Review website links now open this copy of the program.");
            }
            else if (state == ProtocolRegistration.State.NotRegistered && !_settings.LinksPromptShown)
            {
                _settings.LinksPromptShown = true;
                SaveSettings();
                var answer = MessageBox.Show(this,
                    "Let the \"Post to QuickBooks\" button on the DD Review website open this program?\r\n\r\n" +
                    "This is set for your Windows user only and needs no administrator rights. You can undo it with the \"Unregister DD Review links\" button.",
                    Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (answer == DialogResult.Yes) { ProtocolRegistration.Register(exe); Log("DD Review website links now open this program."); }
            }
        }
        catch (Exception ex) { Log("Could not check the DD Review link setup: " + ex.Message); }
        UpdateLinksButton();
    }

    void ToggleLinkRegistration()
    {
        try
        {
            var exe = Environment.ProcessPath ?? "";
            if (ProtocolRegistration.GetState(exe) == ProtocolRegistration.State.RegisteredHere)
            {
                ProtocolRegistration.Unregister();
                Log("DD Review website links no longer open this program.");
            }
            else
            {
                ProtocolRegistration.Register(exe);
                Log("DD Review website links now open this program.");
            }
        }
        catch (Exception ex) { Log("Could not change the link setup: " + ex.Message); MessageBox.Show(this, ex.Message, Text); }
        UpdateLinksButton();
    }

    void UpdateLinksButton()
    {
        bool here;
        try { here = ProtocolRegistration.GetState(Environment.ProcessPath ?? "") == ProtocolRegistration.State.RegisteredHere; }
        catch (Exception) { here = false; }
        _links.Text = here ? "Unregister DD Review links" : "Register DD Review links";
    }

    void SetAllStores(bool on) { for (int i = 0; i < _stores.Items.Count; i++) _stores.SetItemChecked(i, on); }

    void SaveSettings()
    {
        _settings.ConnectionString = _connection.Text.Trim();
        _settings.WriteDebugFiles = _debug.Checked;
        _settings.DebugDirectory = _debugDir.Text.Trim();
        _settings.Save();
    }

    void Log(string s)
    {
        _log.AppendText(LogScrubber.Scrub(s, _connection.Text) + Environment.NewLine);   // never show a password, even inside an error message
    }

    /// <summary>"1.1.0 (abc1234, built 2026-11-02)" from the exe itself, so a screenshot says which build it is.</summary>
    static string VersionText()
    {
        var asm = typeof(MainForm).Assembly;
        string v = System.Diagnostics.FileVersionInfo.GetVersionInfo(asm.Location).ProductVersion ?? asm.GetName().Version?.ToString() ?? "";
        int plus = v.IndexOf('+');
        string ver = plus >= 0 ? v[..plus] : v;
        string sha = plus >= 0 ? v[(plus + 1)..] : "";
        if (sha.Length > 7) sha = sha[..7];
        string built = File.GetLastWriteTime(asm.Location).ToString("yyyy-MM-dd");
        return $"{ver} ({(sha.Length > 0 ? sha + ", " : "")}built {built})";
    }

    /// <summary>Opens the folder the debug files go to, choosing an easy-to-find default the first time.</summary>
    void OpenDebugFolder()
    {
        if (_debugDir.Text.Trim().Length == 0)
            _debugDir.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DDReview", "PosterDebug");
        try
        {
            Directory.CreateDirectory(_debugDir.Text.Trim());
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", "\"" + _debugDir.Text.Trim() + "\"") { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.ComponentModel.Win32Exception)
        { Log("Could not open the debug folder: " + ex.Message); }
    }

    void Busy(bool busy, string status = "")
    {
        _running = busy;
        _status.Text = status;
        _cancel.Enabled = busy;
        _loadStores.Enabled = _preview.Enabled = !busy;
        _post.Enabled = _export.Enabled = !busy && _plan != null;
        UseWaitCursor = busy;
    }

    async Task LoadStoresAsync()
    {
        var cs = _connection.Text.Trim();
        if (cs.Length == 0) { MessageBox.Show(this, "Enter the database connection string first.", Text); return; }
        Busy(true, "Loading stores...");
        try
        {
            var items = await Task.Run(() =>
            {
                var list = new List<StoreItem>();
                using var cn = new SqlConnection(cs);
                cn.Open();
                using var cmd = cn.CreateCommand();
                cmd.CommandText = "select StoreID, isnull(StoreReportName, StoreName) as StoreName, StoreNumber, Active from tblStores order by StoreNumber, StoreName";
                using var rd = cmd.ExecuteReader();
                while (rd.Read())
                    list.Add(new StoreItem(Convert.ToInt32(rd["StoreID"]), $"{rd["StoreNumber"]}  {rd["StoreName"]}" + (Convert.ToInt32(rd["Active"]) == 0 ? "  (inactive)" : "")));
                return list;
            });
            _stores.Items.Clear();
            if (_link != null)
            {
                // Opened from a website link: only the stores that link carries, all checked. Reloading never widens the list.
                var found = items.Where(i => _linkStores.Contains(i.Id)).ToList();
                foreach (var it in found) _stores.Items.Add(it, true);
                _preview.Enabled = found.Count > 0;
                Log($"Loaded {found.Count} of the {_linkStores.Count} stores in the link.");
                var missing = _linkStores.Except(items.Select(i => i.Id)).Order().ToList();
                if (missing.Count > 0) Log("Not in this database, left out: " + string.Join(", ", missing));
                UpdateRecap();
            }
            else
            {
                foreach (var it in items) _stores.Items.Add(it, !it.Text.EndsWith("(inactive)"));
                _preview.Enabled = items.Count > 0;
                Log($"Loaded {items.Count} stores.");
            }
            SaveSettings();
        }
        catch (Exception ex) { Log("Could not load stores: " + ex.Message); MessageBox.Show(this, ex.Message, Text); }
        finally { Busy(false); }
    }

    PostingRequest? BuildRequest()
    {
        if (_package != null) return _package.Launch.Request;   // a package carries its own stores, dates and choice
        var ids = _stores.CheckedItems.Cast<StoreItem>().Select(s => s.Id).ToList();
        if (ids.Count == 0) { MessageBox.Show(this, "Check at least one store.", Text); return null; }
        if (_link != null && ids.Any(id => !_linkStores.Contains(id))) { MessageBox.Show(this, "A store that is not in the DD Review link is checked. Close this window and open the program from its icon to use other stores.", Text); return null; }
        int scope = (_gl.Checked ? 1 : 0) | (_ar.Checked ? 2 : 0) | (_arAdj.Checked ? 16 : 0);
        if (scope == 0) { MessageBox.Show(this, "Choose what to post.", Text); return null; }
        if (_end.Value.Date < _start.Value.Date) { MessageBox.Show(this, "The end date is before the start date.", Text); return null; }
        return new PostingRequest(ids, _start.Value.Date, _end.Value.Date, scope);
    }

    string? DebugFolder() => _debug.Checked && _debugDir.Text.Trim().Length > 0 ? _debugDir.Text.Trim() : null;

    EngineOptions EngineOptions()
    {
        var o = new EngineOptions();
        var max = LegacyRegistry.MaxVersion();
        if (!string.IsNullOrWhiteSpace(max)) o.MaxVersion = max.Trim();
        return o;
    }

    async Task RunAsync(bool commit)
    {
        var req = BuildRequest();
        if (req == null) return;
        if (_package != null && DateTimeOffset.UtcNow > _package.Launch.Expires + PosterLinkRules.ClockSkew)
        {
            Log("This package has expired (packages last one day). Download a new one from the website.");
            MessageBox.Show(this, "This package has expired (packages last one day). Download a new one from the website.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        var cs = _connection.Text.Trim();
        var package = _package;
        var debug = DebugFolder();
        _cts = new CancellationTokenSource();
        var cancel = _cts.Token;
        Busy(true, commit ? "Posting..." : "Previewing...");
        _log.Clear();
        try
        {
            Log(commit ? "Posting to QuickBooks..." : "Previewing (nothing will be changed)...");
            var result = await StaRunner.Run(() =>
            {
                List<PackageRow> rows; PostingOptions options;
                if (package != null) { rows = [.. package.Rows]; options = package.Options; }   // the website already checked day 0 and read the settings
                else
                {
                    // Day 0: nothing earlier than the database's own day 0 (never earlier than 2003-09-29) is posted.
                    var earliest = PosterLinkRules.EffectiveEarliest(SqlPackageSource.ReadDayZero(cs));
                    if (req.Start < earliest) throw new InvalidOperationException($"This database does not accept dates before {earliest:yyyy-MM-dd} (its day 0). Change the From date.");
                    rows = SqlPackageSource.Read(cs, req);
                    options = rows.Count == 0 ? new PostingOptions() : SqlPackageSource.LoadOptions(cs);
                }
                if (rows.Count == 0) return (Plan: (PostingPlan?)null, Report: (PostingReport?)null);
                var plan = PlanBuilder.Build(rows, options);
                if (debug != null)
                {
                    Directory.CreateDirectory(debug);
                    File.WriteAllText(Path.Combine(debug, "QBGeneralLedgerTransaction.xml"), LegacyPlanXml.Write(plan).ToString(SaveOptions.DisableFormatting), new UTF8Encoding(false));
                }
                IQbRequestProcessor qb = new Qbxmlrp2Processor(_settings.AppName, _settings.QuickBooksCompanyFile);
                if (debug != null) qb = new LoggingRequestProcessor(qb, Path.Combine(debug, "QBLogFile.txt"));
                using (qb)
                {
                    var engine = new PostingEngine(qb, EngineOptions());
                    engine.Progress = msg => BeginInvoke(() => _status.Text = msg);
                    return (Plan: (PostingPlan?)plan, Report: (PostingReport?)engine.Run(plan, commit, cancel));
                }
            });
            if (result.Report == null) { Log("There is nothing to post for those stores and dates."); _plan = null; return; }
            Show(result.Report);
            if (!commit) { _plan = result.Plan; _planFor = req; }
            else { _plan = null; _planFor = null; }
        }
        catch (OperationCanceledException) { Log("Cancelled. Whatever was already sent to QuickBooks stays there; run Preview again to see where things stand."); }
        catch (QuickBooksUnavailableException ex) { Log(ex.Message); MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        catch (Exception ex) { Log("Error: " + ex.Message); MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error); }
        finally { Busy(false); }
    }

    void Show(PostingReport r)
    {
        Log(r.Summary);
        Log("");
        if (r.Objects.Count > 0)
        {
            Log((r.Committed ? "Created or changed in QuickBooks" : "Will be created or changed in QuickBooks") + ":");
            foreach (var o in r.Objects.Where(o => o.Outcome != StepOutcome.Failed)) Log("  " + o.What);
            Log("");
        }
        void List(string title, IEnumerable<StepResult> items)
        {
            var l = items.ToList();
            if (l.Count == 0) return;
            Log(title);
            foreach (var i in l) Log("  " + i.What);
            Log("");
        }
        List("Please check (not failures):", r.Warnings.Distinct().Select(w => new StepResult(w, StepOutcome.Unchanged)));
        List(r.Committed ? "Transactions Added" : "Transactions To Add", r.Added);
        List(r.Committed ? "Transactions Modified" : "Transactions To Modify", r.Modified);
        List("Transactions Unchanged", r.Unchanged);
        if (r.HasFailures)
        {
            Log("PROBLEMS:");
            foreach (var f in r.Failures) Log($"  {f.What}: {f.Message}");
        }
    }

    void ExportFiles()
    {
        if (_plan == null || _planFor == null) return;
        using var dlg = new FolderBrowserDialog { Description = "Choose a folder for the QuickBooks import files (IIF, CSV and QBXML)." };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var name = $"DDQuickBooks-{_planFor.Start:yyyyMMdd}-{_planFor.End:yyyyMMdd}";
            var files = DDReview.QuickBooks.Export.ImportFiles.WriteAll(_plan, dlg.SelectedPath, name);
            Log("");
            Log("Saved:");
            foreach (var f in files) Log("  " + f);
            Log("IIF: in QuickBooks choose File > Utilities > Import > IIF Files.");
        }
        catch (Exception ex) { Log("Could not save the files: " + ex.Message); MessageBox.Show(this, ex.Message, Text); }
    }

    async Task PostAsync()
    {
        var req = BuildRequest();
        if (_plan == null || req == null) return;
        if (_planFor != null && (req.Start != _planFor.Start || req.End != _planFor.End || req.Scope != _planFor.Scope || !req.StoreIds.SequenceEqual(_planFor.StoreIds)))
        {
            MessageBox.Show(this, "The stores, dates or choices changed since the preview. Preview again first.", Text);
            return;
        }
        var answer = MessageBox.Show(this,
            "No changes have been made to QuickBooks yet.\r\n\r\nPost these transactions now? Make a QuickBooks backup first if you have not.",
            Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
        if (answer != DialogResult.Yes) return;
        await RunAsync(commit: true);
    }
}
