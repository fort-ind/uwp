using System;
using System.Collections.Generic;
using System.Globalization;
using Windows.Foundation;
using Windows.UI;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Animation;
using Windows.UI.Xaml.Shapes;

namespace Fort.ind_UWP
{
    public static class MfmEffects
    {
        public const double TadaScale = 1.5;

        private const double MaximumScale = 5;

        private const double MaximumPositionEm = 5;

        private const double MaximumBorderWidth = 8;

        private const double MaximumBorderRadius = 32;

        private const double BoxPadding = 2;

        private const double DescentEm = 0.25;

        private const int MinimumDurationMilliseconds = 100;

        private const int MaximumDurationMilliseconds = 20000;

        private static readonly HashSet<string> s_animated = new HashSet<string>(StringComparer.Ordinal)
        {
            "tada", "jelly", "jump", "bounce", "spin", "shake", "twitch", "rainbow", "sparkle"
        };

        private static readonly HashSet<string> s_boxed = new HashSet<string>(StringComparer.Ordinal)
        {
            "flip", "rotate", "scale", "position", "border"
        };

        private static readonly Point[] s_sparkles = { new Point(0.1, 0.2), new Point(0.85, 0.15), new Point(0.5, 0.85), new Point(0.9, 0.75) };

        private static readonly Color s_sparkleColor = Color.FromArgb(0xFF, 0xFF, 0xD5, 0x4F);

        [ThreadStatic]
        private static UISettings t_uiSettings;

        public static bool AnimationsEnabled
        {
            get
            {
                try
                {
                    if (t_uiSettings == null) t_uiSettings = new UISettings();
                    return t_uiSettings.AnimationsEnabled;
                }
                catch (Exception ex)
                {
                    AppLog.Error("MfmEffects: reading the animation setting failed", ex);
                    return false;
                }
            }
        }

        public static bool IsAnimated(string name)
        {
            return name != null && s_animated.Contains(name);
        }

        public static bool IsBoxed(string name)
        {
            return name != null && s_boxed.Contains(name);
        }

        public static FrameworkElement Box(MfmSegment function, FrameworkElement content, double fontSize, bool animate)
        {
            var host = new Grid
            {
                Padding = new Thickness(BoxPadding),
                Margin = new Thickness(0, 0, 0, -(Math.Round(fontSize * DescentEm) + BoxPadding))
            };

            FrameworkElement body = content;
            if (function.FunctionName == "border") body = BorderAround(function, content);
            host.Children.Add(body);

            if (animate && IsAnimated(function.FunctionName)) Animate(function, host, body, content, fontSize);
            else ApplyStatic(function, body, fontSize);

            return host;
        }

        public static FrameworkElement Ruby(string baseText, string reading, double fontSize, RichTextBlock owner)
        {
            var panel = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Margin = new Thickness(0, 0, 0, -Math.Round(fontSize * DescentEm))
            };

            var top = new TextBlock { Text = reading, FontSize = fontSize * 0.5, HorizontalAlignment = HorizontalAlignment.Center, TextWrapping = TextWrapping.NoWrap };
            var bottom = new TextBlock { Text = baseText, FontSize = fontSize, HorizontalAlignment = HorizontalAlignment.Center, TextWrapping = TextWrapping.NoWrap };
            foreach (var text in new[] { top, bottom })
            {
                text.FontFamily = owner.FontFamily;
                text.SetBinding(TextBlock.ForegroundProperty, new Windows.UI.Xaml.Data.Binding { Source = owner, Path = new PropertyPath("Foreground") });
                panel.Children.Add(text);
            }

            return panel;
        }

        public static bool TryParseColor(string value, out Color color)
        {
            color = default(Color);
            if (string.IsNullOrWhiteSpace(value)) return false;

            var hex = value.Trim().TrimStart('#');
            if (hex.Length == 0) return false;
            foreach (var ch in hex)
            {
                if (!Uri.IsHexDigit(ch)) return false;
            }

            string expanded;
            switch (hex.Length)
            {
                case 3:
                case 4:
                    var builder = new System.Text.StringBuilder(hex.Length * 2);
                    foreach (var ch in hex) builder.Append(ch).Append(ch);
                    expanded = builder.ToString();
                    break;
                case 6:
                case 8:
                    expanded = hex;
                    break;
                default:
                    return false;
            }

            var r = Convert.ToByte(expanded.Substring(0, 2), 16);
            var g = Convert.ToByte(expanded.Substring(2, 2), 16);
            var b = Convert.ToByte(expanded.Substring(4, 2), 16);
            var a = expanded.Length == 8 ? Convert.ToByte(expanded.Substring(6, 2), 16) : (byte)0xFF;
            color = Color.FromArgb(a, r, g, b);
            return true;
        }

