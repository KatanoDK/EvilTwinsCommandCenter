using System.Drawing.Drawing2D;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace EvilTwinsUpdater;

internal static class AppTheme
{
    public static readonly Color Canvas = Color.FromArgb(15, 18, 25);
    public static readonly Color Surface = Color.FromArgb(23, 28, 38);
    public static readonly Color Input = Color.FromArgb(17, 22, 31);
    public static readonly Color Border = Color.FromArgb(49, 57, 73);
    public static readonly Color Text = Color.FromArgb(237, 240, 247);
    public static readonly Color Muted = Color.FromArgb(159, 170, 191);
    public static readonly Color Accent = Color.FromArgb(176, 143, 255);
    public static readonly Color AccentFill = Color.FromArgb(112, 76, 204);
    public static readonly Color Success = Color.FromArgb(109, 216, 172);
    public static readonly Color Negative = Color.FromArgb(255, 155, 166);
    public static readonly Color Selected = Color.FromArgb(52, 42, 79);

    public static Label Label(string text, float size = 10, bool bold = false, bool muted = false) => new() {
        Text = text, AutoSize = true, UseMnemonic = false, Tag = muted ? "muted" : null,
        Font = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular),
        Margin = new Padding(0, 0, 0, 10) };

    public static void Apply(Control control)
    {
        control.BackColor = control is TextBoxBase or ListView or ComboBox ? Input
            : control is CardPanel or PageTabs ? Surface
            : control.Parent?.BackColor ?? Canvas;
        control.ForeColor = Equals(control.Tag, "muted") ? Muted : Text;
        if (control is TextBoxBase box) { box.BorderStyle = BorderStyle.FixedSingle; }
        if (control is ComboBox combo) { combo.FlatStyle = FlatStyle.Flat; combo.Height = 32; }
        if (control is CheckBox check) check.FlatStyle = FlatStyle.Flat;
        if (control is ListView list) { list.BorderStyle = BorderStyle.None; list.OwnerDraw = true; StyleList(list); }
        if (control is DataGridView grid) StyleGrid(grid);
        if (control is TextBoxBase or DataGridView or ScrollableControl or ComboBox)
        {
            control.HandleCreated += (_, _) => DarkNativeControl(control);
            if (control.IsHandleCreated) DarkNativeControl(control);
        }
        foreach (Control child in control.Controls) Apply(child);
        if (control is ModernButton button) button.Refresh();
    }

    private static void DarkNativeControl(Control control)
    {
        if (!OperatingSystem.IsWindows()) return;
        SetWindowTheme(control.Handle, "DarkMode_Explorer", null);
        foreach (Control child in control.Controls)
            if (child is ScrollBar && child.IsHandleCreated) SetWindowTheme(child.Handle, "DarkMode_Explorer", null);
    }
    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr window, string application, string? classes);

    private static void StyleList(ListView list)
    {
        list.DrawColumnHeader += (_, args) => {
            using var background = new SolidBrush(Surface);
            args.Graphics.FillRectangle(background, args.Bounds);
            TextRenderer.DrawText(args.Graphics, args.Header?.Text, list.Font,
                Rectangle.Inflate(args.Bounds, -8, 0), Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        };
        list.DrawItem += (_, args) => { if (list.View != View.Details) args.DrawDefault = true; };
        list.DrawSubItem += (_, args) => args.DrawDefault = true;
    }

    private static void StyleGrid(DataGridView grid)
    {
        grid.BackgroundColor = Input; grid.BorderStyle = BorderStyle.None;
        grid.GridColor = Border; grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        grid.EnableHeadersVisualStyles = false; grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
        grid.ColumnHeadersHeight = 38; grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle { BackColor = Surface, ForeColor = Muted,
            Font = new Font("Segoe UI", 9, FontStyle.Bold), Padding = new Padding(8, 0, 8, 0), SelectionBackColor = Surface, SelectionForeColor = Text };
        grid.DefaultCellStyle.BackColor = Input; grid.DefaultCellStyle.ForeColor = Text;
        grid.DefaultCellStyle.SelectionBackColor = Selected; grid.DefaultCellStyle.SelectionForeColor = Text;
        grid.DefaultCellStyle.Padding = new Padding(8, 7, 8, 7);
        grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(21, 26, 36);
        grid.RowTemplate.Height = 38;
    }

    public static GraphicsPath Rounded(RectangleF rectangle, float radius = 8)
    {
        var path = new GraphicsPath(); var diameter = radius * 2;
        path.AddArc(rectangle.Left, rectangle.Top, diameter, diameter, 180, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Top, diameter, diameter, 270, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rectangle.Left, rectangle.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure(); return path;
    }

    public static void DarkTitleBar(Form form)
    {
        if (!OperatingSystem.IsWindows()) return;
        var enabled = 1;
        try
        {
            DwmSetWindowAttribute(form.Handle, 20, ref enabled, sizeof(int));
            var caption = Canvas.R | Canvas.G << 8 | Canvas.B << 16;
            var text = Text.R | Text.G << 8 | Text.B << 16;
            DwmSetWindowAttribute(form.Handle, 35, ref caption, sizeof(int));
            DwmSetWindowAttribute(form.Handle, 36, ref text, sizeof(int));
        }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
    }
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
}

internal sealed class ModernButton : Button
{
    private bool hover;
    [DefaultValue(false)]
    public bool Selected { get; set; }
    public ModernButton()
    {
        FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0; UseVisualStyleBackColor = false;
        Padding = new Padding(14, 8, 14, 8); MinimumSize = new Size(0, 38);
        Margin = new Padding(0, 0, 8, 0); Cursor = Cursors.Hand;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
    }
    protected override void OnMouseEnter(EventArgs args) { hover = true; Invalidate(); base.OnMouseEnter(args); }
    protected override void OnMouseLeave(EventArgs args) { hover = false; Invalidate(); base.OnMouseLeave(args); }
    protected override void OnEnabledChanged(EventArgs args) { Invalidate(); base.OnEnabledChanged(args); }
    protected override void OnPaint(PaintEventArgs args)
    {
        var graphics = args.Graphics; graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(Parent?.BackColor ?? AppTheme.Canvas);
        var primary = Equals(Tag, "primary");
        var quiet = Equals(Tag, "navigation") || Equals(Tag, "tab");
        var fill = !Enabled ? AppTheme.Surface : Selected ? AppTheme.Selected
            : primary ? (hover ? Color.FromArgb(128, 90, 225) : AppTheme.AccentFill)
            : hover ? Color.FromArgb(39, 46, 61) : quiet ? Parent?.BackColor ?? AppTheme.Surface : AppTheme.Surface;
        using var shape = AppTheme.Rounded(new RectangleF(0.5f, 0.5f, Width - 1, Height - 1));
        using var brush = new SolidBrush(fill); graphics.FillPath(brush, shape);
        if (!quiet && !primary) { using var pen = new Pen(AppTheme.Border); graphics.DrawPath(pen, shape); }
        if (Selected && Equals(Tag, "navigation")) { using var accent = new SolidBrush(AppTheme.Accent); graphics.FillRectangle(accent, 0, 10, 3, Height - 20); }
        var textColor = !Enabled ? Color.FromArgb(101, 112, 133) : Selected ? AppTheme.Accent : AppTheme.Text;
        var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis;
        flags |= TextAlign == ContentAlignment.MiddleLeft ? TextFormatFlags.Left : TextFormatFlags.HorizontalCenter;
        var bounds = Rectangle.FromLTRB(Padding.Left, 0, Width - Padding.Right, Height);
        TextRenderer.DrawText(graphics, Text.Replace("&&", "&"), Font, bounds, textColor, flags | TextFormatFlags.NoPrefix);
        if (Focused && ShowFocusCues) { using var pen = new Pen(AppTheme.Accent) { DashStyle = DashStyle.Dot }; graphics.DrawRectangle(pen, 4, 4, Width - 9, Height - 9); }
    }
}

internal sealed class CardPanel : Panel
{
    public CardPanel() { DoubleBuffered = true; Padding = new Padding(20); BackColor = AppTheme.Surface; Margin = new Padding(0, 0, 0, 16); }
    protected override void OnPaintBackground(PaintEventArgs args)
    {
        args.Graphics.Clear(Parent?.BackColor ?? AppTheme.Canvas);
        args.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = AppTheme.Rounded(new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), 10);
        using var fill = new SolidBrush(BackColor); args.Graphics.FillPath(fill, path);
        using var pen = new Pen(AppTheme.Border); args.Graphics.DrawPath(pen, path);
    }
}

