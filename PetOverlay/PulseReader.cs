using System.Globalization;
using System.IO;
using System.Text.Json;

namespace PetOverlay;

public sealed record PulseWindowStat(double Percent, DateTimeOffset? ResetsAt);

public sealed class PulseSnapshot
{
    public PulseWindowStat? Session { get; init; }
    public PulseWindowStat? Weekly { get; init; }
    public double? ContextPct { get; init; }
    public long? ContextUsed { get; init; }
    public long? ContextLimit { get; init; }
    public double? CostUsd { get; init; }
    public string? ModelName { get; init; }
    public string? Effort { get; init; }
    public bool FastMode { get; init; }
    public string? Plan { get; init; }

    // When claude-pulse last repainted, from the file's own mtime - the payload
    // carries no timestamp of its own. Everything above is only as fresh as this.
    public DateTime UpdatedUtc { get; init; }
    public TimeSpan Age => DateTime.UtcNow - UpdatedUtc;
}

// Reads the state claude-pulse (the Claude Code status line) leaves on disk each
// time it repaints. Deliberately does NOT shell out to claude_status.py: that
// script is fed its numbers on stdin by Claude Code, which we can't reproduce,
// and it emits ANSI-escaped bars rather than data.
//
// The files are global rather than per-session, so with several Claude sessions
// open the per-session figures (cost, context) belong to whichever one repainted
// last. The session/weekly windows are account-wide and always right.
public static class PulseReader
{
    private static readonly string StateDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "claude-status");

    private static readonly string StdinCtxPath = Path.Combine(StateDir, "stdin_ctx.json");
    private static readonly string CachePath = Path.Combine(StateDir, "cache.json");

    // Absent state dir means claude-pulse was never installed (or never ran), which
    // is a different story for the user than "installed but we failed to read it".
    public static bool IsInstalled => Directory.Exists(StateDir) && File.Exists(StdinCtxPath);

    // Null on any read failure. claude-pulse writes atomically, but a replace
    // landing mid-read still surfaces as a transient IO error on Windows - the
    // caller is expected to keep showing its last good snapshot rather than blank.
    public static PulseSnapshot? TryRead()
    {
        var ctx = TryReadJson(StdinCtxPath);
        if (ctx is null) return null;

        using (ctx)
        {
            var root = ctx.RootElement;
            using var cache = TryReadJson(CachePath);

            // Rate limits ride along on stdin (Claude Code 2.1.80+) and land in
            // stdin_ctx; cache.json only has them when that path didn't fire.
            var limits = Prop(root, "_rate_limits");
            if (limits is null && cache is not null) limits = Prop(cache.RootElement, "usage");

            return new PulseSnapshot
            {
                Session = ReadWindow(limits, "five_hour"),
                Weekly = ReadWindow(limits, "seven_day"),
                ContextPct = Number(root, "context_pct"),
                ContextUsed = (long?)Number(root, "context_used"),
                ContextLimit = (long?)Number(root, "context_limit"),
                CostUsd = Number(root, "cost_usd"),
                ModelName = Text(root, "model_name"),
                Effort = Text(root, "effort"),
                FastMode = Bool(root, "fast_mode"),
                Plan = cache is null ? null : Text(cache.RootElement, "plan"),
                UpdatedUtc = File.GetLastWriteTimeUtc(StdinCtxPath),
            };
        }
    }

    private static JsonDocument? TryReadJson(string path)
    {
        try
        {
            return JsonDocument.Parse(File.ReadAllText(path));
        }
        catch
        {
            return null;
        }
    }

    private static PulseWindowStat? ReadWindow(JsonElement? limits, string key)
    {
        if (limits is not { } l) return null;
        if (Prop(l, key) is not { } window) return null;
        if (Number(window, "utilization") is not { } pct) return null;

        DateTimeOffset? resets = null;
        if (Text(window, "resets_at") is { } raw &&
            DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
        {
            resets = parsed;
        }

        return new PulseWindowStat(pct, resets);
    }

    private static JsonElement? Prop(JsonElement parent, string name)
    {
        if (parent.ValueKind != JsonValueKind.Object) return null;
        if (!parent.TryGetProperty(name, out var value)) return null;
        return value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined ? null : value;
    }

    private static double? Number(JsonElement parent, string name) =>
        Prop(parent, name) is { ValueKind: JsonValueKind.Number } v ? v.GetDouble() : null;

    private static string? Text(JsonElement parent, string name) =>
        Prop(parent, name) is { ValueKind: JsonValueKind.String } v ? v.GetString() : null;

    private static bool Bool(JsonElement parent, string name) =>
        Prop(parent, name) is { ValueKind: JsonValueKind.True };
}
