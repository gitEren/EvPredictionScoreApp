using System.Text;
using System.Text.Json;
using EVSiteScoring.Api.Domain.Models;
using EVSiteScoring.Api.Utils;
using Microsoft.Extensions.Options;

namespace EVSiteScoring.Api.Services.Providers;

/// <summary>
/// Queries the OSM Overpass API for POIs, road segments, competition and demographic proxies.
/// This provider is the guaranteed fallback when premium APIs are not configured or unavailable.
/// </summary>
public sealed class OverpassProvider
{
    private static readonly string[] AmenityPoiKeys =
    {
        "mall:shop=mall",
        "supermarket:shop=supermarket",
        "office:office=*",
        "school:amenity=school",
        "school:amenity=university",
        "hospital:amenity=hospital",
        "hospital:healthcare=hospital",
        "entertainment:amenity=cinema",
        "entertainment:amenity=theatre",
        "entertainment:amenity=arts_centre",
        "entertainment:leisure=stadium",
        "entertainment:leisure=fitness_centre"
    };

    private static readonly Dictionary<string, double> RoadWeights = new(StringComparer.OrdinalIgnoreCase)
    {
        ["motorway"] = 1.0,
        ["trunk"] = 0.95,
        ["primary"] = 0.85,
        ["secondary"] = 0.7,
        ["tertiary"] = 0.55,
        ["unclassified"] = 0.35,
        ["residential"] = 0.25,
        ["service"] = 0.2
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<OverpassProvider> _logger;
    private readonly string _endpoint;

    public OverpassProvider(IHttpClientFactory httpClientFactory, IOptions<ProviderOptions> options, ILogger<OverpassProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _endpoint = options.Value.OverpassEndpoint;
    }

    public async Task<RawFeatureSnapshot> FetchAsync(GeoJsonPolygon polygon, GeoJsonPoint point, int radiusMeters, CancellationToken cancellationToken)
    {
        var query = BuildQuery(point.Lat, point.Lon, radiusMeters);
        var client = _httpClientFactory.CreateClient("overpass");
        _logger.LogDebug("Querying Overpass fallback at {Endpoint}", _endpoint);

        var responseText = await HttpRetry.WithRetryAsync(async () =>
        {
            using var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("data", query)
            });

            using var response = await client.PostAsync(string.Empty, content, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }, _logger).ConfigureAwait(false);

        using var doc = JsonDocument.Parse(responseText);
        var elements = doc.RootElement.TryGetProperty("elements", out var arr)
            ? arr.EnumerateArray().ToArray()
            : Array.Empty<JsonElement>();

        var poiCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var poiDwellScore = 0.0;
        var freeParking = false;

        var roadWeightedLength = 0.0;
        var minPrimaryDistance = double.PositiveInfinity;
        var minMotorwayDistance = double.PositiveInfinity;

        var competitionGravityNumerator = 0.0;
        var highCompetitionStations = 0;

        var residentialAreaSqm = 0.0;
        var settlementSignal = 0.0;

        var gridSignal = 0.0;

        foreach (var element in elements)
        {
            if (!element.TryGetProperty("tags", out var tagsElement))
            {
                continue;
            }

            var tags = ParseTags(tagsElement);

            // POI counting with dwell-time heuristic weights.
            foreach (var spec in AmenityPoiKeys)
            {
                var parts = spec.Split(':', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length != 2)
                {
                    continue;
                }

                var category = parts[0];
                var kv = parts[1].Split('=');
                if (kv.Length != 2)
                {
                    continue;
                }

                var key = kv[0];
                var value = kv[1];
                if (value == "*")
                {
                    if (tags.ContainsKey(key))
                    {
                        IncrementPoi(poiCounts, category);
                        poiDwellScore += CategoryWeight(category);
                    }
                }
                else if (tags.TryGetValue(key, out var tagValue) && string.Equals(tagValue, value, StringComparison.OrdinalIgnoreCase))
                {
                    IncrementPoi(poiCounts, category);
                    poiDwellScore += CategoryWeight(category);
                }
            }

            if (tags.TryGetValue("amenity", out var amenity))
            {
                if (string.Equals(amenity, "parking", StringComparison.OrdinalIgnoreCase))
                {
                    if (tags.TryGetValue("fee", out var fee) && string.Equals(fee, "no", StringComparison.OrdinalIgnoreCase))
                    {
                        freeParking = true;
                    }
                    else if (!tags.ContainsKey("fee") && tags.TryGetValue("access", out var access) &&
                             string.Equals(access, "customers", StringComparison.OrdinalIgnoreCase))
                    {
                        freeParking = true;
                    }
                }

                if (string.Equals(amenity, "charging_station", StringComparison.OrdinalIgnoreCase))
                {
                    var position = ReadCoordinate(element);
                    if (position is not null)
                    {
                        var distanceMeters = GeoUtils.HaversineDistanceMeters((point.Lat, point.Lon), position.Value);
                        var powerProxy = EstimatePower(tags);
                        competitionGravityNumerator += powerProxy / Math.Pow(distanceMeters + 75, 1.2);
                        if (powerProxy >= 150)
                        {
                            highCompetitionStations++;
                        }
                    }
                }
            }

            if (tags.TryGetValue("highway", out var highwayClass))
            {
                var length = EstimateWayLengthMeters(element);
                if (length > 0 && RoadWeights.TryGetValue(highwayClass, out var weight))
                {
                    roadWeightedLength += length * weight;
                }

                var position = ReadCoordinate(element);
                if (position is not null)
                {
                    var distanceMeters = GeoUtils.HaversineDistanceMeters((point.Lat, point.Lon), position.Value);
                    if (string.Equals(highwayClass, "primary", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(highwayClass, "secondary", StringComparison.OrdinalIgnoreCase))
                    {
                        minPrimaryDistance = Math.Min(minPrimaryDistance, distanceMeters);
                    }

                    if (string.Equals(highwayClass, "motorway", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(highwayClass, "trunk", StringComparison.OrdinalIgnoreCase))
                    {
                        minMotorwayDistance = Math.Min(minMotorwayDistance, distanceMeters);
                    }
                }
            }

            if (tags.TryGetValue("landuse", out var landuse))
            {
                if (string.Equals(landuse, "residential", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(landuse, "commercial", StringComparison.OrdinalIgnoreCase))
                {
                    residentialAreaSqm += EstimateAreaSqm(element);
                }
            }

            if (tags.TryGetValue("place", out var placeType))
            {
                settlementSignal += placeType switch
                {
                    "city" => 6,
                    "town" => 4,
                    "village" => 2,
                    "hamlet" => 1,
                    _ => 0
                };
            }

            if (tags.TryGetValue("power", out var powerType))
            {
                if (string.Equals(powerType, "substation", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(powerType, "transformer", StringComparison.OrdinalIgnoreCase))
                {
                    gridSignal += 3;
                }
                else if (string.Equals(powerType, "line", StringComparison.OrdinalIgnoreCase))
                {
                    gridSignal += 1;
                }
                else if (string.Equals(powerType, "minor_line", StringComparison.OrdinalIgnoreCase))
                {
                    gridSignal += 0.5;
                }
            }
        }

        var areaSqm = GeoUtils.PolygonAreaSquareMeters(polygon);
        if (areaSqm <= 0)
        {
            // Fallback to circular approximation when polygon is degenerate (e.g., user drew a tiny area).
            areaSqm = Math.PI * radiusMeters * radiusMeters;
        }

        var roadDensity = roadWeightedLength / (areaSqm / 1_000_000.0); // weighted km per square km.
        var residentialDensity = residentialAreaSqm / areaSqm;
        var demographyProxy = settlementSignal + (residentialDensity * 80);
        var competitionGravity = competitionGravityNumerator;
        var accessibilityProxy = Math.Min(minPrimaryDistance, minMotorwayDistance);
        if (double.IsInfinity(accessibilityProxy))
        {
            accessibilityProxy = double.NaN;
        }

        return new RawFeatureSnapshot
        {
            PoiCounts = poiCounts,
            PoiDwellScore = poiDwellScore,
            RoadDensity = double.IsFinite(roadDensity) ? roadDensity : 0,
            CompetitionGravity = competitionGravity,
            DemographyProxy = demographyProxy,
            GridProxy = gridSignal,
            AccessibilityMeters = accessibilityProxy,
            ResidentialDensity = residentialDensity,
            FreeParkingBonus = freeParking ? 1 : 0,
            HighCompetitionStations = highCompetitionStations
        };
    }

    private static double EstimateAreaSqm(JsonElement element)
    {
        if (element.TryGetProperty("geometry", out var geometryElement) && geometryElement.ValueKind == JsonValueKind.Array)
        {
            var coords = geometryElement
                .EnumerateArray()
                .Select(node => (node.GetProperty("lon").GetDouble(), node.GetProperty("lat").GetDouble()))
                .ToArray();

            if (coords.Length >= 3)
            {
                var polygon = new GeoJsonPolygon
                {
                    Coordinates = new[]
                    {
                        coords.Select(c => new[] { c.Item1, c.Item2 }).ToArray()
                    }
                };
                return GeoUtils.PolygonAreaSquareMeters(polygon);
            }
        }

        return 0;
    }

    private static Dictionary<string, string> ParseTags(JsonElement tagsElement)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var tag in tagsElement.EnumerateObject())
        {
            dict[tag.Name] = tag.Value.GetString() ?? string.Empty;
        }

        return dict;
    }

    private static (double Lat, double Lon)? ReadCoordinate(JsonElement element)
    {
        if (element.TryGetProperty("lat", out var lat) && element.TryGetProperty("lon", out var lon))
        {
            return (lat.GetDouble(), lon.GetDouble());
        }

        if (element.TryGetProperty("center", out var center) &&
            center.TryGetProperty("lat", out var cLat) && center.TryGetProperty("lon", out var cLon))
        {
            return (cLat.GetDouble(), cLon.GetDouble());
        }

        if (element.TryGetProperty("geometry", out var geometry) && geometry.ValueKind == JsonValueKind.Array)
        {
            var first = geometry.EnumerateArray().FirstOrDefault();
            if (first.ValueKind == JsonValueKind.Object &&
                first.TryGetProperty("lat", out var gLat) && first.TryGetProperty("lon", out var gLon))
            {
                return (gLat.GetDouble(), gLon.GetDouble());
            }
        }

        return null;
    }

    private static double EstimateWayLengthMeters(JsonElement element)
    {
        if (element.TryGetProperty("geometry", out var geometry) && geometry.ValueKind == JsonValueKind.Array)
        {
            var coords = geometry.EnumerateArray()
                .Select(node => (node.GetProperty("lat").GetDouble(), node.GetProperty("lon").GetDouble()))
                .ToArray();

            if (coords.Length < 2)
            {
                return 0;
            }

            double length = 0;
            for (var i = 0; i < coords.Length - 1; i++)
            {
                length += GeoUtils.HaversineDistanceMeters(coords[i], coords[i + 1]);
            }

            return length;
        }

        return 0;
    }

    private static double EstimatePower(IReadOnlyDictionary<string, string> tags)
    {
        if (tags.TryGetValue("capacity", out var capacityStr) && double.TryParse(capacityStr, out var capacity))
        {
            return capacity * 22; // capacity is number of plugs -> approximate 22 kW each.
        }

        if (tags.TryGetValue("socket:output", out var output) && double.TryParse(output.Replace("kW", string.Empty, StringComparison.OrdinalIgnoreCase), out var kw))
        {
            return kw;
        }

        if (tags.TryGetValue("max_power", out var maxPower) && double.TryParse(maxPower.Replace("kW", string.Empty, StringComparison.OrdinalIgnoreCase), out var parsed))
        {
            return parsed;
        }

        return 50; // conservative default for Level 3 chargers.
    }

    private static void IncrementPoi(IDictionary<string, int> poiCounts, string category)
    {
        if (!poiCounts.TryGetValue(category, out var current))
        {
            current = 0;
        }

        poiCounts[category] = current + 1;
    }

    private static double CategoryWeight(string category) => category switch
    {
        "mall" => 6,
        "supermarket" => 4.5,
        "office" => 3,
        "school" => 3.5,
        "hospital" => 5,
        "entertainment" => 4,
        _ => 2
    };

    private static string BuildQuery(double lat, double lon, int radiusMeters)
    {
        var builder = new StringBuilder();
        builder.AppendLine("[out:json][timeout:25];");
        builder.AppendLine("(");
        builder.AppendLine(FormattableString.Invariant($"  node(around:{radiusMeters},{lat},{lon})[amenity];"));
        builder.AppendLine(FormattableString.Invariant($"  way(around:{radiusMeters},{lat},{lon})[amenity];"));
        builder.AppendLine(FormattableString.Invariant($"  node(around:{radiusMeters},{lat},{lon})[shop];"));
        builder.AppendLine(FormattableString.Invariant($"  way(around:{radiusMeters},{lat},{lon})[shop];"));
        builder.AppendLine(FormattableString.Invariant($"  node(around:{radiusMeters},{lat},{lon})[office];"));
        builder.AppendLine(FormattableString.Invariant($"  way(around:{radiusMeters},{lat},{lon})[office];"));
        builder.AppendLine(FormattableString.Invariant($"  node(around:{radiusMeters},{lat},{lon})[highway];"));
        builder.AppendLine(FormattableString.Invariant($"  way(around:{radiusMeters},{lat},{lon})[highway];"));
        builder.AppendLine(FormattableString.Invariant($"  node(around:{radiusMeters},{lat},{lon})[landuse];"));
        builder.AppendLine(FormattableString.Invariant($"  way(around:{radiusMeters},{lat},{lon})[landuse];"));
        builder.AppendLine(FormattableString.Invariant($"  node(around:{radiusMeters},{lat},{lon})[place];"));
        builder.AppendLine(FormattableString.Invariant($"  way(around:{radiusMeters},{lat},{lon})[place];"));
        builder.AppendLine(FormattableString.Invariant($"  node(around:{radiusMeters},{lat},{lon})[power];"));
        builder.AppendLine(FormattableString.Invariant($"  way(around:{radiusMeters},{lat},{lon})[power];"));
        builder.AppendLine(");");
        builder.AppendLine("out tags center qt;");
        builder.AppendLine("out geom qt;");
        return builder.ToString();
    }
}

public sealed class RawFeatureSnapshot
{
    public Dictionary<string, int> PoiCounts { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public double PoiDwellScore { get; init; }
    public double RoadDensity { get; init; }
    public double CompetitionGravity { get; init; }
    public double DemographyProxy { get; init; }
    public double GridProxy { get; init; }
    public double AccessibilityMeters { get; init; }
    public double ResidentialDensity { get; init; }
    public int FreeParkingBonus { get; init; }
    public int HighCompetitionStations { get; init; }
}
