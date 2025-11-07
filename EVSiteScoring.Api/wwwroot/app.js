const state = {
  config: null,
  mapProvider: null,
  polygon: null,
  polygonCoords: null,
  marker: null,
  markerLastValid: null,
  weights: {
    demography: null,
    traffic: null,
    poi: null,
    competition: null,
    grid: null,
    access: null
  }
};

const weightInputs = {
  demography: document.getElementById('weight-demography'),
  traffic: document.getElementById('weight-traffic'),
  poi: document.getElementById('weight-poi'),
  competition: document.getElementById('weight-competition'),
  grid: document.getElementById('weight-grid'),
  access: document.getElementById('weight-access')
};

const weightLabels = {
  demography: document.getElementById('weight-demography-value'),
  traffic: document.getElementById('weight-traffic-value'),
  poi: document.getElementById('weight-poi-value'),
  competition: document.getElementById('weight-competition-value'),
  grid: document.getElementById('weight-grid-value'),
  access: document.getElementById('weight-access-value')
};

const mapProviderWarning = document.getElementById('map-provider-warning');
const scoreButton = document.getElementById('score-button');
const loadingEl = document.getElementById('loading');
const errorEl = document.getElementById('error');
const resultsEl = document.getElementById('results');

async function bootstrap() {
  try {
    const configResponse = await fetch('/config');
    const configJson = await configResponse.json();
    state.config = configJson.responseItem ?? configJson; // handle development mode

    applyDefaultWeights();
    initWeightEvents();

    if (state.config.useGoogleMaps) {
      await loadGoogleMaps(state.config.googleMapsApiKey);
      initGoogleMap();
      state.mapProvider = 'google';
    } else {
      await loadLeaflet();
      initLeafletMap();
      state.mapProvider = 'leaflet';
      showMapWarning('Google Maps key missing – falling back to Leaflet + OpenStreetMap.');
    }
  } catch (error) {
    showError('Failed to bootstrap configuration. ' + error.message);
  }
}

function applyDefaultWeights() {
  const defaults = state.config?.weights ?? {};
  Object.entries(weightInputs).forEach(([key, input]) => {
    const defaultValue = defaults[key] ?? 0.15;
    input.value = Number(defaultValue).toFixed(2);
    weightLabels[key].textContent = input.value;
  });
}

function initWeightEvents() {
  Object.entries(weightInputs).forEach(([key, input]) => {
    input.addEventListener('input', () => {
      weightLabels[key].textContent = Number(input.value).toFixed(2);
      state.weights[key] = Number(input.value);
    });
  });
}

function loadGoogleMaps(apiKey) {
  return new Promise((resolve, reject) => {
    if (!apiKey) {
      reject(new Error('Google Maps API key is empty.'));
      return;
    }

    window.__googleMapsReady = () => resolve();
    const script = document.createElement('script');
    script.src = `https://maps.googleapis.com/maps/api/js?key=${apiKey}&libraries=drawing,geometry&callback=__googleMapsReady`;
    script.async = true;
    script.defer = true;
    script.onerror = () => reject(new Error('Unable to load Google Maps.'));
    document.head.appendChild(script);
  });
}

function loadLeaflet() {
  return new Promise((resolve, reject) => {
    const leafletCss = document.createElement('link');
    leafletCss.rel = 'stylesheet';
    leafletCss.href = 'https://unpkg.com/leaflet@1.9.4/dist/leaflet.css';
    document.head.appendChild(leafletCss);

    const leafletDrawCss = document.createElement('link');
    leafletDrawCss.rel = 'stylesheet';
    leafletDrawCss.href = 'https://unpkg.com/leaflet-draw@1.0.4/dist/leaflet.draw.css';
    document.head.appendChild(leafletDrawCss);

    const script = document.createElement('script');
    script.src = 'https://unpkg.com/leaflet@1.9.4/dist/leaflet.js';
    script.async = true;
    script.onload = () => {
      const drawScript = document.createElement('script');
      drawScript.src = 'https://unpkg.com/leaflet-draw@1.0.4/dist/leaflet.draw.js';
      drawScript.async = true;
      drawScript.onload = resolve;
      drawScript.onerror = () => reject(new Error('Failed to load Leaflet.draw.'));
      document.head.appendChild(drawScript);
    };
    script.onerror = () => reject(new Error('Failed to load Leaflet.'));
    document.head.appendChild(script);
  });
}

function initGoogleMap() {
  const initialCenter = { lat: 41.015137, lng: 28.97953 }; // Istanbul as default
  const map = new google.maps.Map(document.getElementById('map'), {
    center: initialCenter,
    zoom: 13,
    mapTypeId: 'roadmap'
  });

  state.map = map;

  const drawingManager = new google.maps.drawing.DrawingManager({
    drawingMode: google.maps.drawing.OverlayType.POLYGON,
    drawingControl: true,
    drawingControlOptions: {
      drawingModes: [google.maps.drawing.OverlayType.POLYGON]
    }
  });
  drawingManager.setMap(map);

  google.maps.event.addListener(drawingManager, 'overlaycomplete', event => {
    if (state.polygon) {
      state.polygon.setMap(null);
    }
    state.polygon = event.overlay;
    state.polygonCoords = event.overlay.getPath().getArray().map(latLng => [latLng.lng(), latLng.lat()]);
    ensureClosedPolygon();
    drawingManager.setDrawingMode(null);
    placeOrUpdateGoogleMarker();
    updateScoreButtonState();
  });
}

