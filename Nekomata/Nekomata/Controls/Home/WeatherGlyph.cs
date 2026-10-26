using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using Nekomata.Core.Weather;

namespace Nekomata.UI.Controls.Home;

// An animated weather symbol for the Home top line: the sun turns and glows, clouds drift, rain and snow fall, lightning
// flashes, fog rolls, and at night a moon and twinkling stars replace the sun. Drawn on a 100-unit canvas inside a
// Viewbox so it is sharp at any size. With Calm on it is drawn still.
public sealed class WeatherGlyph : Viewbox
{
    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(
        nameof(Kind), typeof(WeatherKind), typeof(WeatherGlyph), new PropertyMetadata(WeatherKind.Clear, Rebuild));
    public static readonly DependencyProperty IsDayProperty = DependencyProperty.Register(
        nameof(IsDay), typeof(bool), typeof(WeatherGlyph), new PropertyMetadata(true, Rebuild));
    public static readonly DependencyProperty CalmProperty = DependencyProperty.Register(
        nameof(Calm), typeof(bool), typeof(WeatherGlyph), new PropertyMetadata(false, Rebuild));

    public WeatherKind Kind { get => (WeatherKind)GetValue(KindProperty); set => SetValue(KindProperty, value); }
    public bool IsDay { get => (bool)GetValue(IsDayProperty); set => SetValue(IsDayProperty, value); }
    public bool Calm { get => (bool)GetValue(CalmProperty); set => SetValue(CalmProperty, value); }

    private static readonly Color Sun = Color.FromRgb(0xFF, 0xC8, 0x57);
    private static readonly Color Moon = Color.FromRgb(0xC9, 0xBB, 0xFF);
    private static readonly Color CloudLight = Color.FromRgb(0xC9, 0xD1, 0xE6);
    private static readonly Color CloudDark = Color.FromRgb(0x7D, 0x87, 0xA3);
    private static readonly Color RainBlue = Color.FromRgb(0x42, 0xD9, 0xFF);

    private readonly Canvas _canvas = new() { Width = 100, Height = 100, ClipToBounds = false };
    private readonly Random _random = new(7);

    public WeatherGlyph()
    {
        Stretch = Stretch.Uniform;
        Child = _canvas;
        Loaded += (_, _) => Build();
    }

    private static void Rebuild(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((WeatherGlyph)d).Build();

    private void Build()
    {
        _canvas.Children.Clear();
        switch (Kind)
        {
            case WeatherKind.Clear:
                if (IsDay) AddSun(50, 50, 22); else AddMoon(50, 50, 24, stars: true);
                break;
            case WeatherKind.PartlyCloudy:
                if (IsDay) AddSun(36, 36, 16); else AddMoon(36, 34, 17, stars: true);
                AddCloud(56, 60, 1.0, CloudLight, drift: 5);
                break;
            case WeatherKind.Cloudy:
                AddCloud(40, 46, 0.75, CloudDark, drift: 4, delay: 1.5);
                AddCloud(56, 58, 1.0, CloudLight, drift: 6);
                break;
            case WeatherKind.Fog:
                AddCloud(50, 40, 0.9, CloudDark, drift: 3);
                for (var i = 0; i < 3; i++) AddFogBand(18 + i * 6, 62 + i * 10, 64 - i * 8, i * 0.7);
                break;
            case WeatherKind.Drizzle:
                AddCloud(50, 42, 1.0, CloudLight, drift: 4);
                AddDrops(6, 0.9, 10, 1.4);
                break;
            case WeatherKind.Showers:
                if (IsDay) AddSun(30, 30, 14); else AddMoon(30, 30, 15, stars: false);
                AddCloud(54, 46, 1.0, CloudLight, drift: 5);
                AddDrops(7, 0.75, 16, 2.0);
                break;
            case WeatherKind.Rain:
                AddCloud(50, 42, 1.05, CloudDark, drift: 4);
                AddDrops(10, 0.6, 18, 2.2);
                break;
            case WeatherKind.Snow:
                AddCloud(50, 40, 1.0, CloudLight, drift: 4);
                AddFlakes(9);
                break;
            case WeatherKind.Thunder:
                AddCloud(50, 40, 1.08, CloudDark, drift: 3);
                AddDrops(7, 0.55, 18, 2.2);
                AddBolt();
                break;
        }
    }

    private void Animate(UIElement target, DependencyProperty property, Timeline animation)
    {
        if (Calm) return;
        target.BeginAnimation(property, (AnimationTimeline)animation);
    }

    private void AddSun(double cx, double cy, double r)
    {
        var glow = new Ellipse { Width = r * 3.2, Height = r * 3.2, Fill = new RadialGradientBrush(Color.FromArgb(0x70, Sun.R, Sun.G, Sun.B), Color.FromArgb(0, Sun.R, Sun.G, Sun.B)) };
        Place(glow, cx - r * 1.6, cy - r * 1.6);
        _canvas.Children.Add(glow);
        Animate(glow, OpacityProperty, new DoubleAnimation(0.55, 1, TimeSpan.FromSeconds(2.6)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase() });

        var rays = new Canvas { Width = 100, Height = 100 };
        for (var i = 0; i < 8; i++)
        {
            var angle = i * Math.PI / 4;
            rays.Children.Add(new Line
            {
                X1 = cx + Math.Cos(angle) * r * 1.3, Y1 = cy + Math.Sin(angle) * r * 1.3,
                X2 = cx + Math.Cos(angle) * r * 1.75, Y2 = cy + Math.Sin(angle) * r * 1.75,
                Stroke = new SolidColorBrush(Sun), StrokeThickness = Math.Max(2, r / 7), StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round
            });
        }
        var spin = new RotateTransform(0, cx, cy);
        rays.RenderTransform = spin;
        _canvas.Children.Add(rays);
        if (!Calm) spin.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 360, TimeSpan.FromSeconds(40)) { RepeatBehavior = RepeatBehavior.Forever });

