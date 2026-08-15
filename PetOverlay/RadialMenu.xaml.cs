using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace PetOverlay;

public partial class RadialMenu : Window
{
    private const double Radius = 100;
    private const double BallSize = 48;

    public RadialMenu(Point center, IReadOnlyList<(string Emoji, string Tooltip, Action OnSelect)> items)
    {
        InitializeComponent();

        var canvasCenter = new Point(160, 160);
        int n = items.Count;

        for (int i = 0; i < n; i++)
        {
            double angle = (Math.PI * 2 * i / n) - (Math.PI / 2);
            var finalPos = new Point(
                canvasCenter.X + (Radius * Math.Cos(angle)),
                canvasCenter.Y + (Radius * Math.Sin(angle)));

            var ball = BuildBall(items[i].Emoji, items[i].Tooltip);
            var action = items[i].OnSelect;
            ball.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                action();
                Close();
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

        MouseLeftButtonUp += (_, _) => Close();
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Close();
        };

        // Show() can race with the OS assigning focus, firing a spurious Deactivated
        // before the window ever really activates. Only treat a deactivation as
        // "click away" once we've confirmed we were activated first.
        bool hasActivated = false;
        Activated += (_, _) => hasActivated = true;
        Deactivated += (_, _) =>
        {
            if (hasActivated) Close();
        };

        ContentRendered += (_, _) =>
        {
            Left = center.X - (ActualWidth / 2);
            Top = center.Y - (ActualHeight / 2);
        };
    }

    private static Border BuildBall(string emoji, string tooltip)
    {
        // WPF's TextBlock renders emoji as a flat black glyph (no color font support),
        // so the ball needs a light fill for the icon to read at all.
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
            ToolTip = tooltip,
            Cursor = Cursors.Hand,
            Effect = new DropShadowEffect { BlurRadius = 10, ShadowDepth = 2, Opacity = 0.5, Color = Colors.Black },
        };
        border.Child = new TextBlock
        {
            Text = emoji,
            FontSize = 20,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        border.MouseEnter += (_, _) => border.Background = hoverBrush;
        border.MouseLeave += (_, _) => border.Background = normalBrush;
        return border;
    }
}