internal sealed class PageTabs : UserControl
{
    private readonly FlowLayoutPanel header = new() { Dock = DockStyle.Top, Height = 52, WrapContents = false, Padding = new Padding(4) };
    private readonly Panel body = new() { Dock = DockStyle.Fill, Padding = new Padding(12) };
    private readonly List<Control> pages = [];
    private readonly List<ModernButton> buttons = [];
    private int selectedIndex;
    public event Action? SelectedIndexChanged;
    [DefaultValue(0)]
    public int SelectedIndex
    {
        get => selectedIndex;
        set
        {
            if (value < 0 || value >= pages.Count) return;
            selectedIndex = value;
            for (var index = 0; index < pages.Count; index++) { pages[index].Visible = index == value; buttons[index].Selected = index == value; buttons[index].Invalidate(); }
            pages[value].BringToFront();
            SelectedIndexChanged?.Invoke();
        }
    }
    public PageTabs() { Dock = DockStyle.Fill; BackColor = AppTheme.Surface; Controls.Add(body); Controls.Add(header); }
    public void AddAction(Control action) => header.Controls.Add(action);
    public void AddPage(string title, Control page)
    {
        var index = pages.Count;
        var button = new ModernButton { Text = title, AutoSize = true, Tag = "tab", MinimumSize = new Size(140, 34), Padding = new Padding(12, 4, 12, 4) };
        button.Click += (_, _) => SelectedIndex = index;
        button.KeyDown += (_, args) => {
            if (args.KeyCode is Keys.Left or Keys.Right) { SelectedIndex = (SelectedIndex + (args.KeyCode == Keys.Right ? 1 : pages.Count - 1)) % pages.Count; buttons[SelectedIndex].Focus(); args.Handled = true; }
        };
        page.Dock = DockStyle.Fill; pages.Add(page); buttons.Add(button); header.Controls.Add(button); body.Controls.Add(page);
        SelectedIndex = selectedIndex;
    }
}

