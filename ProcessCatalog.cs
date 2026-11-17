using System.Diagnostics;
using System.IO;

namespace ByteFlow;
public sealed record ProcessInfo(int Pid, string Name, string Path, bool IsSystem);

// Slow process/module/version queries stay off the UI thread and run only every five seconds.
public sealed class ProcessCatalog
{
    private readonly Dictionary<string, string> names = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<int, ProcessInfo> snapshot = new();
    public Dictionary<int, ProcessInfo> Snapshot => Volatile.Read(ref snapshot);
    public void Scan()
    {
        var result = new Dictionary<int, ProcessInfo>();
        var old = Snapshot;
        string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows).TrimEnd('\\') + "\\";
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    int pid = process.Id;
                    string processName = process.ProcessName;
                    if (old.TryGetValue(pid, out var cached) && cached.Path.Length > 0 &&
                        string.Equals(Path.GetFileNameWithoutExtension(cached.Path), processName, StringComparison.OrdinalIgnoreCase))
                    { result[pid] = cached; continue; }
                    string path = "";
                    try { path = process.MainModule?.FileName ?? ""; } catch { }
                    string name = processName;
                    if (path.Length > 0 && !names.TryGetValue(path, out name!))
                    {
                        try { name = FileVersionInfo.GetVersionInfo(path).FileDescription ?? processName; } catch { }
                        if (string.IsNullOrWhiteSpace(name)) name = processName;
                        names[path] = name;
                    }
                    bool system = path.Length == 0 || path.StartsWith(windows, StringComparison.OrdinalIgnoreCase) || process.SessionId == 0;
                    result[pid] = new(pid, name, path, system);
                }
                catch { }
            }
        }
        Volatile.Write(ref snapshot, result);
    }
}
