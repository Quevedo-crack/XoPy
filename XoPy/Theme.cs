using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace XoPy;

public sealed class Palette
{
    public bool Dark { get; init; }
    public Color Canvas { get; init; }
    public Color Surface { get; init; }
    public Color SurfaceAlt { get; init; }
    public Color Raised { get; init; }
    public Color Hover { get; init; }
    public Color Border { get; init; }
    public Color Text { get; init; }
    public Color Muted { get; init; }
    public Color Accent { get; init; }
    public Color AccentSoft { get; init; }
    public Color Success { get; init; }
    public Color Warning { get; init; }
    public Color Danger { get; init; }
    public Color Selection { get; init; }
    public Color Track { get; init; }

    public static Palette For(bool dark) => dark ? DarkTheme : LightTheme;

    public static readonly Palette DarkTheme = new()
    {
        Dark = true,
        Canvas = Color.FromArgb(24, 27, 32),
        Surface = Color.FromArgb(32, 36, 42),
        SurfaceAlt = Color.FromArgb(37, 41, 48),
        Raised = Color.FromArgb(45, 50, 58),
        Hover = Color.FromArgb(58, 64, 75),
        Border = Color.FromArgb(58, 64, 74),
        Text = Color.FromArgb(233, 237, 243),
        Muted = Color.FromArgb(150, 159, 172),
        Accent = Color.FromArgb(48, 128, 235),
        AccentSoft = Color.FromArgb(32, 74, 130),
        Success = Color.FromArgb(60, 186, 121),
        Warning = Color.FromArgb(226, 166, 58),
        Danger = Color.FromArgb(232, 94, 92),
        Selection = Color.FromArgb(38, 78, 128),
        Track = Color.FromArgb(55, 61, 71)
    };

    public static readonly Palette LightTheme = new()
    {
        Dark = false,
        Canvas = Color.FromArgb(243, 245, 248),
        Surface = Color.FromArgb(255, 255, 255),
        SurfaceAlt = Color.FromArgb(248, 250, 252),
        Raised = Color.FromArgb(238, 241, 245),
        Hover = Color.FromArgb(226, 232, 240),
        Border = Color.FromArgb(211, 217, 226),
        Text = Color.FromArgb(28, 35, 45),
        Muted = Color.FromArgb(110, 121, 136),
        Accent = Color.FromArgb(29, 108, 214),
        AccentSoft = Color.FromArgb(214, 230, 252),
        Success = Color.FromArgb(29, 143, 88),
        Warning = Color.FromArgb(184, 126, 24),
        Danger = Color.FromArgb(196, 61, 58),
        Selection = Color.FromArgb(212, 229, 252),
        Track = Color.FromArgb(224, 229, 236)
    };
}

public static class Icons
{
    public const string Add = "\uE710";
    public const string FolderAdd = "\uE8F4";
    public const string Copy = "\uE8C8";
    public const string Move = "\uE8C6";
    public const string Delete = "\uE74D";
    public const string Verify = "\uE73E";
    public const string Pause = "\uE769";
    public const string Play = "\uE768";
    public const string Stop = "\uE71A";
    public const string Settings = "\uE713";
    public const string More = "\uE712";
    public const string ChevronDown = "\uE70D";
    public const string ChevronRight = "\uE76C";
    public const string Folder = "\uE8B7";
    public const string File = "\uE7C3";
    public const string Open = "\uE8E5";
    public const string History = "\uE81C";
    public const string Save = "\uE74E";
    public const string Star = "\uE734";
    public const string Shield = "\uE83D";
    public const string Warning = "\uE7BA";
    public const string Check = "\uE73E";
    public const string Clock = "\uE823";
    public const string Blocked = "\uE733";
    public const string Remove = "\uE738";
    public const string Up = "\uE70E";
    public const string Down = "\uE70D";
    public const string Refresh = "\uE72C";
    public const string Expand = "\uE710";

