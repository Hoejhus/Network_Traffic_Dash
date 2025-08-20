using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using Microsoft.Diagnostics.Tracing.Session;

namespace Network_Traffic_Dash;

public record PacketMeta(DateTime Ts, int Pid, string Proto, string Src, int Sport, string Dst, int Dport, int Size);

public sealed class EtwListener : IDisposable
{
    private TraceEventSession? _session;
    private Task? _pump;
    private readonly CancellationTokenSource _cts = new();
    public event Action<PacketMeta>? OnPacket;

    public void Start()
    {
        if (TraceEventSession.IsElevated() != true)
            throw new InvalidOperationException("Run Visual Studio as Administrator.");

        const string sessionName = "PacketRadar-KernelNet";
        try { TraceEventSession.GetActiveSession(sessionName)?.Dispose(); } catch { }

        _session = new TraceEventSession(sessionName) { StopOnDispose = true };
        _session.EnableKernelProvider(KernelTraceEventParser.Keywords.NetworkTCPIP);

        var source = _session.Source;
        var kernel = new KernelTraceEventParser(source);

        // IPv4 events
        kernel.TcpIpSend += d => Raise(d, "TCP", isSend: true);
        kernel.TcpIpRecv += d => Raise(d, "TCP", isSend: false);
        kernel.UdpIpSend += d => Raise(d, "UDP", isSend: true);
        kernel.UdpIpRecv += d => Raise(d, "UDP", isSend: false);

        _pump = Task.Run(() => source.Process(), _cts.Token);
    }

    private void Raise(TraceEvent d, string proto, bool isSend)
    {
        try
        {
            var pid = d.ProcessID;
            var size = Convert.ToInt32(d.PayloadByName("size") ?? d.PayloadByName("PayloadLength") ?? 0);

            string src = AsIpV4(d.PayloadByName(isSend ? "saddr" : "daddr"));
            int sport = Convert.ToInt32(d.PayloadByName(isSend ? "sport" : "dport") ?? 0);

            string dst = AsIpV4(d.PayloadByName(isSend ? "daddr" : "saddr"));
            int dport = Convert.ToInt32(d.PayloadByName(isSend ? "dport" : "sport") ?? 0);

            OnPacket?.Invoke(new PacketMeta(DateTime.UtcNow, pid, proto, src, sport, dst, dport, size));
        }
        catch { }
    }

    private static string AsIpV4(object? v)
    {
        try { return v is uint u ? new IPAddress(u).ToString() : IPAddress.Parse(v?.ToString() ?? "0.0.0.0").ToString(); }
        catch { return "0.0.0.0"; }
    }

    public void Dispose()
    {
        try { _cts.Cancel(); } catch { }
        try { _session?.Dispose(); } catch { }
    }
}
