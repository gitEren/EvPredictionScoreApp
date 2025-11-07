using System.Text.Json.Serialization;

namespace EVSiteScoring.Api.Domain.Models;

/// <summary>
/// Minimal GeoJSON polygon definition coming from the frontend drawing tools.
/// </summary>
public sealed class GeoJsonPolygon
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "Polygon";

    [JsonPropertyName("coordinates")]
    public double[][][] Coordinates { get; set; } = Array.Empty<double[][]>();

    public bool IsValid()
    {
        if (!string.Equals(Type, "Polygon", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (Coordinates.Length == 0)
        {
            return false;
        }

        var ring = Coordinates[0];
        return ring.Length >= 4;
    }

    public IEnumerable<(double Lon, double Lat)> ExteriorRing()
    {
        if (Coordinates.Length == 0)
        {
            yield break;
        }

        foreach (var coordinate in Coordinates[0])
        {
            if (coordinate.Length >= 2)
            {
                yield return (coordinate[0], coordinate[1]);
            }
        }
    }
}

/// <summary>
/// Simple latitude/longitude holder used by the scoring API.
/// </summary>
public sealed class GeoJsonPoint
{
    [JsonPropertyName("lat")]
    public double Lat { get; set; }

    [JsonPropertyName("lon")]
    public double Lon { get; set; }

    public bool IsValid()
    {
        return Lat is >= -90 and <= 90 && Lon is >= -180 and <= 180;
    }
}