    public static Image Glyph(string glyph, Color color, int size = 20)
    {
        var bitmap = new Bitmap(size, size);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        graphics.Clear(Color.Transparent);
        using var font = new Font("Segoe MDL2 Assets", size * 0.86f, FontStyle.Regular, GraphicsUnit.Pixel);
        TextRenderer.DrawText(graphics, glyph, font, new Rectangle(0, 0, size, size), color,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
        return bitmap;
    }

    public static Icon AppIcon()
    {
        try
        {
            var icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            if (icon is not null) return icon;
        }
        catch { }
        return SystemIcons.Application;
    }

    public static Icon ProgressIcon(int percent, bool dark, bool paused = false, bool error = false, bool completed = false)
    {
        var clamped = Math.Clamp(percent, 0, 100);
        using var bitmap = new Bitmap(32, 32);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.Clear(Color.Transparent);
            using var background = new SolidBrush(dark ? Color.FromArgb(30, 34, 40) : Color.FromArgb(250, 250, 252));
            using var track = new Pen(dark ? Color.FromArgb(66, 74, 86) : Color.FromArgb(205, 212, 222), 4);
            var color = error ? Color.FromArgb(226, 84, 82)
                : paused ? Color.FromArgb(230, 168, 58)
                : completed ? Color.FromArgb(56, 186, 116)
                : Color.FromArgb(48, 140, 240);
            using var fill = new Pen(color, 4);
            using var text = new SolidBrush(dark ? Color.White : Color.FromArgb(30, 40, 55));
            using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            graphics.FillEllipse(background, 1, 1, 30, 30);
            graphics.DrawArc(track, 4, 4, 24, 24, -90, 359);

            if (clamped > 0) graphics.DrawArc(fill, 4, 4, 24, 24, -90, Math.Min(359, 360 * clamped / 100f));

            if (paused)
            {
                using var bar = new SolidBrush(color);
                graphics.FillRectangle(bar, 12, 11, 3, 10);
                graphics.FillRectangle(bar, 17, 11, 3, 10);
            }
            else
            {
                using var font = new Font("Segoe UI", clamped >= 100 ? 8f : 9f, FontStyle.Bold, GraphicsUnit.Pixel);
                graphics.DrawString(clamped.ToString(), font, text, new RectangleF(2, 2, 28, 28), format);
            }
        }

        var handle = bitmap.GetHicon();
        try
        {
            using var borrowed = Icon.FromHandle(handle);
            return (Icon)borrowed.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr handle);
}

public static class Ui
{
    public static Button ToolButton(string text, string glyph, Palette palette, Color? accent = null)
    {
        var background = accent ?? palette.Raised;
        var foreground = accent.HasValue ? Color.White : palette.Text;
        return new Button
        {
            Text = text,
            Image = Icons.Glyph(glyph, foreground, 18),
            ImageAlign = ContentAlignment.MiddleLeft,
            TextAlign = ContentAlignment.MiddleCenter,
            TextImageRelation = TextImageRelation.ImageBeforeText,
            FlatStyle = FlatStyle.Flat,
            AutoSize = false,
            Size = new Size(112, 38),
            BackColor = background,
            ForeColor = foreground,
            Font = new Font("Segoe UI", 8.75f, FontStyle.Bold),
            Cursor = Cursors.Hand,
            Margin = new Padding(0, 0, 6, 0),
            Padding = new Padding(7, 0, 7, 0),
            FlatAppearance = { BorderSize = 0, MouseOverBackColor = Lighten(background, 14), MouseDownBackColor = Lighten(background, 26) }
        };
    }

    public static Button IconButton(string glyph, Palette palette, string tooltip = "")
    {
        var button = new Button
        {
            Text = string.Empty,
            Image = Icons.Glyph(glyph, palette.Text, 16),
            FlatStyle = FlatStyle.Flat,
            Size = new Size(32, 32),
            BackColor = palette.Raised,
            ForeColor = palette.Text,
            Cursor = Cursors.Hand,
            Margin = new Padding(0, 0, 4, 0),
            FlatAppearance = { BorderSize = 0, MouseOverBackColor = palette.Hover }
        };
        if (!string.IsNullOrEmpty(tooltip)) new ToolTip().SetToolTip(button, tooltip);
        return button;
    }

    public static Button DialogButton(string text, Palette palette, bool primary = false)
    {
        var background = primary ? palette.Accent : palette.Raised;
        var foreground = primary ? Color.White : palette.Text;
        return new Button
        {
            Text = text,
            AutoSize = false,
            Size = new Size(112, 32),
            BackColor = background,
            ForeColor = foreground,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 9f, FontStyle.Bold),
            Cursor = Cursors.Hand,
            Margin = new Padding(6, 0, 0, 0),
            FlatAppearance = { BorderSize = 0, MouseOverBackColor = Lighten(background, 16) }
        };
    }

