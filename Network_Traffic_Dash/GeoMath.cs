using System;
using System.Collections.Generic;

namespace Network_Traffic_Dash
{
    public static class GeoMath
    {
        public static List<(double lat, double lon)> GreatCirclePath(
            double lat1, double lon1, double lat2, double lon2, int steps = 50)
        {
            var coords = new List<(double, double)>();

            // radianer
            lat1 *= Math.PI / 180.0;
            lon1 *= Math.PI / 180.0;
            lat2 *= Math.PI / 180.0;
            lon2 *= Math.PI / 180.0;

            // central vinkel (haversine)
            double d = 2 * Math.Asin(Math.Sqrt(
                Math.Pow(Math.Sin((lat2 - lat1) / 2), 2) +
                Math.Cos(lat1) * Math.Cos(lat2) *
                Math.Pow(Math.Sin((lon2 - lon1) / 2), 2)));

            if (d == 0) return coords;

            for (int i = 0; i <= steps; i++)
            {
                double f = (double)i / steps;

                double A = Math.Sin((1 - f) * d) / Math.Sin(d);
                double B = Math.Sin(f * d) / Math.Sin(d);

                double x = A * Math.Cos(lat1) * Math.Cos(lon1) + B * Math.Cos(lat2) * Math.Cos(lon2);
                double y = A * Math.Cos(lat1) * Math.Sin(lon1) + B * Math.Cos(lat2) * Math.Sin(lon2);
                double z = A * Math.Sin(lat1) + B * Math.Sin(lat2);

                double lat = Math.Atan2(z, Math.Sqrt(x * x + y * y));
                double lon = Math.Atan2(y, x);

                coords.Add((lat * 180.0 / Math.PI, lon * 180.0 / Math.PI));
            }

            return coords;
        }
    }
}
