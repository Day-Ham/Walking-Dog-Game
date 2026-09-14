const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const assert = require('node:assert/strict');

const html = fs.readFileSync(path.join(__dirname, '../Walking Dog/Assets/StreamingAssets/OpenFreeMapMap.html'), 'utf8');
for (const match of html.matchAll(/<script(?:\s[^>]*)?>([\s\S]*?)<\/script>/g)) new vm.Script(match[1]);
const segmentFunction = html.slice(html.indexOf('function toRouteSegments('), html.indexOf('function ensureRouteLayer('));
const segments = vm.runInNewContext(segmentFunction + '\n; toRouteSegments;');
const plain = value => JSON.parse(JSON.stringify(value));
const route = [
  { lng: 121, lat: 14.5, startsNewSegment: true }, { lng: 121, lat: 14.501 },
  { lng: 122, lat: 15, startsNewSegment: true }, { lng: 122, lat: 15.001 }
];
assert.deepEqual(plain(segments(route)), [[[121, 14.5], [121, 14.501]], [[122, 15], [122, 15.001]]]);
assert.equal(segments([route[0]]).length, 0, 'A lone anchor must not draw a line');
assert.equal(segments([route[0], null, route[1]]).length, 0, 'Invalid points must not bridge a gap');
assert.equal(segments([{ ...route[0], startsNewSegment: false }, route[1]]).length, 1, 'Legacy continuous routes remain visible');

const polylines = [];
const context = {
  state: { routePoints: route }, toRouteSegments: segments,
  lonLatToWorldPixels: (x, y) => ({ x, y }),
  document: {
    getElementById: () => ({ setAttribute() {}, appendChild: p => polylines.push(p) }),
    createElementNS: () => ({ attributes: {}, setAttribute(key, value) { this.attributes[key] = value; } })
  }
};
vm.runInNewContext(html.slice(html.indexOf('function renderRasterRoute('), html.indexOf('function renderRasterFallback('))
  + '; renderRasterRoute({width: 720, height: 1280, topLeftX: 0, topLeftY: 0}, 17);', context);
assert.equal(polylines.length, 2, 'Raster fallback must preserve the same two segments');
console.log('Map route checks passed: script syntax, gaps, lone points, legacy routes, and raster rendering.');

const samplePolygon = { rings: [{ points: [{x:13468005,y:1640005},{x:13468155,y:1640005},{x:13468105,y:1640155},{x:13468005,y:1640155}] }] };
const shapes = [];
const sources = new Map();
const layers = new Map([['walk-route-casing', {}]]);
const territoryContext = vm.createContext({
  state: { territoryPolygons: [samplePolygon], territoryRevision: 'alice:1' },
  territoryCacheKey: null, territoryCache: null, vectorTerritoryRevision: null, isMapLoaded: true,
  lonLatToWorldPixels: (lng, lat) => ({ x: (lng - 120) * 100, y: lat * 10 }),
  document: {
    getElementById: () => ({ setAttribute() {}, set textContent(_) { shapes.length = 0; }, appendChild: p => shapes.push(p) }),
    createElementNS: () => ({ attributes: {}, setAttribute(key, value) { this.attributes[key] = value; } })
  },
  map: {
    getSource: key => sources.get(key), getLayer: key => layers.get(key),
    addSource: (key, source) => sources.set(key, { ...source, setData(data) { this.data = data; } }),
    addLayer: (layer, before) => { assert.equal(before, 'walk-route-casing'); layers.set(layer.id, layer); }
  }
});
vm.runInContext(html.slice(html.indexOf('function territoryGeometry('), html.indexOf('function toRouteSegments(')), territoryContext);
const geometry = vm.runInContext('territoryGeometry(state)', territoryContext);
assert.equal(geometry.features.length, 1);
const ring = plain(geometry.features[0].geometry.coordinates[0]);
assert.equal(ring.length, 5);
assert.deepEqual(ring[0], ring[4]);
assert.ok(ring[1][0] > ring[0][0] && ring[2][1] > ring[1][1], 'Projected tiles must preserve east/north orientation');
assert.equal(vm.runInContext('territoryGeometry(state)', territoryContext), geometry, 'Unchanged revisions reuse geometry');
vm.runInContext('ensureTerritoryLayer(); renderRasterTerritory({width:720,height:1280,topLeftX:0,topLeftY:0},17)', territoryContext);
assert.equal(sources.get('owned-territory').data.features.length, 1);
assert.equal(shapes.length, 1, 'Raster fallback draws territory polygons');
assert.equal(shapes[0].attributes.fill, '#20b77a');
territoryContext.state = { territoryPolygons: [], territoryRevision: 'signed-out:0' };
vm.runInContext('ensureTerritoryLayer(); renderRasterTerritory({width:720,height:1280,topLeftX:0,topLeftY:0},17)', territoryContext);
assert.equal(sources.get('owned-territory').data.features.length, 0, 'Signing out clears vector ownership');
assert.equal(shapes.length, 0, 'Signing out clears raster ownership');
sources.clear(); layers.delete('owned-territory-fill'); layers.delete('owned-territory-border');
territoryContext.state = { territoryPolygons: [samplePolygon, { rings: [{ points: [{ x: NaN, y: 0 }] }] }, null], territoryRevision: 'alice:2' };
vm.runInContext('ensureTerritoryLayer()', territoryContext);
assert.equal(sources.get('owned-territory').data.features.length, 1, 'Style reload restores valid tiles and rejects malformed tiles');
const hole = { points: [{x:13468040,y:1640040},{x:13468060,y:1640040},{x:13468060,y:1640060},{x:13468040,y:1640060}] };
territoryContext.state = { territoryPolygons: [{rings: [samplePolygon.rings[0], hole]}], territoryRevision: 'hole:1' };
vm.runInContext('ensureTerritoryLayer(); renderRasterTerritory({width:720,height:1280,topLeftX:0,topLeftY:0},17)', territoryContext);
assert.equal(sources.get('owned-territory').data.features[0].geometry.coordinates.length, 2, 'Vector polygon retains an unowned hole');
assert.equal(shapes[0].attributes['fill-rule'], 'evenodd');
assert.equal((shapes[0].attributes.d.match(/M/g) || []).length, 2, 'Raster path retains the same hole');
console.log('Territory map checks passed: projection, caching, vector layers, raster polygons, account clearing, and style reload.');

const previewUpdates = [];
let legacyPreviewCalls = 0;
const previewContext = vm.createContext({
  state: { territoryPolygons: [] },
  map: { getSource: () => ({ setData: data => previewUpdates.push(data) }) },
  completedLoop: () => true,
  createTerritory: () => legacyPreviewCalls++
});
const previewFunction = html.match(/function updateTerritory\(routePoints\) \{[\s\S]*?\n      \}/)[0];
vm.runInContext(previewFunction + '\nupdateTerritory([]);', previewContext);
assert.equal(legacyPreviewCalls, 0, 'Saved tile state must not also draw an unvalidated loop as ownership');
assert.equal(previewUpdates[0].features.length, 0, 'Saved tile state clears any older loop preview');
previewContext.state = {};
vm.runInContext('updateTerritory([]);', previewContext);
assert.equal(legacyPreviewCalls, 1, 'Legacy callers retain their existing loop preview');
console.log('Merged map checks passed: saved ownership takes precedence and the legacy preview remains compatible.');