    public static Label Label(string text, Palette palette, float size = 9f, bool bold = false, Color? color = null)
        => new()
        {
            Text = text,
            AutoSize = true,
            ForeColor = color ?? palette.Text,
            BackColor = Color.Transparent,
            Font = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular),
            Margin = new Padding(0)
        };

    public static Color Lighten(Color color, int amount)
        => Color.FromArgb(color.A, Math.Min(255, color.R + amount), Math.Min(255, color.G + amount), Math.Min(255, color.B + amount));

    public static GraphicsPath RoundedRectangle(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        var diameter = Math.Max(1, Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height)));
        var arc = new Rectangle(bounds.X, bounds.Y, diameter, diameter);
        path.AddArc(arc, 180, 90);
        arc.X = bounds.Right - diameter;
        path.AddArc(arc, 270, 90);
        arc.Y = bounds.Bottom - diameter;
        path.AddArc(arc, 0, 90);
        arc.X = bounds.X;
        path.AddArc(arc, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static void DrawProgress(Graphics graphics, Rectangle bounds, double percent, Palette palette, Color? fill = null)
    {
        if (bounds.Width <= 2 || bounds.Height <= 2) return;
        var radius = Math.Max(2, bounds.Height / 2);
        using (var track = RoundedRectangle(bounds, radius))
        using (var brush = new SolidBrush(palette.Track))
            graphics.FillPath(brush, track);

        var width = (int)Math.Round((bounds.Width - 2) * Math.Clamp(percent, 0, 100) / 100d);
        if (width > 0)
        {
            var filled = new Rectangle(bounds.X + 1, bounds.Y + 1, Math.Max(1, width), Math.Max(1, bounds.Height - 2));
            using var path = RoundedRectangle(filled, Math.Max(1, radius - 1));
            using var brush = new SolidBrush(fill ?? palette.Accent);
            graphics.FillPath(brush, path);
        }
    }

    public static void StyleMenu(ToolStrip menu, Palette palette)
    {
        menu.RenderMode = ToolStripRenderMode.Professional;
        menu.Renderer = new DarkMenuRenderer(palette);
        menu.BackColor = palette.Surface;
        menu.ForeColor = palette.Text;
        menu.Font = new Font("Segoe UI", 9f);
    }
}

internal sealed class MenuColors : ProfessionalColorTable
{
    private readonly Palette _palette;
    public MenuColors(Palette palette) => _palette = palette;

    public override Color MenuBorder => _palette.Border;
    public override Color MenuItemBorder => _palette.Accent;
    public override Color MenuItemSelected => _palette.Hover;
    public override Color MenuItemSelectedGradientBegin => _palette.Hover;
    public override Color MenuItemSelectedGradientEnd => _palette.Hover;
    public override Color MenuStripGradientBegin => _palette.Canvas;
    public override Color MenuStripGradientEnd => _palette.Canvas;
    public override Color ToolStripDropDownBackground => _palette.Surface;
    public override Color ImageMarginGradientBegin => _palette.Surface;
    public override Color ImageMarginGradientMiddle => _palette.Surface;
    public override Color ImageMarginGradientEnd => _palette.Surface;
    public override Color SeparatorDark => _palette.Border;
    public override Color SeparatorLight => _palette.Border;
    public override Color ButtonSelectedHighlight => _palette.Hover;
    public override Color ButtonSelectedGradientBegin => _palette.Hover;
    public override Color ButtonSelectedGradientEnd => _palette.Hover;
    public override Color CheckBackground => _palette.Accent;
    public override Color CheckSelectedBackground => _palette.Accent;
    public override Color CheckPressedBackground => _palette.Accent;
}

