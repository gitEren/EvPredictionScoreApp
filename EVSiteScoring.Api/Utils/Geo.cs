using System.Globalization;
using EVSiteScoring.Api.Domain.Models;

namespace EVSiteScoring.Api.Utils;

public static class GeoUtils
{
    private const double EarthRadiusMeters = 6_371_000;

    public static double HaversineDistanceMeters((double Lat, double Lon) a, (double Lat, double Lon) b)
    {
        var dLat = DegreesToRadians(b.Lat - a.Lat);
        var dLon = DegreesToRadians(b.Lon - a.Lon);
        var lat1 = DegreesToRadians(a.Lat);
        var lat2 = DegreesToRadians(b.Lat);

        var sinLat = Math.Sin(dLat / 2);
        var sinLon = Math.Sin(dLon / 2);
        var aVal = sinLat * sinLat + sinLon * sinLon * Math.Cos(lat1) * Math.Cos(lat2);
        var c = 2 * Math.Atan2(Math.Sqrt(aVal), Math.Sqrt(Math.Max(0, 1 - aVal)));
        return EarthRadiusMeters * c;
    }

    public static double PolygonAreaSquareMeters(GeoJsonPolygon polygon)
    {
        var coordinates = polygon.ExteriorRing().ToArray();
        if (coordinates.Length < 3)
        {
            return 0;
        }

        // Use the shoelace formula in projected meters using equirectangular approximation.
        var first = coordinates[0];
        var meters = coordinates
            .Select(coord => ProjectToMeters(coord, first))
            .ToArray();

        double sum = 0;
        for (var i = 0; i < meters.Length; i++)
        {
            var (x1, y1) = meters[i];
            var (x2, y2) = meters[(i + 1) % meters.Length];
            sum += (x1 * y2) - (x2 * y1);
        }

        return Math.Abs(sum) / 2.0;
    }

    public static (double X, double Y) ProjectToMeters((double Lon, double Lat) coordinate, (double Lon, double Lat) origin)
    {
        var x = DegreesToRadians(coordinate.Lon - origin.Lon) * EarthRadiusMeters * Math.Cos(DegreesToRadians(origin.Lat));
        var y = DegreesToRadians(coordinate.Lat - origin.Lat) * EarthRadiusMeters;
        return (x, y);
    }

    public static string ToCoordinateString(double lat, double lon) =>
        string.Create(CultureInfo.InvariantCulture, $"{lat},{lon}");

    private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180.0;
}