function initLeafletMap() {
  const map = L.map('map').setView([39.925533, 32.866287], 13);
  L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
    attribution: '&copy; OpenStreetMap contributors'
  }).addTo(map);

  state.map = map;

  const drawnItems = new L.FeatureGroup();
  map.addLayer(drawnItems);

  const drawControl = new L.Control.Draw({
    draw: {
      marker: false,
      circle: false,
      rectangle: false,
      circlemarker: false,
      polyline: false,
      polygon: {
        allowIntersection: false,
        showArea: true,
        shapeOptions: {
          color: '#2563eb'
        }
      }
    },
    edit: {
      featureGroup: drawnItems,
      remove: true
    }
  });
  map.addControl(drawControl);

  map.on(L.Draw.Event.CREATED, event => {
    drawnItems.clearLayers();
    const layer = event.layer;
    drawnItems.addLayer(layer);
    const latLngs = layer.getLatLngs()[0].map(ll => [ll.lng, ll.lat]);
    state.polygon = layer;
    state.polygonCoords = latLngs;
    ensureClosedPolygon();
    placeOrUpdateLeafletMarker();
    updateScoreButtonState();
  });
}

function placeOrUpdateGoogleMarker() {
  if (!state.map || !Array.isArray(state.polygonCoords)) return;

  const centroid = getPolygonCentroid(state.polygonCoords);
  const latLng = new google.maps.LatLng(centroid.lat, centroid.lng);

  if (state.marker) {
    state.marker.setMap(null);
  }

  state.marker = new google.maps.Marker({
    position: latLng,
    map: state.map,
    draggable: true
  });

  state.markerLastValid = { lat: centroid.lat, lng: centroid.lng };
  hideError();

  state.marker.addListener('dragend', () => {
    const position = state.marker.getPosition();
    const candidate = [position.lng(), position.lat()];
    if (!isPointInsidePolygon(candidate, state.polygonCoords)) {
      showError('Marker must stay inside the polygon.');
      state.marker.setPosition(new google.maps.LatLng(state.markerLastValid.lat, state.markerLastValid.lng));
      return;
    }

    hideError();
    state.markerLastValid = { lat: position.lat(), lng: position.lng() };
    updateScoreButtonState();
  });
}

function placeOrUpdateLeafletMarker() {
  if (!state.map || !Array.isArray(state.polygonCoords)) return;

  const centroid = getPolygonCentroid(state.polygonCoords);
  const latLng = L.latLng(centroid.lat, centroid.lng);

  if (state.marker) {
    state.map.removeLayer(state.marker);
  }

  state.marker = L.marker(latLng, { draggable: true }).addTo(state.map);
  state.markerLastValid = { lat: centroid.lat, lng: centroid.lng };
  hideError();

  state.marker.on('dragend', event => {
    const position = event.target.getLatLng();
    const candidate = [position.lng, position.lat];
    if (!isPointInsidePolygon(candidate, state.polygonCoords)) {
      showError('Marker must stay inside the polygon.');
      state.marker.setLatLng(L.latLng(state.markerLastValid.lat, state.markerLastValid.lng));
      return;
    }

    hideError();
    state.markerLastValid = { lat: position.lat, lng: position.lng };
    updateScoreButtonState();
  });
}

function getPolygonCentroid(coords) {
  if (!Array.isArray(coords) || coords.length === 0) {
    return { lat: 0, lng: 0 };
  }

  let area = 0;
  let cx = 0;
  let cy = 0;

  for (let i = 0, j = coords.length - 1; i < coords.length; j = i++) {
    const [x0, y0] = coords[j];
    const [x1, y1] = coords[i];
    const f = x0 * y1 - x1 * y0;
    area += f;
    cx += (x0 + x1) * f;
    cy += (y0 + y1) * f;
  }

  area *= 0.5;
  if (Math.abs(area) < 1e-9) {
    const [lon, lat] = coords[0];
    return { lat, lng: lon };
  }

  const lon = cx / (6 * area);
  const lat = cy / (6 * area);
  if (!isFinite(lat) || !isFinite(lon)) {
    const [fallbackLon, fallbackLat] = coords[0];
    return { lat: fallbackLat, lng: fallbackLon };
  }

  return { lat, lng: lon };
}

function ensureClosedPolygon() {
  if (!state.polygonCoords) return;
  const first = state.polygonCoords[0];
  const last = state.polygonCoords[state.polygonCoords.length - 1];
  if (first[0] !== last[0] || first[1] !== last[1]) {
    state.polygonCoords.push(first);
  }
}

