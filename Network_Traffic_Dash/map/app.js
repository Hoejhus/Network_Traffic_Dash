const map = L.map('map').setView([20, 0], 2);
L.tileLayer('https://tile.openstreetmap.org/{z}/{x}/{y}.png', {
    maxZoom: 6, attribution: '&copy; OpenStreetMap'
}).addTo(map);

const liveLayer = L.layerGroup().addTo(map);
const histMarkers = new Map(); // ip -> marker (persist)

function radiusFromBytes(bytes) { return Math.max(4, Math.log2(bytes + 1) * 1.3); }
function colorFromKey(key) {
    let h = 0; for (let i = 0; i < key.length; i++) h = (h * 31 + key.charCodeAt(i)) >>> 0;
    return `hsl(${h % 360} 80% 45%)`;
}

window.chrome?.webview?.addEventListener('message', ev => {
    const { mode, points } = ev.data || {};
    if (!points) return;

    if (mode === 'live') {
        liveLayer.clearLayers();
        points.forEach(p => {
            if (!Number.isFinite(p.lat) || !Number.isFinite(p.lon)) return;
            const m = L.circleMarker([p.lat, p.lon], {
                radius: radiusFromBytes(p.bytes),
                color: colorFromKey(p.colorKey || p.ip),
                fillOpacity: 0.35,
                weight: 2
            });
            m.bindTooltip((p.label || "").replaceAll('\n', '<br/>'), { direction: 'top' });
            m.addTo(liveLayer);
        });
    } else {
        points.forEach(p => {
            if (!Number.isFinite(p.lat) || !Number.isFinite(p.lon)) return;
            const existing = histMarkers.get(p.ip);
            if (!existing) {
                const m = L.circleMarker([p.lat, p.lon], {
                    radius: radiusFromBytes(p.bytes),
                    color: colorFromKey(p.colorKey || p.ip),
                    fillOpacity: 0.35,
                    weight: 2
                })
                    .bindTooltip((p.label || "").replaceAll('\n', '<br/>'), { direction: 'top' })
                    .addTo(map);
                histMarkers.set(p.ip, m);
            } else {
                existing.setLatLng([p.lat, p.lon]);
                existing.setRadius(radiusFromBytes(p.bytes));
                existing.setStyle({ color: colorFromKey(p.colorKey || p.ip) });
                existing.setTooltipContent((p.label || "").replaceAll('\n', '<br/>'));
            }
        });
    }
});
