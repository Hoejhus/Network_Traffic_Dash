# Network_Traffic_Dash
A funny hobby project which is a fast desktop network monitor for Windows that shows where your apps are talking to—live—on an interactive world map. It captures all TCP/UDP flows from your machine via ETW, geolocates destinations with MaxMind GeoLite2, and visualizes them in a Leaflet map embedded with WebView2. Hover or click to animate a great-circle “flight” from you to the destination. Search and two tabs (Live & History) make it easy to explore.

# Requirements
Windows 10/11
Visual Studio 2022 with .NET tooling
.NET 8 (net8.0-windows)
Run as Administrator (ETW kernel sessions require elevation)
WebView2 Runtime

# Quick start
Clone/Open the solution in Visual Studio 2022.

Add MaxMind databases (required):
Create Assets/GeoIP/ in the project (if not present).
Sign up/log in at MaxMind (free GeoLite2):
Download GeoLite2-City.mmdb and GeoLite2-ASN.mmdb (optional: GeoLite2-Country.mmdb).
Place the .mmdb files into Assets/GeoIP/.
In VS, for each .mmdb file: Build Action = Content, Copy to Output Directory = Copy always.

Build the solution.

Run as Administrator (start VS as Admin, then F5).
