using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

namespace PetOverlay;

public class RadialItem
{
    public string? SpritePath { get; init; }
    public string? Emoji { get; init; }
    public required string Tooltip { get; init; }
    public Action? OnSelect { get; init; }
    public IReadOnlyList<RadialItem>? Children { get; init; }
}

public partial class RadialMenu : Window
{
    private const double Radius = 100;
    private const double BallSize = 48;

    private readonly Stack<IReadOnlyList<RadialItem>> _levelStack = new();
    private readonly string _backIconPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "icons", "menu", "back.png");
    private bool _closing;

    public RadialMenu(Point center, IReadOnlyList<RadialItem> rootItems)
    {
        InitializeComponent();

        RenderLevel(rootItems);

        MouseLeftButtonUp += (_, _) => TryClose();
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) TryClose();
        };

        // Show() can race with the OS assigning focus, firing a spurious Deactivated
        // before the window ever really activates. Only treat a deactivation as
        // "click away" once we've confirmed we were activated first.
        bool hasActivated = false;
        Activated += (_, _) => hasActivated = true;
        Deactivated += (_, _) =>
        {
            if (hasActivated) TryClose();
        };

        ContentRendered += (_, _) =>
        {
            Left = center.X - (ActualWidth / 2);
            Top = center.Y - (ActualHeight / 2);
        };
    }

    // Several independent paths can each try to close this window (click-away,
    // Escape, picking a leaf item, losing activation) and can fire in quick
    // succession from a single user action - guard against a second Close() call
    // landing while the first is still tearing the window down.
    private void TryClose()
    {
        if (_closing) return;
        _closing = true;
        Close();
    }

    // Re-lays-out the ring for a given level, re-running the launch animation. Used
    // both for the initial root level and whenever navigating into/out of a submenu.
    private void RenderLevel(IReadOnlyList<RadialItem> items)
    {
        ItemsCanvas.Children.Clear();

        var slots = new List<(RadialItem Item, Action OnClick)>();

        foreach (var item in items)
        {
            if (item.Children is { } children)
            {
                slots.Add((item, () =>
                {
                    _levelStack.Push(items);
                    RenderLevel(children);
                }));
            }
            else
            {
                var action = item.OnSelect;
                slots.Add((item, () =>
                {
                    action?.Invoke();
                    TryClose();
                }));
            }
        }

        if (_levelStack.Count > 0)
        {
            var backItem = new RadialItem { SpritePath = _backIconPath, Tooltip = "Back" };
            slots.Add((backItem, () =>
            {
                var parent = _levelStack.Pop();
                RenderLevel(parent);
            }));
        }

        var canvasCenter = new Point(160, 160);
        int n = slots.Count;

        for (int i = 0; i < n; i++)
        {
            double angle = (Math.PI * 2 * i / n) - (Math.PI / 2);
            var finalPos = new Point(
                canvasCenter.X + (Radius * Math.Cos(angle)),
                canvasCenter.Y + (Radius * Math.Sin(angle)));

            var (item, onClick) = slots[i];
            var ball = BuildBall(item);
            ball.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                onClick();
            };

            Canvas.SetLeft(ball, finalPos.X - (BallSize / 2));
            Canvas.SetTop(ball, finalPos.Y - (BallSize / 2));
            ItemsCanvas.Children.Add(ball);

            // Ball starts pulled back to the center (where Claudy is) and pops out to
            // its resting spot on a stagger, mirroring the "gooey menu" launch feel.
            var translate = new TranslateTransform(canvasCenter.X - finalPos.X, canvasCenter.Y - finalPos.Y);
            var scale = new ScaleTransform(0.3, 0.3);
            ball.RenderTransformOrigin = new Point(0.5, 0.5);
            ball.RenderTransform = new TransformGroup { Children = { scale, translate } };

            var delay = TimeSpan.FromMilliseconds(40 * i);
            var ease = new BackEase { Amplitude = 0.7, EasingMode = EasingMode.EaseOut };
            var duration = TimeSpan.FromMilliseconds(320);

            var moveX = new DoubleAnimation(0, duration) { BeginTime = delay, EasingFunction = ease };
            var moveY = new DoubleAnimation(0, duration) { BeginTime = delay, EasingFunction = ease };
            var scaleUp = new DoubleAnimation(1.0, duration) { BeginTime = delay, EasingFunction = ease };

            translate.BeginAnimation(TranslateTransform.XProperty, moveX);
            translate.BeginAnimation(TranslateTransform.YProperty, moveY);
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleUp);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleUp);
        }
    }

    private static Border BuildBall(RadialItem item)
    {
        // WPF's TextBlock renders emoji as a flat black glyph (no color font support),
        // so an emoji fallback still needs a light fill for the icon to read at all.
        var normalBrush = new SolidColorBrush(Color.FromRgb(250, 250, 252));
        var hoverBrush = new SolidColorBrush(Color.FromRgb(210, 228, 255));

        var border = new Border
        {
            Width = BallSize,
            Height = BallSize,
            CornerRadius = new CornerRadius(BallSize / 2),
            Background = normalBrush,
            BorderBrush = new SolidColorBrush(Color.FromArgb(160, 40, 40, 46)),
            BorderThickness = new Thickness(1.5),
            ToolTip = item.Tooltip,
            Cursor = Cursors.Hand,
            Effect = new DropShadowEffect { BlurRadius = 10, ShadowDepth = 2, Opacity = 0.5, Color = Colors.Black },
        };

        if (item.SpritePath != null && System.IO.File.Exists(item.SpritePath))
        {
            var image = new Image
            {
                Source = LoadBitmap(item.SpritePath),
                Width = 28,
                Height = 28,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
            border.Child = image;
        }
        else
        {
            border.Child = new TextBlock
            {
                Text = item.Emoji ?? "?",
                FontSize = 20,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
        }

        border.MouseEnter += (_, _) => border.Background = hoverBrush;
        border.MouseLeave += (_, _) => border.Background = normalBrush;
        return border;
    }

    private static BitmapImage LoadBitmap(string path)
    {
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.UriSource = new Uri(path, UriKind.Absolute);
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }
}
