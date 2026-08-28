using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace PetOverlay;

// A read-only heads-up panel over the same numbers claude-pulse puts in the Claude
// Code status line - for when the terminal isn't the window you're looking at.
// Follows the pet like the other pet-anchored windows, and shows itself without
// stealing focus (ShowActivated="False"), since a HUD that pulls focus off the
// terminal every time it opens defeats the point.
public partial class PulseHud : Window
{
    // The pulse files only change when the status line repaints, so polling hard
    // buys nothing; the reset countdowns are recomputed locally on the same tick
    // and stay live even while the payload itself is frozen.
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(2);

    // Past this, the numbers are old enough that the age note earns its place and
    // the panel dims to say so.
    private static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(2);

    private readonly Func<Point> _followTarget;
    private readonly DispatcherTimer _followTimer;
    private readonly DispatcherTimer _refreshTimer;

    private readonly BarRow _sessionRow;
    private readonly BarRow _weeklyRow;
    private readonly BarRow _contextRow;

    // claude-pulse replaces its state files atomically, but a read landing on the
    // replace still fails transiently - keep drawing the last good numbers rather
    // than blinking to empty.
    private PulseSnapshot? _last;

    public PulseHud(Func<Point> followTarget)
    {
        InitializeComponent();
        _followTarget = followTarget;

        _sessionRow = new BarRow("Session");
        _weeklyRow = new BarRow("Weekly");
        _contextRow = new BarRow("Context");
        RowsPanel.Children.Add(_sessionRow.Root);
        RowsPanel.Children.Add(_weeklyRow.Root);
        RowsPanel.Children.Add(_contextRow.Root);

        // Nothing here is interactive, so any click on it is a dismiss. The X is the
        // discoverable version of that same gesture - the panel never takes focus
        // (ShowActivated="False"), so Escape only lands on the rare occasion something
        // else handed it focus, and "pick Pulse again" means a trip through the ring.
        MouseLeftButtonUp += (_, _) => Close();
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Close();
        };

