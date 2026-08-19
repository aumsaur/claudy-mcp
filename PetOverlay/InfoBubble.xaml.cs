using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace PetOverlay;

public partial class InfoBubble : Window
{
    private readonly Rect _anchor;
    private readonly Func<Point>? _followTarget;
    private readonly DispatcherTimer _autoCloseTimer;
    private readonly double _sideOffset;
    private DispatcherTimer? _followTimer;

    public InfoBubble(string text, Rect anchor, TimeSpan duration, Func<Point>? followTarget = null)
    {
        InitializeComponent();
        _anchor = anchor;
        _followTarget = followTarget;
        _sideOffset = (Random.Shared.NextDouble() - 0.5) * 50; // small left/right jitter so it doesn't look like a centered tooltip
        EmojiIcons.SetRichText(MessageText, text);

        MouseLeftButtonDown += (_, _) => Close();

        _autoCloseTimer = new DispatcherTimer { Interval = duration };
        _autoCloseTimer.Tick += (_, _) => Close();
        _autoCloseTimer.Start();

        Closed += (_, _) =>
        {
            _autoCloseTimer.Stop();
            _followTimer?.Stop();
        };

        ContentRendered += (_, _) =>
        {
            PositionNearAnchor();
            if (_followTarget != null)
            {
                _followTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
                _followTimer.Tick += (_, _) => PositionAtTarget();
                _followTimer.Start();
            }
        };
    }

    private void PositionAtTarget()
    {
        var p = _followTarget!();
        Left = p.X - (ActualWidth / 2) + _sideOffset;
        Top = p.Y - ActualHeight + 6;
    }

    private void PositionNearAnchor()
    {
        var working = SystemParameters.WorkArea;

        double left = _anchor.Left + (_anchor.Width / 2) - (ActualWidth / 2) + _sideOffset;
        if (left < working.Left) left = working.Left + 8;
        if (left + ActualWidth > working.Right) left = working.Right - ActualWidth - 8;

        double top = _anchor.Top - ActualHeight + 6;
        if (top < working.Top) top = _anchor.Bottom + 10;

        Left = left;
        Top = top;
    }
}
