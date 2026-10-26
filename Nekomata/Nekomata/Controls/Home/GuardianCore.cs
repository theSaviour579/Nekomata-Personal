using System.Collections;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using Nekomata.UI.Controls.Common;
using Nekomata.UI.ViewModels;

namespace Nekomata.UI.Controls.Home;

// The Guardian core at the centre of the home page. The outer ring is the working day (focus purple, meetings cyan,
// own time grey; finished items fade, a cyan marker shows now); the inner ring is progress through what is happening
// now (a focus block, a meeting, a countdown to a deadline). The core breathes while Guardian listens, thinks
// or speaks. Drawn on a fixed 680-unit canvas and scaled by a Viewbox, so it is sharp at any size.
public sealed class GuardianCore : Canvas
{
    public static readonly DependencyProperty ItemsProperty = DependencyProperty.Register(
        nameof(Items), typeof(IEnumerable), typeof(GuardianCore), new PropertyMetadata(null, OnItemsChanged));
    public static readonly DependencyProperty DayStartProperty = DependencyProperty.Register(
        nameof(DayStart), typeof(DateTime), typeof(GuardianCore), new PropertyMetadata(DateTime.Today.AddHours(8), Redraw));
    public static readonly DependencyProperty DayEndProperty = DependencyProperty.Register(
        nameof(DayEnd), typeof(DateTime), typeof(GuardianCore), new PropertyMetadata(DateTime.Today.AddHours(17), Redraw));
    public static readonly DependencyProperty NowProperty = DependencyProperty.Register(
        nameof(Now), typeof(DateTime), typeof(GuardianCore), new PropertyMetadata(DateTime.Now, Redraw));
    public static readonly DependencyProperty ShowNowProperty = DependencyProperty.Register(
        nameof(ShowNow), typeof(bool), typeof(GuardianCore), new PropertyMetadata(true, Redraw));
    public static readonly DependencyProperty ProgressProperty = DependencyProperty.Register(
        nameof(Progress), typeof(double), typeof(GuardianCore), new PropertyMetadata(0d, Redraw));
    public static readonly DependencyProperty AccentProperty = DependencyProperty.Register(
        nameof(Accent), typeof(Color), typeof(GuardianCore), new PropertyMetadata(Color.FromRgb(0x9C, 0x85, 0xFF), Redraw));
    public static readonly DependencyProperty DimDayProperty = DependencyProperty.Register(
        nameof(DimDay), typeof(bool), typeof(GuardianCore), new PropertyMetadata(false, Redraw));
    public static readonly DependencyProperty RingCommandProperty = DependencyProperty.Register(
        nameof(RingCommand), typeof(System.Windows.Input.ICommand), typeof(GuardianCore), new PropertyMetadata(null, Redraw));
    public static readonly DependencyProperty MoveCommandProperty = DependencyProperty.Register(
        nameof(MoveCommand), typeof(System.Windows.Input.ICommand), typeof(GuardianCore), new PropertyMetadata(null));
    public static readonly DependencyProperty CalmProperty = DependencyProperty.Register(
        nameof(Calm), typeof(bool), typeof(GuardianCore), new PropertyMetadata(false, OnStateChanged));
    public static readonly DependencyProperty IntroCountProperty = DependencyProperty.Register(
        nameof(IntroCount), typeof(int), typeof(GuardianCore), new PropertyMetadata(0, OnIntroCountChanged));
    public static readonly DependencyProperty CelebrateCountProperty = DependencyProperty.Register(
        nameof(CelebrateCount), typeof(int), typeof(GuardianCore), new PropertyMetadata(0, OnCelebrateCountChanged));
    public static readonly DependencyProperty PulseCountProperty = DependencyProperty.Register(
        nameof(PulseCount), typeof(int), typeof(GuardianCore), new PropertyMetadata(0, OnPulseCountChanged));
    public static readonly DependencyProperty StateProperty = DependencyProperty.Register(
        nameof(State), typeof(GuardianPresenceState), typeof(GuardianCore), new PropertyMetadata(GuardianPresenceState.Idle, OnStateChanged));