        var closeIdle = new SolidColorBrush(Color.FromRgb(0x8A, 0x8A, 0x96));
        var closeHot = new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xEE));
        var closeHover = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x42));
        CloseButton.MouseEnter += (_, _) =>
        {
            CloseButton.Background = closeHover;
            CloseGlyph.Foreground = closeHot;
        };
        CloseButton.MouseLeave += (_, _) =>
        {
            CloseButton.Background = Brushes.Transparent;
            CloseGlyph.Foreground = closeIdle;
        };
        CloseButton.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            Close();
        };

        _followTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
        _followTimer.Tick += (_, _) => PositionAtTarget();

        _refreshTimer = new DispatcherTimer { Interval = RefreshInterval };
        _refreshTimer.Tick += (_, _) => Refresh();

        Closed += (_, _) =>
        {
            _followTimer.Stop();
            _refreshTimer.Stop();
        };

        ContentRendered += (_, _) =>
        {
            PositionAtTarget();
            _followTimer.Start();
            _refreshTimer.Start();
        };

        Refresh();
    }

    private void Refresh()
    {
        if (!PulseReader.IsInstalled && _last is null)
        {
            RowsPanel.Visibility = Visibility.Collapsed;
            AgeText.Text = "";
            FooterText.Text = "claude-pulse isn’t running here. Set it up as your Claude Code status line and this fills in.";
            return;
        }

        if (PulseReader.TryRead() is { } fresh) _last = fresh;
        if (_last is not { } snap) return;

        RowsPanel.Visibility = Visibility.Visible;

        _sessionRow.Update(snap.Session?.Percent, ResetNote(snap.Session));
        _weeklyRow.Update(snap.Weekly?.Percent, ResetNote(snap.Weekly));
        _contextRow.Update(snap.ContextPct, ContextNote(snap));

        FooterText.Text = BuildFooter(snap);

        // Everything above is a snapshot of the last repaint, cost most of all -
        // dimming the bars but leaving "$6.08" at full brightness would keep the
        // most staleness-sensitive number on the panel looking authoritative.
        var age = snap.Age;
        var stale = age > StaleAfter;
        AgeText.Text = stale ? $"{FormatSpan(age)} ago" : "";
        RowsPanel.Opacity = stale ? 0.55 : 1.0;
        FooterText.Opacity = stale ? 0.55 : 1.0;
    }

    private static string BuildFooter(PulseSnapshot snap)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(snap.ModelName)) parts.Add(snap.ModelName!);
        if (!string.IsNullOrWhiteSpace(snap.Effort)) parts.Add(Capitalize(snap.Effort!));
        if (snap.FastMode) parts.Add("fast");
        if (snap.CostUsd is { } cost) parts.Add($"${cost:0.00}");
        if (!string.IsNullOrWhiteSpace(snap.Plan)) parts.Add(snap.Plan!);
        return string.Join("  ·  ", parts);
    }

    private static string ResetNote(PulseWindowStat? stat)
    {
        if (stat?.ResetsAt is not { } resets) return "";
        var left = resets - DateTimeOffset.UtcNow;
        return left <= TimeSpan.Zero ? "resets now" : $"{FormatSpan(left)} left";
    }

    private static string ContextNote(PulseSnapshot snap)
    {
        if (snap.ContextUsed is not { } used || snap.ContextLimit is not { } limit || limit <= 0) return "";
        return $"{FormatTokens(used)}/{FormatTokens(limit)}";
    }

    private static string FormatSpan(TimeSpan span)
    {
        if (span.TotalDays >= 1) return $"{(int)span.TotalDays}d {span.Hours}h";
        if (span.TotalHours >= 1) return $"{(int)span.TotalHours}h {span.Minutes}m";
        if (span.TotalMinutes >= 1) return $"{(int)span.TotalMinutes}m";
        return "<1m";
    }

    private static string FormatTokens(long tokens)
    {
        if (tokens >= 1_000_000) return $"{tokens / 1_000_000.0:0.#}M";
        if (tokens >= 1_000) return $"{tokens / 1_000.0:0}k";
        return tokens.ToString();
    }

    private static string Capitalize(string text) =>
        text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

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

    // One "Label [====----] 42% note" line. Built once and mutated on each refresh
    // so a tick can't reflow the panel out from under the pointer.
    private sealed class BarRow
    {
        private const double BarWidth = 116;

        private static readonly Brush Track = new SolidColorBrush(Color.FromRgb(0x2E, 0x2E, 0x36));
        private static readonly Brush Good = new SolidColorBrush(Color.FromRgb(0x5C, 0xC8, 0x6E));
        private static readonly Brush Warn = new SolidColorBrush(Color.FromRgb(0xE0, 0xB3, 0x4E));
        private static readonly Brush Hot = new SolidColorBrush(Color.FromRgb(0xE0, 0x5F, 0x54));

        private readonly Border _fill;
        private readonly TextBlock _value;
        private readonly TextBlock _note;

        public Grid Root { get; }

        public BarRow(string label)
        {
            _fill = new Border
            {
                CornerRadius = new CornerRadius(4),
                Background = Good,
                HorizontalAlignment = HorizontalAlignment.Left,
                Width = 0,
            };

            var bar = new Grid { Height = 8, VerticalAlignment = VerticalAlignment.Center, Width = BarWidth };
            bar.Children.Add(new Border { CornerRadius = new CornerRadius(4), Background = Track });
            bar.Children.Add(_fill);

            _value = new TextBlock
            {
                Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xEE)),
                FontSize = 11,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
            };

            _note = new TextBlock
            {
                Foreground = new SolidColorBrush(Color.FromRgb(0x7C, 0x7C, 0x88)),
                FontSize = 10,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0),
            };

            Root = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            Root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56) });
            Root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) });
            Root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var caption = new TextBlock
            {
                Text = label,
                Foreground = new SolidColorBrush(Color.FromRgb(0xB4, 0xB4, 0xC0)),
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
            };

            Grid.SetColumn(caption, 0);
            Grid.SetColumn(bar, 1);
            Grid.SetColumn(_value, 2);
            Grid.SetColumn(_note, 3);
            Root.Children.Add(caption);
            Root.Children.Add(bar);
            Root.Children.Add(_value);
            Root.Children.Add(_note);
        }

        public void Update(double? percent, string note)
        {
            // A window the plan doesn't report (Claude Pro leaves the per-model caps
            // null) gets hidden rather than drawn as a confident 0%.
            if (percent is not { } pct)
            {
                Root.Visibility = Visibility.Collapsed;
                return;
            }

            Root.Visibility = Visibility.Visible;
            var clamped = Math.Clamp(pct, 0, 100);
            _fill.Width = BarWidth * clamped / 100.0;
            _fill.Background = clamped >= 85 ? Hot : clamped >= 60 ? Warn : Good;
            _value.Text = $"{clamped:0}%";
            _note.Text = note;
        }
    }
}