        var disc = new Ellipse
        {
            Width = r * 2, Height = r * 2,
            Fill = new RadialGradientBrush(Color.FromRgb(0xFF, 0xE3, 0x9A), Sun) { GradientOrigin = new Point(0.35, 0.35) },
            Effect = new DropShadowEffect { Color = Sun, BlurRadius = 16, ShadowDepth = 0, Opacity = 0.8 }
        };
        Place(disc, cx - r, cy - r);
        _canvas.Children.Add(disc);
    }

    private void AddMoon(double cx, double cy, double r, bool stars)
    {
        if (stars)
        {
            foreach (var (x, y, s, delay) in new[] { (16.0, 20.0, 2.4, 0.0), (82.0, 18.0, 2.0, 0.8), (78.0, 70.0, 1.8, 1.6), (22.0, 76.0, 1.6, 2.2), (88.0, 44.0, 1.4, 1.1) })
            {
                var star = new Ellipse { Width = s * 2, Height = s * 2, Fill = Brushes.White };
                Place(star, x - s, y - s);
                _canvas.Children.Add(star);
                Animate(star, OpacityProperty, new DoubleAnimation(0.15, 1, TimeSpan.FromSeconds(1.4)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, BeginTime = TimeSpan.FromSeconds(delay) });
            }
        }
        var crescent = new Path
        {
            Data = new CombinedGeometry(GeometryCombineMode.Exclude,
                new EllipseGeometry(new Point(cx, cy), r, r), new EllipseGeometry(new Point(cx + r * 0.55, cy - r * 0.35), r * 0.85, r * 0.85)),
            Fill = new SolidColorBrush(Moon),
            Effect = new DropShadowEffect { Color = Moon, BlurRadius = 14, ShadowDepth = 0, Opacity = 0.7 }
        };
        _canvas.Children.Add(crescent);
        Animate(crescent, OpacityProperty, new DoubleAnimation(0.8, 1, TimeSpan.FromSeconds(3)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
    }

    private void AddCloud(double cx, double cy, double scale, Color colour, double drift, double delay = 0)
    {
        var shape = new GeometryGroup { FillRule = FillRule.Nonzero };
        shape.Children.Add(new EllipseGeometry(new Point(-14, 4), 14, 11));
        shape.Children.Add(new EllipseGeometry(new Point(2, -6), 17, 16));
        shape.Children.Add(new EllipseGeometry(new Point(17, 4), 13, 11));
        shape.Children.Add(new RectangleGeometry(new Rect(-14, 2, 31, 13), 6, 6));
        var cloud = new Path
        {
            Data = shape,
            Fill = new LinearGradientBrush(Colors.White, colour, 90) { Opacity = 0.95 },
            Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 8, ShadowDepth = 2, Opacity = 0.35 }
        };
        var move = new TranslateTransform(cx, cy);
        cloud.RenderTransform = new TransformGroup { Children = { new ScaleTransform(scale, scale), move } };
        _canvas.Children.Add(cloud);
        if (!Calm)
            move.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(cx - drift, cx + drift, TimeSpan.FromSeconds(5 + drift / 2))
            { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }, BeginTime = TimeSpan.FromSeconds(delay) });
    }

    private void AddDrops(int count, double seconds, double length, double thickness)
    {
        for (var i = 0; i < count; i++)
        {
            var x = 28 + i * (46.0 / Math.Max(1, count - 1)) + _random.NextDouble() * 3;
            var drop = new Line
            {
                X1 = 0, Y1 = 0, X2 = -length * 0.25, Y2 = length * 0.6,
                Stroke = new SolidColorBrush(RainBlue), StrokeThickness = thickness, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
                Opacity = Calm ? 0.8 : 0
            };
            var fall = new TranslateTransform(x, 58 + (i % 3) * 6);
            drop.RenderTransform = fall;
            _canvas.Children.Add(drop);
            if (Calm) continue;
            var begin = TimeSpan.FromSeconds(_random.NextDouble() * seconds);
            fall.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(56, 92, TimeSpan.FromSeconds(seconds)) { RepeatBehavior = RepeatBehavior.Forever, BeginTime = begin });
            fall.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(x, x - 6, TimeSpan.FromSeconds(seconds)) { RepeatBehavior = RepeatBehavior.Forever, BeginTime = begin });
            var fade = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(seconds), RepeatBehavior = RepeatBehavior.Forever, BeginTime = begin };
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(0)));
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(0.95, KeyTime.FromPercent(0.2)));
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(0.9, KeyTime.FromPercent(0.75)));
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(1)));
            drop.BeginAnimation(OpacityProperty, fade);
        }
    }

    private void AddFlakes(int count)
    {
        for (var i = 0; i < count; i++)
        {
            var size = 3 + _random.NextDouble() * 2.5;
            var flake = new Ellipse { Width = size, Height = size, Fill = Brushes.White, Opacity = Calm ? 0.9 : 0 };
            var x = 26 + i * (48.0 / Math.Max(1, count - 1));
            var move = new TranslateTransform(x, 60 + (i % 3) * 9);
            flake.RenderTransform = move;
            _canvas.Children.Add(flake);
            if (Calm) continue;
            var seconds = 2.6 + _random.NextDouble() * 1.4;
            var begin = TimeSpan.FromSeconds(_random.NextDouble() * seconds);
            move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(56, 94, TimeSpan.FromSeconds(seconds)) { RepeatBehavior = RepeatBehavior.Forever, BeginTime = begin });
            move.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(x - 3, x + 3, TimeSpan.FromSeconds(seconds / 3))
            { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase(), BeginTime = begin });
            var fade = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(seconds), RepeatBehavior = RepeatBehavior.Forever, BeginTime = begin };
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(0)));
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromPercent(0.2)));
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(1)));
            flake.BeginAnimation(OpacityProperty, fade);
        }
    }

    private void AddFogBand(double x, double y, double width, double delay)
    {
        var band = new Rectangle { Width = width, Height = 4, RadiusX = 2, RadiusY = 2, Fill = new SolidColorBrush(CloudLight), Opacity = 0.75 };
        var move = new TranslateTransform(x, y);
        band.RenderTransform = move;
        _canvas.Children.Add(band);
        if (!Calm)
            move.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(x - 6, x + 6, TimeSpan.FromSeconds(4))
            { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }, BeginTime = TimeSpan.FromSeconds(delay) });
    }

    private void AddBolt()
    {
        var bolt = new Polygon
        {
            Points = new PointCollection { new(52, 52), new(42, 72), new(50, 72), new(44, 90), new(62, 64), new(53, 64), new(58, 52) },
            Fill = new SolidColorBrush(Sun),
            Effect = new DropShadowEffect { Color = Sun, BlurRadius = 18, ShadowDepth = 0, Opacity = 0.9 },
            Opacity = Calm ? 1 : 0
        };
        _canvas.Children.Add(bolt);
        if (Calm) return;
        var flash = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(4.5), RepeatBehavior = RepeatBehavior.Forever };
        flash.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromPercent(0)));
        flash.KeyFrames.Add(new DiscreteDoubleKeyFrame(1, KeyTime.FromPercent(0.62)));
        flash.KeyFrames.Add(new DiscreteDoubleKeyFrame(0.2, KeyTime.FromPercent(0.65)));
        flash.KeyFrames.Add(new DiscreteDoubleKeyFrame(1, KeyTime.FromPercent(0.68)));
        flash.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(0.8)));
        bolt.BeginAnimation(OpacityProperty, flash);
    }

    private static void Place(UIElement element, double x, double y)
    {
        Canvas.SetLeft(element, x);
        Canvas.SetTop(element, y);
    }
}
