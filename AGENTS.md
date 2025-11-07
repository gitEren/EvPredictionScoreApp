0. Purpose & Scope

EVSiteScoring (EVSS) is an AI-powered geospatial scoring system that evaluates potential EV charging station locations.
The user draws a polygon on a map, selects a target point, and receives a realistic feasibility score (0–100) based on demographic, traffic, POI, competition, and grid factors — using real or fallback data sources (Google APIs or OSM/Overpass).

Constraints

No Docker, no database.

Runs locally via dotnet run.

Opens UI automatically.

API keys are stored as placeholders in appsettings.json.

Stack: C# .NET 8 WebAPI + HTML/CSS/Vanilla JS frontend.

1. Frontend Agent (UI)

Responsibilities

Create index.html, styles.css, and app.js inside wwwroot/.

Implement the map interface using Google Maps JS; automatically fallback to Leaflet/OSM when no key is provided.

Allow the user to draw polygons, drop a marker, adjust weight sliders, and trigger scoring.

Display results (total score, per-component bars, SHAP-like contributions, warnings).

Inputs

/config, /score, /features endpoints from backend.

Outputs

Single-page responsive UI that runs on startup.

Definition of Done

“Calculate Score” button is disabled until both polygon & marker exist.

Google Maps → Leaflet fallback works seamlessly.

All /score components are visualized properly.

App opens automatically when dotnet run starts the server.

2. API Agent (Backend – C# .NET 8 WebAPI)

Responsibilities

Build a minimal API with endpoints:

GET /health

GET /config

POST /features

POST /score

Serve static files (wwwroot), configure CORS, logging, and appsettings.

Implement unified ServiceResponse { status, responseItem, message } pattern.

Inputs

appsettings.json → Public/Providers/Scoring sections.

Models: GeoJson, Weights, FeatureVector.

Outputs

A working REST API serving both JSON and the UI.

Definition of Done

dotnet run starts both API & frontend.

Friendly message for any errors; full logs for debugging.

Average /score latency < 15 s (Overpass API tolerance).

3. Maps & POI Data Agent

Responsibilities

Query spatial data providers:

Google Places API (primary, if key exists).

Overpass API (fallback, no key needed).

Open-Meteo (optional weather proxy).

Fetch POIs, road classes, and nearby charging stations (amenity=charging_station).

Inputs

target_point, polygon, radius (default 1500 m).

API keys and endpoints from appsettings.

Outputs

Raw features: POI counts, road density, competition list, etc.

Definition of Done

If Google API fails, Overpass fallback triggers automatically.

Adds warning entries (e.g., "Overpass fallback used").

Handles rate-limits via exponential backoff.

4. Feature Engineering Agent

Responsibilities

Convert raw data into normalized features:

CompetitionGravity, POI_DwellScore, RoadDensity, DemographyProxy, GridProxy, Accessibility.

Apply robust min–max scaling (5–95 percentile clipping) to 0–100 range.

Compute per-component values used in scoring.

Inputs

Maps/POI Data Agent outputs + weight configuration.

Outputs

components object (0–100 values) ready for scoring.

Definition of Done

All components are finite (no NaN/∞).

Each component within [0, 100].

Scaling parameters can be overridden through appsettings.

5. Scoring & Explainability Agent

Responsibilities

Combine components into a weighted composite score:

Score = clip(100 * (0.20*Demography +
                    0.20*Traffic +
                    0.15*POI +
                    0.20*(1-Competition) +
                    0.15*GridProxy +
                    0.10*Accessibility)
             - Penalties + Bonuses)


Apply penalties/bonuses from config.

Estimate daily sessions, kWh, and peak kW via linear formulas.

Produce feature contribution list (positive/negative) and warnings.

Inputs

Normalized features, scoring weights, penalty/bonus parameters.

Outputs

/score response including:

score

prediction

explain

components

warnings

Definition of Done

Score clamped to 0–100.

Contribution percentages consistent with total score.

Missing data handled gracefully (fallback penalty).

6. QA & Testing Agent

Responsibilities

Validate endpoint health and sample responses.

Verify full “draw polygon → drop marker → calculate score” flow.

Test fallback mode (no Google key) and normal mode (with key).

Ensure numeric outputs are plausible and deterministic.

Inputs

Running API + UI instance.

Outputs

Console or log report summarizing test results.

Definition of Done

/health → 200 OK

/config → valid JSON keys

/score → returns status=success and numerical values

Map UI interaction works without manual setup

7. Collaboration and Dependencies
Agent	Depends On	Provides To
Frontend	API Agent	End User
API Agent	Feature Engineering & Scoring Agents	Frontend
Maps/POI Data Agent	External APIs	Feature Engineering
Feature Engineering Agent	Maps/POI Data Agent	Scoring
Scoring Agent	Feature Engineering Agent	API Agent
QA Agent	All	Verification Reports
8. Definition of Done (Whole Project)

One-click launch: dotnet run opens functional UI.

Map interaction → valid score response under 15 s.

Works both with and without Google keys.

Human-readable explanations (contribution list).

Clean, commented, buildable codebase (no external dependencies).
