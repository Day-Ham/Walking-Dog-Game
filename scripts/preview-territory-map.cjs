// Generates an ignored visual fixture from the real map HTML, using synthetic coordinates.
// Open Walking Dog/Logs/territory-map-preview.html; add ?raster for fallback rendering.
const fs = require('node:fs');
const path = require('node:path');
const project = path.join(__dirname, '../Walking Dog');
let html = fs.readFileSync(path.join(project, 'Assets/StreamingAssets/OpenFreeMapMap.html'), 'utf8');
const corner = (x, y) => ({ lng: x / 6378137 * 180 / Math.PI, lat: (2 * Math.atan(Math.exp(y / 6378137)) - Math.PI / 2) * 180 / Math.PI });
const points = [[5, 5], [155, 5], [115, 155], [5, 105]].map(([x,y]) => ({x:269360 * 50 + x, y:32800 * 50 + y}));
const polygons = [{ rings: [{ points }] }];
const route = [...points, points[0]].map((p, index) => ({ ...corner(p.x, p.y), startsNewSegment: index === 0 }));
const state = { ...corner(269360 * 50 + 75, 32800 * 50 + 75), hasLocation: true, follow: true, allowGestures: true,
  zoom: 17, style: 'liberty', routePoints: route, territoryPolygons: polygons, territoryRevision: 'synthetic-preview:2', territoryMessage: 'Your territory · 15,600 m²' };
html = html.replace('if (!window.maplibregl) {', 'if (location.search.includes("raster") || !window.maplibregl) {');
html = html.replace('</head>', '<style>html {background:#102d29} body {width:390px;height:640px;margin:24px auto;border-radius:16px;overflow:hidden}</style></head>');
html = html.replace('</body>', `<script>window.updateDogWalkState(${JSON.stringify(state)});</script></body>`);
fs.mkdirSync(path.join(project, 'Logs'), { recursive: true });
fs.writeFileSync(path.join(project, 'Logs/territory-map-preview.html'), html);
console.log('Created synthetic map fixture: Walking Dog/Logs/territory-map-preview.html');
