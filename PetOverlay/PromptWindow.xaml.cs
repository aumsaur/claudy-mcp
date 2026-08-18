using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace PetOverlay;

public partial class PromptWindow : Window
{
    private readonly TaskCompletionSource<PromptResponse> _tcs = new();
    private readonly DispatcherTimer _timeoutTimer;
    private readonly DispatcherTimer _followTimer;
    private readonly Func<Point> _followTarget;
    private readonly double _sideOffset;
    private TextBox? _textBox;
    private bool _freeTextOverrideShown;

    public Task<PromptResponse> ResultTask => _tcs.Task;

    public PromptWindow(PromptRequest request, Func<Point> followTarget)
    {
        InitializeComponent();
        _followTarget = followTarget;
        _sideOffset = (Random.Shared.NextDouble() - 0.5) * 40;
        QuestionText.Text = request.Question;

        switch (request.Kind)
        {
            case "choice":
                BuildChoice(request.Options ?? Array.Empty<string>());
                break;
            case "text":
                BuildText(request.Placeholder);
                break;
            default:
                BuildYesNo();
                break;
        }

        _timeoutTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(Math.Max(1, request.TimeoutSeconds)) };
        _timeoutTimer.Tick += (_, _) => Complete(new PromptResponse { Status = "timeout" });
        _timeoutTimer.Start();

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Complete(new PromptResponse { Status = "cancelled" });
        };

        _followTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
        _followTimer.Tick += (_, _) => PositionAtTarget();

        Closed += (_, _) =>
        {
            _timeoutTimer.Stop();
            _followTimer.Stop();
        };
        ContentRendered += (_, _) =>
        {
            PositionAtTarget();
            _followTimer.Start();
            _textBox?.Focus();
        };
    }

    private void PositionAtTarget()
    {
        var working = Forms.Screen.PrimaryScreen!.WorkingArea;
        var p = _followTarget();

        double left = p.X - (ActualWidth / 2) + _sideOffset;
        if (left < working.Left) left = working.Left + 8;
        if (left + ActualWidth > working.Right) left = working.Right - ActualWidth - 8;

        double top = p.Y - ActualHeight + 6;
        if (top < working.Top) top = working.Top + 8;

        Left = left;
        Top = top;
    }

    private Button MakeButton(string text, bool primary)
    {
        return new Button
        {
            Content = text,
            Padding = new Thickness(14, 6, 14, 6),
            Margin = new Thickness(0, 0, 8, 0),
            BorderThickness = new Thickness(0),
            Background = new SolidColorBrush(primary ? Color.FromRgb(60, 120, 220) : Color.FromRgb(55, 55, 60)),
            Foreground = Brushes.White,
        };
    }

    private void BuildYesNo()
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var no = MakeButton("No", false);
        no.Click += (_, _) => Complete(new PromptResponse { Status = "answered", Answer = "no" });
        var yes = MakeButton("Yes", true);
        yes.Margin = new Thickness(0);
        yes.Click += (_, _) => Complete(new PromptResponse { Status = "answered", Answer = "yes" });
        panel.Children.Add(no);
        panel.Children.Add(yes);
        ContentPanel.Children.Add(panel);
    }

    private void BuildChoice(string[] options)
    {
        foreach (var opt in options)
        {
            var b = MakeButton(opt, false);
            b.HorizontalContentAlignment = HorizontalAlignment.Left;
            b.HorizontalAlignment = HorizontalAlignment.Stretch;
            b.Margin = new Thickness(0, 0, 0, 6);
            b.Click += (_, _) => Complete(new PromptResponse { Status = "answered", Answer = opt });
            ContentPanel.Children.Add(b);
        }
    }

    private void BuildText(string? placeholder)
    {
        if (!string.IsNullOrEmpty(placeholder))
        {
            ContentPanel.Children.Add(new TextBlock
            {
                Text = placeholder,
                Foreground = Brushes.Gray,
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 6),
            });
        }

        _textBox = new TextBox { Padding = new Thickness(6), Margin = new Thickness(0, 0, 0, 10), MinWidth = 260 };
        _textBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) Complete(new PromptResponse { Status = "answered", Answer = _textBox.Text });
        };
        ContentPanel.Children.Add(_textBox);

        var panel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = MakeButton("Cancel", false);
        cancel.Click += (_, _) => Complete(new PromptResponse { Status = "cancelled" });
        var submit = MakeButton("Submit", true);
        submit.Margin = new Thickness(0);
        submit.Click += (_, _) => Complete(new PromptResponse { Status = "answered", Answer = _textBox.Text });
        panel.Children.Add(cancel);
        panel.Children.Add(submit);
        ContentPanel.Children.Add(panel);
    }

    // Lets the user type a free-form answer even when the question was asked as
    // yesno/choice (via the radial menu's Prompt item) - an escape hatch over the
    // buttons already shown, not a replacement for them.
    public void ShowFreeTextOverride()
    {
        if (_textBox != null)
        {
            _textBox.Focus();
            return;
        }
        if (_freeTextOverrideShown) return;
        _freeTextOverrideShown = true;

        ContentPanel.Children.Add(new TextBlock
        {
            Text = "Or type your own answer:",
            Foreground = Brushes.Gray,
            FontSize = 11,
            Margin = new Thickness(0, 12, 0, 6),
        });

        _textBox = new TextBox { Padding = new Thickness(6), Margin = new Thickness(0, 0, 0, 10), MinWidth = 260 };
        _textBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) Complete(new PromptResponse { Status = "answered", Answer = _textBox.Text });
        };
        ContentPanel.Children.Add(_textBox);

        var panel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var submit = MakeButton("Submit", true);
        submit.Click += (_, _) => Complete(new PromptResponse { Status = "answered", Answer = _textBox.Text });
        panel.Children.Add(submit);
        ContentPanel.Children.Add(panel);

        _textBox.Focus();
    }

    private void Complete(PromptResponse response)
    {
        if (_tcs.Task.IsCompleted) return;
        _tcs.SetResult(response);
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        if (!_tcs.Task.IsCompleted) _tcs.SetResult(new PromptResponse { Status = "cancelled" });
    }
}
