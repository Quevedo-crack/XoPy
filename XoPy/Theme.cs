using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
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

    /// <summary>
    /// Paleta tomada de FlashCopyPro: grises neutros (sin el tinte azulado que tenía XoPy),
    /// acento #0078D4 de Windows 11 y verde/ámbar/rojo más vivos. Es la capa visual que el
    /// usuario quería traerse del otro programa.
    /// </summary>
    public static readonly Palette DarkTheme = new()
    {
        Dark = true,
        Canvas = Color.FromArgb(0x20, 0x20, 0x20),
        Surface = Color.FromArgb(0x2D, 0x2D, 0x2D),
        SurfaceAlt = Color.FromArgb(0x29, 0x29, 0x29),
        Raised = Color.FromArgb(0x3A, 0x3A, 0x3A),
        Hover = Color.FromArgb(0x45, 0x45, 0x45),
        Border = Color.FromArgb(0x3A, 0x3A, 0x3A),
        Text = Color.FromArgb(0xE6, 0xE6, 0xE6),
        Muted = Color.FromArgb(0x9A, 0x9A, 0x9A),
        Accent = Color.FromArgb(0x00, 0x78, 0xD4),
        AccentSoft = Color.FromArgb(0x1F, 0x3D, 0x5C),
        Success = Color.FromArgb(0x6C, 0xCB, 0x5F),
        Warning = Color.FromArgb(0xF4, 0xC5, 0x42),
        Danger = Color.FromArgb(0xFF, 0x6B, 0x6B),
        Selection = Color.FromArgb(0x11, 0x4A, 0x78),
        Track = Color.FromArgb(0x3A, 0x3A, 0x3A)
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
    // Rayo: indica el modo multihilo (varios archivos a la vez).
    public const string Lightning = "\uE945";
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
    /// <summary>Barrer/vaciar: distinto de la papelera para no confundirse con «Eliminar del disco».</summary>
    public const string ClearList = "\uE894";
    public const string Up = "\uE70E";
    public const string Down = "\uE70D";
    public const string Refresh = "\uE72C";
    public const string Expand = "\uE710";
    public const string SortAsc = "\uE8CB";   // flechas arriba/abajo: ordenar

    /// <summary>
    /// Fuente de iconos: en Windows 11 se usa «Segoe Fluent Icons» (los mismos dibujos, pero
    /// con el trazo nuevo) y en Windows 10 se cae a «Segoe MDL2 Assets». Se resuelve UNA vez:
    /// recorrer las fuentes instaladas en cada pintado era carísimo. Viene de FlashCopyPro.
    /// </summary>
    public static readonly string IconFamily = ResolveIconFamily();

    private static string ResolveIconFamily()
    {
        try
        {
            using var installed = new InstalledFontCollection();
            foreach (var family in installed.Families)
                if (string.Equals(family.Name, "Segoe Fluent Icons", StringComparison.OrdinalIgnoreCase))
                    return family.Name;
        }
        catch { }
        return "Segoe MDL2 Assets";
    }

    public static Image Glyph(string glyph, Color color, int size = 20)
    {
        var bitmap = new Bitmap(size, size);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        graphics.Clear(Color.Transparent);
        using var font = new Font(IconFamily, size * 0.86f, FontStyle.Regular, GraphicsUnit.Pixel);
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

    /// <summary>Icono cuadrado con el porcentaje grande, para el botón de la barra de tareas.</summary>
    public static Icon PercentBadge(int percent, bool dark, bool paused = false, bool error = false)
    {
        var clamped = Math.Clamp(percent, 0, 100);
        // Superposición del botón de la barra de tareas (junto al menú Inicio). Antes se
        // dibujaba a SANGRE sobre los 128 px enteros y Windows la pegaba encima del icono de
        // la aplicación tapándolo: se veía un número gigantesco. Ahora el número vive dentro
        // de un círculo con margen, así que la insignia ocupa bastante menos y el icono de
        // XoPy se sigue viendo detrás.
        const int size = 128;
        const int margin = 16;           // aire alrededor: la insignia se ve más pequeña
        var box = new Rectangle(margin, margin, size - margin * 2, size - margin * 2);
        using var bitmap = new Bitmap(size, size);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            graphics.Clear(Color.Transparent);
            var color = error ? Color.FromArgb(214, 66, 64)
                : paused ? Color.FromArgb(214, 148, 38)
                : clamped >= 100 ? Color.FromArgb(38, 160, 96)
                : Color.FromArgb(48, 140, 240);
            // Borde blanco: separa la insignia del icono que tiene debajo.
            using (var halo = new Pen(Color.FromArgb(235, 255, 255, 255), 8f))
                graphics.DrawEllipse(halo, box);
            using (var background = new SolidBrush(color))
                graphics.FillEllipse(background, box);

            using var text = new SolidBrush(Color.White);
            // Sin el símbolo «%»: a 16 px reales no se lee y solo robaba sitio al número.
            // El tamaño se MIDE para que el número entre entero: «100» se quedaba cortado.
            var label = clamped.ToString();
            var inner = new RectangleF(box.X + 6, box.Y + 4, box.Width - 12, box.Height - 8);
            DrawFittedNumber(graphics, label, inner, text, 72f);
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

    /// <summary>
    /// Escribe un número centrado y lo más grande posible DENTRO del hueco, sin que se corte
    /// ni una cifra. Antes el tamaño de letra se elegía a ojo según los dígitos y el «100»
    /// se quedaba sin el último cero en el icono del área de notificación.
    /// </summary>
    private static void DrawFittedNumber(Graphics graphics, string label, RectangleF box, Brush brush, float maxPixels)
    {
        // Sin los márgenes que GDI+ añade por su cuenta: son los que se comían la cifra.
        using var format = new StringFormat(StringFormat.GenericTypographic)
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
            FormatFlags = StringFormatFlags.NoWrap | StringFormatFlags.NoClip
        };
        var pixels = maxPixels;
        while (pixels > 8f)
        {
            using var probe = new Font("Segoe UI", pixels, FontStyle.Bold, GraphicsUnit.Pixel);
            var measured = graphics.MeasureString(label, probe, new SizeF(10000f, 10000f), format);
            if (measured.Width <= box.Width && measured.Height <= box.Height)
            {
                graphics.DrawString(label, probe, brush, box, format);
                return;
            }
            pixels -= 2f;
        }
        using var smallest = new Font("Segoe UI", 8f, FontStyle.Bold, GraphicsUnit.Pixel);
        graphics.DrawString(label, smallest, brush, box, format);
    }

    private static GraphicsPath RoundedRect(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        var size = radius * 2;
        path.AddArc(bounds.Left, bounds.Top, size, size, 180, 90);
        path.AddArc(bounds.Right - size, bounds.Top, size, size, 270, 90);
        path.AddArc(bounds.Right - size, bounds.Bottom - size, size, size, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - size, size, size, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static Icon ProgressIcon(int percent, bool dark, bool paused = false, bool error = false, bool completed = false)
    {
        var clamped = Math.Clamp(percent, 0, 100);
        // Icono del área de notificación (junto al reloj). Windows lo enseña a 16 px: el
        // anillo de progreso que llevaba antes se comía el centro y el número quedaba
        // diminuto. Ahora el número OCUPA TODO el icono —fondo de color liso, cifras a
        // sangre— y el progreso es una franja fina en la base. Se dibuja a 64 px para que
        // Windows lo reduzca con buena calidad.
        const int box = 64;
        using var bitmap = new Bitmap(box, box);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            graphics.Clear(Color.Transparent);
            var color = error ? Color.FromArgb(226, 84, 82)
                : paused ? Color.FromArgb(230, 168, 58)
                : completed || clamped >= 100 ? Color.FromArgb(56, 186, 116)
                : Color.FromArgb(48, 140, 240);

            using (var background = new SolidBrush(color))
            using (var shape = RoundedRect(new Rectangle(0, 0, box, box), 12))
                graphics.FillPath(background, shape);

            using var white = new SolidBrush(Color.White);

            if (paused)
            {
                graphics.FillRectangle(white, 20, 16, 8, 32);
                graphics.FillRectangle(white, 36, 16, 8, 32);
            }
            else
            {
                // Cifras lo más grandes que quepan ENTERAS: el tamaño se mide, no se adivina
                // por el número de dígitos (así «100» ya no pierde el último cero).
                var label = clamped.ToString();
                DrawFittedNumber(graphics, label, new RectangleF(3, 1, box - 6, box - 15), white, 54f);

                // Franja de progreso en la base: se entiende de un vistazo aun sin leer el número.
                using var trackBrush = new SolidBrush(Color.FromArgb(90, 0, 0, 0));
                graphics.FillRectangle(trackBrush, 6, box - 11, box - 12, 6);
                var width = (int)Math.Round((box - 12) * clamped / 100.0);
                if (width > 0) graphics.FillRectangle(white, 6, box - 11, width, 6);
            }
            _ = dark;
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

    /// <summary>
    /// Botón de cuadro de diálogo. TODOS IGUALES: mismo fondo, mismo texto y mismo borde.
    /// El parámetro <paramref name="primary"/> se mantiene para no tocar las llamadas, pero
    /// ya no pinta nada de otro color: el usuario pidió que ninguna respuesta venga
    /// resaltada, para decidir leyendo y no por el color del botón.
    /// </summary>
    public static Button DialogButton(string text, Palette palette, bool primary = false)
    {
        var background = palette.Raised;
        var button = new Button
        {
            Text = text,
            AutoSize = false,
            Size = new Size(112, 32),
            BackColor = background,
            ForeColor = palette.Text,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 9f),
            Cursor = Cursors.Hand,
            Margin = new Padding(6, 0, 0, 0),
            // Sin foco dibujado: el rectángulo de puntos del botón predeterminado también
            // es «resaltar uno más que los demás».
            UseVisualStyleBackColor = false,
            TabStop = true,
            FlatAppearance = { BorderSize = 1, MouseOverBackColor = Lighten(background, 16) }
        };
        button.FlatAppearance.BorderColor = palette.Border;
        return button;
    }

    /// <summary>
    /// Etiqueta de UNA sola línea que recorta con puntos suspensivos. El Label de WinForms
    /// parte el texto en varias líneas cuando no cabe a lo ancho (AutoEllipsis solo actúa
    /// después de partirlo), y en una franja de 36 px eso se ve como dos renglones
    /// apretados: pasaba con el recuento de la lista («4 archivo(s) · 22.0 KB»).
    /// </summary>
    public sealed class OneLineLabel : Label
    {
        public OneLineLabel()
        {
            AutoSize = false;
            UseCompatibleTextRendering = false;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var align = TextAlign switch
            {
                ContentAlignment.MiddleRight or ContentAlignment.TopRight or ContentAlignment.BottomRight
                    => TextFormatFlags.Right,
                ContentAlignment.MiddleCenter or ContentAlignment.TopCenter or ContentAlignment.BottomCenter
                    => TextFormatFlags.HorizontalCenter,
                _ => TextFormatFlags.Left
            };
            var flags = TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis
                        | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter | align;
            // El fondo ya lo pinta OnPaintBackground (y respeta el color transparente).
            TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, ForeColor, flags);
        }
    }

    /// <summary>Etiqueta de una línea con el estilo del tema.</summary>
    public static OneLineLabel OneLine(string text, Palette palette, float size = 9f, bool bold = false, Color? color = null)
        => new()
        {
            Text = text,
            ForeColor = color ?? palette.Text,
            BackColor = Color.Transparent,
            Font = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular),
            Margin = new Padding(0)
        };

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

    public static void StyleNumeric(NumericUpDown numeric, Palette palette)
    {
        numeric.BackColor = palette.SurfaceAlt;
        numeric.ForeColor = palette.Text;
        numeric.BorderStyle = BorderStyle.FixedSingle;
        foreach (Control child in numeric.Controls)
        {
            child.BackColor = palette.SurfaceAlt;
            child.ForeColor = palette.Text;
            if (child is TextBox text) text.BorderStyle = BorderStyle.None;
        }
    }

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

    /// <summary>
    /// Repasa TODA la ventana y pinta con la paleta cualquier control que se haya quedado
    /// con los colores del sistema. Es lo que elimina las «barras blancas»: un Panel, un
    /// TableLayoutPanel o un TextBox al que se le olvidó el BackColor sale con el gris claro
    /// de Windows (#F0F0F0) o con blanco puro, y en una ventana oscura se ve como una franja
    /// encendida. Solo toca lo que sigue en color de sistema: lo que ya tiene un color
    /// propio (barras de progreso, pastillas de estado, botones de acento) no se pisa.
    /// </summary>
    public static void PaintAll(Control root, Palette palette)
    {
        // Nunca debe tumbar una ventana por un color: si algo falla, se deja como estaba.
        if (root is null || palette is null) return;
        try { Apply(root, palette, 0); }
        catch { }
    }

    private static bool IsSystemBackground(Color color) =>
        color.IsSystemColor
        || color.ToArgb() == Color.White.ToArgb()
        || color.ToArgb() == SystemColors.Control.ToArgb()
        || color.ToArgb() == SystemColors.Window.ToArgb()
        || color.ToArgb() == SystemColors.ControlLight.ToArgb()
        || color.ToArgb() == SystemColors.ControlLightLight.ToArgb()
        || color.ToArgb() == SystemColors.AppWorkspace.ToArgb();

    private static bool IsSystemForeground(Color color) =>
        color.IsSystemColor
        || color.ToArgb() == Color.Black.ToArgb()
        || color.ToArgb() == SystemColors.ControlText.ToArgb()
        || color.ToArgb() == SystemColors.WindowText.ToArgb();

    private static void Apply(Control control, Palette palette, int depth)
    {
        // Tope de profundidad: una jerarquía con un ciclo (no debería existir) no debe colgar
        // el arranque de la ventana.
        if (depth > 40) return;

        switch (control)
        {
            // Los de texto editable van sobre la superficie elevada, no sobre el lienzo:
            // así se distingue dónde se puede escribir.
            case TextBoxBase box:
                if (IsSystemBackground(box.BackColor)) box.BackColor = palette.SurfaceAlt;
                if (IsSystemForeground(box.ForeColor)) box.ForeColor = palette.Text;
                break;
            case ComboBox combo:
                if (IsSystemBackground(combo.BackColor)) combo.BackColor = palette.Raised;
                if (IsSystemForeground(combo.ForeColor)) combo.ForeColor = palette.Text;
                combo.FlatStyle = FlatStyle.Flat;
                break;
            case ListBox list:
                if (IsSystemBackground(list.BackColor)) list.BackColor = palette.Surface;
                if (IsSystemForeground(list.ForeColor)) list.ForeColor = palette.Text;
                list.BorderStyle = BorderStyle.FixedSingle;
                break;
            case ListView view:
                if (IsSystemBackground(view.BackColor)) view.BackColor = palette.Surface;
                if (IsSystemForeground(view.ForeColor)) view.ForeColor = palette.Text;
                break;
            case TreeView tree:
                if (IsSystemBackground(tree.BackColor)) tree.BackColor = palette.Surface;
                if (IsSystemForeground(tree.ForeColor)) tree.ForeColor = palette.Text;
                tree.BorderStyle = BorderStyle.None;
                break;
            case NumericUpDown numeric:
                StyleNumeric(numeric, palette);
                break;
            case Button button:
                if (IsSystemBackground(button.BackColor))
                {
                    button.BackColor = palette.Raised;
                    button.FlatStyle = FlatStyle.Flat;
                    button.FlatAppearance.BorderSize = 0;
                }
                if (IsSystemForeground(button.ForeColor)) button.ForeColor = palette.Text;
                break;
            case ProgressBar bar:
                if (IsSystemBackground(bar.BackColor)) bar.BackColor = palette.Track;
                break;
            // «Label» a secas sería el método Ui.Label de esta misma clase (grupo de
            // métodos, no un tipo): hay que nombrar el control con su espacio completo.
            case System.Windows.Forms.Label or CheckBox or RadioButton:
                // Las etiquetas van transparentes para heredar el fondo del contenedor: si se
                // les pone un color fijo se recortan en rectángulos cuando el padre cambia.
                if (IsSystemBackground(control.BackColor)) control.BackColor = Color.Transparent;
                if (IsSystemForeground(control.ForeColor)) control.ForeColor = palette.Text;
                break;
            case TabControl tabs:
                if (IsSystemBackground(tabs.BackColor)) tabs.BackColor = palette.Canvas;
                break;
            case ToolStrip strip:
                if (IsSystemBackground(strip.BackColor)) strip.BackColor = palette.Surface;
                if (IsSystemForeground(strip.ForeColor)) strip.ForeColor = palette.Text;
                break;
            default:
                if (IsSystemBackground(control.BackColor)) control.BackColor = palette.Canvas;
                if (IsSystemForeground(control.ForeColor)) control.ForeColor = palette.Text;
                break;
        }

        foreach (Control child in control.Controls) Apply(child, palette, depth + 1);
    }

    /// <summary>
    /// Pinta de oscuro la BARRA DE TÍTULO de una ventana (la que dibuja Windows, no XoPy).
    /// Sin esto, los cuadros de diálogo salían con la barra blanca de fábrica encima de una
    /// ventana negra: era la «barra blanca» más llamativa de todas.
    /// </summary>
    public static void DarkFrame(Form form, Palette palette)
    {
        if (form is null || palette is null || !palette.Dark || !form.IsHandleCreated) return;
        try
        {
            var enabled = 1;
            // 20 = DWMWA_USE_IMMERSIVE_DARK_MODE en Windows 10 20H1 en adelante; 19 en las
            // versiones anteriores. Se prueba el nuevo y, si falla, el antiguo.
            if (NativeTheme.DwmSetWindowAttribute(form.Handle, 20, ref enabled, sizeof(int)) != 0)
                NativeTheme.DwmSetWindowAttribute(form.Handle, 19, ref enabled, sizeof(int));
        }
        catch { }
    }

    /// <summary>
    /// Oscurece las barras de desplazamiento NATIVAS de todos los controles de una ventana
    /// (paneles con desplazamiento, listas, árboles y cuadros de texto). Windows las pinta
    /// en blanco mientras no se le pida el tema oscuro para ese control concreto.
    /// </summary>
    public static void DarkScrollbars(Control root, Palette palette)
    {
        if (root is null || palette is null || !palette.Dark) return;
        Walk(root, 0);

        static void Walk(Control control, int depth)
        {
            if (depth > 40) return;
            try
            {
                if (control.IsHandleCreated
                    && (control is ScrollableControl { AutoScroll: true } or ListView or ListBox
                        or TreeView or TextBoxBase or ComboBox or DataGridView))
                {
                    NativeTheme.SetWindowTheme(control.Handle, "DarkMode_Explorer", null);
                }
            }
            catch { }
            foreach (Control child in control.Controls) Walk(child, depth + 1);
        }
    }

    public static void StyleMenu(ToolStrip menu, Palette palette)
    {
        menu.RenderMode = ToolStripRenderMode.Professional;
        menu.Renderer = new DarkMenuRenderer(palette);
        menu.BackColor = palette.Surface;
        menu.ForeColor = palette.Text;
        menu.Font = new Font("Segoe UI", 9f);

        // El renderer profesional no colorea el texto de los menús de primer nivel y quedaba
        // casi negro sobre negro. Se fija el color a mano en cada elemento.
        foreach (ToolStripItem item in menu.Items)
            item.ForeColor = palette.Text;
        // ToolStrip solo expone ItemAdded (ToolStripItemEventHandler), no ItemsAdded.
        menu.ItemAdded += (_, e) =>
        {
            if (e.Item is not null) e.Item.ForeColor = palette.Text;
        };
    }
}

/// <summary>Funciones de Windows que hacen falta para el tema oscuro de los marcos nativos.</summary>
internal static class NativeTheme
{
    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    internal static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [System.Runtime.InteropServices.DllImport("uxtheme.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    internal static extern int SetWindowTheme(IntPtr hwnd, string appName, string? idList);

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);

    private const int WM_VSCROLL = 0x0115;
    private const int SB_TOP = 6;

    /// <summary>
    /// Sube del todo un cuadro de texto de varias líneas. Poner el cursor en la posición 0
    /// no basta si el control no tiene el foco: el texto se quedaba enseñando el final.
    /// </summary>
    internal static void ScrollToTop(Control control)
    {
        if (control is null || !control.IsHandleCreated) return;
        try { SendMessage(control.Handle, WM_VSCROLL, new IntPtr(SB_TOP), IntPtr.Zero); }
        catch { }
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

    /// <summary>
    /// Fuente de la marca de verificación de los menús. Compartida y estática: crear un
    /// Font dentro de un OnRender se hace en CADA repintado y agota los recursos GDI.
    /// </summary>
    private static readonly Font CheckFont = new(Icons.IconFamily, 9f, FontStyle.Regular, GraphicsUnit.Pixel);

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
        // Los elementos de la barra de menús (Archivo, Cola…) comparten el fondo del menú
        // superior; si se pintan como el desplegable, el texto claro se pierde.
        var onMenuBar = item.Owner is MenuStrip;
        var background = selected ? _palette.Hover : onMenuBar ? _palette.Canvas : _palette.Surface;
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
        TextRenderer.DrawText(e.Graphics, "\uE73E", CheckFont,
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

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color FillColor
    {
        get => _fill;
        set { _fill = value; Invalidate(); }
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
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

/// <summary>
/// Etiqueta con forma de píldora, como los rótulos «Copiando»/«Destino» de TeraCopy.
/// Se dibuja a mano para que el redondeo y el color coincidan exactamente con el tema.
/// </summary>
internal sealed class PillLabel : Control
{
    private readonly Palette _palette;
    private string _text;

    public PillLabel(string text, Palette palette)
    {
        _palette = palette;
        _text = text;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                 | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        Font = new Font("Segoe UI", 8.25f, FontStyle.Regular);
        Height = 22;
    }

    [AllowNull]
    public override string Text
    {
        get => _text;
        set
        {
            var next = value ?? string.Empty;
            if (string.Equals(_text, next, StringComparison.Ordinal)) return;
            _text = next;
            Invalidate();
        }
    }

    /// <summary>Color de acento de la píldora (por defecto, gris del tema).</summary>
    /// <remarks>
    /// El diseñador de WinForms exige declarar explícitamente cómo se serializa una
    /// propiedad propia en un control (WFO1000); se oculta porque no se edita en el diseñador.
    /// </remarks>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color Accent { get; set; } = Color.Empty;

    protected override void OnPaint(PaintEventArgs e)
    {
        var graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using (var background = new SolidBrush(_palette.Canvas)) graphics.FillRectangle(background, ClientRectangle);

        var accent = Accent.IsEmpty ? _palette.Raised : Accent;
        var bounds = new Rectangle(0, Math.Max(0, (Height - 20) / 2), Math.Max(1, Width - 1), 20);
        using var path = Ui.RoundedRectangle(bounds, 10);
        using (var fill = new SolidBrush(accent)) graphics.FillPath(fill, path);
        using var border = new Pen(_palette.Border);
        graphics.DrawPath(border, path);

        TextRenderer.DrawText(graphics, _text, Font, bounds, _palette.Text,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
            | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
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
        using (var border = new Pen(_palette.Border, 1))
            graphics.DrawRectangle(border, 0, 0, width, height);

        if (_samples.Count < 2)
        {
            using var idle = new SolidBrush(_palette.Muted);
            using var idleFont = new Font("Segoe UI", 8.25f);
            graphics.DrawString("Velocidad  ·  Sin actividad", idleFont, idle, new PointF(10, 8));
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
        using var labelFont = new Font("Segoe UI", 8.25f);
        graphics.DrawString("Máx. " + Format.Speed(max), labelFont, label, new PointF(10, 6));
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
        Height = 30;
        // Ancho mínimo: el texto debe caber entero sin cortarse; crece con el ancho del diálogo.
        Width = 380;
        MinimumSize = new Size(300, 30);
        AutoEllipsis = true;
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

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
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
