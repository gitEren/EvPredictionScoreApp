namespace EVSiteScoring.Api.Domain.Models;

public sealed class PublicOptions
{
    public bool UseGoogleMaps { get; set; }
    public string GoogleMapsApiKey { get; set; } = string.Empty;
    public int DefaultRadiusMeters { get; set; } = 1500;
    public double AvgKwhPerSession { get; set; } = 18;
    public double PeakKwFactor { get; set; } = 0.6;
}

public sealed class ProviderOptions
{
    public bool EnableGooglePlaces { get; set; }
    public string GooglePlacesApiKey { get; set; } = string.Empty;
    public string OverpassEndpoint { get; set; } = "https://overpass-api.de/api/interpreter";
    public string OpenMeteoEndpoint { get; set; } = "https://api.open-meteo.com/v1/forecast";
    public int RequestTimeoutSeconds { get; set; } = 25;
}

public sealed class WeightOptions
{
    public double Demography { get; set; } = 0.2;
    public double Traffic { get; set; } = 0.2;
    public double Poi { get; set; } = 0.15;
    public double Competition { get; set; } = 0.2;
    public double GridProxy { get; set; } = 0.15;
    public double Accessibility { get; set; } = 0.1;

    public (double Demography, double Traffic, double Poi, double Competition, double Grid, double Accessibility) Deconstruct() =>
        (Demography, Traffic, Poi, Competition, GridProxy, Accessibility);
}

public sealed class PenaltyOptions
{
    public double HighCompetition { get; set; } = 10;
    public double FallbackData { get; set; } = 3;
}

public sealed class BonusOptions
{
    public double FreeParkingPoi { get; set; } = 6;
}

public sealed class NormalizationRange
{
    public double Low { get; set; }
    public double High { get; set; }
}

public sealed class PredictionOptions
{
    public double BaseSessions { get; set; } = 12;
    public double TrafficMultiplier { get; set; } = 0.3;
    public double PoiMultiplier { get; set; } = 0.25;
    public double CompetitionMultiplier { get; set; } = 0.35;
    public double DemographyMultiplier { get; set; } = 0.28;
}

public sealed class ScoringOptions
{
    public WeightOptions Weights { get; set; } = new();
    public PenaltyOptions Penalties { get; set; } = new();
    public BonusOptions Bonuses { get; set; } = new();
    public Dictionary<string, NormalizationRange> Normalization { get; set; } = new();
    public PredictionOptions Prediction { get; set; } = new();
}
