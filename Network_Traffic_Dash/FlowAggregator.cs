using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;

namespace Network_Traffic_Dash;

public sealed class FlowAggregator
{
    public record LiveRow(string Proc, string Dst, int Port, string Proto, long Bps, int Count);
    public record HistRow(string Org, string Dst, string Country, string Proto, string Bytes, long Count);
    public record MapPoint(string ip, double lat, double lon, long bytes, long count, string colorKey, string label);

    private sealed class FlowState
    {
        public long WindowBytes;
        public double EmaBps;
        public DateTime LastSeenUtc;
        public long TotalBytes;
        public string? ProcName;
    }

    private readonly ConcurrentDictionary<(int Pid, string Dst, int Port, string Proto), FlowState> _flows = new();
    private readonly ConcurrentDictionary<string, (long TotalBytes, long FlowCount, HashSet<string> Procs)> _history = new();

    private readonly GeoService _geo;
    private readonly int _ttlSeconds;

    public FlowAggregator(GeoService geo, int activeTtlSeconds = 3)
    {
        _geo = geo;
        _ttlSeconds = Math.Max(1, activeTtlSeconds);
    }

    public void Ingest(PacketMeta p)
    {
        var key = (p.Pid, p.Dst, p.Dport, p.Proto);
        var st = _flows.GetOrAdd(key, _ => new FlowState());
        st.WindowBytes += p.Size;
        st.TotalBytes += p.Size;
        st.LastSeenUtc = DateTime.UtcNow;
        st.ProcName ??= SafeProcName(p.Pid);

        _history.AddOrUpdate(p.Dst,
            _ => (p.Size, 1, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { st.ProcName! }),
            (_, cur) => (cur.TotalBytes + p.Size, cur.FlowCount + 1, AddProc(cur.Procs, st.ProcName!)));
    }

    private static HashSet<string> AddProc(HashSet<string> set, string p) { set.Add(p); return set; }

    public (IReadOnlyList<LiveRow> LiveRows, IReadOnlyList<HistRow> HistRows,
            IReadOnlyList<MapPoint> LivePoints, IReadOnlyList<MapPoint> HistPoints) Snapshot()
    {
        var now = DateTime.UtcNow;

        // ---------- LIVE ----------
        var liveFlowItems = new List<(string Proc, string Dst, int Port, string Proto, long Bps)>();
        foreach (var kv in _flows.ToArray())
        {
            var key = kv.Key; var st = kv.Value;
            var bytes = st.WindowBytes; st.WindowBytes = 0;
            st.EmaBps = st.EmaBps * 0.6 + bytes * 0.4;

            var age = (now - st.LastSeenUtc).TotalSeconds;
            if (age <= _ttlSeconds || st.EmaBps > 1)
                liveFlowItems.Add((st.ProcName ?? $"PID {key.Pid}", key.Dst, key.Port, key.Proto, (long)st.EmaBps));
            else
                _flows.TryRemove(key, out _);
        }

        var liveRows = liveFlowItems
            .GroupBy(x => (x.Proc, x.Dst, x.Port, x.Proto))
            .Select(g => new LiveRow(g.Key.Proc, g.Key.Dst, g.Key.Port, g.Key.Proto,
                                     g.Sum(v => v.Bps), g.Count()))
            .OrderByDescending(x => x.Bps)
            .Take(100)
            .ToList();

        // Live punkter (pr. Dst)
        var livePoints = liveFlowItems
            .GroupBy(x => x.Dst)
            .Select(g =>
            {
                var ip = g.Key;
                if (!IsPublicIp(ip)) return null;
                var geo = _geo.Lookup(ip);
                if (geo == null) return null;

                var bytes = g.Sum(v => v.Bps);
                var country = geo.Value.country;
                var org = geo.Value.org;
                var label = $"{org} (LIVE)\nIP: {ip}\nLand: {country}\nFlows: {g.Count()}\nThroughput: {FormatBytes(bytes)}/s";
                var colorKey = string.IsNullOrEmpty(org) ? ip : org;
                return new MapPoint(ip, geo.Value.lat, geo.Value.lon, bytes, g.Count(), colorKey, label);
            })
            .Where(p => p != null)!.Cast<MapPoint>()
            .ToList();

        // ---------- HISTORIK ----------
        var histRows = _history
            .Where(kv => IsPublicIp(kv.Key))
            .Select(kv =>
            {
                var ip = kv.Key;
                var meta = _geo.Lookup(ip);
                var country = meta?.country ?? "";
                var org = meta?.org ?? "";
                return new HistRow(
                    string.IsNullOrEmpty(org) ? "(ukendt)" : org,
                    ip,
                    country,
                    "-", // samlet historik (ikke per-proto)
                    FormatBytes(kv.Value.TotalBytes),
                    kv.Value.FlowCount
                );
            })
            .OrderByDescending(x => x.Count)
            .Take(200)
            .ToList();

        var histPoints = _history
            .Where(kv => IsPublicIp(kv.Key))
            .Select(kv =>
            {
                var ip = kv.Key;
                var meta = _geo.Lookup(ip); if (meta == null) return null;
                var org = meta.Value.org;
                var country = meta.Value.country;
                var label = $"{org}\nIP: {ip}\nLand: {country}\nFlows: {kv.Value.FlowCount}\nAkkumuleret: {FormatBytes(kv.Value.TotalBytes)}";
                var colorKey = string.IsNullOrEmpty(org) ? ip : org;
                return new MapPoint(ip, meta.Value.lat, meta.Value.lon, kv.Value.TotalBytes, kv.Value.FlowCount, colorKey, label);
            })
            .Where(p => p != null)!.Cast<MapPoint>()
            .ToList();

        return (liveRows, histRows, livePoints, histPoints);
    }

    private static string SafeProcName(int pid)
    {
        try { return Process.GetProcessById(pid).ProcessName; } catch { return $"PID {pid}"; }
    }

    private static bool IsPublicIp(string ip)
    {
        if (!IPAddress.TryParse(ip, out var addr)) return false;
        if (addr.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            var b = addr.GetAddressBytes();
            if (b[0] == 10) return false;
            if (b[0] == 172 && b[1] >= 16 && b[1] <= 31) return false;
            if (b[0] == 192 && b[1] == 168) return false;
            if (b[0] == 127) return false;
            if (b[0] == 169 && b[1] == 254) return false;
        }
        return true;
    }

    private static string FormatBytes(long b)
    {
        string[] u = { "B", "KB", "MB", "GB", "TB" };
        double v = b; int i = 0; while (v >= 1024 && i < u.Length - 1) { v /= 1024; i++; }
        return $"{v:0.0} {u[i]}";
    }
}