        private static double Number(MfmSegment function, string name, double fallback)
        {
            double value;
            var text = function.Argument(name);
            return text != null && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                   && !double.IsNaN(value) && !double.IsInfinity(value)
                   ? value
                   : fallback;
        }

        private static double Clamp(double value, double minimum, double maximum)
        {
            return Math.Max(minimum, Math.Min(maximum, value));
        }

        private static FrameworkElement BorderAround(MfmSegment function, FrameworkElement content)
        {
            Color color;
            var brush = TryParseColor(function.Argument("color"), out color)
                        ? new SolidColorBrush(color)
                        : Application.Current.Resources["SystemControlHighlightAccentBrush"] as Brush;

            return new Border
            {
                BorderThickness = new Thickness(Clamp(Number(function, "width", 1), 0, MaximumBorderWidth)),
                CornerRadius = new CornerRadius(Clamp(Number(function, "radius", 0), 0, MaximumBorderRadius)),
                BorderBrush = brush,
                Child = content
            };
        }

        private static void ApplyStatic(MfmSegment function, FrameworkElement body, double fontSize)
        {
            var transform = new CompositeTransform();
            switch (function.FunctionName)
            {
                case "flip":
                    var vertical = function.HasArgument("v");
                    var horizontal = function.HasArgument("h") || !vertical;
                    transform.ScaleX = horizontal ? -1 : 1;
                    transform.ScaleY = vertical ? -1 : 1;
                    break;
                case "rotate":
                    transform.Rotation = Number(function, "deg", 90);
                    break;
                case "scale":
                    transform.ScaleX = Clamp(Number(function, "x", 1), -MaximumScale, MaximumScale);
                    transform.ScaleY = Clamp(Number(function, "y", 1), -MaximumScale, MaximumScale);
                    break;
                case "position":
                    transform.TranslateX = Clamp(Number(function, "x", 0), -MaximumPositionEm, MaximumPositionEm) * fontSize;
                    transform.TranslateY = Clamp(Number(function, "y", 0), -MaximumPositionEm, MaximumPositionEm) * fontSize;
                    break;
                default:
                    return;
            }

            body.RenderTransformOrigin = new Point(0.5, 0.5);
            body.RenderTransform = transform;
        }

        private static int Milliseconds(string value, int fallback, int minimum)
        {
            if (string.IsNullOrWhiteSpace(value)) return fallback;

            var text = value.Trim();
            double number;
            double milliseconds;
            if (text.EndsWith("ms", StringComparison.OrdinalIgnoreCase)
                && double.TryParse(text.Substring(0, text.Length - 2), NumberStyles.Float, CultureInfo.InvariantCulture, out number))
            {
                milliseconds = number;
            }
            else if (text.EndsWith("s", StringComparison.OrdinalIgnoreCase)
                     && double.TryParse(text.Substring(0, text.Length - 1), NumberStyles.Float, CultureInfo.InvariantCulture, out number))
            {
                milliseconds = number * 1000;
            }
            else
            {
                return fallback;
            }

            if (double.IsNaN(milliseconds) || double.IsInfinity(milliseconds)) return fallback;
            return (int)Clamp(milliseconds, minimum, MaximumDurationMilliseconds);
        }

        private static int DefaultDuration(string name)
        {
            switch (name)
            {
                case "spin":
                    return 1500;
                case "jump":
                case "bounce":
                    return 750;
                case "shake":
                case "twitch":
                    return 500;
                default:
                    return 1000;
            }
        }