/// <summary>Renderiza menús y desplegables con la paleta de la aplicación (texto, fondo, flechas, separadores y marcas).</summary>
internal sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
{
    private readonly Palette _palette;

    public DarkMenuRenderer(Palette palette) : base(new MenuColors(palette))
    {
        _palette = palette;
        RoundedEdges = false;
    }

    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
    {
        using var brush = new SolidBrush(e.ToolStrip is MenuStrip ? _palette.Canvas : _palette.Surface);
        e.Graphics.FillRectangle(brush, e.AffectedBounds);
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        using var pen = new Pen(_palette.Border);
        if (e.ToolStrip is ToolStripDropDown)
        {
            e.Graphics.DrawRectangle(pen, new Rectangle(0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1));
            return;
        }
        if (e.ToolStrip is MenuStrip) return;
        e.Graphics.DrawLine(pen, 0, e.ToolStrip.Height - 1, e.ToolStrip.Width, e.ToolStrip.Height - 1);
    }

    protected override void OnRenderButtonBackground(ToolStripItemRenderEventArgs e)
    {
        if (e.Item is not ToolStripButton) { base.OnRenderButtonBackground(e); return; }

        var background = e.Item.BackColor;
        if (background.IsEmpty || background == Color.Transparent) background = _palette.Surface;
        if (e.Item.Pressed) background = Ui.Lighten(background, 26);
        else if (e.Item.Selected) background = Ui.Lighten(background, 14);
        if (!e.Item.Enabled) background = _palette.SurfaceAlt;

        using var brush = new SolidBrush(background);
        using var path = Ui.RoundedRectangle(new Rectangle(0, 0, Math.Max(1, e.Item.Width - 1), Math.Max(1, e.Item.Height - 1)), 5);
        e.Graphics.FillPath(brush, path);
    }

    protected override void OnRenderOverflowButtonBackground(ToolStripItemRenderEventArgs e)
    {
        using var background = new SolidBrush(_palette.Raised);
        e.Graphics.FillRectangle(background, new Rectangle(Point.Empty, e.Item.Size));
        using var pen = new Pen(_palette.Text, 1.6f);
        var centerX = e.Item.Width / 2;
        var centerY = e.Item.Height / 2;
        e.Graphics.DrawLines(pen, new[]
        {
            new Point(centerX - 4, centerY - 2),
            new Point(centerX, centerY + 2),
            new Point(centerX + 4, centerY - 2)
        });
    }

    protected override void OnRenderImageMargin(ToolStripRenderEventArgs e)
    {
        using var brush = new SolidBrush(_palette.Surface);
        e.Graphics.FillRectangle(brush, e.AffectedBounds);
    }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        var item = e.Item;
        var selected = item.Selected && item.Enabled;
        var background = selected ? _palette.Hover : _palette.Surface;
        using var brush = new SolidBrush(background);
        var bounds = new Rectangle(2, 0, Math.Max(0, e.Item.Width - 4), e.Item.Height);
        e.Graphics.FillRectangle(brush, bounds);
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        var accent = e.Item.BackColor == _palette.Accent;
        e.TextColor = !e.Item.Enabled ? _palette.Muted : accent ? Color.White : _palette.Text;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        e.ArrowColor = _palette.Text;
        base.OnRenderArrow(e);
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        using var brush = new SolidBrush(_palette.Border);
        e.Graphics.FillRectangle(brush, new Rectangle(6, e.Item.Height / 2, Math.Max(0, e.Item.Width - 12), 1));
    }

    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        var bounds = e.ImageRectangle;
        using var background = new SolidBrush(_palette.Accent);
        using var path = Ui.RoundedRectangle(bounds, 3);
        e.Graphics.FillPath(background, path);
        TextRenderer.DrawText(e.Graphics, "\uE73E", new Font("Segoe MDL2 Assets", 9f, FontStyle.Regular, GraphicsUnit.Pixel),
            bounds, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }

    protected override void OnRenderToolStripStatusLabelBackground(ToolStripItemRenderEventArgs e)
    {
        using var brush = new SolidBrush(_palette.Surface);
        e.Graphics.FillRectangle(brush, new Rectangle(Point.Empty, e.Item.Size));
    }
}

internal sealed class ProgressStrip : Control
{
    private double _value;
    private readonly Palette _palette;
    private Color _fill;

    public ProgressStrip(Palette palette, Color? fill = null)
    {
        _palette = palette;
        _fill = fill ?? palette.Accent;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
        Height = 12;
    }

    public Color FillColor
    {
        get => _fill;
        set { _fill = value; Invalidate(); }
    }

    public double Value
    {
        get => _value;
        set
        {
            var clamped = Math.Clamp(value, 0, 100);
            if (Math.Abs(clamped - _value) < 0.05) return;
            _value = clamped;
            Invalidate();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        Ui.DrawProgress(e.Graphics, new Rectangle(0, 0, Width - 1, Height - 1), _value, _palette, _fill);
    }
}

/// <summary>Gráfica de velocidad de transferencia (estilo TeraCopy).</summary>
internal sealed class SpeedGraph : Control
{
    private readonly Palette _palette;
    private readonly List<double> _samples = new();
    private const int Capacity = 150;

