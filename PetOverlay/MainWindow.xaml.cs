using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Forms = System.Windows.Forms;
using Point = System.Windows.Point;

namespace PetOverlay;

public enum PetMode
{
    Idle,
    FollowingCursor,
    Playing,
    ReturningHome,
}

public partial class MainWindow : Window
{
    private static readonly string[] Moods =
    {
        "Just vibing 😊",
        "Kinda sleepy... 😴",
        "Bored, entertain me? 🥱",
        "Feeling bouncy today! 🤩",
        "*stares at the cursor* 👀",
        "Snack time? 🍪",
        "Booping around ✨",
    };

    private static readonly (string Label, string Emoji)[] Toys =
    {
        ("Ball", "🎾"),
        ("Yarn", "🧶"),
        ("Wand", "✨"),
    };

    private readonly DispatcherTimer _tickTimer;
    private readonly PipeServer _pipeServer;
    private readonly Dictionary<string, BitmapImage> _sprites = new();
    private readonly List<(DateTime Time, double X)> _patSamples = new();
    private readonly Random _rng = new();

    private Forms.NotifyIcon? _trayIcon;
    private Window? _activeBubble;
    private ToyMarker? _toyMarker;
    private Point _toyPos;

    private PetMode _mode = PetMode.Idle;
    private DateTime _sessionUntil = DateTime.MinValue;
    private DateTime _nextToyMove = DateTime.MinValue;
    private DateTime _nextWanderCheck = DateTime.MinValue;
    private DateTime _nextMoodCheck = DateTime.MinValue;
    private DateTime _alertUntil = DateTime.MinValue;
    private DateTime _lastPatTrigger = DateTime.MinValue;

    private Point _restPosition;
    private string _facing = "south";
    private double _phase;

    public MainWindow()
    {
        InitializeComponent();

        Loaded += (_, _) =>
        {
            PositionBottomRight();
            _restPosition = new Point(Left, Top);
            LoadSprites();
            _nextWanderCheck = DateTime.UtcNow.AddSeconds(_rng.Next(20, 40));
            _nextMoodCheck = DateTime.UtcNow.AddSeconds(_rng.Next(30, 60));
        };

        MouseLeftButtonDown += (_, _) => DragMove();
        MouseRightButtonUp += (_, _) => ShowRadialMenu();
        PreviewMouseMove += OnPetMouseMove;

        _tickTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
        _tickTimer.Tick += (_, _) => Tick();
        _tickTimer.Start();

        CreateTrayIcon();

        _pipeServer = new PipeServer(ShowPromptAsync);
        _pipeServer.Start();

        Closed += (_, _) =>
        {
            _pipeServer.Stop();
            _trayIcon?.Dispose();
            _toyMarker?.Close();
        };
    }

    // ---- setup ----

    private void PositionBottomRight()
    {
        var working = Forms.Screen.PrimaryScreen!.WorkingArea;
        Left = working.Right - Width - 24;
        Top = working.Bottom - Height - 24;
    }

