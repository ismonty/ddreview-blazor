namespace DDReview.QuickBooks.Win;

/// <summary>
/// The look of the DD Review website for the poster window: dark navy panels, cream text, the blue header bar, gold buttons.
/// The colours are the ones in the website's app.css (--card, --tile, --field, --ink, --muted, --header, --gold, --link).
/// </summary>
static class DdTheme
{
    public static readonly Color Black = ColorTranslator.FromHtml("#000000");
    public static readonly Color Card = ColorTranslator.FromHtml("#1d2731");
    public static readonly Color Tile = ColorTranslator.FromHtml("#09314d");
    public static readonly Color Field = ColorTranslator.FromHtml("#0b1219");
    public static readonly Color Ink = ColorTranslator.FromHtml("#f7eedf");
    public static readonly Color Muted = ColorTranslator.FromHtml("#a9bbc9");
    public static readonly Color Edge = ColorTranslator.FromHtml("#4b7391");
    public static readonly Color Header = ColorTranslator.FromHtml("#328cc1");
    public static readonly Color HeaderInk = ColorTranslator.FromHtml("#07202f");
    public static readonly Color Gold = ColorTranslator.FromHtml("#d9b310");
    public static readonly Color GoldInk = ColorTranslator.FromHtml("#111111");
    public static readonly Color Link = ColorTranslator.FromHtml("#7abcdc");

    /// <summary>The bar across the top: the gold "MontySoft DD Review" brand on navy, and the window's purpose on the blue header.</summary>
    public static Control Banner(string title)
    {
        var bar = new Panel { Dock = DockStyle.Top, Height = 46, BackColor = Header };
        var brand = new Label
        {
            Text = "MontySoft DD Review™", AutoSize = false, Width = 230, Dock = DockStyle.Left, TextAlign = ContentAlignment.MiddleCenter,
            BackColor = Card, ForeColor = Gold, Font = new Font("Segoe UI", 13f, FontStyle.Bold),
        };
        var text = new Label
        {
            Text = title, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(16, 0, 0, 0),
            BackColor = Header, ForeColor = HeaderInk, Font = new Font("Segoe UI", 12f, FontStyle.Bold),
        };
        bar.Controls.Add(text);
        bar.Controls.Add(brand);
        return bar;
    }

    /// <summary>Paints a control and everything inside it. Gold buttons are the main actions (named in <paramref name="primary"/>); the other buttons are navy with a gold edge, like the site's small buttons.</summary>
    public static void Apply(Control root, params Control[] primary)
    {
        foreach (Control c in root.Controls) Apply(c, primary);
        switch (root)
        {
            case Form f: f.BackColor = Card; f.ForeColor = Ink; f.Font = new Font("Segoe UI", 9.5f); break;
            case Button b: StyleButton(b, primary.Contains(b)); break;
            case TextBox t: t.BackColor = Field; t.ForeColor = Ink; t.BorderStyle = BorderStyle.FixedSingle; break;
            case ListBox l: l.BackColor = Field; l.ForeColor = Ink; l.BorderStyle = BorderStyle.FixedSingle; break;
            case DateTimePicker d:
                d.CalendarMonthBackground = Field; d.CalendarForeColor = Ink; d.CalendarTitleBackColor = Header; d.CalendarTitleForeColor = HeaderInk; d.CalendarTrailingForeColor = Muted;
                break;
            case GroupBox g: g.ForeColor = Gold; g.BackColor = Card; break;
            case SplitContainer s: s.BackColor = Edge; s.Panel1.BackColor = Card; s.Panel2.BackColor = Card; break;
            case Label or CheckBox: root.BackColor = Color.Transparent; root.ForeColor = Ink; break;
            case Panel or FlowLayoutPanel or TableLayoutPanel: if (root.BackColor != Header) root.BackColor = Card; root.ForeColor = Ink; break;
        }
    }

    static void StyleButton(Button b, bool primary)
    {
        b.FlatStyle = FlatStyle.Flat;
        b.UseVisualStyleBackColor = false;
        b.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
        b.Padding = new Padding(10, 3, 10, 3);
        b.Cursor = Cursors.Hand;
        b.FlatAppearance.BorderColor = Gold;
        b.FlatAppearance.BorderSize = 1;
        void Paint()
        {
            if (!b.Enabled) { b.BackColor = Card; b.ForeColor = Muted; b.FlatAppearance.BorderColor = Edge; b.Cursor = Cursors.Default; }
            else if (primary) { b.BackColor = Gold; b.ForeColor = GoldInk; b.FlatAppearance.BorderColor = Gold; b.Cursor = Cursors.Hand; }
            else { b.BackColor = Tile; b.ForeColor = Ink; b.FlatAppearance.BorderColor = Gold; b.Cursor = Cursors.Hand; }
        }
        b.EnabledChanged += (_, _) => Paint();
        Paint();
    }
}