    public IEnumerable? Items { get => (IEnumerable?)GetValue(ItemsProperty); set => SetValue(ItemsProperty, value); }
    public DateTime DayStart { get => (DateTime)GetValue(DayStartProperty); set => SetValue(DayStartProperty, value); }
    public DateTime DayEnd { get => (DateTime)GetValue(DayEndProperty); set => SetValue(DayEndProperty, value); }
    public DateTime Now { get => (DateTime)GetValue(NowProperty); set => SetValue(NowProperty, value); }
    public bool ShowNow { get => (bool)GetValue(ShowNowProperty); set => SetValue(ShowNowProperty, value); }
    /// <summary>0–1 through the current thing; 0 hides the progress arc.</summary>
    public double Progress { get => (double)GetValue(ProgressProperty); set => SetValue(ProgressProperty, value); }
    public Color Accent { get => (Color)GetValue(AccentProperty); set => SetValue(AccentProperty, value); }
    public bool DimDay { get => (bool)GetValue(DimDayProperty); set => SetValue(DimDayProperty, value); }
    public GuardianPresenceState State { get => (GuardianPresenceState)GetValue(StateProperty); set => SetValue(StateProperty, value); }
    /// <summary>Bind to a count of things Guardian has done; each increase sends one ripple out from the core.</summary>
    public int PulseCount { get => (int)GetValue(PulseCountProperty); set => SetValue(PulseCountProperty, value); }
    /// <summary>Each increase sets off a short burst of green ripples (a queue cleared, a breach gone).</summary>
    public int CelebrateCount { get => (int)GetValue(CelebrateCountProperty); set => SetValue(CelebrateCountProperty, value); }
    /// <summary>Executed with a HomeRingAction when a menu item is chosen on a ring segment.</summary>
    public System.Windows.Input.ICommand? RingCommand { get => (System.Windows.Input.ICommand?)GetValue(RingCommandProperty); set => SetValue(RingCommandProperty, value); }
    /// <summary>Executed with a HomeRingMove when a focus block is dragged along the ring.</summary>
    public System.Windows.Input.ICommand? MoveCommand { get => (System.Windows.Input.ICommand?)GetValue(MoveCommandProperty); set => SetValue(MoveCommandProperty, value); }
    /// <summary>Calm mode: just a slow breath. No orbits, ripples or flashes.</summary>
    public bool Calm { get => (bool)GetValue(CalmProperty); set => SetValue(CalmProperty, value); }
    /// <summary>Each increase plays the start-up animation: the day's segments draw themselves in.</summary>
    public int IntroCount { get => (int)GetValue(IntroCountProperty); set => SetValue(IntroCountProperty, value); }

    private const double Extent = 680, C = 340, DayRadius = 262, ProgressRadius = 222, CoreRadius = 190;
    private static readonly Color Purple = Color.FromRgb(0x7B, 0x5D, 0xFF);
    private static readonly Color Cyan = Color.FromRgb(0x42, 0xD9, 0xFF);
    private static readonly Color Own = Color.FromRgb(0x3A, 0x45, 0x60);
    private static readonly Color Track = Color.FromRgb(0x1C, 0x18, 0x38);
    private static readonly Color Label = Color.FromRgb(0x6F, 0x7F, 0x99);
    private Ellipse? _glow;
    // The animated layer lives across redraws so its animations keep running.
    private readonly Canvas _fx = new() { Width = 680, Height = 680, IsHitTestVisible = false };
    private Canvas? _orbitA, _orbitB;
    private Ellipse? _halo;
    private readonly System.Windows.Threading.DispatcherTimer _ripples = new() { Interval = TimeSpan.FromMilliseconds(900) };

    public GuardianCore()
    {
        Width = Extent;
        Height = Extent;
        BuildFx();
        _ripples.Tick += (_, _) => Ripple(0.55, 1.0);
        Loaded += (_, _) => Draw();
        // The ring works out which segment is under the pointer from its angle and radius, rather than relying on
        // hitting the thin arc shapes themselves (which never received the mouse inside the Viewbox).
        Background = Brushes.Transparent;
        MouseMove += OnRingMouseMove;
        MouseLeftButtonDown += OnRingMouseDown;
        MouseLeftButtonUp += OnRingMouseUp;
        MouseLeave += (_, _) => { if (_dragArc is null) Highlight(null); };
        Unloaded += (_, _) => _ripples.Stop();
    }

