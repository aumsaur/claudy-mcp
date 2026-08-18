using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Point = System.Windows.Point;

namespace PetOverlay;

// Full-screen click-catcher used to pick a screen point for "place this toy
// here" interactions (ball, food). Fixed-size like AimLine (not
// SizeToContent - see the memory'd shrink-to-fit pitfall for that combo with
// AllowsTransparency), but unlike AimLine this one IS hit-test visible since
// its whole job is catching the next click.
//
// Spans the *virtual* (all-monitors) desktop, not just the primary screen -
// the pet can be dragged onto a secondary monitor (MainWindow.DragMove), and
// a placement overlay that only covered the primary screen would then sit on
// a completely different monitor than where the user is actually looking,
// which reads as "the toy spawns somewhere random and I have to go hunt for
// it" rather than "click anywhere". Uses WPF's own SystemParameters instead
// of System.Windows.Forms.Screen for this specifically so the bounds stay in
// the same coordinate space as Left/Top/the mouse-event coordinates below -
// no Forms/WPF unit mixing to worry about.
public partial class PlacementOverlay : Window
{
    public event Action<Point>? Placed;
    public event Action? Cancelled;

    private bool _resolved;

    public PlacementOverlay(string? spritePath)
    {
        InitializeComponent();

        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;
        RootCanvas.Width = Width;
        RootCanvas.Height = Height;

        if (spritePath != null && System.IO.File.Exists(spritePath))
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(spritePath, UriKind.Absolute);
            bmp.EndInit();
            bmp.Freeze();
            GhostImage.Source = bmp;
        }

        MouseMove += OnMouseMove;
        MouseLeftButtonDown += (_, e) =>
        {
            var p = e.GetPosition(this);
            Resolve(cancel: false, new Point(Left + p.X, Top + p.Y));
        };
        MouseRightButtonDown += (_, _) => Resolve(cancel: true);
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Resolve(cancel: true);
        };

        // Being Topmost is NOT enough to reliably receive the click: other
        // topmost windows exist (the pet itself, bubbles, and whatever
        // fullscreen app the user is actually looking at), and whichever one
        // is above this one under the cursor gets the mouse input instead.
        // That produced the original bug report - the ghost icon only tracked
        // while this window happened to be on top ("I have to hover it"), the
        // click never landed, and it appeared to vanish whenever focus moved.
        // Capturing the mouse routes every move/click here regardless of
        // z-order or focus until we release it, which is exactly the "next
        // click anywhere goes to me" semantics this window needs.
        Loaded += (_, _) =>
        {
            Activate();
            RootCanvas.CaptureMouse();
        };

        // If capture is lost some other way (another app force-grabbing input,
        // an alt-tab), don't strand the user in an invisible modal state where
        // clicks do nothing - cancel out so the menu can just be reopened.
        RootCanvas.LostMouseCapture += (_, _) => Resolve(cancel: true);
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        var p = e.GetPosition(this);
        Canvas.SetLeft(GhostImage, p.X - (GhostImage.Width / 2));
        Canvas.SetTop(GhostImage, p.Y - (GhostImage.Height / 2));
    }

    private void Resolve(bool cancel, Point point = default)
    {
        // Set first: releasing capture below re-enters here via LostMouseCapture,
        // and the handler that fires on a real click would otherwise be followed
        // by a spurious "cancelled".
        if (_resolved) return;
        _resolved = true;

        // Release *and fully close* before invoking Placed. Mouse capture is
        // per-thread in Win32, so a handler that takes capture for itself (the
        // ball hands the still-held button straight into its throw drag) would
        // have that capture torn back down when this window closed a moment
        // later - which showed up as "I place it, drag immediately, and nothing
        // happens until I press a second time".
        RootCanvas.ReleaseMouseCapture();
        Close();

        if (cancel) Cancelled?.Invoke();
        else Placed?.Invoke(point);
    }
}