internal sealed class ModernComboBox : ComboBox
{
    public ModernComboBox() { DrawMode = DrawMode.OwnerDrawFixed; FlatStyle = FlatStyle.Flat; ItemHeight = 24; }
    protected override void WndProc(ref Message message)
    {
        base.WndProc(ref message);
        if (message.Msg is not (0x000F or 0x0317 or 0x0318)) return; // Paint the native arrow in the same palette, including bitmap previews.
        using var graphics = message.Msg is 0x0317 or 0x0318 && message.WParam != IntPtr.Zero
            ? Graphics.FromHdc(message.WParam) : Graphics.FromHwnd(Handle);
        var arrowWidth = Math.Max(24, SystemInformation.VerticalScrollBarWidth);
        using var fill = new SolidBrush(Enabled ? AppTheme.Surface : AppTheme.Input);
        graphics.FillRectangle(fill, Width - arrowWidth, 1, arrowWidth - 1, Height - 2);
        using var border = new Pen(Focused ? AppTheme.Accent : AppTheme.Border);
        graphics.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
        var center = new Point(Width - arrowWidth / 2, Height / 2);
        using var arrow = new SolidBrush(Enabled ? AppTheme.Muted : AppTheme.Border);
        graphics.FillPolygon(arrow, [new Point(center.X - 4, center.Y - 2), new Point(center.X + 4, center.Y - 2), new Point(center.X, center.Y + 3)]);
    }
    protected override void OnDrawItem(DrawItemEventArgs args)
    {
        var selected = (args.State & DrawItemState.Selected) != 0 && (args.State & DrawItemState.ComboBoxEdit) == 0;
        using var fill = new SolidBrush(selected ? AppTheme.Selected : AppTheme.Input);
        args.Graphics.FillRectangle(fill, args.Bounds);
        var text = args.Index >= 0 ? GetItemText(Items[args.Index]) : Text;
        TextRenderer.DrawText(args.Graphics, text, Font, Rectangle.Inflate(args.Bounds, -6, 0),
            Enabled ? AppTheme.Text : AppTheme.Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
    }
}

internal sealed class ModernProgressBar : ProgressBar
{
    private readonly System.Windows.Forms.Timer animation = new() { Interval = 40 };
    private int offset;
    public ModernProgressBar()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        animation.Tick += (_, _) => { offset = (offset + 5) % Math.Max(1, Width); Invalidate(); };
    }
    protected override void OnVisibleChanged(EventArgs args) { animation.Enabled = Visible; base.OnVisibleChanged(args); }
    protected override void OnPaint(PaintEventArgs args)
    {
        args.Graphics.Clear(AppTheme.Border);
        using var fill = new SolidBrush(AppTheme.Accent);
        if (Style == ProgressBarStyle.Marquee) args.Graphics.FillRectangle(fill, offset, 0, Math.Max(40, Width / 5), Height);
        else args.Graphics.FillRectangle(fill, 0, 0, Width * (float)(Value - Minimum) / Math.Max(1, Maximum - Minimum), Height);
    }
    protected override void Dispose(bool disposing) { if (disposing) animation.Dispose(); base.Dispose(disposing); }
}