    public SpeedGraph(Palette palette)
    {
        _palette = palette;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = palette.SurfaceAlt;
        Height = 120;
    }

    public void Reset()
    {
        _samples.Clear();
        Invalidate();
    }

    public void Add(double bytesPerSecond)
    {
        if (bytesPerSecond < 0) bytesPerSecond = 0;
        _samples.Add(bytesPerSecond);
        while (_samples.Count > Capacity) _samples.RemoveAt(0);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var width = Width - 1;
        var height = Height - 1;
        using (var background = new SolidBrush(_palette.SurfaceAlt))
            graphics.FillRectangle(background, 0, 0, Width, Height);

        using (var grid = new Pen(Color.FromArgb(38, _palette.Border), 1))
        {
            for (var i = 1; i < 4; i++)
            {
                var y = height * i / 4f;
                graphics.DrawLine(grid, 0, y, width, y);
            }
        }

        if (_samples.Count < 2)
        {
            using var idle = new SolidBrush(_palette.Muted);
            graphics.DrawString("Sin actividad", new Font("Segoe UI", 9f), idle, new PointF(12, 10));
            return;
        }

        var max = Math.Max(1, _samples.Max());
        var points = new PointF[_samples.Count];
        for (var i = 0; i < _samples.Count; i++)
        {
            var x = width * i / (float)Math.Max(1, _samples.Count - 1);
            var y = height - 2 - (float)(_samples[i] / max) * (height - 8);
            points[i] = new PointF(x, y);
        }

        using (var area = new GraphicsPath())
        {
            area.AddLines(points);
            area.AddLine(points[^1].X, points[^1].Y, width, height);
            area.AddLine(width, height, 0, height);
            area.CloseFigure();
            using var gradient = new LinearGradientBrush(new Rectangle(0, 0, Math.Max(1, Width), Math.Max(1, Height)),
                Color.FromArgb(150, _palette.Accent), Color.FromArgb(18, _palette.Accent), LinearGradientMode.Vertical);
            graphics.FillPath(gradient, area);
        }

        using (var line = new Pen(_palette.Accent, 1.8f))
            graphics.DrawLines(line, points);

        using var label = new SolidBrush(_palette.Muted);
        graphics.DrawString("Máx. " + Format.Speed(max), new Font("Segoe UI", 8.25f), label, new PointF(10, 6));
    }
}

internal sealed class ChoiceButton : Button
{
    private readonly string[] _items;
    private readonly ContextMenuStrip _menu = new();
    private readonly Palette _palette;
    private int _selectedIndex;

    public ChoiceButton(Palette palette, string[] items, int selected)
    {
        _palette = palette;
        _items = items;
        _selectedIndex = Math.Clamp(selected, 0, items.Length - 1);

        Text = _items[_selectedIndex];
        TextAlign = ContentAlignment.MiddleLeft;
        FlatStyle = FlatStyle.Flat;
        BackColor = palette.Surface;
        ForeColor = palette.Text;
        Font = new Font("Segoe UI", 9f);
        Height = 32;
        Width = 340;
        Cursor = Cursors.Hand;
        Image = Icons.Glyph(Icons.ChevronDown, palette.Muted, 12);
        ImageAlign = ContentAlignment.MiddleRight;
        TextImageRelation = TextImageRelation.TextBeforeImage;
        Padding = new Padding(9, 0, 6, 0);
        FlatAppearance.BorderColor = palette.Border;
        FlatAppearance.BorderSize = 1;
        FlatAppearance.MouseOverBackColor = palette.Hover;

        Ui.StyleMenu(_menu, palette);
        _menu.ShowImageMargin = false;
        Click += (_, _) => ShowMenu();
    }

    public event EventHandler? SelectedIndexChanged;

    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            var clamped = Math.Clamp(value, 0, _items.Length - 1);
            if (clamped == _selectedIndex) return;
            _selectedIndex = clamped;
            Text = _items[_selectedIndex];
            SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void ShowMenu()
    {
        _menu.Items.Clear();
        for (var i = 0; i < _items.Length; i++)
        {
            var index = i;
            var item = new ToolStripMenuItem(_items[i]) { Checked = i == _selectedIndex };
            item.Click += (_, _) => SelectedIndex = index;
            _menu.Items.Add(item);
        }
        _menu.Show(this, new Point(0, Height));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _menu.Dispose();
        base.Dispose(disposing);
    }
}