        private static void Animate(MfmSegment function, Grid host, FrameworkElement body, FrameworkElement content, double fontSize)
        {
            var name = function.FunctionName;
            var duration = Milliseconds(function.Argument("speed"), DefaultDuration(name), MinimumDurationMilliseconds);
            var storyboard = new Storyboard
            {
                Duration = new Duration(TimeSpan.FromMilliseconds(duration)),
                RepeatBehavior = RepeatBehavior.Forever,
                AutoReverse = function.HasArgument("alternate"),
                BeginTime = TimeSpan.FromMilliseconds(Milliseconds(function.Argument("delay"), 0, 0))
            };

            var transform = new CompositeTransform();
            body.RenderTransformOrigin = new Point(0.5, 0.5);
            body.RenderTransform = transform;

            switch (name)
            {
                case "spin":
                    var direction = function.HasArgument("left") ? -1.0 : 1.0;
                    if (function.HasArgument("x") || function.HasArgument("y"))
                    {
                        var projection = new PlaneProjection();
                        body.Projection = projection;
                        var axis = function.HasArgument("x") ? "RotationX" : "RotationY";
                        storyboard.Children.Add(Sampled(projection, axis, duration, 2, t => t * 360 * direction));
                    }
                    else
                    {
                        storyboard.Children.Add(Sampled(transform, "Rotation", duration, 2, t => t * 360 * direction));
                    }
                    break;
                case "jelly":
                    storyboard.Children.Add(Sampled(transform, "ScaleX", duration, 30, t => 1 + Jelly(t) * 0.25));
                    storyboard.Children.Add(Sampled(transform, "ScaleY", duration, 30, t => 1 - Jelly(t) * 0.25));
                    break;
                case "tada":
                    storyboard.Children.Add(Sampled(transform, "ScaleX", duration, 50, TadaPulse));
                    storyboard.Children.Add(Sampled(transform, "ScaleY", duration, 50, TadaPulse));
                    storyboard.Children.Add(Sampled(transform, "Rotation", duration, 50, t => t >= 0.2 && t <= 0.9 ? Math.Sin(t * 10 * Math.PI) * 3 : 0));
                    break;
                case "jump":
                    storyboard.Children.Add(Sampled(transform, "TranslateY", duration, 30, t => t < 0.5 ? -Math.Sin(t * 2 * Math.PI) * fontSize * 0.75 : 0));
                    break;
                case "bounce":
                    body.RenderTransformOrigin = new Point(0.5, 1);
                    storyboard.Children.Add(Sampled(transform, "TranslateY", duration, 30, t => -Math.Abs(Math.Sin(t * Math.PI)) * fontSize * 0.5));
                    storyboard.Children.Add(Sampled(transform, "ScaleY", duration, 30, t => t > 0.9 || t < 0.1 ? 0.8 : 1));
                    break;
                case "shake":
                    storyboard.Children.Add(Sampled(transform, "TranslateX", duration, 60, t => Math.Sin(t * 2 * Math.PI * 5) * fontSize * 0.08));
                    storyboard.Children.Add(Sampled(transform, "TranslateY", duration, 60, t => Math.Sin(t * 2 * Math.PI * 7) * fontSize * 0.06));
                    storyboard.Children.Add(Sampled(transform, "Rotation", duration, 60, t => Math.Sin(t * 2 * Math.PI * 3) * 4));
                    break;
                case "twitch":
                    storyboard.Children.Add(Sampled(transform, "TranslateX", duration, 120, t => Math.Sin(t * 2 * Math.PI * 13) * fontSize * 0.12));
                    storyboard.Children.Add(Sampled(transform, "TranslateY", duration, 120, t => Math.Sin(t * 2 * Math.PI * 11) * fontSize * 0.12));
                    break;
                case "rainbow":
                    var rich = content as RichTextBlock;
                    if (rich == null) return;

                    var brush = new SolidColorBrush(Hue(0));
                    rich.Foreground = brush;
                    storyboard.Children.Add(Rainbow(brush, duration));
                    break;
                case "sparkle":
                    AddSparkles(host, storyboard, duration);
                    break;
                default:
                    return;
            }

            host.Loaded += (sender, args) =>
            {
                try
                {
                    storyboard.Begin();
                }
                catch (Exception ex)
                {
                    AppLog.Error("MfmEffects: starting an MFM animation failed", ex);
                }
            };
            host.Unloaded += (sender, args) => storyboard.Stop();
        }

        private static double Jelly(double t)
        {
            return Math.Sin(t * 2 * Math.PI) * (1 - t);
        }

        private static double TadaPulse(double t)
        {
            if (t < 0.2) return 0.9;
            return t < 0.9 ? 1.1 : 1;
        }

        private static DoubleAnimationUsingKeyFrames Sampled(DependencyObject target, string property, int duration, int steps, Func<double, double> value)
        {
            var animation = new DoubleAnimationUsingKeyFrames();
            for (var step = 0; step <= steps; step++)
            {
                var t = (double)step / steps;
                animation.KeyFrames.Add(new LinearDoubleKeyFrame
                {
                    KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(duration * t)),
                    Value = value(t)
                });
            }

