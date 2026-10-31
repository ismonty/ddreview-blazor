using System.Text;
using System.Xml.Linq;
using DDReview.QuickBooks.Model;
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
    readonly TextBox _connection = new() { Dock = DockStyle.Fill, UseSystemPasswordChar = false };
    readonly CheckedListBox _stores = new() { Dock = DockStyle.Fill, CheckOnClick = true, IntegralHeight = false };
    readonly DateTimePicker _start = new() { Format = DateTimePickerFormat.Short, Width = 110 };
    readonly DateTimePicker _end = new() { Format = DateTimePickerFormat.Short, Width = 110 };
    readonly CheckBox _gl = new() { Text = "General ledger (sales, cash, tax)", Checked = true, AutoSize = true };
    readonly CheckBox _ar = new() { Text = "Accounts receivable (house charges and payments)", Checked = true, AutoSize = true };
    readonly CheckBox _arAdj = new() { Text = "Accounts receivable adjustments", AutoSize = true };
    readonly CheckBox _debug = new() { Text = "Write debug files to:", AutoSize = true };
    readonly TextBox _debugDir = new() { Width = 320 };
    readonly Button _loadStores = new() { Text = "Connect and load stores", AutoSize = true };
    readonly Button _preview = new() { Text = "Preview (changes nothing)", AutoSize = true, Enabled = false };
    readonly Button _post = new() { Text = "Post to QuickBooks...", AutoSize = true, Enabled = false };
    readonly Button _export = new() { Text = "Save import files...", AutoSize = true, Enabled = false };
    readonly Button _cancel = new() { Text = "Cancel", AutoSize = true, Enabled = false };
    readonly TextBox _log = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Font = new Font("Consolas", 9f) };
    readonly Label _status = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };

    CancellationTokenSource? _cts;
    PostingPlan? _plan;
    PostingRequest? _planFor;

    sealed record StoreItem(int Id, string Text) { public override string ToString() => Text; }

    public MainForm()
    {
        Text = "Post to QuickBooks";
        Width = 980; Height = 740; MinimumSize = new Size(820, 560);
        StartPosition = FormStartPosition.CenterScreen;

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
        top.Controls.Add(_loadStores, 2, 0);

        var middle = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, SplitterDistance = 440 };
        var storesBox = new GroupBox { Text = "Stores", Dock = DockStyle.Fill, Padding = new Padding(6) };
        var storeButtons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true };
        var all = new Button { Text = "All", AutoSize = true };
        var none = new Button { Text = "None", AutoSize = true };
        all.Click += (_, _) => SetAllStores(true);
        none.Click += (_, _) => SetAllStores(false);
        storeButtons.Controls.AddRange(new Control[] { all, none });
        storesBox.Controls.Add(_stores);
        storesBox.Controls.Add(storeButtons);
        middle.Panel1.Controls.Add(storesBox);

        var opts = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(8) };
        opts.Controls.Add(Row(new Label { Text = "From", AutoSize = true, Anchor = AnchorStyles.Left }, _start, new Label { Text = "to", AutoSize = true }, _end));
        opts.Controls.Add(_gl); opts.Controls.Add(_ar); opts.Controls.Add(_arAdj);
        opts.Controls.Add(Row(_debug, _debugDir));
        middle.Panel2.Controls.Add(opts);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(8, 4, 8, 4) };
        buttons.Controls.AddRange(new Control[] { _preview, _post, _export, _cancel });

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

        _loadStores.Click += async (_, _) => await LoadStoresAsync();
        _preview.Click += async (_, _) => await RunAsync(commit: false);
        _post.Click += async (_, _) => await PostAsync();
        _cancel.Click += (_, _) => _cts?.Cancel();
        _export.Click += (_, _) => ExportFiles();
        FormClosing += (_, _) => SaveSettings();

        Log("This program posts the general ledger and accounts receivable from your DD database into QuickBooks Desktop.");
        Log("1. Open your company file in QuickBooks.  2. Connect and load stores.  3. Choose stores and dates, then Preview.  4. Post.");
    }

    static FlowLayoutPanel Row(params Control[] c)
    {
        var p = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        p.Controls.AddRange(c);
        return p;
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
        _log.AppendText(s + Environment.NewLine);
    }

    void Busy(bool busy, string status = "")
    {
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
            foreach (var it in items) _stores.Items.Add(it, !it.Text.EndsWith("(inactive)"));
            _preview.Enabled = items.Count > 0;
            Log($"Loaded {items.Count} stores.");
            SaveSettings();
        }
        catch (Exception ex) { Log("Could not load stores: " + ex.Message); MessageBox.Show(this, ex.Message, Text); }
        finally { Busy(false); }
    }

    PostingRequest? BuildRequest()
    {
        var ids = _stores.CheckedItems.Cast<StoreItem>().Select(s => s.Id).ToList();
        if (ids.Count == 0) { MessageBox.Show(this, "Check at least one store.", Text); return null; }
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
        var cs = _connection.Text.Trim();
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
                var rows = SqlPackageSource.Read(cs, req);
                if (rows.Count == 0) return (Plan: (PostingPlan?)null, Report: (PostingReport?)null);
                var plan = PlanBuilder.Build(rows, SqlPackageSource.LoadOptions(cs));
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
