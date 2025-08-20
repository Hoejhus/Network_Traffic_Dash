// --- Basemap ---
const map = L.map('map').setView([56, 10], 3);
L.tileLayer('https://tile.openstreetmap.org/{z}/{x}/{y}.png', {
    maxZoom: 6, attribution: '&copy; OpenStreetMap'
}).addTo(map);

// Layers + state
const liveLayer = L.layerGroup().addTo(map);
const liveMarkers = new Map();   // ip -> marker (rebuilt each live update)
const histMarkers = new Map();   // ip -> marker (persistent while in history)
let flightLine = null;
let flightTicket = 0;            // cancels older animations

// "Home" (startpunkt for streger). Lås gerne til dine coords:
const home = { lat: map.getCenter().lat, lon: map.getCenter().lng };
// fx: const home = { lat: 55.6761, lon: 12.5683 }; // København

// --- Utils ---
function radiusFromBytes(bytes) { return Math.max(4, Math.log2(bytes + 1) * 1.3); }
function colorFromKey(key) {
    let h = 0; for (let i = 0; i < key.length; i++) h = (h * 31 + key.charCodeAt(i)) >>> 0;
    return `hsl(${h % 360} 80% 45%)`;
}
function clearFlight() {
    if (flightLine) { map.removeLayer(flightLine); flightLine = null; }
    flightTicket++; // invalidér alle igangværende animationer
}

// Great-circle (slerp) mellem to punkter
function greatCirclePoints(a, b, steps = 90) {
    // a=[lat,lon], b=[lat,lon] i grader
    const toRad = d => d * Math.PI / 180, toDeg = r => r * 180 / Math.PI;
    let [lat1, lon1] = [toRad(a[0]), toRad(a[1])];
    let [lat2, lon2] = [toRad(b[0]), toRad(b[1])];

    const d = 2 * Math.asin(Math.sqrt(
        Math.pow(Math.sin((lat2 - lat1) / 2), 2) +
        Math.cos(lat1) * Math.cos(lat2) * Math.pow(Math.sin((lon2 - lon1) / 2), 2)
    ));
    if (!isFinite(d) || d === 0) return [a, b];

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

// Tegn animeret polyline – uden “spøgelseslinjer”
function animatePath(latlngs, color) {
    clearFlight();
    const myTicket = flightTicket;             // hver animation får sin egen billet
    const line = L.polyline([], { color, weight: 2, opacity: 0.9 }).addTo(map);
    flightLine = line;

    let i = 0;
    (function step() {
        if (myTicket !== flightTicket) return;   // afbrudt
        if (i < latlngs.length) {
            line.addLatLng(latlngs[i++]);
            requestAnimationFrame(step);
        }
    })();
}

// Opret/Opdater en marker + hover/click-animation
function upsertMarker(p, containerMapOrLayer, storeMap) {
    const color = colorFromKey(p.colorKey || p.ip);
    const opts = { radius: radiusFromBytes(p.bytes), color, fillOpacity: 0.35, weight: 2 };
    let m = storeMap.get(p.ip);

    if (!m) {
        m = L.circleMarker([p.lat, p.lon], opts).addTo(containerMapOrLayer);
        storeMap.set(p.ip, m);
    } else {
        // (live markers kan flytte/ændre radius)
        m.setLatLng([p.lat, p.lon]);
        m.setRadius(radiusFromBytes(p.bytes));
        m.setStyle({ color });
    }

    const tip = (p.label || "").replaceAll('\n', '<br/>');
    if (m.getTooltip()) m.setTooltipContent(tip); else m.bindTooltip(tip, { direction: 'top' });

    const runAnim = () => {
        const path = greatCirclePoints([home.lat, home.lon], [p.lat, p.lon], 90);
        animatePath(path, color);
        try { m.openTooltip(); } catch { }
    };

    m.off('mouseover').on('mouseover', runAnim);
    m.off('click').on('click', runAnim);

    return m;
}

// --- WebView2 messages ---
window.chrome?.webview?.addEventListener('message', ev => {
    let data = ev.data;
    if (typeof data === 'string') { try { data = JSON.parse(data); } catch { return; } }
    if (!data) return;

    // Fokus fra C# (klik i listen)
    if (data.cmd === 'focus' && data.ip) {
        const m = liveMarkers.get(data.ip) || histMarkers.get(data.ip);
        if (m) {
            const ll = m.getLatLng();
            const color = (m.options && m.options.color) || '#1976d2';
            const path = greatCirclePoints([home.lat, home.lon], [ll.lat, ll.lng], 90);
            animatePath(path, color);
            map.flyTo(ll, Math.max(map.getZoom(), 3), { duration: 0.6 });
            try { m.openTooltip(); } catch { }
        }
        return;
    }

    // Points til kortet
    const { mode, points } = data;
    if (!Array.isArray(points)) return;

    if (mode === 'live') {
        liveLayer.clearLayers();
        liveMarkers.clear();
        points.forEach(p => {
            if (Number.isFinite(p.lat) && Number.isFinite(p.lon))
                upsertMarker(p, liveLayer, liveMarkers);
        });
    } else {
        points.forEach(p => {
            if (Number.isFinite(p.lat) && Number.isFinite(p.lon))
                upsertMarker(p, map, histMarkers);
        });
    }
});
