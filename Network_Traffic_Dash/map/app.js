// --- Basemap ---
const map = L.map('map').setView([56, 10], 3);
L.tileLayer('https://tile.openstreetmap.org/{z}/{x}/{y}.png', {
    maxZoom: 6, attribution: '&copy; OpenStreetMap'
}).addTo(map);

// Layers og state
const liveLayer = L.layerGroup().addTo(map);
const histMarkers = new Map();  
let flight;                      

// Vælg din "hjemme"-position (bruges som startpunkt for stregerne)
const home = { lat: map.getCenter().lat, lon: map.getCenter().lng };

// --- Utils ---
function radiusFromBytes(bytes) { return Math.max(4, Math.log2(bytes + 1) * 1.3); }
function colorFromKey(key) {
    let h = 0; for (let i = 0; i < key.length; i++) h = (h * 31 + key.charCodeAt(i)) >>> 0;
    return `hsl(${h % 360} 80% 45%)`;
}
function clearFlight() { if (flight) { map.removeLayer(flight); flight = null; } }

// Great-circle beregning (slerp) mellem to punkter
function greatCirclePoints(a, b, steps = 80) {
    // a=[lat,lon], b=[lat,lon] i grader
    const toRad = d => d * Math.PI / 180, toDeg = r => r * 180 / Math.PI;
    let [lat1, lon1] = [toRad(a[0]), toRad(a[1])];
    let [lat2, lon2] = [toRad(b[0]), toRad(b[1])];

    const d = 2 * Math.asin(Math.sqrt(
        Math.pow(Math.sin((lat2 - lat1) / 2), 2) +
        Math.cos(lat1) * Math.cos(lat2) * Math.pow(Math.sin((lon2 - lon1) / 2), 2)
    ));
    if (d === 0 || isNaN(d)) return [a, b];

    const pts = [];
    for (let i = 0; i <= steps; i++) {
        const f = i / steps;
        const A = Math.sin((1 - f) * d) / Math.sin(d);
        const B = Math.sin(f * d) / Math.sin(d);
        const x = A * Math.cos(lat1) * Math.cos(lon1) + B * Math.cos(lat2) * Math.cos(lon2);
        const y = A * Math.cos(lat1) * Math.sin(lon1) + B * Math.cos(lat2) * Math.sin(lon2);
        const z = A * Math.sin(lat1) + B * Math.sin(lat2);
        const lat = Math.atan2(z, Math.sqrt(x * x + y * y));
        const lon = Math.atan2(y, x);
        pts.push([toDeg(lat), toDeg(lon)]);
    }
    return pts;
}

// Tegn animeret polyline
function animatePath(latlngs, color) {
    clearFlight();
    flight = L.polyline([], { color, weight: 2, opacity: 0.9 }).addTo(map);
    let i = 0;
    function step() {
        if (i < latlngs.length) {
            flight.addLatLng(latlngs[i++]);
            requestAnimationFrame(step);
        }
    }
    step();
}

function upsertMarkerForPoint(p, layerOrMap) {
    const color = colorFromKey(p.colorKey || p.ip);
    const opts = { radius: radiusFromBytes(p.bytes), color, fillOpacity: 0.35, weight: 2 };

    let m;
    if (layerOrMap === map) {
        // historik: genbrug hvis den findes
        m = histMarkers.get(p.ip);
        if (!m) {
            m = L.circleMarker([p.lat, p.lon], opts).addTo(map);
            histMarkers.set(p.ip, m);
        } else {
            m.setLatLng([p.lat, p.lon]);
            m.setRadius(radiusFromBytes(p.bytes));
            m.setStyle({ color });
        }
    } else {
        m = L.circleMarker([p.lat, p.lon], opts).addTo(layerOrMap);
    }

    // Tooltip
    const tip = (p.label || "").replaceAll('\n', '<br/>');
    if (m.getTooltip()) m.setTooltipContent(tip); else m.bindTooltip(tip, { direction: 'top' });

    m.off('mouseover'); // undgå dobbelthooks
    m.on('mouseover', () => {
        const path = greatCirclePoints([home.lat, home.lon], [p.lat, p.lon], 90);
        animatePath(path, color);
    });

    return m;
}

// --- WebView2 message handler ---
window.chrome?.webview?.addEventListener('message', ev => {
    let data = ev.data;
    if (typeof data === 'string') { try { data = JSON.parse(data); } catch { return; } }
    if (!data || !Array.isArray(data.points)) return;

    const { mode, points } = data;

    if (mode === 'live') {
        liveLayer.clearLayers();
        points.forEach(p => {
            if (Number.isFinite(p.lat) && Number.isFinite(p.lon))
                upsertMarkerForPoint(p, liveLayer);
        });
    } else {
        points.forEach(p => {
            if (Number.isFinite(p.lat) && Number.isFinite(p.lon))
                upsertMarkerForPoint(p, map);
        });
    }
});
