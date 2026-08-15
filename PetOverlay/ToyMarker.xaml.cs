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
        Closed += (_, _) => _aimLine?.Close();
    }

    public void MoveTo(Point center)
    {
        _center = center;
        if (ActualWidth > 0) ApplyCenter();
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
        _aimLine = new AimLine();
        _aimLine.Show();
        _aimLine.UpdateLine(CenterPoint, CenterPoint);

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
