using EVSiteScoring.Api.Domain.Models;
using EVSiteScoring.Api.Services.Providers;
using EVSiteScoring.Api.Utils;
using Microsoft.Extensions.Options;

namespace EVSiteScoring.Api.Services;

/// <summary>
/// Coordinates provider calls and normalisation logic to deliver bounded feature scores (0-100) to the scoring engine.
/// </summary>
public sealed class FeatureEngine
{
    private readonly OverpassProvider _overpassProvider;
    private readonly GooglePlacesProvider _googlePlacesProvider;
    private readonly PublicOptions _publicOptions;
    private readonly ScoringOptions _scoringOptions;
    private readonly ILogger<FeatureEngine> _logger;

    public FeatureEngine(
        OverpassProvider overpassProvider,
        GooglePlacesProvider googlePlacesProvider,
        IOptions<PublicOptions> publicOptions,
        IOptions<ScoringOptions> scoringOptions,
        ILogger<FeatureEngine> logger)
    {
        _overpassProvider = overpassProvider;
        _googlePlacesProvider = googlePlacesProvider;
        _publicOptions = publicOptions.Value;
        _scoringOptions = scoringOptions.Value;
        _logger = logger;
    }

    public async Task<FeatureExtractionResult> ExtractAsync(ScoreRequest request, CancellationToken cancellationToken)
    {
        if (request.Polygon is null || !request.Polygon.IsValid())
        {
            throw new ArgumentException("Polygon is invalid. Draw a closed polygon with at least four coordinates.");
        }

        if (request.TargetPoint is null || !request.TargetPoint.IsValid())
        {
            throw new ArgumentException("Target point is invalid. Place the marker within the polygon bounds.");
        }

        var radius = _publicOptions.DefaultRadiusMeters;
        var rawSnapshot = await _overpassProvider.FetchAsync(request.Polygon, request.TargetPoint, radius, cancellationToken).ConfigureAwait(false);

        var warnings = new List<string>();
        var usedFallback = false;

        var poiCounts = new Dictionary<string, int>(rawSnapshot.PoiCounts, StringComparer.OrdinalIgnoreCase);
        var poiScore = rawSnapshot.PoiDwellScore;

        var googleCounts = await _googlePlacesProvider.TryFetchPoiCountsAsync(request.TargetPoint, radius, cancellationToken).ConfigureAwait(false);
        if (googleCounts is not null)
        {
            // Google counts override the fallback ones for the same categories (higher accuracy & coverage).
            foreach (var kvp in googleCounts)
            {
                poiCounts[kvp.Key] = kvp.Value;
            }

            poiScore = CalculatePoiScore(poiCounts);
        }
        else
        {
            // No Google key or quota left: rely solely on Overpass results and surface a warning + penalty hook.
            usedFallback = true;
            warnings.Add("Overpass fallback used");
            poiScore = CalculatePoiScore(poiCounts);
        }

        var demographyScore = Normalize(rawSnapshot.DemographyProxy, "Demography");
        var trafficScore = Normalize(rawSnapshot.RoadDensity, "Traffic");
        var poiComponentScore = Normalize(poiScore, "POI");
        var competitionScore = Normalize(rawSnapshot.CompetitionGravity, "Competition");
        var gridScore = Normalize(rawSnapshot.GridProxy, "GridProxy");
        var accessibilityScore = NormalizeInverse(rawSnapshot.AccessibilityMeters, "Accessibility");

        if (double.IsNaN(accessibilityScore))
        {
            accessibilityScore = 40; // mild default when we cannot find any major road nearby.
            warnings.Add("No major road detected within radius");
        }

        if (rawSnapshot.HighCompetitionStations >= 4)
        {
            warnings.Add("High density of fast charging competition");
        }

        if (rawSnapshot.FreeParkingBonus > 0)
        {
            warnings.Add("Nearby free parking detected");
        }

        var components = new FeatureComponentScores(
            Demography: demographyScore,
            Traffic: trafficScore,
            Poi: poiComponentScore,
            Competition: competitionScore,
            GridProxy: gridScore,
            Accessibility: accessibilityScore);

        var rawFeatures = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            ["demography_proxy"] = rawSnapshot.DemographyProxy,
            ["road_density_weighted_km_per_km2"] = rawSnapshot.RoadDensity,
            ["poi_weighted_score"] = poiScore,
            ["competition_gravity"] = rawSnapshot.CompetitionGravity,
            ["grid_signal"] = rawSnapshot.GridProxy,
            ["accessibility_meters"] = rawSnapshot.AccessibilityMeters,
            ["residential_density_ratio"] = rawSnapshot.ResidentialDensity
        };

        return new FeatureExtractionResult
        {
            Components = components,
            RawFeatures = rawFeatures,
            Warnings = warnings,
            UsedFallback = usedFallback,
            FreeParking = rawSnapshot.FreeParkingBonus > 0,
            CompetitionRaw = rawSnapshot.CompetitionGravity,
            CompetitionNormalized = competitionScore,
            PoiScore = poiComponentScore
        };
    }

    private double Normalize(double value, string key)
    {
        // Normalisation bounds are driven by appsettings.json (Scoring:Normalization). This keeps scaling transparent.
        if (!_scoringOptions.Normalization.TryGetValue(key, out var range) || Math.Abs(range.High - range.Low) < double.Epsilon)
        {
            _logger.LogWarning("Missing or invalid normalization range for {Key}. Using default 0-100 scaling.", key);
            range = new NormalizationRange { Low = 0, High = 100 };
        }

        var normalised = (value - range.Low) / (range.High - range.Low);
        var clamped = Math.Clamp(normalised, 0, 1);
        return Math.Round(clamped * 100, 2);
    }

    private double NormalizeInverse(double value, string key)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return double.NaN;
        }

        var direct = Normalize(value, key);
        return Math.Round(100 - direct, 2);
    }

    private static double CalculatePoiScore(IReadOnlyDictionary<string, int> poiCounts)
    {
        double score = 0;
        foreach (var (category, count) in poiCounts)
        {
            score += CategoryWeight(category) * Math.Sqrt(count);
        }

        return score;
    }

    private static double CategoryWeight(string category) => category.ToLowerInvariant() switch
    {
        "mall" => 6,
        "supermarket" => 4.5,
        "office" => 3,
        "school" => 3.5,
        "hospital" => 5,
        "entertainment" => 4,
        _ => 2
    };
}

public sealed class FeatureExtractionResult
{
    public required FeatureComponentScores Components { get; init; }
    public required IReadOnlyDictionary<string, double> RawFeatures { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
    public required bool UsedFallback { get; init; }
    public required bool FreeParking { get; init; }
    public required double CompetitionRaw { get; init; }
    public required double CompetitionNormalized { get; init; }
    public required double PoiScore { get; init; }
}
