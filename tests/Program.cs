using ByteFlow;
using System.Net;
using System.Net.Sockets;
using System.IO;

// Persistence and clearing regressions run without starting ETW.
var testDirectory = Path.Combine(Path.GetTempPath(), "ByteFlow-tests-" + Guid.NewGuid().ToString("N"));
var store = new UsageStore(testDirectory);
store.Add("app-a.exe", "App A", 100, 50);
store.Add("app-a.exe", "App A", 30, 20);
store.Add("app-b.exe", "App B", 7, 9);
store.Save();
var restored = new UsageStore(testDirectory);
if (restored.Snapshot().Sum(x => x.Total) != 216) throw new Exception("Usage persistence failed");
restored.Clear("APP-A.EXE");
if (new UsageStore(testDirectory).Snapshot().Single().Total != 16) throw new Exception("Per-app clear failed");
restored.Clear();
restored.Add("app-a.exe", "App A", 1, 2);
restored.Save();
if (new UsageStore(testDirectory).Snapshot().Single().Total != 3) throw new Exception("Post-clear counting failed");
Console.WriteLine("PASS: usage accumulation, save/reload, per-app clear, clear-all, and post-clear counting.");
if (args.Contains("--storage-only")) return;

// Run from an elevated terminal. This changes no firewall rules.
using var monitor = new NetworkMonitor();
string? error = null;
var worker = Task.Run(() => monitor.Run(message => Volatile.Write(ref error, message)));
for (int i = 0; i < 100 && !monitor.IsRunning && Volatile.Read(ref error) == null; i++) await Task.Delay(100);
if (!monitor.IsRunning) throw new Exception("ETW failed to start: " + (error ?? "timeout"));
await Task.Delay(500);
int pid = Environment.ProcessId;
monitor.Counters.TryGetValue(pid, out var before);
long initialDown = before == null ? 0 : Interlocked.Read(ref before.Received);
long initialUp = before == null ? 0 : Interlocked.Read(ref before.Sent);
var listener = new TcpListener(IPAddress.Loopback, 0);
listener.Start();
var receive = Task.Run(async () =>
{
    using var peer = await listener.AcceptTcpClientAsync();
    var buffer = new byte[65536];
    while (await peer.GetStream().ReadAsync(buffer) > 0) { }
});
using (var client = new TcpClient())
{
    await client.ConnectAsync((IPEndPoint)listener.LocalEndpoint);
    var buffer = new byte[65536];
    for (int i = 0; i < 128; i++) await client.GetStream().WriteAsync(buffer);
}
await receive.WaitAsync(TimeSpan.FromSeconds(10));
listener.Stop();
long down = 0, up = 0;
for (int i = 0; i < 100; i++)
{
    if (monitor.Counters.TryGetValue(pid, out var value))
    {
        down = Interlocked.Read(ref value.Received) - initialDown;
        up = Interlocked.Read(ref value.Sent) - initialUp;
    }
    if (down >= 8388608 && up >= 8388608) break;
    await Task.Delay(100);
}
if (down < 8388608 || up < 8388608) throw new Exception($"Incorrect process attribution: down={down}, up={up}");
Console.WriteLine($"PASS: ETW startup and own-process TCP attribution; received={down}, sent={up} bytes.");
monitor.Dispose();
await worker.WaitAsync(TimeSpan.FromSeconds(5));
