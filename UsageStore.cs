using System.IO;
using System.Text.Json;
namespace ByteFlow;
public sealed record UsageEntry(string Day, string Path, string Name, long Received, long Sent)
{
    public long Total => Received + Sent;
}
public sealed class UsageStore
{
    public static string DataDirectory => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ByteFlow");
    private readonly string file;
    private readonly Dictionary<string, UsageEntry> entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly object gate = new();
    public UsageStore(string? directory = null)
    {
        file = System.IO.Path.Combine(directory ?? DataDirectory, "usage.json");
        if (!File.Exists(file)) return;
        foreach (var entry in JsonSerializer.Deserialize<List<UsageEntry>>(File.ReadAllText(file)) ?? [])
            entries[entry.Day + "|" + entry.Path] = entry;
    }
    public void Add(string path, string name, long down, long up)
    {
        if (down <= 0 && up <= 0) return;
        string day = DateTime.Now.ToString("yyyy-MM-dd"), key = day + "|" + path;
        lock (gate)
        {
            entries.TryGetValue(key, out var old);
            entries[key] = new(day, path, name, (old?.Received ?? 0) + Math.Max(0, down), (old?.Sent ?? 0) + Math.Max(0, up));
        }
    }
    public List<UsageEntry> Snapshot() { lock (gate) return entries.Values.OrderByDescending(e => e.Day).ThenByDescending(e => e.Total).ToList(); }
    public void Clear(string? path = null)
    {
        lock (gate)
        {
            foreach (var key in entries.Where(p => path == null || string.Equals(p.Value.Path, path, StringComparison.OrdinalIgnoreCase)).Select(p => p.Key).ToList()) entries.Remove(key);
            Save();
        }
    }
    public void Save()
    {
        lock (gate)
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(file)!);
            string temporary = file + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(entries.Values));
            File.Move(temporary, file, true);
        }
    }
}
