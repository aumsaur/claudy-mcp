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
    Catching,
    Eating,
    ReturningHome,
    Socializing,
}

public partial class MainWindow : Window
{
    // Mood is optional (null = just show the bubble, no expression sprite change) -
    // only some moods map cleanly onto a "happy"/"scared" bucket right now.
    private static readonly (string Text, string? Mood)[] Moods =
    {
        ("Just vibing 😊", "happy"),
        ("Kinda sleepy... 😴", null),
        ("Bored, entertain me? 🥱", null),
        ("Feeling bouncy today! 🤩", "happy"),
        ("*stares at the cursor* 👀", null),
        ("Snack time? 🍪", "happy"),
        ("Booping around ✨", "happy"),
    };

    private static readonly (string Label, string Emoji)[] Toys =
    {
        ("Ball", "🎾"),
        ("Yarn", "🧶"),
        ("Wand", "✨"),
    };

    private readonly DispatcherTimer _tickTimer;
    private readonly PipeServer _pipeServer;
    private SkinDef _baseSkin = null!; // set in LoadSprites(), before which ApplySprite() no-ops
    private SkinDef _currentSkin = null!;
    private readonly Dictionary<string, SkinDef> _skins = new(); // "Base" + anything found under skins/
    private readonly List<(DateTime Time, double X)> _patSamples = new();
    private readonly Random _rng = new();

    // Expression overlay on top of the plain directional sprite: talking (driven
    // directly by whether a real PromptWindow is open) always wins, then a timed
    // mood expression, then the normal facing sprite.
    private string? _expressionMood;
    private DateTime _expressionUntil = DateTime.MinValue;
    private int _talkFrameIndex;
    private DateTime _nextTalkFrameFlip = DateTime.MinValue;

    private const double TickSeconds = 0.04;

    private Forms.NotifyIcon? _trayIcon;
    private Window? _activeBubble;
    private ToyMarker? _toyMarker;
    private Point _toyPos;

    private ToyMarker? _ballMarker;
    private Vector _ballVelocity;

    private ToyMarker? _foodMarker;
    private Point _foodPos;
    private bool _reachedFood;
    private DateTime _eatingUntil = DateTime.MinValue;

    private PlacementOverlay? _placementOverlay;

    private string _displayName;
    private readonly string _defaultName;
    private RenameWindow? _renameWindow;
    private readonly int _parentPid;
    private readonly string _sessionCwd;
    private DateTime _nextParentCheck = DateTime.MinValue;
    private Nameplate? _nameplate;
    private List<PetRegistry.SiblingInfo> _siblingPositions = new();
    private DateTime _nextRegistryPublish = DateTime.MinValue;
    private DateTime _nextRegistryRead = DateTime.MinValue;

    private enum SocialKind { HangOut, Prank, Poke }
    private SocialKind _socialKind;
    private int _socialTargetPid;
    private string _socialTargetName = "";
    private bool _socialGreeted;
    private DateTime _nextSocialCheck = DateTime.MinValue;

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

    public MainWindow(string pipeName, string displayName, int parentPid, string sessionCwd)
    {
        InitializeComponent();

        _defaultName = displayName;
        // Before CreateTrayIcon/the nameplate read it: a name the user set by hand in
        // an earlier session for this same folder wins over the folder-derived default.
        _displayName = PetNames.Load(sessionCwd) ?? displayName;
        _parentPid = parentPid;
        _sessionCwd = sessionCwd;

        Loaded += (_, _) =>
        {
            PositionBottomRight();
            _restPosition = new Point(Left, Top);
            LoadSprites();
            _nameplate = new Nameplate(_displayName, GetNameplateAnchorPoint());
            _nameplate.Show();
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

        _pipeServer = new PipeServer(pipeName, ShowPromptAsync);
        _pipeServer.Start();

        Closed += (_, _) =>
        {
            _pipeServer.Stop();
            _trayIcon?.Dispose();
            _toyMarker?.Close();
            _ballMarker?.Close();
            _foodMarker?.Close();
            _placementOverlay?.Close();
            _renameWindow?.Close();
            _nameplate?.Close();
            PetRegistry.Unpublish();
        };
    }

    // ---- setup ----

    private void PositionBottomRight()
    {
        // SystemParameters.WorkArea, not Forms.Screen.WorkingArea: the latter is in
        // physical pixels, which on a scaled display lands every window far past the
        // corner and off-screen. WPF's Left/Top/Width are device-independent units.
        var working = SystemParameters.WorkArea;

        // Fan concurrent instances out diagonally from the corner instead of stacking
        // them on top of each other. Ranked by process start order (not a raw "how many
        // others are running right now" count) so two pets launched back-to-back don't
        // both see "1 other instance" and land on the identical offset.
        var slot = GetInstanceSlot();
        const double stagger = 130;
        double offsetX = (slot % 5) * stagger;
        double offsetY = (slot / 5) * stagger;

        Left = working.Right - Width - 24 - offsetX;
        Top = working.Bottom - Height - 24 - offsetY;
    }

    private static int GetInstanceSlot()
    {
        try
        {
            var current = System.Diagnostics.Process.GetCurrentProcess();
            var all = System.Diagnostics.Process.GetProcessesByName(current.ProcessName);

            var ranked = new List<(int Id, DateTime Start)>();
            foreach (var p in all)
            {
                DateTime start;
                try { start = p.StartTime; } catch { start = DateTime.MaxValue; }
                ranked.Add((p.Id, start));
            }
            ranked.Sort((a, b) =>
            {
                var cmp = a.Start.CompareTo(b.Start);
                return cmp != 0 ? cmp : a.Id.CompareTo(b.Id);
            });

            return Math.Max(0, ranked.FindIndex(p => p.Id == current.Id));
        }
        catch
        {
            return 0;
        }
    }

    private void LoadSprites()
    {
        // Every skin, including the default look, lives under Assets/claudy/skins/
        // with the identical layout (south/east/west/north required, mood_*/talk_*
        // optional) - no special-casing a "base" skin against everything else, the
        // only asymmetry is which folder name maps to the display name "Base" and
        // becomes _baseSkin (the fallback source for other skins' missing mood/talk).
        var skinsRoot = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "claudy", "skins");
        foreach (var skinDir in System.IO.Directory.GetDirectories(skinsRoot))
        {
            var folderName = System.IO.Path.GetFileName(skinDir);
            if (!System.IO.File.Exists(System.IO.Path.Combine(skinDir, "south.png"))) continue; // not a real skin folder

            var isBase = folderName.Equals("base", StringComparison.OrdinalIgnoreCase);
            var skin = LoadSkin(isBase ? "Base" : folderName, skinDir);
            _skins[skin.Name] = skin;
            if (isBase) _baseSkin = skin;
        }

        _currentSkin = _baseSkin;
        PetImage.Source = _currentSkin.Sprites["south"];
    }

