using EVSiteScoring.Api.Domain.Models;
using Microsoft.Extensions.Options;

namespace EVSiteScoring.Api.Services;

/// <summary>
/// Combines normalized feature components with configured weights to produce the final EV site score and predictions.
/// </summary>
public sealed class ScoringEngine
{
    private readonly ScoringOptions _scoringOptions;
    private readonly PublicOptions _publicOptions;
    private readonly ILogger<ScoringEngine> _logger;

    public ScoringEngine(IOptions<ScoringOptions> scoringOptions, IOptions<PublicOptions> publicOptions, ILogger<ScoringEngine> logger)
    {
        _scoringOptions = scoringOptions.Value;
        _publicOptions = publicOptions.Value;
        _logger = logger;
    }

    public ScoreResponse CalculateScore(FeatureExtractionResult features, WeightOverrides? overrides)
    {
        var weights = BuildWeights(overrides);

        var contributions = new List<FeatureContribution>();
        double total = 0;

        void AddContribution(string feature, double value)
        {
            var rounded = Math.Round(value, 2);
            contributions.Add(new FeatureContribution(feature, rounded));
            total += rounded;
        }

        AddContribution("demography", weights.Demography * features.Components.Demography);
        AddContribution("traffic_road_density", weights.Traffic * features.Components.Traffic);
        AddContribution("poi_dwell_match", weights.Poi * features.Components.Poi);
        AddContribution("competition_relief", weights.Competition * (100 - features.Components.Competition));
        AddContribution("grid_proxy", weights.GridProxy * features.Components.GridProxy);
        AddContribution("accessibility", weights.Accessibility * features.Components.Accessibility);

        if (features.CompetitionNormalized > 75)
        {
            // Penalty magnitude lives in Scoring:Penalties:HighCompetition.
            AddContribution("penalty_high_competition", -_scoringOptions.Penalties.HighCompetition);
        }

        if (features.UsedFallback)
        {
            // Overpass-only runs are slightly riskier, so we subtract the configurable fallback penalty.
            AddContribution("penalty_fallback_uncertainty", -_scoringOptions.Penalties.FallbackData);
        }

        if (features.FreeParking)
        {
            // Free parking detection awards the configured bonus (Scoring:Bonuses:FreeParkingPoi).
            AddContribution("bonus_free_parking", _scoringOptions.Bonuses.FreeParkingPoi);
        }

        var score = Math.Clamp(total, 0, 100);

        var prediction = BuildPrediction(features.Components, score);

        return new ScoreResponse
        {
            Score = score,
            Prediction = prediction,
            Explain = contributions,
            Components = features.Components,
            Warnings = features.Warnings.ToArray()
        };
    }

    private WeightOptions BuildWeights(WeightOverrides? overrides)
    {
        // Weight overrides come from the client sliders. Absent values fall back to appsettings.json defaults.
        var weights = new WeightOptions
        {
            Demography = overrides?.Demography ?? _scoringOptions.Weights.Demography,
            Traffic = overrides?.Traffic ?? _scoringOptions.Weights.Traffic,
            Poi = overrides?.Poi ?? _scoringOptions.Weights.Poi,
            Competition = overrides?.Competition ?? _scoringOptions.Weights.Competition,
            GridProxy = overrides?.Grid ?? _scoringOptions.Weights.GridProxy,
            Accessibility = overrides?.Access ?? _scoringOptions.Weights.Accessibility
        };

        var sum = weights.Demography + weights.Traffic + weights.Poi + weights.Competition + weights.GridProxy + weights.Accessibility;
        if (sum <= 0)
        {
            _logger.LogWarning("Received zero weights; reverting to defaults.");
            return _scoringOptions.Weights;
        }

        // Keep the vector normalised so contributions sum nicely to the final 0-100 score.
        weights.Demography /= sum;
        weights.Traffic /= sum;
        weights.Poi /= sum;
        weights.Competition /= sum;
        weights.GridProxy /= sum;
        weights.Accessibility /= sum;

        return weights;
    }

    private PredictionResult BuildPrediction(FeatureComponentScores components, double score)
    {
        var predictionOptions = _scoringOptions.Prediction;
        var sessions = predictionOptions.BaseSessions
            + predictionOptions.TrafficMultiplier * (components.Traffic / 100)
            + predictionOptions.PoiMultiplier * (components.Poi / 100)
            - predictionOptions.CompetitionMultiplier * (components.Competition / 100)
            + predictionOptions.DemographyMultiplier * (components.Demography / 100);

        sessions = Math.Max(sessions, 1.5);
        var kwh = sessions * _publicOptions.AvgKwhPerSession;
        var peakKw = _publicOptions.PeakKwFactor * kwh / 10.0;

        return new PredictionResult(
            SessionsPerDay: Math.Round(sessions, 2),
            KwhPerDay: Math.Round(kwh, 2),
            PeakKw: Math.Round(peakKw, 2));
    }
}
