using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace PetOverlay;

public partial class ToyMarker : Window
{
    private Point _center;
    private Point _dragStart;
    private bool _dragging;

    // A drag is driven by polling, not by mouse capture. Capture is still asked
    // for (it makes the events flow the normal way when it is granted) but it is
    // routinely refused or torn down here - the placement overlay closing right
    // as the throw begins takes the thread's capture with it - and every failure
    // mode of a capture-dependent drag looks the same to the user: the gesture
    // silently does nothing and they have to press again. Polling the physical
    // cursor and button instead means the pull and the release land wherever the
    // mouse actually is, whatever has focus or capture at the time.
    private readonly DispatcherTimer _dragTicker = new() { Interval = TimeSpan.FromMilliseconds(16) };

    private Point CursorDiu()
    {
        var p = Forms.Cursor.Position;
        return DpiUtil.PhysicalToDiu(this, new Point(p.X, p.Y));
    }
    private AimLine? _aimLine;

    public bool IsThrowable { get; set; }
    public event Action<Vector>? Thrown;

    public Point CenterPoint => new(Left + (ActualWidth / 2), Top + (ActualHeight / 2));

    public ToyMarker(string emoji, Point initialCenter, string? spritePath = null)
    {
        InitializeComponent();

        if (spritePath != null && System.IO.File.Exists(spritePath))
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(spritePath, UriKind.Absolute);
            bmp.EndInit();
            bmp.Freeze();
            SpriteImage.Source = bmp;
            SpriteImage.Visibility = Visibility.Visible;
            EmojiText.Visibility = Visibility.Collapsed;

            // The sprite is visible enough on its own — the circular backdrop was
            // only there to make emoji glyphs readable, so drop it here. The brush
            // has to stay a real, *nearly* transparent one rather than null or
            // Brushes.Transparent: on an AllowsTransparency window the OS doesn't
            // reliably deliver mouse input over fully alpha-0 pixels, which shrank
            // the grab area to just the 32x32 sprite instead of the whole marker.
            RootBorder.Background = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromArgb(0x01, 0xFF, 0xFF, 0xFF));
            RootBorder.BorderThickness = new Thickness(0);
        }
        else
        {
            EmojiText.Text = emoji;
        }

        _center = initialCenter;
        ContentRendered += (_, _) => ApplyCenter();

        RootBorder.MouseLeftButtonDown += OnMouseDown;
        RootBorder.MouseMove += OnMouseMove;
        RootBorder.MouseLeftButtonUp += OnMouseUp;
        _dragTicker.Tick += (_, _) => TickDrag();
        Closed += (_, _) => CancelDrag();
    }

    public void MoveTo(Point center)
    {
        _center = center;
        if (ActualWidth > 0) ApplyCenter();
    }

    // Starts the slingshot drag without waiting for a fresh press on the marker
    // itself. Used when the ball is placed by PlacementOverlay: the user is
    // already holding the button down from the placing click, so placing and
    // pulling back is one continuous gesture instead of click, then click
    // again, then drag. Deliberately reads the start point the same way
    // OnMouseDown does (rather than taking the overlay's WPF-space point) so
    // the pull vector stays measured in one consistent coordinate space with
    // the move/up handlers that finish the throw.
    public void BeginDrag()
    {
        if (!IsThrowable) return;

        // Show() hasn't necessarily measured a SizeToContent window yet, and
        // CenterPoint depends on ActualWidth/Height - force layout so the aim
        // line's origin is the ball's real center, not the pre-measure 0,0.
        UpdateLayout();
        ApplyCenter();

        // Ask for capture, but carry on without it - the ticker, not capture, is
        // what keeps the drag alive from here. Bailing on a refused capture made
        // the whole gesture silently dead, which is the "I place it, drag, nothing
        // happens, and I have to press a second time" report.
        RootBorder.CaptureMouse();
        StartDrag();
    }

    private void StartDrag()
    {
        // Cursor.Position is physical pixels; _dragStart is compared against
        // CenterPoint and drives throw power, both in device-independent units.
        _dragStart = CursorDiu();
        _dragging = true;
        ShowAimLine();
        _dragTicker.Start();
    }

    // Always goes through here so a previous aim line can never be orphaned by
    // being overwritten in the field while its window is still on screen -
    // that's what left a stray dot/line sitting at the old start point after a
    // drag that got interrupted rather than finished.
    private void ShowAimLine()
    {
        _aimLine?.Close();
        _aimLine = new AimLine();
        _aimLine.Show();
        _aimLine.UpdateLine(CenterPoint, CenterPoint);
    }

    // A drag can end without a MouseUp ever reaching us (capture stolen by
    // another app, the window closing mid-pull). Without this the marker stays
    // stuck in _dragging with its aim line orphaned on screen forever.
    private void CancelDrag()
    {
        _dragging = false;
        _dragTicker.Stop();
        _aimLine?.Close();
        _aimLine = null;
    }

    // The button can come back up anywhere - over another window, over a part of
    // the screen this little marker does not cover - so the release that ends the
    // throw is read from the physical button state rather than waited for as a
    // MouseUp that may never be delivered here.
    private void TickDrag()
    {
        if (!_dragging) return;

        UpdateAimLine(CursorDiu());
        if ((GetAsyncKeyState(VkLButton) & 0x8000) == 0) FinishDrag();
    }

    private void UpdateAimLine(Point cursor)
    {
        var center = CenterPoint;

        // Point the line where the ball will actually fly (opposite the pull),
        // not at the cursor itself — a trajectory preview, not a pull indicator.
        var throwPoint = new Point((2 * center.X) - cursor.X, (2 * center.Y) - cursor.Y);
        _aimLine?.UpdateLine(center, throwPoint);
    }

    private void FinishDrag()
    {
        if (!_dragging) return;
        _dragging = false;
        _dragTicker.Stop();
        RootBorder.ReleaseMouseCapture();
        _aimLine?.Close();
        _aimLine = null;

        var displacement = CursorDiu() - _dragStart;

        // Slingshot-style: how far you pulled it is the throw, independent of
        // how fast you moved the mouse — a small tap-and-release doesn't throw at all.
        if (displacement.Length > 20)
        {
            Thrown?.Invoke(displacement);
        }
    }

    private void ApplyCenter()
    {
        Left = _center.X - (ActualWidth / 2);
        Top = _center.Y - (ActualHeight / 2);
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!IsThrowable) return;
        RootBorder.CaptureMouse();

        // Ball stays put while pulled back; the aim line shows direction/power instead.
        StartDrag();

        e.Handled = true;
    }

    // The ticker already redraws the aim line; this only makes it feel immediate
    // when capture *is* granted and moves arrive faster than the tick.
    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragging) return;
        UpdateAimLine(CursorDiu());
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        FinishDrag();
    }

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    private const int VkLButton = 0x01;
}