    // Loads one skin's sprites from a directory - every skin (the base look and
    // anything under skins/) goes through this exact same loader, no special-casing.
    private static SkinDef LoadSkin(string name, string dir)
    {
        var sprites = new Dictionary<string, BitmapImage>();
        foreach (var direction in new[] { "south", "east", "west", "north" })
        {
            var path = System.IO.Path.Combine(dir, direction + ".png");
            if (System.IO.File.Exists(path)) sprites[direction] = LoadBitmap(path);
        }

        Dictionary<string, BitmapImage>? moodSprites = null;
        foreach (var mood in new[] { "happy", "scared" })
        {
            var path = System.IO.Path.Combine(dir, $"mood_{mood}.png");
            if (!System.IO.File.Exists(path)) continue;
            moodSprites ??= new Dictionary<string, BitmapImage>();
            moodSprites[mood] = LoadBitmap(path);
        }

        // Numbered sequence (talk_0.png, talk_1.png, ...) generated together as one
        // animation via PixelLab's animate-character, so the eyes/body stay identical
        // frame to frame and only the mouth moves - stops as soon as a number is missing.
        List<BitmapImage>? talkFrames = null;
        for (var i = 0; ; i++)
        {
            var path = System.IO.Path.Combine(dir, $"talk_{i}.png");
            if (!System.IO.File.Exists(path)) break;
            talkFrames ??= new List<BitmapImage>();
            talkFrames.Add(LoadBitmap(path));
        }

        return new SkinDef { Name = name, Dir = dir, Sprites = sprites, MoodSprites = moodSprites, TalkFrames = talkFrames };
    }

    private static BitmapImage LoadBitmap(string path)
    {
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.UriSource = new Uri(path, UriKind.Absolute);
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }

    // ---- main loop ----

