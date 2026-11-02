using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace FullAIon2Companion;

/// <summary>
/// The crafting card, opened from the overlay strip's ingot: the level and EXP of the profession crafted last (with the EXP
/// its level needs and how many crafts are left, once known), the other professions, and the session crafting: crafts, EXP
/// gained, levels and what was made, with their hourly rates and «Reiniciar» (<see cref="CraftWatch"/>). Moves and resizes
/// like the Abyss card: beside the overlay until it is dragged by its header (a double click sends it back), and its corner
/// makes it bigger or smaller. Takes the mouse, never the keyboard focus.
/// </summary>
public sealed class CraftWindow : Window
{
    public const string IngotIcon = "/assets/game/Icon_Item_Craft_Ore_Ingot_A_c_001.webp";
    static readonly Brush Muted = Paint("#95A4B6"), Gold = Paint("#E9C77E"), Teal = Paint("#71DED6"), Dim = Paint("#5F7480"),
        Up = Paint("#5FE0A0"), Track = Paint("#22313C"), Line = Paint("#1F2C31");
    readonly StackPanel body = new();
    string shownKey = "";
    double scale = 1, active;
    /// <summary>The card was dragged to a new place, or a double click on its header sent it back beside the overlay (null).</summary>
    public event Action<Point?>? Placed;
    /// <summary>The corner was dragged: the card's own size (1 = as drawn).</summary>
    public event Action<double>? Resized;
    /// <summary>While the header is being dragged: the overlay leaves the card where the mouse takes it.</summary>
    public bool Dragging { get; private set; }
    readonly System.Windows.Controls.Primitives.Thumb corner = new() { Width = 16, Height = 16, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
        Margin = new Thickness(0, 0, -10, -8), Cursor = Cursors.SizeNWSE, ToolTip = "Arrastra para cambiar el tamaño de la ficha" };
    /// <summary>«Reiniciar»: a new session (set by the overlay).</summary>
    public Action? ResetRequested { get; set; }
    /// <summary>The × of the card.</summary>
    public event Action? CloseRequested;
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr hwnd, int index, int value);
    public CraftWindow(double opacity)
    {
        Title = "FullAIon2Companion · Fabricación"; Width = 300; SizeToContent = SizeToContent.Height;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; AllowsTransparency = true; Background = Brushes.Transparent;
        Topmost = true; ShowActivated = false; ShowInTaskbar = false;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        byte alpha = (byte)Math.Clamp(opacity * 255, 90, 250);
        var mark = new FrameworkElementFactory(typeof(TextBlock));
        mark.SetValue(TextBlock.TextProperty, "◢"); mark.SetValue(TextBlock.ForegroundProperty, Teal); mark.SetValue(TextBlock.FontSizeProperty, 13.0);
        mark.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Right); mark.SetValue(VerticalAlignmentProperty, VerticalAlignment.Bottom);
        corner.Template = new ControlTemplate(typeof(System.Windows.Controls.Primitives.Thumb)) { VisualTree = mark };
        corner.DragDelta += (_, e) => Scale(scale + e.HorizontalChange * scale / 300);
        corner.DragCompleted += (_, _) => Resized?.Invoke(scale);
        var face = new Grid(); face.Children.Add(body); face.Children.Add(corner);
        Content = new Border { Background = new SolidColorBrush(Color.FromArgb(alpha, 13, 20, 27)), BorderBrush = new SolidColorBrush(Color.FromArgb(200, 51, 87, 91)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 9, 12, 10), Child = face };
        SourceInitialized += (_, _) => { var hwnd = new WindowInteropHelper(this).Handle; SetWindowLong(hwnd, -20, GetWindowLong(hwnd, -20) | 0x08000000 | 0x80); };
    }
    public void ApplyAppearance(byte alpha, double k)
    {
        ((Border)Content).Background = new SolidColorBrush(Color.FromArgb(alpha, 13, 20, 27));
        Scale(k);
    }
    void Scale(double k)
    {
        scale = Math.Clamp(k, .7, 2.2);
        ((Border)Content).LayoutTransform = new ScaleTransform(scale, scale); Width = 300 * scale;
    }

    /// <summary>Draws the card when something on it changed, and says whether it did (the overlay then gives it its style at
    /// once). Not while a button of the card is held down: a rebuild would swallow the click.</summary>
    public bool Render(CraftWatch.View v)
    {
        var s = v.Session;
        string key = $"{L.Language}|" + string.Join(";", v.Professions.Select(p => $"{p.Profession}:{p.Level}:{p.Exp}:{p.Needed}:{p.Session?.Crafts}:{p.Session?.LevelUps}"))
            + (s is null ? "" : $"|{s.Active:0}|{string.Join(",", s.Recipes.Select(r => $"{r.Key}:{r.Value}"))}");
        if (key == shownKey || IsMouseOver && Mouse.LeftButton == MouseButtonState.Pressed) return false;
        shownKey = key;
        body.Children.Clear();
        body.Children.Add(Head());
        active = s?.Active ?? 0;
        double hours = Math.Max(active, 60) / 3600;

        if (v.Professions.Count == 0)
            body.Children.Add(Note("Tus oficios salen al entrar al juego con la app abierta, o con tu primera fabricación."));
        else
        {
            // The profession crafted last, big: its level, its EXP against what the level needs, and what is left.
            var main = v.Professions[0];
            var title = new TextBlock { Margin = new Thickness(0, 6, 0, 2), FontSize = 15, Foreground = Brushes.White };
            title.Inlines.Add(new Run(CraftWatch.Names[main.Profession]) { FontWeight = FontWeights.Bold, Foreground = Gold });
            if (main.Level is int lv)
            {
                // As the game writes it: «Especialista nv. 1» for the whole level 51.
                var (grade, own) = CraftWatch.Grade(lv);
                title.Inlines.Add(new Run("  "));
                title.Inlines.Add(new Run(grade) { FontWeight = FontWeights.SemiBold, Foreground = GradeColor(lv) });
                title.Inlines.Add(new Run($" nv. {own}") { FontWeight = FontWeights.SemiBold, Foreground = GradeColor(lv) });
            }
            body.Children.Add(title);
            var g = Grid2();
            if (main.Exp is long exp)
            {
                if (main.Needed is long need) Row(g, "EXP", Strong($"{exp:N0}"), new Run($" / {need:N0}") { Foreground = Muted });
                else Row(g, "EXP", Strong($"{exp:N0}"));
            }
            body.Children.Add(g);
            if (main.Exp is long e && main.Needed is long n && n > 0)
            {
                body.Children.Add(Bar(Math.Clamp(e / (double)n, 0, 1)));
                long left = Math.Max(0, n - e);
                // How many crafts are left, each giving what the last one gave (a batch of the same recipe, as in the game).
                string crafts = main.Session is { Last: > 0 } p ? $" · ≈ {Math.Ceiling(left / (double)p.Last):N0} fabricaciones" : "";
                body.Children.Add(Note($"Faltan {left:N0} de EXP para subir de nivel{crafts}"));
            }
            // The caps: 50 waits for the promotion quest, 100 is the last level.
            else if (main.Level is 50) body.Children.Add(Note("Tope de Principiante: completa la misión de ascenso para pasar a Especialista."));
            else if (main.Level is >= 100) body.Children.Add(Note("Nivel máximo del oficio."));

            // The other professions, one line each.
            if (v.Professions.Count > 1)
            {
                body.Children.Add(Section("OTROS OFICIOS"));
                var others = Grid2();
                foreach (var o in v.Professions.Skip(1))
                {
                    var runs = new List<Run>();
                    if (o.Level is int l) { var (grade, own) = CraftWatch.Grade(l); runs.Add(new Run(grade + " ") { Foreground = GradeColor(l) }); runs.Add(Strong($"nv. {own}")); }
                    else runs.Add(Strong("nv. ?"));
                    if (o.Exp is long oe) runs.Add(new Run(o.Needed is long on ? $"  {oe:N0} / {on:N0}" : $"  {oe:N0} EXP") { Foreground = Muted });
                    Row(others, CraftWatch.Names[o.Profession], [.. runs]);
                }
                body.Children.Add(others);
            }
        }

        // The session crafting.
        var head = new DockPanel { Margin = new Thickness(0, 9, 0, 3) };
        var reset = new Button { Content = new TextBlock { Text = "Reiniciar", FontSize = 10.5, Foreground = Teal }, Padding = new Thickness(7, 1, 7, 2), Cursor = Cursors.Hand, Focusable = false,
            Background = Paint("#2213242C"), BorderBrush = Paint("#33575B"), BorderThickness = new Thickness(1), ToolTip = "Empieza una sesión nueva: tu próxima fabricación la abre", Visibility = s is null ? Visibility.Collapsed : Visibility.Visible };
        reset.Click += (_, _) => { ResetRequested?.Invoke(); shownKey = ""; };
        DockPanel.SetDock(reset, Dock.Right); head.Children.Add(reset);
        var sessionTitle = Label("ESTA SESIÓN", 10, Teal); sessionTitle.FontWeight = FontWeights.SemiBold; head.Children.Add(sessionTitle);
        body.Children.Add(head);
        if (s is null) body.Children.Add(Note("Empieza sola con tu primera fabricación."));
        else
        {
            var g = Grid2();
            Row(g, "Tiempo fabricando", Strong(Span(s.Active)), new Run($"  desde las {s.Start:HH:mm}") { Foreground = Muted });
            body.Children.Add(g);
            foreach (var (profession, p) in s.Professions.OrderByDescending(p => p.Key == v.Current).ThenByDescending(p => p.Value.Crafts))
            {
                // One profession: its lines under the session; several: each one under its name.
                if (s.Professions.Count > 1) { var name = Label(CraftWatch.Names[profession], 10, Gold); name.Margin = new Thickness(0, 5, 0, 1); body.Children.Add(name); }
                var rows = Grid2();
                Row(rows, "Fabricaciones", Strong($"{p.Crafts:N0}"), PerHour(p.Crafts / hours));
                Row(rows, "EXP ganada", Strong($"+{p.Exp:N0}", Up), PerHour(p.Exp / hours));
                int now = v.Professions.FirstOrDefault(l => l.Profession == profession)?.Level ?? 0;
                if (p.LevelUps > 0 && p.From > 0 && now > 0)
                {
                    // In one grade «nv. 41 → 44»; across the promotion «Principiante 49 → Especialista 1».
                    var (fromGrade, fromOwn) = CraftWatch.Grade(p.From); var (toGrade, toOwn) = CraftWatch.Grade(now);
                    if (fromGrade == toGrade) Row(rows, "Niveles", Strong($"nv. {fromOwn} → {toOwn}", Gold), new Run($"  +{p.LevelUps}") { Foreground = Up });
                    else Row(rows, "Niveles", new Run(fromGrade) { Foreground = GradeColor(p.From) }, Strong($" {fromOwn} → ", Gold), new Run(toGrade) { Foreground = GradeColor(now) }, Strong($" {toOwn}", Gold), new Run($"  +{p.LevelUps}") { Foreground = Up });
                }
                else if (p.LevelUps > 0) Row(rows, "Niveles", Strong($"+{p.LevelUps}", Gold));
                body.Children.Add(rows);
            }
            // What was made: the recipe's item, as many as the crafts times what each one makes; recipes of the same item
            // (one ingot or ten) on one line.
            var made = s.Recipes.Select(r => (Item: CraftWatch.Product(r.Key)?.Item ?? -r.Key, Count: (long)r.Value * (CraftWatch.Product(r.Key)?.Count ?? 1)))
                .GroupBy(r => r.Item).Select(g => (Item: g.Key, Count: g.Sum(r => r.Count))).OrderByDescending(r => r.Count).ToList();
            if (made.Count > 0)
            {
                var label = Label("FABRICADO", 9.5, Dim); label.Margin = new Thickness(0, 6, 0, 2); body.Children.Add(label);
                foreach (var m in made.Take(8))
                {
                    var row = new DockPanel { Margin = new Thickness(0, 1, 0, 1) };
                    var info = m.Item > 0 ? InventoryWatch.Info(m.Item) : ("Receta desconocida", null, "", 0);
                    var count = Label($"×{m.Count:N0}", 10.5, Brushes.White); count.FontWeight = FontWeights.SemiBold; count.Margin = new Thickness(6, 0, 0, 0);
                    DockPanel.SetDock(count, Dock.Right); row.Children.Add(count);
                    if (OverlayWindow.Icon(info.Icon) is { } image) { var pic = new Image { Source = image, Width = 16, Height = 16, Margin = new Thickness(0, 0, 5, 0) }; DockPanel.SetDock(pic, Dock.Left); row.Children.Add(pic); }
                    row.Children.Add(Label(info.Name, 10.5, Paint(DropsWindow.Grades.TryGetValue(info.Grade, out var gr) ? gr.Color : "#DCE1E9")));
                    body.Children.Add(row);
                }
                if (made.Count > 8) body.Children.Add(Note($"y {made.Count - 8} objetos más"));
            }
        }
        body.Children.Add(new Border { Height = 1, Background = Line, Margin = new Thickness(0, 8, 0, 0) });
        body.Children.Add(Note("Cuenta tus fabricaciones con la app abierta y se guarda por personaje."));
        L.Apply(body); // drawn between two passes of L.ApplyAll
        return true;
    }

    UIElement Head()
    {
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 2), Background = Brushes.Transparent, Cursor = Cursors.SizeAll, ToolTip = "Arrastra para mover · doble clic: vuelve junto al overlay" };
        head.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount == 2) { e.Handled = true; Placed?.Invoke(null); return; }
            double left = Left, top = Top;
            Dragging = true;
            try { DragMove(); } catch (InvalidOperationException) { }
            finally { Dragging = false; }
            if (Left != left || Top != top) Placed?.Invoke(new Point(Left, Top));
        };
        var close = new Button { Content = new TextBlock { Text = "×", FontSize = 16, Foreground = Muted }, Width = 22, Height = 20, Cursor = Cursors.Hand, Focusable = false,
            Background = Brushes.Transparent, BorderThickness = new Thickness(0), ToolTip = "Cerrar", Padding = new Thickness(0, -3, 0, 0) };
        close.Click += (_, _) => CloseRequested?.Invoke();
        DockPanel.SetDock(close, Dock.Right); head.Children.Add(close);
        var ingot = new Image { Source = OverlayWindow.Icon(IngotIcon), Width = 20, Height = 20, Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(ingot, Dock.Left); head.Children.Add(ingot);
        var title = Label("FABRICACIÓN", 12.5, Gold); title.FontWeight = FontWeights.Bold; head.Children.Add(title);
        return head;
    }
    // The grade's colour: Especialista green, as in the game's crafting window.
    static Brush GradeColor(int level) => level > 50 ? Specialist : Brushes.White;
    static readonly Brush Specialist = Paint("#4FD77A");
    static UIElement Section(string text)
    {
        var t = Label(text, 10, Teal); t.FontWeight = FontWeights.SemiBold; t.Margin = new Thickness(0, 7, 0, 3);
        return t;
    }
    static Grid Grid2()
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        return g;
    }
    static void Row(Grid g, string label, params Run[] value)
    {
        int at = g.RowDefinitions.Count; g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var name = Label(label, 10.5, Muted); name.Margin = new Thickness(0, 1, 14, 1); Grid.SetRow(name, at); g.Children.Add(name);
        var figures = new TextBlock { FontSize = 10.5, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        foreach (var run in value) figures.Inlines.Add(run);
        Grid.SetRow(figures, at); Grid.SetColumn(figures, 1); g.Children.Add(figures);
    }
    static UIElement Bar(double ratio)
    {
        var bar = new Grid { Height = 5, Margin = new Thickness(0, 3, 0, 1) };
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ratio, GridUnitType.Star) });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1 - ratio, GridUnitType.Star) });
        var track = new Border { Background = Track, CornerRadius = new CornerRadius(3) }; Grid.SetColumnSpan(track, 2);
        bar.Children.Add(track); bar.Children.Add(new Border { Background = Gold, CornerRadius = new CornerRadius(3) });
        return bar;
    }
    static TextBlock Note(string text) { var t = Label(text, 10, Dim); t.TextWrapping = TextWrapping.Wrap; t.TextTrimming = TextTrimming.None; t.Margin = new Thickness(0, 3, 0, 0); return t; }
    static Run Strong(string text, Brush? color = null) => new(text) { FontWeight = FontWeights.SemiBold, Foreground = color ?? Brushes.White };
    // The rate per hour crafting, once there are 5 minutes of it.
    Run PerHour(double value) => new(active >= 300 ? $"  {value:N0} / h" : "") { Foreground = Muted };
    static string Span(double seconds) { var t = TimeSpan.FromSeconds(Math.Max(0, seconds)); return t.TotalHours >= 1 ? $"{(int)t.TotalHours} h {t.Minutes:00} m" : $"{t.Minutes} m {t.Seconds:00} s"; }
    static Brush Paint(string color) { var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)); b.Freeze(); return b; }
    static TextBlock Label(string text, double size, Brush color) => new() { Text = text, FontSize = size, Foreground = color, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
}