    private void LoadSprites()
    {
        foreach (var name in new[] { "south", "east", "west", "north" })
        {
            var path = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "claudy", name + ".png");
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(path, UriKind.Absolute);
            bmp.EndInit();
            bmp.Freeze();
            _sprites[name] = bmp;
        }
        PetImage.Source = _sprites["south"];
    }

    // ---- main loop ----

    private void Tick()
    {
        AnimateIdleBobAndSquish();

        if (_activeBubble is PromptWindow) return; // freeze movement while a real question is pending

        switch (_mode)
        {
            case PetMode.FollowingCursor:
                if (DateTime.UtcNow >= _sessionUntil)
                {
                    _mode = PetMode.ReturningHome;
                }
                else
                {
                    var c = GetGlobalCursorPos();
                    StepToward(new Point(c.X - (Width / 2), c.Y - Height - 30));
                }
                break;

            case PetMode.Playing:
                if (DateTime.UtcNow >= _sessionUntil)
                {
                    EndPlay();
                    _mode = PetMode.ReturningHome;
                }
                else
                {
                    if (DateTime.UtcNow >= _nextToyMove)
                    {
                        MoveToyRandomly();
                        _nextToyMove = DateTime.UtcNow.AddSeconds(_rng.Next(1, 3));
                    }
                    StepToward(new Point(_toyPos.X - (Width / 2) + 12, _toyPos.Y - (Height / 2) + 12));
                }
                break;

            case PetMode.ReturningHome:
                StepToward(_restPosition, arrivalMode: PetMode.Idle);
                break;

            case PetMode.Idle:
                MaybeSpontaneousWander();
                MaybeShowMood();
                break;
        }
    }

    private void StepToward(Point target, PetMode? arrivalMode = null)
    {
        double dx = target.X - Left;
        double dy = target.Y - Top;
        double dist = Math.Sqrt((dx * dx) + (dy * dy));
        UpdateFacing(dx, dy, dist);

        if (dist < 2)
        {
            Left = target.X;
            Top = target.Y;
            if (arrivalMode.HasValue) _mode = arrivalMode.Value;
            return;
        }

        double step = Math.Min(4.0, dist);
        Left += (dx / dist) * step;
        Top += (dy / dist) * step;
    }

    private void UpdateFacing(double dx, double dy, double dist)
    {
        if (dist < 3) return;
        string next = Math.Abs(dx) > Math.Abs(dy)
            ? (dx > 0 ? "east" : "west")
            : (dy > 0 ? "south" : "north");
        if (next != _facing)
        {
            _facing = next;
            PetImage.Source = _sprites[_facing];
        }
    }

    private void AnimateIdleBobAndSquish()
    {
        bool alert = DateTime.UtcNow < _alertUntil;
        double amplitude = alert ? 8 : 4;
        double speed = alert ? 0.35 : 0.12;

        _phase += speed;
        BodyBob.Y = amplitude * Math.Sin(_phase);

        double scale = alert ? 1.0 + (0.04 * Math.Abs(Math.Sin(_phase * 2))) : 1.0;
        BodyScale.ScaleX = scale;
        BodyScale.ScaleY = scale;
    }

    private void Bounce() => _alertUntil = DateTime.UtcNow.AddMilliseconds(900);

    // ---- follow / lure / play ----

    private void StartFollowCursor(TimeSpan duration)
    {
        _mode = PetMode.FollowingCursor;
        _sessionUntil = DateTime.UtcNow.Add(duration);
    }

    private void MaybeSpontaneousWander()
    {
        if (DateTime.UtcNow < _nextWanderCheck) return;
        _nextWanderCheck = DateTime.UtcNow.AddSeconds(_rng.Next(25, 50));
        if (_rng.NextDouble() < 0.5)
        {
            StartFollowCursor(TimeSpan.FromSeconds(_rng.Next(4, 8)));
        }
    }

    private void StartPlay(string label, string emoji)
    {
        _mode = PetMode.Playing;
        _sessionUntil = DateTime.UtcNow.AddSeconds(15);
        _nextToyMove = DateTime.UtcNow;

        var working = Forms.Screen.PrimaryScreen!.WorkingArea;
        _toyPos = new Point(
            _rng.Next(working.Left + 80, working.Right - 80),
            _rng.Next(working.Top + 80, working.Bottom - 80));

        _toyMarker?.Close();
        _toyMarker = new ToyMarker(emoji, _toyPos);
        _toyMarker.Show();

        TryShowCasualBubble($"Ooh, {label}! 🎉", TimeSpan.FromSeconds(2.5));
    }

    private void MoveToyRandomly()
    {
        var working = Forms.Screen.PrimaryScreen!.WorkingArea;
        _toyPos = new Point(
            _rng.Next(working.Left + 80, working.Right - 80),
            _rng.Next(working.Top + 80, working.Bottom - 80));
        _toyMarker?.MoveTo(_toyPos);
    }

    private void EndPlay()
    {
        _toyMarker?.Close();
        _toyMarker = null;
    }

    // ---- mood bubbles / prompt bubbles (shared ownership, prompt always wins) ----

    private void MaybeShowMood()
    {
        if (DateTime.UtcNow < _nextMoodCheck) return;
        _nextMoodCheck = DateTime.UtcNow.AddSeconds(_rng.Next(90, 180));
        if (_activeBubble != null) return;
        TryShowCasualBubble(Moods[_rng.Next(Moods.Length)], TimeSpan.FromSeconds(4));
    }

    private Point GetPetAnchorPoint() => new(Left + (Width / 2), Top + BodyBob.Y);

    private void TryShowCasualBubble(string text, TimeSpan duration)
    {
        if (_activeBubble is PromptWindow) return;
        if (_activeBubble is InfoBubble existing) existing.Close();

        var anchor = new Rect(Left, Top, Width, Height);
        var bubble = new InfoBubble(text, anchor, duration, GetPetAnchorPoint);
        bubble.Closed += (_, _) =>
        {
            if (ReferenceEquals(_activeBubble, bubble)) _activeBubble = null;
        };
        _activeBubble = bubble;
        bubble.Show();
    }

    public async Task<PromptResponse> ShowPromptAsync(PromptRequest request)
    {
        PromptWindow? prompt = null;

        await Dispatcher.InvokeAsync(() =>
        {
            _activeBubble?.Close();
            Bounce();

            prompt = new PromptWindow(request, GetPetAnchorPoint);
            prompt.Closed += (_, _) =>
            {
                if (ReferenceEquals(_activeBubble, prompt)) _activeBubble = null;
            };
            _activeBubble = prompt;
            prompt.Show();
            prompt.Activate();
        });

        return await prompt!.ResultTask;
    }

    // ---- pat detection (local mouse move over the pet, no P/Invoke needed) ----

    private void OnPetMouseMove(object sender, MouseEventArgs e)
    {
        var pos = e.GetPosition(this);
        var now = DateTime.UtcNow;
        _patSamples.Add((now, pos.X));
        while (_patSamples.Count > 0 && (now - _patSamples[0].Time) > TimeSpan.FromSeconds(1.2))
        {
            _patSamples.RemoveAt(0);
        }

        if (now - _lastPatTrigger < TimeSpan.FromSeconds(2)) return;

        if (CountReversals(_patSamples, minDelta: 6) >= 3)
        {
            _lastPatTrigger = now;
            _patSamples.Clear();
            TriggerPatReaction();
        }
    }

    private static int CountReversals(List<(DateTime Time, double X)> samples, double minDelta)
    {
        int reversals = 0;
        int sign = 0;
        if (samples.Count == 0) return 0;
        double lastX = samples[0].X;
        for (int i = 1; i < samples.Count; i++)
        {
            double delta = samples[i].X - lastX;
            if (Math.Abs(delta) < minDelta) continue;
            int s = Math.Sign(delta);
            if (sign != 0 && s != sign) reversals++;
            sign = s;
            lastX = samples[i].X;
        }
        return reversals;
    }

    private void TriggerPatReaction()
    {
        Bounce();
        TryShowCasualBubble("💕", TimeSpan.FromSeconds(1.8));
    }

    // ---- global cursor position (only needed for follow-pointer) ----

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out Win32Point lpPoint);

    [StructLayout(LayoutKind.Sequential)]
    private struct Win32Point
    {
        public int X;
        public int Y;
    }

    private static Point GetGlobalCursorPos()
    {
        GetCursorPos(out var p);
        return new Point(p.X, p.Y);
    }

    // ---- radial menu (right-click on Claudy) ----

    private void ShowRadialMenu()
    {
        var center = new Point(Left + (Width / 2), Top + (Height / 2));
        var items = new (string Emoji, string Tooltip, Action OnSelect)[]
        {
            ("🎾", "Ball", () => StartPlay("Ball", "🎾")),
            ("🧶", "Yarn", () => StartPlay("Yarn", "🧶")),
            ("✨", "Wand", () => StartPlay("Wand", "✨")),
            ("👋", "Call Claudy", () => StartFollowCursor(TimeSpan.FromSeconds(10))),
        };

        var menu = new RadialMenu(center, items);
        menu.Show();
    }

    // ---- tray icon ----

    private void CreateTrayIcon()
    {
        _trayIcon = new Forms.NotifyIcon
        {
            Icon = BuildIcon(),
            Visible = true,
            Text = "Claudy",
        };

        var menu = new Forms.ContextMenuStrip();

        var playMenu = new Forms.ToolStripMenuItem("Play");
        foreach (var (label, emoji) in Toys)
        {
            playMenu.DropDownItems.Add($"{emoji} {label}", null, (_, _) => Dispatcher.Invoke(() => StartPlay(label, emoji)));
        }
        menu.Items.Add(playMenu);

        menu.Items.Add("Call Claudy", null, (_, _) => Dispatcher.Invoke(() => StartFollowCursor(TimeSpan.FromSeconds(10))));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => System.Windows.Application.Current.Shutdown());

        _trayIcon.ContextMenuStrip = menu;
    }

    private static System.Drawing.Icon BuildIcon()
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(System.Drawing.Color.Transparent);
            using var brush = new SolidBrush(System.Drawing.Color.FromArgb(255, 124, 156, 255));
            g.FillEllipse(brush, 2, 4, 28, 24);
        }
        return System.Drawing.Icon.FromHandle(bmp.GetHicon());
    }
}
