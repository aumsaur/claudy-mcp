using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace PetOverlay;

// A free-standing "message Claude" composer, distinct from PromptWindow (which
// answers a question Claude is already waiting on). No pipe is open here - the
// message just gets handed to onSubmit, which is responsible for queuing it
// somewhere Claude will check on its own next turn.
public partial class QueueMessageWindow : Window
{
    private readonly Func<Point> _followTarget;
    private readonly Action<string> _onSubmit;
    private readonly DispatcherTimer _followTimer;
    private readonly double _sideOffset;

    public QueueMessageWindow(Func<Point> followTarget, Action<string> onSubmit)
    {
        InitializeComponent();
        _followTarget = followTarget;
        _onSubmit = onSubmit;
        _sideOffset = (Random.Shared.NextDouble() - 0.5) * 40;

        CancelButton.Click += (_, _) => Close();
        SubmitButton.Click += (_, _) => Submit();
        MessageBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) Submit();
        };
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Close();
        };

        _followTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
        _followTimer.Tick += (_, _) => PositionAtTarget();

        Closed += (_, _) => _followTimer.Stop();
        ContentRendered += (_, _) =>
        {
            PositionAtTarget();
            _followTimer.Start();
            MessageBox.Focus();
        };
    }

    private void Submit()
    {
        var text = MessageBox.Text;
        Close();
        if (!string.IsNullOrWhiteSpace(text)) _onSubmit(text);
    }

    private void PositionAtTarget()
    {
        var working = SystemParameters.WorkArea;
        var p = _followTarget();

        double left = p.X - (ActualWidth / 2) + _sideOffset;
        if (left < working.Left) left = working.Left + 8;
        if (left + ActualWidth > working.Right) left = working.Right - ActualWidth - 8;

        double top = p.Y - ActualHeight + 6;
        if (top < working.Top) top = working.Top + 8;

        Left = left;
        Top = top;
    }
}
