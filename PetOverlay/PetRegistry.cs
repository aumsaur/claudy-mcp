using System.IO;
using System.Text.Json;
using System.Windows;

namespace PetOverlay;

// Lightweight cross-process spatial awareness AND social layer: each Claudy instance
// is a separate process with no shared memory, so instead of a full IPC broadcast,
// every instance periodically drops its own state into a shared folder and reads its
// siblings' latest state from there (positions for separation/mingling, a status for
// "am I free to be approached", a display name for prank/hangout bubbles). A small
// single-slot "inbox" file per pid doubles as a one-shot event mailbox so a prank can
// be seen (and reacted to) by its target - best effort, not guaranteed delivery.
public static class PetRegistry
{
    private static readonly string Dir = Path.Combine(Path.GetTempPath(), "ClaudyPets");
    private static readonly int SelfPid = Environment.ProcessId;
    private static readonly string SelfPath = Path.Combine(Dir, $"{SelfPid}.json");
    private static readonly string InboxPath = Path.Combine(Dir, $"{SelfPid}.inbox.json");

    public static void Publish(Point center, string displayName, string status)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            var json = JsonSerializer.Serialize(new PositionEntry
            {
                X = center.X,
                Y = center.Y,
                Ts = DateTime.UtcNow,
                Name = displayName,
                Status = status,
            });
            File.WriteAllText(SelfPath, json);
        }
        catch
        {
            // best effort only
        }
    }

    public static List<SiblingInfo> ReadOthers()
    {
        var result = new List<SiblingInfo>();
        try
        {
            if (!Directory.Exists(Dir)) return result;
            foreach (var file in Directory.EnumerateFiles(Dir, "*.json"))
            {
                if (file.EndsWith(".inbox.json", StringComparison.OrdinalIgnoreCase)) continue;
                if (string.Equals(file, SelfPath, StringComparison.OrdinalIgnoreCase)) continue;
                try
                {
                    var entry = JsonSerializer.Deserialize<PositionEntry>(File.ReadAllText(file));
                    if (entry is null) continue;
                    // Skip entries from processes that died without cleaning up after themselves.
                    if (DateTime.UtcNow - entry.Ts > TimeSpan.FromSeconds(5)) continue;

                    var name = Path.GetFileNameWithoutExtension(file);
                    if (!int.TryParse(name, out var pid)) continue;

                    result.Add(new SiblingInfo
                    {
                        Pid = pid,
                        Name = entry.Name,
                        X = entry.X,
                        Y = entry.Y,
                        Status = entry.Status,
                    });
                }
                catch
                {
                    // partially-written or corrupt file; next refresh will pick up a good one
                }
            }
        }
        catch
        {
            // best effort only
        }
        return result;
    }

    public static void Unpublish()
    {
        try { File.Delete(SelfPath); } catch { /* best effort */ }
        try { File.Delete(InboxPath); } catch { /* best effort */ }
    }

    // Drops a one-shot event into the target's inbox. Later writes overwrite earlier
    // unread ones - fine for an occasional whimsical prank, not meant to be reliable.
    public static void SendEvent(int targetPid, string type)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            var path = Path.Combine(Dir, $"{targetPid}.inbox.json");
            File.WriteAllText(path, JsonSerializer.Serialize(new InboxEvent { Type = type, Ts = DateTime.UtcNow }));
        }
        catch
        {
            // best effort only
        }
    }

    public static InboxEvent? ReadAndClearEvent()
    {
        try
        {
            if (!File.Exists(InboxPath)) return null;
            var text = File.ReadAllText(InboxPath);
            try { File.Delete(InboxPath); } catch { /* consumed either way */ }

            var evt = JsonSerializer.Deserialize<InboxEvent>(text);
            if (evt != null && DateTime.UtcNow - evt.Ts <= TimeSpan.FromSeconds(6)) return evt;
            return null;
        }
        catch
        {
            return null;
        }
    }

    public class SiblingInfo
    {
        public int Pid { get; set; }
        public string Name { get; set; } = "";
        public double X { get; set; }
        public double Y { get; set; }
        public string Status { get; set; } = "idle";
        public Point Position => new(X, Y);
    }

    public class InboxEvent
    {
        public string Type { get; set; } = "";
        public DateTime Ts { get; set; }
    }

    private class PositionEntry
    {
        public double X { get; set; }
        public double Y { get; set; }
        public DateTime Ts { get; set; }
        public string Name { get; set; } = "";
        public string Status { get; set; } = "idle";
    }
}
