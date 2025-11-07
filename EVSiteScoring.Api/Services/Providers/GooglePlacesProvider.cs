using System.Text.Json;
using EVSiteScoring.Api.Domain.Models;
using Microsoft.Extensions.Options;

namespace EVSiteScoring.Api.Services.Providers;

/// <summary>
/// Optional Google Places provider used for POI enrichment when an API key is configured.
/// When unavailable, FeatureEngine will fall back to the Overpass-only snapshot.
/// </summary>
public sealed class GooglePlacesProvider
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ProviderOptions _options;
    private readonly ILogger<GooglePlacesProvider> _logger;

    private static readonly (string Category, string Type, string? Keyword)[] PoiRequests =
    {
        ("mall", "shopping_mall", null),
        ("supermarket", "supermarket", null),
        ("office", "point_of_interest", "office"),
        ("school", "school", null),
        ("hospital", "hospital", null),
        ("entertainment", "movie_theater", null)
    };

    public GooglePlacesProvider(IHttpClientFactory httpClientFactory, IOptions<ProviderOptions> options, ILogger<GooglePlacesProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
    }

    public bool IsConfigured => _options.EnableGooglePlaces && !string.IsNullOrWhiteSpace(_options.GooglePlacesApiKey);

    public async Task<Dictionary<string, int>?> TryFetchPoiCountsAsync(GeoJsonPoint targetPoint, int radiusMeters, CancellationToken cancellationToken)
    {
        if (!IsConfigured)
        {
            return null;
        }

        var client = _httpClientFactory.CreateClient("google-places");
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var request in PoiRequests)
        {
            try
            {
                var url = BuildUrl(targetPoint, radiusMeters, request.Type, request.Keyword);
                using var response = await client.GetAsync(url, cancellationToken).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
                if (doc.RootElement.TryGetProperty("results", out var results))
                {
                    counts[request.Category] = results.GetArrayLength();
                }
            }
            catch (Exception ex)
            {
                // Google quota errors are non-fatal. Log and continue so that the Overpass fallback still runs.
                _logger.LogWarning(ex, "Failed to query Google Places for category {Category}. Falling back to Overpass data.", request.Category);
                return null;
            }
        }

        return counts;
    }

    private string BuildUrl(GeoJsonPoint point, int radiusMeters, string type, string? keyword)
    {
        var uriBuilder = new UriBuilder("https://maps.googleapis.com/maps/api/place/nearbysearch/json");
        var query = new List<string>
        {
            $"location={point.Lat},{point.Lon}",
            $"radius={radiusMeters}",
            $"type={type}",
            $"key={_options.GooglePlacesApiKey}"
        };

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            query.Add($"keyword={Uri.EscapeDataString(keyword)}");
        }

        uriBuilder.Query = string.Join('&', query);
        return uriBuilder.Uri.ToString();
    }
}