function isPointInsidePolygon(point, polygon) {
  // point: [lon, lat]; polygon: array of [lon, lat]
  let inside = false;
  for (let i = 0, j = polygon.length - 1; i < polygon.length; j = i++) {
    const xi = polygon[i][0], yi = polygon[i][1];
    const xj = polygon[j][0], yj = polygon[j][1];

    const intersect = yi > point[1] !== yj > point[1] &&
      point[0] < ((xj - xi) * (point[1] - yi)) / (yj - yi + 1e-9) + xi;
    if (intersect) inside = !inside;
  }
  return inside;
}

scoreButton.addEventListener('click', () => {
  if (!state.polygonCoords || !state.marker) {
    showError('Draw a polygon and place the marker first.');
    return;
  }
  hideError();
  submitScore();
});

async function submitScore() {
  toggleLoading(true);
  try {
    const payload = buildPayload();
    const response = await fetch('/score', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(payload)
    });

    const json = await response.json();
    if (!response.ok || json.status !== 'success') {
      throw new Error(json.message ?? 'Scoring failed');
    }

    renderResults(json.responseItem);
  } catch (error) {
    showError(error.message ?? 'Unexpected error');
  } finally {
    toggleLoading(false);
  }
}

function buildPayload() {
  const coords = state.polygonCoords.map(([lon, lat]) => [lon, lat]);
  const weights = {};
  Object.entries(state.weights).forEach(([key, value]) => {
    if (value !== null) {
      weights[key] = value;
    }
  });

  return {
    polygon: {
      type: 'Polygon',
      coordinates: [coords]
    },
    target_point: {
      lat: state.marker.getPosition ? state.marker.getPosition().lat() : state.marker.getLatLng().lat,
      lon: state.marker.getPosition ? state.marker.getPosition().lng() : state.marker.getLatLng().lng
    },
    weights
  };
}

function renderResults(result) {
  if (!result) return;

  resultsEl.innerHTML = '';
  resultsEl.classList.remove('hidden');

  const badge = document.createElement('div');
  badge.className = 'score-badge';
  badge.textContent = `Score ${Math.round(result.score)}`;
  resultsEl.appendChild(badge);

  const prediction = document.createElement('div');
  prediction.className = 'prediction';
  prediction.innerHTML = `
    <h3>Prediction</h3>
    <div class="prediction-grid">
      <div><span class="label">Sessions/day</span><span class="value">${result.prediction.sessions_per_day.toFixed(2)}</span></div>
      <div><span class="label">kWh/day</span><span class="value">${result.prediction.kwh_per_day.toFixed(2)}</span></div>
      <div><span class="label">Peak kW</span><span class="value">${result.prediction.peak_kw.toFixed(2)}</span></div>
    </div>`;
  resultsEl.appendChild(prediction);

  const components = document.createElement('div');
  components.className = 'components';
  components.innerHTML = '<h3>Component Breakdown</h3>';
  const list = document.createElement('ul');
  list.className = 'components-list';

  Object.entries(result.components).forEach(([key, value]) => {
    const item = document.createElement('li');
    item.innerHTML = `
      <span class="label">${formatComponentKey(key)}</span>
      <div class="bar"><div class="bar-fill" style="width:${Math.max(0, Math.min(100, value))}%"></div></div>
      <span class="value">${value.toFixed(1)}</span>`;
    list.appendChild(item);
  });
  components.appendChild(list);
  resultsEl.appendChild(components);

  if (Array.isArray(result.explain)) {
    const explain = document.createElement('div');
    explain.className = 'explain';
    explain.innerHTML = '<h3>Feature Contributions</h3>';
    const ul = document.createElement('ul');
    result.explain.forEach(entry => {
      const li = document.createElement('li');
      const value = Number(entry.contribution).toFixed(2);
      li.innerHTML = `<span>${entry.feature.replace(/_/g, ' ')}</span><span class="value ${value >= 0 ? 'pos' : 'neg'}">${value}</span>`;
      ul.appendChild(li);
    });
    explain.appendChild(ul);
    resultsEl.appendChild(explain);
  }

  if (Array.isArray(result.warnings) && result.warnings.length > 0) {
    const warnings = document.createElement('div');
    warnings.className = 'banner banner-warning';
    warnings.innerHTML = '<strong>Warnings</strong><ul>' + result.warnings.map(w => `<li>${w}</li>`).join('') + '</ul>';
    resultsEl.appendChild(warnings);
  }
}

function formatComponentKey(key) {
  return key.replace(/_/g, ' ').replace(/\b\w/g, char => char.toUpperCase());
}

function updateScoreButtonState() {
  const ready = Array.isArray(state.polygonCoords) && state.polygonCoords.length >= 4 && state.marker;
  scoreButton.disabled = !ready;
}

function toggleLoading(isLoading) {
  if (isLoading) {
    loadingEl.classList.remove('hidden');
    scoreButton.disabled = true;
  } else {
    loadingEl.classList.add('hidden');
    updateScoreButtonState();
  }
}

function showError(message) {
  errorEl.textContent = message;
  errorEl.classList.remove('hidden');
}

function hideError() {
  errorEl.classList.add('hidden');
  errorEl.textContent = '';
}

function showMapWarning(message) {
  mapProviderWarning.textContent = message;
  mapProviderWarning.classList.remove('hidden');
}

bootstrap();
