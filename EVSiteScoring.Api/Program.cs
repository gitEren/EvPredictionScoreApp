using EVSiteScoring.Api.Domain.Models;
using EVSiteScoring.Api.Services;
using EVSiteScoring.Api.Services.Providers;
using EVSiteScoring.Api.Utils;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

// Configuration is driven by appsettings.json; expose strongly typed option classes for DI consumers.
builder.Services.Configure<PublicOptions>(builder.Configuration.GetSection("Public"));
builder.Services.Configure<ProviderOptions>(builder.Configuration.GetSection("Providers"));
builder.Services.Configure<ScoringOptions>(builder.Configuration.GetSection("Scoring"));

builder.Services.AddSingleton<OverpassProvider>();
builder.Services.AddSingleton<GooglePlacesProvider>();
builder.Services.AddSingleton<FeatureEngine>();
builder.Services.AddSingleton<ScoringEngine>();

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

builder.Services.AddHttpClient("overpass", (serviceProvider, client) =>
{
    var options = serviceProvider.GetRequiredService<IOptions<ProviderOptions>>().Value;
    // Overpass endpoint & timeout fully configurable via Providers section.
    client.BaseAddress = new Uri(options.OverpassEndpoint);
    client.Timeout = TimeSpan.FromSeconds(Math.Max(5, options.RequestTimeoutSeconds));
    client.DefaultRequestHeaders.UserAgent.ParseAdd("EVSiteScoring/1.0 (+https://example.com)");
});

builder.Services.AddHttpClient("google-places", (serviceProvider, client) =>
{
    var options = serviceProvider.GetRequiredService<IOptions<ProviderOptions>>().Value;
    // Google quota is optional; we simply align timeout with Overpass for consistency.
    client.Timeout = TimeSpan.FromSeconds(Math.Max(5, options.RequestTimeoutSeconds));
});

var app = builder.Build();

app.UseCors();
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/health", () => Results.Ok("ok"));

app.MapGet("/config", ([FromServices] IOptions<PublicOptions> publicOptions, [FromServices] IOptions<ScoringOptions> scoringOptions) =>
{
    var publicConfig = publicOptions.Value;
    var scoringConfig = scoringOptions.Value;

    var payload = new
    {
        useGoogleMaps = publicConfig.UseGoogleMaps && !string.IsNullOrWhiteSpace(publicConfig.GoogleMapsApiKey),
        googleMapsApiKey = publicConfig.GoogleMapsApiKey,
        defaultRadiusMeters = publicConfig.DefaultRadiusMeters,
        weights = new
        {
            demography = scoringConfig.Weights.Demography,
            traffic = scoringConfig.Weights.Traffic,
            poi = scoringConfig.Weights.Poi,
            competition = scoringConfig.Weights.Competition,
            grid = scoringConfig.Weights.GridProxy,
            access = scoringConfig.Weights.Accessibility
        }
    };

    return Results.Json(ServiceResponse<object>.Success(payload));
});

app.MapPost("/features", async ([FromBody] ScoreRequest request, FeatureEngine featureEngine, CancellationToken cancellationToken) =>
{
    try
    {
        var extraction = await featureEngine.ExtractAsync(request, cancellationToken).ConfigureAwait(false);
        var response = new FeatureResponse
        {
            Components = extraction.Components,
            RawFeatures = extraction.RawFeatures,
            Warnings = extraction.Warnings
        };

        return Results.Json(ServiceResponse<FeatureResponse>.Success(response));
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(ServiceResponse<FeatureResponse>.Failure(ex.Message));
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Unhandled error while extracting features");
        return Results.Problem(ex.Message);
    }
});

app.MapPost("/score", async ([FromBody] ScoreRequest request, FeatureEngine featureEngine, ScoringEngine scoringEngine, CancellationToken cancellationToken) =>
{
    try
    {
        var extraction = await featureEngine.ExtractAsync(request, cancellationToken).ConfigureAwait(false);
        var score = scoringEngine.CalculateScore(extraction, request.Weights);
        return Results.Json(ServiceResponse<ScoreResponse>.Success(score));
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(ServiceResponse<ScoreResponse>.Failure(ex.Message));
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Unhandled error while scoring location");
        return Results.Problem(ex.Message);
    }
});

app.MapFallbackToFile("index.html");

app.Lifetime.ApplicationStarted.Register(() =>
{
    var logger = app.Logger;
    var url = app.Urls.FirstOrDefault() ?? "http://localhost:5000";
    BrowserLauncher.Launch(url, logger);
});

app.Run();
