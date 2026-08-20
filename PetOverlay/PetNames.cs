using System.IO;
using System.Text.Json;

namespace PetOverlay;

// The nameplate label normally comes from the session's folder name, but a name the
// user set by hand should outlive the process - every Claude session spawns its own
// short-lived pet, so an in-memory rename would be forgotten almost immediately.
// Overrides live in one small json map under %APPDATA%\Claudy, keyed by session cwd
// so each project keeps its own pet name. Best effort throughout: a missing, corrupt
// or unwritable file just means "no override", never a crash.
public static class PetNames
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Claudy",
        "names.json");

    public static string? Load(string sessionKey)
    {
        var key = Normalize(sessionKey);
        if (key is null) return null;

        var map = ReadMap();
        if (map.TryGetValue(key, out var name) && !string.IsNullOrWhiteSpace(name)) return name;
        return null;
    }

    public static void Save(string sessionKey, string name)
    {
        var key = Normalize(sessionKey);
        if (key is null) return;

        var map = ReadMap();
        map[key] = name;
        WriteMap(map);
    }

    public static void Clear(string sessionKey)
    {
        var key = Normalize(sessionKey);
        if (key is null) return;

        var map = ReadMap();
        if (map.Remove(key)) WriteMap(map);
    }

    // Launched without --session-cwd there's nothing stable to key on, so a rename
    // stays in-memory for that run rather than being written under a bogus key.
    private static string? Normalize(string sessionKey)
    {
        if (string.IsNullOrWhiteSpace(sessionKey)) return null;
        return sessionKey.Trim()
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .ToLowerInvariant();
    }

    private static Dictionary<string, string> ReadMap()
    {
        try
        {
            if (!File.Exists(FilePath)) return new Dictionary<string, string>();
            var map = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(FilePath));
            return map ?? new Dictionary<string, string>();
        }
        catch
        {
            return new Dictionary<string, string>();
        }
    }

    private static void WriteMap(Dictionary<string, string> map)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(map));
        }
        catch
        {
            // best effort only
        }
    }
}