    private void Tick()
    {
        AnimateIdleBobAndSquish();
        _nameplate?.MoveTo(GetNameplateAnchorPoint());
        UpdatePetRegistry();
        CheckParentAlive();

        if (_activeBubble is PromptWindow) return; // freeze movement while a real question is pending

        // Skip while actively socializing - the approach logic already stops at a
        // deliberate close distance, and separation would just fight it the whole way.
        if (_mode != PetMode.Socializing) ApplySeparation();

        CheckSocialInbox();

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
                    // Stop as soon as it actually reaches the cursor instead of
                    // camping there for the rest of the session timer - if the
                    // cursor moves again before that, this recomputes next tick
                    // same as before.
                    StepToward(new Point(c.X - (Width / 2), c.Y - Height - 30), arrivalMode: PetMode.Idle);
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

            case PetMode.Catching:
                TickBallPhysics();
                break;

            case PetMode.Eating:
                TickEating();
                break;

            case PetMode.ReturningHome:
                StepToward(_restPosition, arrivalMode: PetMode.Idle);
                break;

            case PetMode.Socializing:
                TickSocial();
                break;

            case PetMode.Idle:
                MaybeSpontaneousWander();
                MaybeShowMood();
                MaybeInitiateSocial();
                break;
        }
    }

    private string GetStatus() => _mode == PetMode.Idle && _activeBubble is not PromptWindow ? "idle" : "busy";

    private void UpdatePetRegistry()
    {
        var now = DateTime.UtcNow;
        if (now >= _nextRegistryPublish)
        {
            PetRegistry.Publish(new Point(Left + (Width / 2), Top + (Height / 2)), _displayName, GetStatus());
            _nextRegistryPublish = now.AddMilliseconds(250);
        }
        if (now >= _nextRegistryRead)
        {
            _siblingPositions = PetRegistry.ReadOthers();
            _nextRegistryRead = now.AddMilliseconds(300);
        }
    }

    // Node spawns the pet detached so it survives between individual ask_user
    // calls within a session - but that also means nothing kills it when the
    // whole Claude Code session ends normally (closing the terminal, etc.)
    // without going through the radial menu's Close item. Self-close instead of
    // lingering as an orphan once the parent process that launched us is gone.
    private void CheckParentAlive()
    {
        if (_parentPid <= 0) return; // launched without tracking (manual/legacy launch)

        var now = DateTime.UtcNow;
        if (now < _nextParentCheck) return;
        _nextParentCheck = now.AddSeconds(5);

        try
        {
            var proc = System.Diagnostics.Process.GetProcessById(_parentPid);
            // Also guard against the OS having recycled the pid onto an unrelated
            // process once the real parent (always "node") has exited.
            if (proc.HasExited || !string.Equals(proc.ProcessName, "node", StringComparison.OrdinalIgnoreCase))
            {
                System.Windows.Application.Current.Shutdown();
            }
        }
        catch (ArgumentException)
        {
            // no process with that id exists anymore
            System.Windows.Application.Current.Shutdown();
        }
    }

    // Gently steers this pet away from any sibling Claudy (a different process,
    // read from the shared position registry) that's crowding its space, so
    // multiple concurrent instances drift apart instead of sitting on top of
    // each other while wandering, following the cursor, etc.
    private void ApplySeparation()
    {
        if (_siblingPositions.Count == 0) return;

        var myCenter = new Point(Left + (Width / 2), Top + (Height / 2));
        double pushX = 0, pushY = 0;
        const double minDist = 150;

        foreach (var other in _siblingPositions)
        {
            double dx = myCenter.X - other.X;
            double dy = myCenter.Y - other.Y;
            double dist = Math.Sqrt((dx * dx) + (dy * dy));
            if (dist < 1)
            {
                dx = _rng.NextDouble() - 0.5;
                dy = _rng.NextDouble() - 0.5;
                dist = 1;
            }
            if (dist < minDist)
            {
                double strength = (minDist - dist) / minDist;
                pushX += (dx / dist) * strength;
                pushY += (dy / dist) * strength;
            }
        }

        if (pushX == 0 && pushY == 0) return;

        var working = SystemParameters.WorkArea;
        const double maxNudge = 3.0; // px/tick - gentle, doesn't fight normal movement
        Left = Math.Clamp(Left + (pushX * maxNudge), working.Left, working.Right - Width);
        Top = Math.Clamp(Top + (pushY * maxNudge), working.Top, working.Bottom - Height);
    }

    // ---- socializing (prank / hang out with other Claudy instances) ----

    private void MaybeInitiateSocial()
    {
        if (DateTime.UtcNow < _nextSocialCheck) return;

        var candidates = new List<PetRegistry.SiblingInfo>();
        foreach (var s in _siblingPositions)
        {
            if (s.Status == "idle") candidates.Add(s);
        }

        if (candidates.Count == 0)
        {
            // No one to socialize with right now (or a freshly-launched sibling's
            // registry entry hasn't shown up yet) - retry soon instead of burning
            // the long cooldown on a check that never had a real chance.
            _nextSocialCheck = DateTime.UtcNow.AddSeconds(_rng.Next(5, 12));
            return;
        }

        _nextSocialCheck = DateTime.UtcNow.AddSeconds(_rng.Next(50, 110));
        if (_rng.NextDouble() > 0.4) return; // don't socialize every single eligible window

        var target = candidates[_rng.Next(candidates.Count)];
        _socialTargetPid = target.Pid;
        _socialTargetName = string.IsNullOrWhiteSpace(target.Name) ? "a friend" : target.Name;
        _socialKind = _rng.Next(3) switch
        {
            0 => SocialKind.Prank,
            1 => SocialKind.Poke,
            _ => SocialKind.HangOut,
        };
        _socialGreeted = false;
        _sessionUntil = DateTime.UtcNow.AddSeconds(_socialKind switch
        {
            SocialKind.HangOut => _rng.Next(15, 30),
            SocialKind.Poke => 4,
            _ => 6, // Prank
        });
        _mode = PetMode.Socializing;
    }

    private void TickSocial()
    {
        PetRegistry.SiblingInfo? target = null;
        foreach (var s in _siblingPositions)
        {
            if (s.Pid == _socialTargetPid) { target = s; break; }
        }
        if (target is null)
        {
            // sibling closed / went out of view mid-approach - give up gracefully
            _mode = PetMode.ReturningHome;
            return;
        }

        const double approachDist = 120;
        var myCenter = new Point(Left + (Width / 2), Top + (Height / 2));
        double dx = target.X - myCenter.X;
        double dy = target.Y - myCenter.Y;
        double dist = Math.Sqrt((dx * dx) + (dy * dy));
        if (dist < 1) dist = 1;

        if (dist > approachDist + 4)
        {
            double ratio = (dist - approachDist) / dist;
            var standoff = new Point(myCenter.X + (dx * ratio), myCenter.Y + (dy * ratio));
            StepToward(new Point(standoff.X - (Width / 2), standoff.Y - (Height / 2)));
            return;
        }

        UpdateFacing(dx, dy, dist);

        if (!_socialGreeted)
        {
            _socialGreeted = true;
            switch (_socialKind)
            {
                case SocialKind.Prank:
                    Bounce();
                    TryShowCasualBubble($"Boo, {_socialTargetName}! 😈", TimeSpan.FromSeconds(2.5), "happy");
                    PetRegistry.SendEvent(_socialTargetPid, "prank");
                    break;

                case SocialKind.Poke:
                    var hand = new GhostHand(new Point(target.X, target.Y - 24));
                    hand.Show();
                    TryShowCasualBubble($"*pokes {_socialTargetName}* 👉", TimeSpan.FromSeconds(2), "happy");
                    PetRegistry.SendEvent(_socialTargetPid, "poke");
                    break;

                default: // HangOut
                    TryShowCasualBubble($"Hey {_socialTargetName}, mind if I hang out? 👋", TimeSpan.FromSeconds(3), "happy");
                    break;
            }
        }

        if (DateTime.UtcNow >= _sessionUntil)
        {
            _mode = PetMode.ReturningHome;
        }
    }

    private void CheckSocialInbox()
    {
        var evt = PetRegistry.ReadAndClearEvent();
        if (evt is null) return;

        switch (evt.Type)
        {
            case "prank":
                Bounce();
                TryShowCasualBubble("Hey!! You got me! 😆", TimeSpan.FromSeconds(2.5), "scared");
                break;

            case "poke":
                Bounce();
                TryShowCasualBubble("💕", TimeSpan.FromSeconds(1.8), "happy");
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
        }
        ApplySprite();
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

        ApplySprite();
    }

    // Talking (real ask_user conversation) beats a timed mood expression, which
    // beats the plain directional idle sprite. Only one active source of truth
    // (this method) ever assigns PetImage.Source. Resolves through _currentSkin,
    // falling back to _baseSkin for anything the current skin doesn't define its
    // own version of (expected for the first pass - a skin only needs its own
    // south/east/west/north, mood/talk sprites are optional per skin).
    private void ApplySprite()
    {
        if (_baseSkin == null) return; // LoadSprites() hasn't run yet (pre-Loaded tick)

        var now = DateTime.UtcNow;
        var talkFrames = _currentSkin.TalkFrames ?? _baseSkin.TalkFrames;

        if (_activeBubble is PromptWindow && talkFrames is { Count: > 0 })
        {
            if (now >= _nextTalkFrameFlip)
            {
                _talkFrameIndex = (_talkFrameIndex + 1) % talkFrames.Count;
                _nextTalkFrameFlip = now.AddMilliseconds(180);
            }
            PetImage.Source = talkFrames[_talkFrameIndex];
            return;
        }

        if (_expressionMood != null)
        {
            var moodSprites = _currentSkin.MoodSprites ?? _baseSkin.MoodSprites;
            if (now < _expressionUntil && (moodSprites?.TryGetValue(_expressionMood, out var moodBmp) ?? false))
            {
                PetImage.Source = moodBmp;
                return;
            }
            _expressionMood = null;
        }

        var sprites = _currentSkin.Sprites.ContainsKey(_facing) ? _currentSkin.Sprites : _baseSkin.Sprites;
        PetImage.Source = sprites[_facing];
    }

    private void SetExpression(string mood, TimeSpan duration)
    {
        _expressionMood = mood;
        _expressionUntil = DateTime.UtcNow.Add(duration);
        // Expression art is only drawn front-facing, so turn to face the viewer
        // whenever one shows (petting, moods, interaction reactions) instead of
        // risking it appearing sideways mid-walk.
        _facing = "south";
        ApplySprite();
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
        EndAllToys();
        _mode = PetMode.Playing;
        _sessionUntil = DateTime.UtcNow.AddSeconds(15);
        _nextToyMove = DateTime.UtcNow;

        var working = SystemParameters.WorkArea;
        _toyPos = new Point(
            _rng.Next((int)working.Left + 80, (int)working.Right - 80),
            _rng.Next((int)working.Top + 80, (int)working.Bottom - 80));

        _toyMarker?.Close();
        _toyMarker = new ToyMarker(emoji, _toyPos);
        _toyMarker.Show();

        TryShowCasualBubble($"Ooh, {label}! 🎉", TimeSpan.FromSeconds(2.5), "happy");
    }

    private void MoveToyRandomly()
    {
        var working = SystemParameters.WorkArea;
        _toyPos = new Point(
            _rng.Next((int)working.Left + 80, (int)working.Right - 80),
            _rng.Next((int)working.Top + 80, (int)working.Bottom - 80));
        _toyMarker?.MoveTo(_toyPos);
    }

    private void EndPlay()
    {
        _toyMarker?.Close();
        _toyMarker = null;
    }

    // Cancels whatever toy interaction is currently active (any of: teleport-
    // chase toy, thrown/in-flight ball, food-eating, or a still-pending
    // placement click) before starting a new one - every toy-start path should
    // call this first so switching toys mid-interaction can't leave stray
    // markers/overlays or a stale PetMode behind.
    private void EndAllToys()
    {
        EndPlay();
        EndBallCatch();
        EndFoodEating();
        _placementOverlay?.Close();
        _placementOverlay = null;
        if (_mode is PetMode.Playing or PetMode.Catching or PetMode.Eating) _mode = PetMode.Idle;
    }

    // ---- throw-and-catch (ball) ----

    // Two steps: arm placement (next click anywhere places the ball there,
    // via PlacementOverlay), then the existing drag-to-slingshot interaction
    // on the placed ball takes over exactly as before.
    private void StartBallCatch()
    {
        EndAllToys();

        var ballSprite = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "toys", "ball.png");
        TryShowCasualBubble("Press anywhere and drag to throw! 🎾", TimeSpan.FromSeconds(4));

        _placementOverlay = new PlacementOverlay(ballSprite);
        _placementOverlay.Placed += PlaceBall;
        _placementOverlay.Cancelled += () => _placementOverlay = null;
        _placementOverlay.Show();
    }

    private void PlaceBall(Point point)
    {
        _placementOverlay = null;

        var ballSprite = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "toys", "ball.png");
        _ballMarker = new ToyMarker("🎾", point, ballSprite) { IsThrowable = true };
        _ballMarker.Thrown += OnBallThrown;
        _ballMarker.Show();

        // The placing click's button is still held, so roll straight into the
        // slingshot pull - one gesture (press anywhere, drag, release) does
        // place-aim-throw, rather than making the user click the ball again.
        _ballMarker.BeginDrag();

        TryShowCasualBubble("Now drag to pull back, and let go to throw! 🎾", TimeSpan.FromSeconds(4));
    }

    private void OnBallThrown(Vector displacement)
    {
        if (_ballMarker == null) return;

        // Slingshot: pull back, release forward — throw direction is opposite the drag.
        var velocity = displacement * -4.0;
        if (velocity.Length > 1800)
        {
            velocity *= 1800 / velocity.Length;
        }

        _ballVelocity = velocity;
        _mode = PetMode.Catching;
    }

    private void TickBallPhysics()
    {
        if (_ballMarker == null)
        {
            _mode = PetMode.Idle;
            return;
        }

        var working = SystemParameters.WorkArea;
        var pos = _ballMarker.CenterPoint;
        pos = new Point(pos.X + (_ballVelocity.X * TickSeconds), pos.Y + (_ballVelocity.Y * TickSeconds));
        _ballVelocity *= 0.94; // friction

        if (pos.X < working.Left + 22 || pos.X > working.Right - 22)
        {
            _ballVelocity.X = -_ballVelocity.X * 0.6;
            pos.X = Math.Clamp(pos.X, working.Left + 22, working.Right - 22);
        }
        if (pos.Y < working.Top + 22 || pos.Y > working.Bottom - 22)
        {
            _ballVelocity.Y = -_ballVelocity.Y * 0.6;
            pos.Y = Math.Clamp(pos.Y, working.Top + 22, working.Bottom - 22);
        }

        _ballMarker.MoveTo(pos);
        StepToward(new Point(pos.X - (Width / 2), pos.Y - (Height / 2)));

        double dx = (Left + (Width / 2)) - pos.X;
        double dy = (Top + (Height / 2)) - pos.Y;
        bool closeEnough = Math.Sqrt((dx * dx) + (dy * dy)) < 55;
        bool slowEnough = _ballVelocity.Length < 25;
        if (closeEnough && slowEnough)
        {
            CatchBall();
        }
    }

    private void CatchBall()
    {
        _mode = PetMode.Idle;
        Bounce();
        TryShowCasualBubble("Got it! 🎾", TimeSpan.FromSeconds(2), "happy");
        EndBallCatch();
    }

    private void EndBallCatch()
    {
        _ballMarker?.Close();
        _ballMarker = null;
        _ballVelocity = new Vector(0, 0);
    }

    // ---- food (click-to-place, walk over, then sit and eat for a bit) ----

    // Same placement step as the ball, but no drag/throw afterward - once
    // placed the food just sits there as a walk-to target.
    private void ArmFoodPlacement()
    {
        EndAllToys();

        var foodSprite = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "toys", "food.png");
        TryShowCasualBubble("Click anywhere to place food! 🍪", TimeSpan.FromSeconds(4));

        _placementOverlay = new PlacementOverlay(foodSprite);
        _placementOverlay.Placed += PlaceFood;
        _placementOverlay.Cancelled += () => _placementOverlay = null;
        _placementOverlay.Show();
    }

    private void PlaceFood(Point point)
    {
        _placementOverlay = null;

        var foodSprite = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "toys", "food.png");
        _foodMarker = new ToyMarker("🍪", point, foodSprite);
        _foodMarker.Show();
        // Food should always read as sitting under the pet (not just once eating
        // starts) - set this once, deterministically, right away rather than
        // reacting at arrival time.
        PlaceWindowBehind(_foodMarker, this);

        _foodPos = point;
        _reachedFood = false;
        _mode = PetMode.Eating;
    }

    private void TickEating()
    {
        if (_foodMarker == null)
        {
            _mode = PetMode.Idle;
            return;
        }

        if (!_reachedFood)
        {
            var target = new Point(_foodPos.X - (Width / 2), _foodPos.Y - (Height / 2));
            StepToward(target);

            double dx = target.X - Left;
            double dy = target.Y - Top;
            if (Math.Sqrt((dx * dx) + (dy * dy)) < 4)
            {
                _reachedFood = true;
                _eatingUntil = DateTime.UtcNow.AddSeconds(3.5);

                // Back to the viewer, hunched over the food - expression art is
                // always front-facing so this skips SetExpression entirely and
                // just points the plain idle sprite north (z-order already
                // handled once in PlaceFood, via PlaceWindowBehind).
                _facing = "north";
                ApplySprite();

                TryShowCasualBubble("nom nom 🍪", TimeSpan.FromSeconds(3.5));
            }
        }
        else if (DateTime.UtcNow >= _eatingUntil)
        {
            EndFoodEating();
            _mode = PetMode.ReturningHome;
        }
    }

    private void EndFoodEating()
    {
        _foodMarker?.Close();
        _foodMarker = null;
    }

    // ---- mood bubbles / prompt bubbles (shared ownership, prompt always wins) ----

    private void MaybeShowMood()
    {
        if (DateTime.UtcNow < _nextMoodCheck) return;
        _nextMoodCheck = DateTime.UtcNow.AddSeconds(_rng.Next(90, 180));
        if (_activeBubble != null) return;
        var (text, mood) = Moods[_rng.Next(Moods.Length)];
        TryShowCasualBubble(text, TimeSpan.FromSeconds(4), mood);
    }

    private Point GetPetAnchorPoint() => new(Left + (Width / 2), Top + BodyBob.Y);

    // Below the sprite's feet, not above the head - the badge's top edge sits here.
    private Point GetNameplateAnchorPoint() => new(Left + (Width / 2), Top + Height - 10);

    private void TryShowCasualBubble(string text, TimeSpan duration, string? mood = null)
    {
        if (_activeBubble is PromptWindow) return;
        if (_renameWindow != null) return; // don't cover the box being typed in
        if (_activeBubble is InfoBubble existing) existing.Close();

        if (mood != null) SetExpression(mood, duration + TimeSpan.FromMilliseconds(300));

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
            _renameWindow?.Close();
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
        TryShowCasualBubble("💕", TimeSpan.FromSeconds(1.8), "happy");
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

    private Point GetGlobalCursorPos()
    {
        GetCursorPos(out var p);
        // GetCursorPos is in physical pixels; the caller offsets this by Width/Height
        // and hands it to StepToward, which sets Left/Top - all device-independent
        // units. Without the conversion the pet aims past the cursor by the DPI factor.
        return DpiUtil.PhysicalToDiu(this, new Point(p.X, p.Y));
    }

    // ---- explicit window z-order (food needs to sit visually behind the pet) ----

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoActivate = 0x0010;

    // Window.Activate() (tried first) only asks the OS to bring a window to the
    // front of the shared Topmost band - it's a request, not a guarantee, and in
    // practice something shown afterward (e.g. the "nom nom" bubble) could still
    // end up back in front of it. SetWindowPos's hWndInsertAfter is an explicit,
    // deterministic ordering command instead: place `window` immediately behind
    // `reference` and leave it there.
    private static void PlaceWindowBehind(Window window, Window reference)
    {
        var target = new System.Windows.Interop.WindowInteropHelper(window).Handle;
        var behind = new System.Windows.Interop.WindowInteropHelper(reference).Handle;
        if (target == IntPtr.Zero || behind == IntPtr.Zero) return;
        SetWindowPos(target, behind, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
    }

    // ---- radial menu (right-click on Claudy) ----

    private void ShowRadialMenu()
    {
        var center = new Point(Left + (Width / 2), Top + (Height / 2));
        var menuIconsDir = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "icons", "menu");
        var ballSprite = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "toys", "ball.png");
        var foodSprite = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "toys", "food.png");

        var items = new List<RadialItem>
        {
            new()
            {
                SpritePath = System.IO.Path.Combine(menuIconsDir, "follow.png"),
                Tooltip = "Follow",
                OnSelect = () => StartFollowCursor(TimeSpan.FromSeconds(10)),
            },
            new()
            {
                SpritePath = ballSprite,
                Tooltip = "Toy",
                Children = new List<RadialItem>
                {
                    new() { SpritePath = ballSprite, Tooltip = "Ball", OnSelect = () => StartBallCatch() },
                    new() { SpritePath = foodSprite, Tooltip = "Food", OnSelect = () => ArmFoodPlacement() },
                },
            },
            new()
            {
                SpritePath = System.IO.Path.Combine(menuIconsDir, "prompt.png"),
                Tooltip = "Prompt",
                OnSelect = ActivatePromptFreeText,
            },
            new()
            {
                SpritePath = System.IO.Path.Combine(menuIconsDir, "clothing.png"),
                Tooltip = "Clothing",
                Children = BuildSkinMenuItems(),
            },
            new()
            {
                SpritePath = System.IO.Path.Combine(menuIconsDir, "nametag.png"),
                Tooltip = "Rename",
                OnSelect = ShowRenameDialog,
            },
            new()
            {
                SpritePath = System.IO.Path.Combine(menuIconsDir, "close.png"),
                Tooltip = "Close",
                OnSelect = () => System.Windows.Application.Current.Shutdown(),
            },
        };

        var menu = new RadialMenu(center, items);
        menu.Show();
    }

    // Each skin's own south sprite doubles as its submenu icon - same pattern as
    // the Toy submenu using ball.png as both the category icon and the Ball item.
    private List<RadialItem> BuildSkinMenuItems()
    {
        var items = new List<RadialItem>();
        foreach (var skin in _skins.Values)
        {
            items.Add(new RadialItem
            {
                SpritePath = System.IO.Path.Combine(skin.Dir, "south.png"),
                Tooltip = skin.Name,
                OnSelect = () => SelectSkin(skin.Name),
            });
        }
        return items;
    }

    // ---- name tag ----

    private void ShowRenameDialog()
    {
        if (_renameWindow != null)
        {
            _renameWindow.Activate();
            return;
        }

        var window = new RenameWindow(_displayName, _defaultName, GetPetAnchorPoint, ApplyRename);
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_renameWindow, window)) _renameWindow = null;
        };
        _renameWindow = window;
        window.Show();
        window.Activate();
    }

    // An empty box means "drop my custom name", so the override is removed and the
    // folder-derived default comes back rather than the badge going blank.
    private void ApplyRename(string typed)
    {
        var name = string.IsNullOrWhiteSpace(typed) ? _defaultName : typed;
        if (string.Equals(name, _displayName, StringComparison.Ordinal)) return;

        if (string.IsNullOrWhiteSpace(typed)) PetNames.Clear(_sessionCwd);
        else PetNames.Save(_sessionCwd, name);

        ApplyDisplayName(name);
        TryShowCasualBubble($"I'm {name} now! ✨", TimeSpan.FromSeconds(3), "happy");
    }

    // Everything the name feeds except the registry, which siblings re-read from the
    // next publish (250ms away) on its own.
    private void ApplyDisplayName(string name)
    {
        _displayName = name;
        _nameplate?.SetName(name);
        if (_trayIcon != null) _trayIcon.Text = BuildTrayText();
    }

    private string BuildTrayText()
    {
        var text = $"Claudy — {_displayName}";
        return text.Length > 63 ? text[..63] : text;
    }

    private void SelectSkin(string name)
    {
        if (!_skins.TryGetValue(name, out var skin)) return;
        _currentSkin = skin;
        ApplySprite();
    }

    // There's no live channel for the pet to push a message into an idle Claude
    // Code session (the pipe only exists while Claude is already blocked on an
    // open ask_user call), so this only overrides that open question - typing
    // there answers it directly, same conversation, no new process involved.
    //
    // The idle-case ("nothing's pending, message Claude anyway") had a real
    // implementation - QueueMessageWindow + SpawnClaudeWithMessage below, which
    // spawns `claude -p` in its own terminal - but it's disabled per explicit user
    // request: every use is a disconnected new session (not this conversation)
    // AND leaves behind an open terminal window with no cleanup, and none of the
    // considered fixes (auto-close terminal / queue-file-only / /loop polling)
    // felt right yet. Left the working code in place rather than deleting it -
    // reconnect by restoring the else-branch that called QueueMessageWindow if
    // this gets revisited. See project memory for the full tradeoff writeup.
    private void ActivatePromptFreeText()
    {
        if (_activeBubble is PromptWindow prompt)
        {
            prompt.ShowFreeTextOverride();
            prompt.Activate();
            return;
        }

        TryShowCasualBubble("Nothing's asking right now! 👀", TimeSpan.FromSeconds(2.5));
    }

    // Fires the message off immediately as its own `claude -p` run in a visible
    // terminal, rather than just queuing it for whenever Claude next happens to be
    // invoked some other way - that's what actually satisfies "message it like chat"
    // instead of silently waiting on a listener. Falls back to the file queue
    // (checked via CLAUDE.md's inbox instruction) only if the spawn itself fails.
    private void QueueMessageForClaude(string message)
    {
        try
        {
            SpawnClaudeWithMessage(message);
            TryShowCasualBubble("On it! Opening a terminal now... ✨", TimeSpan.FromSeconds(3), "happy");
        }
        catch
        {
            QueueMessageToInboxFile(message);
        }
    }

    private void SpawnClaudeWithMessage(string message)
    {
        // The message is never embedded as literal text in the generated script - it's
        // written to its own temp file and read back via Get-Content, so arbitrary
        // typed content (quotes, semicolons, backticks, $(...) etc.) can't affect the
        // script. A real .ps1 file (not an inline -Command string) so it can log its
        // own diagnostics - a debug log next to it is what let this get root-caused
        // after the first version silently failed for the user with no visible cause.
        var msgFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"claudy-msg-{Guid.NewGuid():N}.txt");
        System.IO.File.WriteAllText(msgFile, message);

        var scriptFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"claudy-run-{Guid.NewGuid():N}.ps1");
        var logFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "claudy-spawn-debug.log");

        var script = string.Join("\n", new[]
        {
            $"\"=== spawn $(Get-Date -Format o) ===\" | Out-File -FilePath \"{logFile}\" -Append -Encoding utf8",
            "Write-Host \"Sending your message to Claude...\"",
            "try {",
            "    $claudeCmd = Get-Command claude -ErrorAction Stop",
            $"    \"claude resolved to: $($claudeCmd.Source)\" | Out-File -FilePath \"{logFile}\" -Append -Encoding utf8",
            "} catch {",
            $"    \"claude NOT FOUND: $_\" | Out-File -FilePath \"{logFile}\" -Append -Encoding utf8",
            "    Write-Host \"claude command not found on PATH\" -ForegroundColor Red",
            "}",
            $"Set-Location -LiteralPath \"{_sessionCwd}\"",
            $"$msg = Get-Content -Raw -LiteralPath \"{msgFile}\"",
            $"claude -p $msg 2>&1 | Tee-Object -FilePath \"{logFile}\" -Append",
            $"Remove-Item -LiteralPath \"{msgFile}\" -ErrorAction SilentlyContinue",
            $"\"=== done ===\" | Out-File -FilePath \"{logFile}\" -Append -Encoding utf8",
        });
        System.IO.File.WriteAllText(scriptFile, script);

        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "powershell.exe",
            WorkingDirectory = _sessionCwd,
            // UseShellExecute=false (not true) so EnvironmentVariables below is
            // actually mutable - a console-subsystem child spawned from this
            // no-console WPF app still gets its own visible new console window
            // without it, same as UseShellExecute=true would have given.
            UseShellExecute = false,
            CreateNoWindow = false,
        };
        psi.ArgumentList.Add("-NoExit");
        psi.ArgumentList.Add("-ExecutionPolicy");
        psi.ArgumentList.Add("Bypass");
        psi.ArgumentList.Add("-File");
        psi.ArgumentList.Add(scriptFile);

        // Root cause of an earlier version of this hanging indefinitely (confirmed
        // via the debug log: it stalled right after resolving `claude`, before any
        // output): this pet process inherited CLAUDE_CODE_SESSION_ID/SSE_PORT/etc.
        // from Claude Code's own MCP child-process environment, all the way down
        // through Node -> PetOverlay.exe -> here. The spawned `claude -p` picked
        // those up and appears to hang trying to negotiate with a "parent" session
        // that isn't really there (the pet, not an actual Claude Code process).
        // Strip them so this starts as a genuinely independent session.
        var envKeysToStrip = psi.EnvironmentVariables.Keys
            .Cast<string>()
            .Where(k => k.StartsWith("CLAUDE", StringComparison.OrdinalIgnoreCase)
                || k.StartsWith("ANTHROPIC", StringComparison.OrdinalIgnoreCase))
            .ToList();
        foreach (var key in envKeysToStrip)
        {
            psi.EnvironmentVariables.Remove(key);
        }

        System.Diagnostics.Process.Start(psi);
    }

    private void QueueMessageToInboxFile(string message)
    {
        try
        {
            var path = System.IO.Path.Combine(_sessionCwd, ".claudy-inbox.json");
            var json = System.Text.Json.JsonSerializer.Serialize(new
            {
                message,
                queuedAt = DateTime.UtcNow,
            });
            System.IO.File.WriteAllText(path, json);
            TryShowCasualBubble("Couldn't open a terminal directly - queued for next time instead. 😳", TimeSpan.FromSeconds(3.5), "scared");
        }
        catch
        {
            TryShowCasualBubble("Hmm, couldn't send that at all. 😳", TimeSpan.FromSeconds(2.5), "scared");
        }
    }

    // ---- tray icon ----

    private void CreateTrayIcon()
    {
        _trayIcon = new Forms.NotifyIcon
        {
            Icon = BuildIcon(),
            Visible = true,
            Text = BuildTrayText(),
        };

        var menu = new Forms.ContextMenuStrip();

        var playMenu = new Forms.ToolStripMenuItem("Play");
        foreach (var (label, emoji) in Toys)
        {
            var action = label == "Ball"
                ? new Action(StartBallCatch)
                : () => StartPlay(label, emoji);
            playMenu.DropDownItems.Add($"{emoji} {label}", null, (_, _) => Dispatcher.Invoke(action));
        }
        playMenu.DropDownItems.Add("🍪 Food", null, (_, _) => Dispatcher.Invoke(new Action(ArmFoodPlacement)));
        menu.Items.Add(playMenu);

        menu.Items.Add("Call Claudy", null, (_, _) => Dispatcher.Invoke(() => StartFollowCursor(TimeSpan.FromSeconds(10))));
        menu.Items.Add("Rename...", null, (_, _) => Dispatcher.Invoke(new Action(ShowRenameDialog)));
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
