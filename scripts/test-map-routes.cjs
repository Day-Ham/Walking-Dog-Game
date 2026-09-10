const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const assert = require('node:assert/strict');

const html = fs.readFileSync(path.join(__dirname, '../Walking Dog/Assets/StreamingAssets/OpenFreeMapMap.html'), 'utf8');
for (const match of html.matchAll(/<script(?:\s[^>]*)?>([\s\S]*?)<\/script>/g)) new vm.Script(match[1]);
const segmentFunction = html.slice(html.indexOf('function toRouteSegments('), html.indexOf('function ensureRouteLayer('));
const segments = vm.runInNewContext(`(${segmentFunction.trim()})`);
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