            Storyboard.SetTarget(animation, target);
            Storyboard.SetTargetProperty(animation, property);
            return animation;
        }

        private static ColorAnimationUsingKeyFrames Rainbow(SolidColorBrush brush, int duration)
        {
            const int steps = 12;
            var animation = new ColorAnimationUsingKeyFrames { EnableDependentAnimation = true };
            for (var step = 0; step <= steps; step++)
            {
                var t = (double)step / steps;
                animation.KeyFrames.Add(new LinearColorKeyFrame
                {
                    KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(duration * t)),
                    Value = Hue(t * 360)
                });
            }

            Storyboard.SetTarget(animation, brush);
            Storyboard.SetTargetProperty(animation, "Color");
            return animation;
        }

        private static Color Hue(double degrees)
        {
            const double saturation = 0.8;
            const double value = 0.9;
            var h = (degrees % 360) / 60;
            var chroma = value * saturation;
            var x = chroma * (1 - Math.Abs(h % 2 - 1));
            var m = value - chroma;

            double r, g, b;
            if (h < 1) { r = chroma; g = x; b = 0; }
            else if (h < 2) { r = x; g = chroma; b = 0; }
            else if (h < 3) { r = 0; g = chroma; b = x; }
            else if (h < 4) { r = 0; g = x; b = chroma; }
            else if (h < 5) { r = x; g = 0; b = chroma; }
            else { r = chroma; g = 0; b = x; }

            return Color.FromArgb(0xFF, ToByte(r + m), ToByte(g + m), ToByte(b + m));
        }

        private static byte ToByte(double channel)
        {
            return (byte)Math.Round(Clamp(channel, 0, 1) * 255, MidpointRounding.AwayFromZero);
        }

        private static void AddSparkles(Grid host, Storyboard storyboard, int duration)
        {
            var canvas = new Canvas { IsHitTestVisible = false };
            var placements = new List<KeyValuePair<Point, TransformGroup>>();

            for (var index = 0; index < s_sparkles.Length; index++)
            {
                var pulse = new ScaleTransform();
                var group = new TransformGroup();
                group.Children.Add(pulse);
                group.Children.Add(new ScaleTransform());
                group.Children.Add(new TranslateTransform());

                var star = new Path { Data = StarGeometry(), Fill = new SolidColorBrush(s_sparkleColor), RenderTransform = group, Opacity = 0 };
                canvas.Children.Add(star);
                placements.Add(new KeyValuePair<Point, TransformGroup>(s_sparkles[index], group));

                var phase = (double)index / s_sparkles.Length;
                Func<double, double> alpha = t => Math.Sin(((t + phase) % 1) * Math.PI);
                storyboard.Children.Add(Sampled(pulse, "ScaleX", duration, 24, alpha));
                storyboard.Children.Add(Sampled(pulse, "ScaleY", duration, 24, alpha));
                storyboard.Children.Add(Sampled(star, "Opacity", duration, 24, alpha));
            }

            host.Children.Add(canvas);
            host.SizeChanged += (sender, args) =>
            {
                var radius = Math.Min(args.NewSize.Width, args.NewSize.Height) * 0.18;
                foreach (var placement in placements)
                {
                    var size = (ScaleTransform)placement.Value.Children[1];
                    size.ScaleX = radius;
                    size.ScaleY = radius;

                    var place = (TranslateTransform)placement.Value.Children[2];
                    place.X = args.NewSize.Width * placement.Key.X;
                    place.Y = args.NewSize.Height * placement.Key.Y;
                }
            };
        }

        private static Geometry StarGeometry()
        {
            var figure = new PathFigure { StartPoint = new Point(0, -1), IsClosed = true, IsFilled = true };
            figure.Segments.Add(new QuadraticBezierSegment { Point1 = new Point(0, 0), Point2 = new Point(1, 0) });
            figure.Segments.Add(new QuadraticBezierSegment { Point1 = new Point(0, 0), Point2 = new Point(0, 1) });
            figure.Segments.Add(new QuadraticBezierSegment { Point1 = new Point(0, 0), Point2 = new Point(-1, 0) });
            figure.Segments.Add(new QuadraticBezierSegment { Point1 = new Point(0, 0), Point2 = new Point(0, -1) });

            var geometry = new PathGeometry();
            geometry.Figures.Add(figure);
            return geometry;
        }
    }
}