    private static void OnItemsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var core = (GuardianCore)d;
        if (e.OldValue is INotifyCollectionChanged oldList) oldList.CollectionChanged -= core.OnCollectionChanged;
        if (e.NewValue is INotifyCollectionChanged newList) newList.CollectionChanged += core.OnCollectionChanged;
        core.Draw();
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => Dispatcher.BeginInvoke(Draw);
    private static void Redraw(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((GuardianCore)d).Draw();
    private static void OnStateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((GuardianCore)d).Breathe();

    private static void OnIntroCountChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is int now && e.OldValue is int before && now > before) ((GuardianCore)d).PlayIntro();
    }

    // Start-up: the core swells in, then the day's segments appear one after another round the ring.
    private void PlayIntro()
    {
        if (Calm) return;
        var segments = Children.OfType<Path>().Where(p => p.Tag is HomeTimelineItem).ToList();
        var i = 0;
        foreach (var path in segments)
        {
            var target = path.Opacity;
            path.Opacity = 0;
            path.BeginAnimation(OpacityProperty, new DoubleAnimation(0, target, TimeSpan.FromMilliseconds(320)) { BeginTime = TimeSpan.FromMilliseconds(500 + 110 * i++) });
        }
        Ripple(1.0, 1.8);
        var later = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500 + 110 * i + 200) };
        later.Tick += (_, _) => { later.Stop(); Ripple(0.6, 1.4); };
        later.Start();
    }

    private static void OnCelebrateCountChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is not int now || e.OldValue is not int before || now <= before) return;
        var core = (GuardianCore)d;
        var green = Color.FromRgb(0x46, 0xF0, 0xB4);
        for (var i = 0; i < 3; i++)
        {
            var delay = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(260 * i + 1) };
            delay.Tick += (_, _) => { delay.Stop(); core.Ripple(1.0, 1.5, green); };
            delay.Start();
        }
    }

    private static void OnPulseCountChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is int now && e.OldValue is int before && now > before) ((GuardianCore)d).Ripple(0.9, 1.6);
    }

    private double Angle(DateTime t)
    {
        var span = Math.Max(1, (DayEnd - DayStart).TotalMinutes);
        return -75 + Math.Clamp((t - DayStart).TotalMinutes / span, 0, 1) * 330;
    }

    private static Point Polar(double r, double degrees)
    {
        var a = degrees * Math.PI / 180;
        return new Point(C + r * Math.Cos(a), C + r * Math.Sin(a));
    }

    private static Path Arc(double r, double from, double to, Color color, double thickness, double opacity = 1, bool round = false)
    {
        if (to - from >= 359.9) to = from + 359.9;
        var figure = new PathFigure { StartPoint = Polar(r, from), IsClosed = false };
        figure.Segments.Add(new ArcSegment(Polar(r, to), new Size(r, r), 0, to - from > 180, SweepDirection.Clockwise, true));
        return new Path
        {
            Data = new PathGeometry(new[] { figure }), Stroke = new SolidColorBrush(color), StrokeThickness = thickness, Opacity = opacity,
            StrokeStartLineCap = round ? PenLineCap.Round : PenLineCap.Flat, StrokeEndLineCap = round ? PenLineCap.Round : PenLineCap.Flat
        };
    }

    private void Draw()
    {
        Children.Clear();
        _hoverArc = null;
        var accent = Accent;

        // Breathing glow behind the core.
        _glow = new Ellipse
        {
            Width = CoreRadius * 2 + 40, Height = CoreRadius * 2 + 40,
            Fill = new RadialGradientBrush(Color.FromArgb(0x40, accent.R, accent.G, accent.B), Color.FromArgb(0, accent.R, accent.G, accent.B)),
            Opacity = 0.7
        };
        Place(_glow, C - CoreRadius - 20, C - CoreRadius - 20);
        Place(new Ellipse { Width = 600, Height = 600, Stroke = new SolidColorBrush(Color.FromArgb(0x1A, 0x9C, 0x85, 0xFF)), StrokeThickness = 1 }, C - 300, C - 300);
        Place(new Ellipse
        {
            Width = CoreRadius * 2, Height = CoreRadius * 2,
            Fill = new SolidColorBrush(Color.FromArgb(0x12, accent.R, accent.G, accent.B)),
            Stroke = new SolidColorBrush(Color.FromArgb(0x40, accent.R, accent.G, accent.B)), StrokeThickness = 1
        }, C - CoreRadius, C - CoreRadius);

        // The day.
        var dayOpacity = DimDay ? 0.45 : 1;
        Children.Add(Arc(DayRadius, -75, 255, Track, 16, dayOpacity));
        foreach (var item in (Items ?? Array.Empty<object>()).OfType<HomeTimelineItem>())
        {
            if (item.End <= DayStart || item.Start >= DayEnd) continue;
            var a0 = Angle(item.Start) + 0.6;
            var a1 = Math.Max(Angle(item.End) - 0.6, a0 + 1.2);
            var color = item.Kind switch { "focus" => Purple, "meeting" => Cyan, _ => Own };
            var past = ShowNow && item.End <= Now;
            var current = ShowNow && item.Start <= Now && item.End > Now;
            if (current && item.Kind == "focus") color = Color.FromRgb(0x9C, 0x85, 0xFF);
            var arc = Arc(DayRadius, a0, a1, color, 16, dayOpacity * (past ? 0.4 : 1));
            arc.ToolTip = RingCommand is null ? $"{item.TimeLabel}  {item.Title}" : null;
            arc.Tag = item;
            Children.Add(arc);
        }
        for (var h = DayStart.Hour + (DayStart.Minute > 0 ? 1 : 0); h <= DayEnd.Hour; h++)
        {
            var at = DayStart.Date.AddHours(h);
            if (at < DayStart || at > DayEnd) continue;
            var a = Angle(at);
            var p0 = Polar(DayRadius + 14, a);
            var p1 = Polar(DayRadius + 22, a);
            Children.Add(new Line { X1 = p0.X, Y1 = p0.Y, X2 = p1.X, Y2 = p1.Y, Stroke = new SolidColorBrush(Color.FromRgb(0x4A, 0x58, 0x78)), StrokeThickness = 1.5 });
            var text = new TextBlock { Text = h.ToString("00"), FontSize = 13, Foreground = new SolidColorBrush(Label), FontFamily = new FontFamily("Cascadia Mono, Consolas") };
            var tp = Polar(DayRadius + 40, a);
            Place(text, tp.X - 9, tp.Y - 9);
        }
        if (ShowNow && Now >= DayStart && Now <= DayEnd)
        {
            var a = Angle(Now);
            var p0 = Polar(DayRadius - 20, a);
            var p1 = Polar(DayRadius + 20, a);
            Children.Add(new Line
            {
                X1 = p0.X, Y1 = p0.Y, X2 = p1.X, Y2 = p1.Y, Stroke = new SolidColorBrush(Cyan), StrokeThickness = 3,
                StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
                Effect = new DropShadowEffect { Color = Cyan, BlurRadius = 10, ShadowDepth = 0, Opacity = 0.9 }
            });
            Place(new Ellipse { Width = 10, Height = 10, Fill = new SolidColorBrush(Cyan) }, p1.X - 5, p1.Y - 5);
        }

        // What is happening now.
        Place(new Ellipse { Width = ProgressRadius * 2, Height = ProgressRadius * 2, Stroke = new SolidColorBrush(Color.FromArgb(0x55, accent.R, accent.G, accent.B)), StrokeThickness = 6, Opacity = 0.45 },
            C - ProgressRadius, C - ProgressRadius);
        for (var i = 0; i < 12; i++)
        {
            var p0 = Polar(ProgressRadius - 14, -90 + i * 30);
            var p1 = Polar(ProgressRadius - 24, -90 + i * 30);
            Children.Add(new Line { X1 = p0.X, Y1 = p0.Y, X2 = p1.X, Y2 = p1.Y, Stroke = new SolidColorBrush(Color.FromRgb(0x2E, 0x2A, 0x55)), StrokeThickness = 1.2 });
        }
        if (Progress > 0.002)
            Children.Add(Arc(ProgressRadius, -90, -90 + 360 * Math.Clamp(Progress, 0, 1), accent, 6, 1, round: true));

        if (_halo is not null) _halo.Stroke = new SolidColorBrush(Color.FromArgb(0x90, accent.R, accent.G, accent.B));
        // Orbits are cyan ("alive"), except in battle mode where everything goes the alert colour.
        var orbit = accent.R > 0xE0 && accent.G < 0x80 ? accent : Cyan;
        foreach (var layer in new[] { _orbitA, _orbitB })
            if (layer is not null)
                foreach (var path in layer.Children.OfType<Path>())
                {
                    var faint = path.Stroke is SolidColorBrush { Color.A: < 0xFF };
                    path.Stroke = new SolidColorBrush(faint ? Color.FromArgb(0x55, orbit.R, orbit.G, orbit.B) : orbit);
                    if (path.Effect is DropShadowEffect glow) glow.Color = orbit;
                }
        Children.Add(_fx);
        Breathe();
    }

    // Personality. Idle: a slow breath and one faint arc drifting round the core, like something keeping watch.
    // Thinking: two bright arcs spin in opposite directions. Listening: ripples flow out of the core as if it is
    // taking the room in. Speaking: the core's rim pulses quickly. Acting (did something on its own): a bright flash.
    // Every time Guardian does something for you (PulseCount goes up) one ripple goes out.
    private void Breathe()
    {
        if (_glow is null) return;
        var state = Calm ? GuardianPresenceState.Idle : State;
        var (low, high, seconds) = state switch
        {
            GuardianPresenceState.Listening => (0.6, 1.0, 0.8),
            GuardianPresenceState.Thinking => (0.5, 1.0, 1.1),
            GuardianPresenceState.Speaking => (0.55, 1.0, 0.35),
            GuardianPresenceState.Acting => (0.7, 1.0, 0.5),
            _ => (0.35, 0.7, 3.2)
        };
        _glow.BeginAnimation(OpacityProperty, new DoubleAnimation(low, high, TimeSpan.FromSeconds(seconds))
        {
            AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        });

        var thinking = state is GuardianPresenceState.Thinking or GuardianPresenceState.Acting;
        Spin(_orbitA, thinking ? 1.6 : state == GuardianPresenceState.Speaking ? 6 : 26, clockwise: true);
        Spin(_orbitB, thinking ? 2.4 : 40, clockwise: false);
        Fade(_orbitA, Calm ? 0 : thinking ? 1 : state == GuardianPresenceState.Idle ? 0.35 : 0.6);
        Fade(_orbitB, thinking ? 0.9 : 0);

        if (_halo is not null)
        {
            if (state == GuardianPresenceState.Speaking)
                _halo.BeginAnimation(Shape.StrokeThicknessProperty, new DoubleAnimation(1, 5, TimeSpan.FromSeconds(0.18))
                    { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
            else
                _halo.BeginAnimation(Shape.StrokeThicknessProperty, null);
        }

        if (state == GuardianPresenceState.Listening) { if (!_ripples.IsEnabled) { Ripple(0.55, 1.0); _ripples.Start(); } }
        else _ripples.Stop();
        if (state == GuardianPresenceState.Acting) Ripple(1.0, 1.4);
    }

    private void BuildFx()
    {
        _orbitA = OrbitArc(205, 0, 38, 3);
        _orbitB = OrbitArc(198, 180, 26, 2);
        _halo = new Ellipse { Width = CoreRadius * 2, Height = CoreRadius * 2, StrokeThickness = 1, Opacity = 0.8 };
        SetLeft(_halo, C - CoreRadius);
        SetTop(_halo, C - CoreRadius);
        _fx.Children.Add(_orbitA);
        _fx.Children.Add(_orbitB);
        _fx.Children.Add(_halo);
    }

    private Canvas OrbitArc(double r, double start, double sweep, double thickness)
    {
        var layer = new Canvas { Width = 680, Height = 680, RenderTransform = new RotateTransform(0, C, C), Opacity = 0 };
        var arc = Arc(r, start, start + sweep, Color.FromRgb(0x42, 0xD9, 0xFF), thickness, 1, round: true);
        arc.Effect = new DropShadowEffect { Color = Color.FromRgb(0x42, 0xD9, 0xFF), BlurRadius = 12, ShadowDepth = 0, Opacity = 0.9 };
        layer.Children.Add(arc);
        // A trailing, fainter tail makes it read as motion rather than a blip.
        layer.Children.Add(Arc(r, start - sweep * 0.9, start, Color.FromArgb(0x55, 0x42, 0xD9, 0xFF), thickness * 0.7, 1, round: true));
        return layer;
    }

    private static void Spin(Canvas? layer, double seconds, bool clockwise)
    {
        if (layer?.RenderTransform is not RotateTransform rotate) return;
        var from = rotate.Angle % 360;
        rotate.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(from, from + (clockwise ? 360 : -360), TimeSpan.FromSeconds(seconds))
            { RepeatBehavior = RepeatBehavior.Forever });
    }

    private static void Fade(UIElement? element, double to) =>
        element?.BeginAnimation(OpacityProperty, new DoubleAnimation(to, TimeSpan.FromMilliseconds(400)));

    // ---- Clicking and dragging ring segments ----

    private Path? _dragArc;
    private HomeTimelineItem? _dragItem;
    private double _dragStartAngle;
    private bool _dragging;

    private Path? _hoverArc;

    private (Path Arc, HomeTimelineItem Item)? SegmentAt(Point p)
    {
        var r = Math.Sqrt((p.X - C) * (p.X - C) + (p.Y - C) * (p.Y - C));
        if (Math.Abs(r - DayRadius) > 18) return null;
        var degrees = MouseAngle(p);
        while (degrees < -75) degrees += 360;
        if (degrees > 255) return null;
        var t = DayStart.AddMinutes((degrees + 75) / 330 * Math.Max(1, (DayEnd - DayStart).TotalMinutes));
        foreach (var arc in Children.OfType<Path>())
            if (arc.Tag is HomeTimelineItem item && item.Start <= t && item.End > t) return (arc, item);
        return null;
    }

    private void Highlight(Path? arc)
    {
        if (ReferenceEquals(_hoverArc, arc)) return;
        if (_hoverArc is not null) _hoverArc.StrokeThickness = 16;
        _hoverArc = arc;
        if (arc is not null) arc.StrokeThickness = 22;
    }

    private void OnRingMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        var position = e.GetPosition(this);
        if (_dragArc is { } dragged && _dragItem is { } moving && IsMouseCaptured)
        {
            if (moving.Kind != "focus" || MoveCommand is null) return;
            var delta = AngleDelta(MouseAngle(position), _dragStartAngle);
            if (!_dragging && Math.Abs(delta) < 3) return;
            _dragging = true;
            dragged.RenderTransform = new RotateTransform(delta, C, C);
            dragged.Opacity = 0.8;
            ToolTip = $"Move to {SnappedStart(moving, delta):HH:mm}";
            return;
        }
        if (RingCommand is null) return;
        var hit = SegmentAt(position);
        Highlight(hit?.Arc);
        Cursor = hit is null ? null
            : hit.Value.Item.Kind == "focus" && MoveCommand is not null ? System.Windows.Input.Cursors.SizeAll : System.Windows.Input.Cursors.Hand;
        var tip = hit is null ? null
            : $"{hit.Value.Item.TimeLabel}  {hit.Value.Item.Title}" + (hit.Value.Item.Kind == "focus" ? "\nClick for options · drag to move" : "\nClick for options");
        if (!Equals(ToolTip, tip)) ToolTip = tip;
    }

    private void OnRingMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (RingCommand is null || SegmentAt(e.GetPosition(this)) is not { } hit) return;
        _dragArc = hit.Arc;
        _dragItem = hit.Item;
        _dragStartAngle = MouseAngle(e.GetPosition(this));
        _dragging = false;
        CaptureMouse();
        e.Handled = true;
    }

    private void OnRingMouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_dragArc is not { } arc || _dragItem is not { } item) return;
        ReleaseMouseCapture();
        var delta = AngleDelta(MouseAngle(e.GetPosition(this)), _dragStartAngle);
        var wasDragging = _dragging;
        _dragArc = null;
        _dragItem = null;
        _dragging = false;
        e.Handled = true;
        if (wasDragging && MoveCommand is not null)
        {
            var start = SnappedStart(item, delta);
            if (start != item.Start) MoveCommand.Execute(new HomeRingMove(item, start));
            else Draw();
            return;
        }
        OpenMenu(arc, item);
    }

    private DateTime SnappedStart(HomeTimelineItem item, double deltaDegrees)
    {
        var minutesPerDegree = (DayEnd - DayStart).TotalMinutes / 330;
        var shifted = item.Start.AddMinutes(deltaDegrees * minutesPerDegree);
        var snapped = new DateTime(shifted.Year, shifted.Month, shifted.Day, shifted.Hour, shifted.Minute / 5 * 5, 0);
        var length = item.End - item.Start;
        if (snapped < DayStart) snapped = DayStart;
        if (snapped + length > DayEnd) snapped = DayEnd - length;
        return snapped;
    }

    private static double MouseAngle(Point p) => Math.Atan2(p.Y - C, p.X - C) * 180 / Math.PI;

    private static double AngleDelta(double now, double start)
    {
        var d = now - start;
        while (d > 180) d -= 360;
        while (d < -180) d += 360;
        return d;
    }

    private void OpenMenu(FrameworkElement target, HomeTimelineItem item)
    {
        if (RingCommand is null) return;
        var menu = new ContextMenu { PlacementTarget = target };
        void Add(string header, string action) =>
            menu.Items.Add(new MenuItem { Header = header, Command = RingCommand, CommandParameter = new HomeRingAction(item, action) });
        menu.Items.Add(new MenuItem { Header = $"{item.TimeLabel}  {item.Title}", IsEnabled = false });
        // Starting or pushing only makes sense for today's blocks, not tomorrow's (shown in the evening).
        if (item.Kind == "focus" && item.Start.Date == DateTime.Today) { Add("Start this now", "start"); Add("Push it later today", "push"); }
        if (item.Kind == "meeting") Add("Brief me on this", "brief");
        Add("Open in Outlook", "open");
        menu.IsOpen = true;
    }

    private void Ripple(double strength, double seconds, Color? color = null)
    {
        if (Calm) return;
        var accent = color ?? Accent;
        var ring = new Ellipse
        {
            Width = CoreRadius * 2, Height = CoreRadius * 2, StrokeThickness = 2,
            Stroke = new SolidColorBrush(Color.FromArgb((byte)(255 * strength), accent.R, accent.G, accent.B)),
            RenderTransform = new ScaleTransform(1, 1, CoreRadius, CoreRadius)
        };
        SetLeft(ring, C - CoreRadius);
        SetTop(ring, C - CoreRadius);
        _fx.Children.Add(ring);
        var duration = TimeSpan.FromSeconds(seconds);
        var grow = new DoubleAnimation(1, 1.42, duration) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
        var fade = new DoubleAnimation(1, 0, duration);
        fade.Completed += (_, _) => _fx.Children.Remove(ring);
        ((ScaleTransform)ring.RenderTransform).BeginAnimation(ScaleTransform.ScaleXProperty, grow);
        ((ScaleTransform)ring.RenderTransform).BeginAnimation(ScaleTransform.ScaleYProperty, grow);
        ring.BeginAnimation(OpacityProperty, fade);
    }

    private void Place(UIElement element, double x, double y)
    {
        SetLeft(element, x);
        SetTop(element, y);
        Children.Add(element);
    }
}
