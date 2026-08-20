using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace PetOverlay;

// Edits the name on the badge under the pet (and everything derived from it: the
// tray tooltip, and the name siblings greet it by). Follows the pet the same way
// the other pet-anchored windows do, so it stays attached while Claudy wanders.
// Submitting an empty box is the documented way to drop a custom name and fall
// back to the folder-derived default, which is why onSubmit takes the raw trimmed
// string rather than refusing to report a blank one.
public partial class RenameWindow : Window
{
    private readonly Func<Point> _followTarget;
    private readonly Action<string> _onSubmit;
    private readonly DispatcherTimer _followTimer;

    public RenameWindow(string currentName, string defaultName, Func<Point> followTarget, Action<string> onSubmit)
    {
        InitializeComponent();
        _followTarget = followTarget;
        _onSubmit = onSubmit;

        HintText.Text = $"What should the badge say? Leave it empty to go back to \u201C{defaultName}\u201D.";
        NameBox.Text = currentName;

        CancelButton.Click += (_, _) => Close();
        SaveButton.Click += (_, _) => Submit();
        NameBox.KeyDown += (_, e) =>
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
            NameBox.Focus();
            NameBox.SelectAll();
        };
    }

    private void Submit()
    {
        var text = NameBox.Text.Trim();
        Close();
        _onSubmit(text);
    }

    private void PositionAtTarget()
    {
        var working = SystemParameters.WorkArea;
        var p = _followTarget();

        double left = p.X - (ActualWidth / 2);
        if (left < working.Left) left = working.Left + 8;
        if (left + ActualWidth > working.Right) left = working.Right - ActualWidth - 8;

        double top = p.Y - ActualHeight + 6;
        if (top < working.Top) top = working.Top + 8;

        Left = left;
        Top = top;
    }
}
