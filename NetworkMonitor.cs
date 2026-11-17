using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Session;

namespace ByteFlow;
public sealed class Traffic
{
    public long Received, Sent;
}
public sealed class NetworkMonitor : IDisposable
{
    public readonly ConcurrentDictionary<int, Traffic> Counters = new();
    private TraceEventSession? session;
    private long eventCount;
    private int running;
    public bool IsRunning => Volatile.Read(ref running) == 1;
    public long EventCount => Interlocked.Read(ref eventCount);
    public void Run(Action<string> failure)
    {
        try
        {
            if (TraceEventSession.IsElevated() != true)
                throw new InvalidOperationException(Localization.T("网络速率采集需要管理员权限。请退出后右键 ByteFlow.exe，选择“以管理员身份运行”。", "Network capture requires administrator privileges. Close the app, then right-click ByteFlow.exe and choose Run as administrator."));
            using var active = new TraceEventSession("ByteFlow-" + Environment.ProcessId);
            session = active;
            active.StopOnDispose = true;
            // Enable the kernel provider before Source creates/starts the real-time session.
            active.EnableKernelProvider(KernelTraceEventParser.Keywords.NetworkTCPIP);
            active.Source.Kernel.TcpIpRecv += e => Add(e.ProcessID, e.size, false);
            active.Source.Kernel.TcpIpSend += e => Add(e.ProcessID, e.size, true);
            active.Source.Kernel.UdpIpRecv += e => Add(e.ProcessID, e.size, false);
            active.Source.Kernel.UdpIpSend += e => Add(e.ProcessID, e.size, true);
            active.Source.Kernel.TcpIpRecvIPV6 += e => Add(e.ProcessID, e.size, false);
            active.Source.Kernel.TcpIpSendIPV6 += e => Add(e.ProcessID, e.size, true);
            active.Source.Kernel.UdpIpRecvIPV6 += e => Add(e.ProcessID, e.size, false);
            active.Source.Kernel.UdpIpSendIPV6 += e => Add(e.ProcessID, e.size, true);
            Volatile.Write(ref running, 1);
            active.Source.Process();
        }
        catch (Exception e) { failure(e.Message); }
        finally { Volatile.Write(ref running, 0); }
    }
    private void Add(int pid, int bytes, bool sent)
    {
        if (pid < 0 || bytes < 0) return;
        Interlocked.Increment(ref eventCount);
        var value = Counters.GetOrAdd(pid, _ => new());
        if (sent) Interlocked.Add(ref value.Sent, bytes); else Interlocked.Add(ref value.Received, bytes);
    }
    public void Dispose() => session?.Dispose();
}
