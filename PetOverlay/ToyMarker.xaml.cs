using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Forms = System.Windows.Forms;

namespace PetOverlay;

public partial class ToyMarker : Window
{
    private Point _center;
    private Point _dragStart;
    private bool _dragging;
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
            // only there to make emoji glyphs readable, so drop it here (keep the
            // brush as Transparent, not null, so the area stays draggable).
            RootBorder.Background = System.Windows.Media.Brushes.Transparent;
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
        RootBorder.LostMouseCapture += (_, _) => CancelDrag();
        Closed += (_, _) => _aimLine?.Close();
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

        // Take capture first and bail if it's refused, rather than optimistically
        // entering the dragging state - a "started" drag with no capture is the
        // stuck-and-invisible case (no MouseUp will ever arrive to end it).
        if (!RootBorder.CaptureMouse()) return;

        var p = Forms.Cursor.Position;
        _dragging = true;
        _dragStart = new Point(p.X, p.Y);
        ShowAimLine();
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
        _aimLine?.Close();
        _aimLine = null;
    }

    private void ApplyCenter()
    {
        Left = _center.X - (ActualWidth / 2);
        Top = _center.Y - (ActualHeight / 2);
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!IsThrowable) return;
        _dragging = true;
        var p = Forms.Cursor.Position;
        _dragStart = new Point(p.X, p.Y);
        RootBorder.CaptureMouse();

        // Ball stays put while pulled back; the aim line shows direction/power instead.
        ShowAimLine();

        e.Handled = true;
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragging) return;
        var p = Forms.Cursor.Position;
        var cursor = new Point(p.X, p.Y);
        var center = CenterPoint;

        // Point the line where the ball will actually fly (opposite the pull),
        // not at the cursor itself — a trajectory preview, not a pull indicator.
        var throwPoint = new Point((2 * center.X) - cursor.X, (2 * center.Y) - cursor.Y);
        _aimLine?.UpdateLine(center, throwPoint);
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        RootBorder.ReleaseMouseCapture();
        _aimLine?.Close();
        _aimLine = null;

        var p = Forms.Cursor.Position;
        var displacement = new Point(p.X, p.Y) - _dragStart;

        // Slingshot-style: how far you pulled it is the throw, independent of
        // how fast you moved the mouse — a small tap-and-release doesn't throw at all.
        if (displacement.Length > 20)
        {
            Thrown?.Invoke(displacement);
        }
    }
}
