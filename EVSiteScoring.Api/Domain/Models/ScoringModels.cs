using System.Text.Json.Serialization;

namespace EVSiteScoring.Api.Domain.Models;

public sealed class WeightOverrides
{
    [JsonPropertyName("demography")]
    public double? Demography { get; set; }

    [JsonPropertyName("traffic")]
    public double? Traffic { get; set; }

    [JsonPropertyName("poi")]
    public double? Poi { get; set; }

    [JsonPropertyName("competition")]
    public double? Competition { get; set; }

    [JsonPropertyName("grid")]
    public double? Grid { get; set; }

    [JsonPropertyName("access")]
    public double? Access { get; set; }
}

public sealed class ScoreRequest
{
    [JsonPropertyName("polygon")]
    public GeoJsonPolygon? Polygon { get; set; }

    [JsonPropertyName("target_point")]
    public GeoJsonPoint? TargetPoint { get; set; }

    [JsonPropertyName("weights")]
    public WeightOverrides? Weights { get; set; }
}

public sealed record FeatureComponentScores(
    [property: JsonPropertyName("demography")] double Demography,
    [property: JsonPropertyName("traffic")] double Traffic,
    [property: JsonPropertyName("poi")] double Poi,
    [property: JsonPropertyName("competition")] double Competition,
    [property: JsonPropertyName("grid_proxy")] double GridProxy,
    [property: JsonPropertyName("accessibility")] double Accessibility);

public sealed record FeatureContribution(
    [property: JsonPropertyName("feature")] string Feature,
    [property: JsonPropertyName("contribution")] double Contribution);

public sealed record PredictionResult(
    [property: JsonPropertyName("sessions_per_day")] double SessionsPerDay,
    [property: JsonPropertyName("kwh_per_day")] double KwhPerDay,
    [property: JsonPropertyName("peak_kw")] double PeakKw);

public sealed class ScoreResponse
{
    public required double Score { get; init; }
    public required PredictionResult Prediction { get; init; }
    public required IReadOnlyList<FeatureContribution> Explain { get; init; }
    public required FeatureComponentScores Components { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
}

public sealed class FeatureResponse
{
    public required FeatureComponentScores Components { get; init; }
    public required IReadOnlyDictionary<string, double> RawFeatures { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
}

public sealed class ServiceResponse<T>
{
    public ServiceResponse(string status, T? responseItem, string? message)
    {
        Status = status;
        ResponseItem = responseItem;
        Message = message;
    }

    [JsonPropertyName("status")]
    public string Status { get; }

    [JsonPropertyName("responseItem")]
    public T? ResponseItem { get; }

    [JsonPropertyName("message")]
    public string? Message { get; }

    public static ServiceResponse<T> Success(T payload) => new("success", payload, null);

    public static ServiceResponse<T> Failure(string message) => new("error", default, message);
}
