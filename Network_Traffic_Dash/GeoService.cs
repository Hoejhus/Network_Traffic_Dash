using MaxMind.GeoIP2;
using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;

namespace Network_Traffic_Dash;

public sealed class GeoService : IDisposable
{
    private DatabaseReader? _city, _asn, _country;
    private readonly Dictionary<string, (double lat, double lon, string country, string org)> _cache = new();

    public GeoService()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "Assets", "GeoIP");
        if (!Directory.Exists(dir))
        {
            MessageBox.Show("Assets\\GeoIP folder not found.");
            return;
        }

        foreach (var path in Directory.EnumerateFiles(dir, "*.mmdb"))
        {
            try
            {
                var r = new DatabaseReader(path);
                var t = (r.Metadata?.DatabaseType ?? "").ToLowerInvariant();
                if (t.Contains("city") && _city == null) _city = r;
                else if (t.Contains("asn") && _asn == null) _asn = r;
                else if (t.Contains("country") && _country == null) _country = r;
                else r.Dispose();
            }
            catch { /* ignore bad mmdb */ }
        }

        if (_city == null)
            MessageBox.Show("GeoLite2-City.mmdb not found – points will be at (0,0).");
    }

    public (double lat, double lon, string country, string org)? Lookup(string ip)
    {
        if (_cache.TryGetValue(ip, out var hit)) return hit;

        double lat = 0, lon = 0;
        string org = "", country = "";

        try
        {
            if (_city != null)
            {
                var c = _city.City(ip);
                lat = c.Location.Latitude ?? 0;
                lon = c.Location.Longitude ?? 0;
                country = c.Country.IsoCode ?? c.Country.Name ?? "";
            }
            else if (_country != null)
            {
                var c = _country.Country(ip);
                country = c.Country.IsoCode ?? c.Country.Names.GetValueOrDefault("en") ?? "";
            }
        }
        catch { }

        try
        {
            if (_asn != null)
            {
                var a = _asn.Asn(ip);
                org = $"{a.AutonomousSystemNumber} {a.AutonomousSystemOrganization}";
            }
        }
        catch { }

        var val = (lat, lon, country, org);
        _cache[ip] = val;
        return val;
    }

    public void Dispose()
    {
        _city?.Dispose();
        _asn?.Dispose();
        _country?.Dispose();
    }
}
