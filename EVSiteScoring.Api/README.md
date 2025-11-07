# EVSiteScoring (EVSS)

EVSiteScoring (EVSS) is a single-command web application that scores potential EV charging locations using a hybrid of premium (Google) and open data sources. The backend is a .NET 8 minimal API that serves the frontend, performs feature extraction via Overpass/Google Places, normalises the data, and returns a transparent score breakdown.

## Quick start

1. (Optional) Update `appsettings.json` with valid Google Maps/Places API keys. Leaving the placeholders empty automatically enables the Overpass fallback.
2. Restore dependencies and launch the app:

   ```bash
   dotnet restore
   dotnet run
   ```

   Kestrel automatically opens your default browser once the API is ready.

3. Browse to the printed URL (defaults to `http://localhost:5000` if the browser does not open).

## Usage

1. Draw a polygon around the candidate catchment area.
2. Drag the target marker (auto-dropped inside the polygon) to your preferred spot within the boundary.
3. Optionally tweak the component weights (they are normalised server-side).
4. Press **Calculate Score** to receive:
   * A 0–100 feasibility score.
   * Per-component 0–100 subscores.
   * Contribution list showing how each factor affects the final score, including penalties/bonuses.
   * Demand proxies (sessions/day, kWh/day, peak kW).
   * Warnings (e.g., Overpass fallback used, high competition).

The UI automatically falls back to Leaflet + OpenStreetMap tiles when a Google Maps JavaScript key is not provided.

## API surface

| Method | Endpoint   | Description |
|--------|------------|-------------|
| GET    | `/health`  | Returns `"ok"` for simple health probes. |
| GET    | `/config`  | Exposes public configuration (Google Maps usage, default weights, default radius). |
| POST   | `/features`| Returns raw/normalised features for a polygon + target point (useful for debugging). |
| POST   | `/score`   | Returns the full scoring payload (score, prediction, components, explainability, warnings). |

All endpoints follow the `ServiceResponse` contract `{ status, responseItem, message }`.

## Configuration

`appsettings.json` contains the non-secret configuration:

* `Public` — Maps behaviour, radius, demand proxies.
* `Providers` — API keys/endpoints for Google Places and Overpass. The application automatically chooses Google when `EnableGooglePlaces` and the key are set; otherwise it falls back to Overpass and adds a warning/penalty.
* `Scoring` — Component weights, penalties, bonuses, normalisation bounds, and demand prediction coefficients.

You can override these values via environment variables or `appsettings.Development.json` as usual.

## Sample locations

Paste the following coordinates when testing:

* **Istanbul (Istinye Park Mall)** — draw around `41.1097, 29.0247`.
* **Ankara (Middle East Technical University)** — draw around `39.8921, 32.7780`.
* **Izmir (Konak Square)** — draw around `38.4192, 27.1287`.

## Architecture overview

* **Program.cs** wires up the minimal API, static file hosting, service registration, and automatic browser launch.
* **Services/Providers** contain the Overpass and Google Places integrations. Overpass is the authoritative fallback and provides roads, POIs, demography proxies, and competition data. Google Places enriches POI counts when available.
* **FeatureEngine** orchestrates provider calls, performs robust normalisation (with clearly commented fallbacks), and emits component scores ready for scoring.
* **ScoringEngine** combines the components with configured weights, applies penalties/bonuses (competition surge, fallback uncertainty, free parking bonus), and generates demand proxies plus the explanation list.
* **Frontend (wwwroot)** implements a responsive vanilla JS UI with Google Maps / Leaflet fallback, weight sliders, and score visualisation.

## Development notes

* All provider calls use `HttpClientFactory` with retry/backoff to tolerate transient Overpass errors.
* Nullability is enabled and all async calls use `async/await` patterns.
* Inline comments highlight where fallbacks, normalisation, and configurable weights apply.

Enjoy exploring EV siting scenarios with transparent, data-driven scoring!
