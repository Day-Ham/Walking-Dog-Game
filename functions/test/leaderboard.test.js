"use strict";

const { test, before, beforeEach, after } = require("node:test");
const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const { initializeTestEnvironment, assertFails, assertSucceeds } = require("@firebase/rules-unit-testing");
const { initializeApp, deleteApp } = require("firebase-admin/app");
const { getFirestore } = require("firebase-admin/firestore");
const { doc, setDoc, getDoc, getDocs, collection, query, orderBy, limit, serverTimestamp } = require("firebase/firestore");
const { countWalk, refreshDisplayName, PLAYERS } = require("../leaderboard");

// Fail closed: these integration tests must never use a real database.
const host = process.env.FIRESTORE_EMULATOR_HOST;
if (!host || !/^(127\.0\.0\.1|localhost):\d+$/.test(host))
  throw new Error("Run npm run test:emulator; a local Firestore emulator is required.");
const projectId = "demo-walking-dog";
let environment, app, db;
const walk = (id, steps = 100, distance = 75) => ({ schemaVersion: 1, id,
  startedAtUtc: "2026-09-16T01:00:00Z", endedAtUtc: "2026-09-16T01:02:00Z",
  steps, distanceMeters: distance, durationSeconds: 120 });
const seed = (uid, id, steps, distance) => db.doc(`users/${uid}/walks/${id}`).set(walk(id, steps, distance));
const player = uid => db.doc(`${PLAYERS}/${uid}`).get();

before(async () => {
  environment = await initializeTestEnvironment({ projectId,
    firestore: { rules: fs.readFileSync(path.join(__dirname, "../../firestore.rules"), "utf8") } });
  app = initializeApp({ projectId }, "leaderboard-tests");
  db = getFirestore(app);
});
beforeEach(async () => environment.clearFirestore());
after(async () => { await environment?.cleanup(); if (app) await deleteApp(app); });

test("duplicate delivery and backfill count one walk only once", async () => {
  await seed("alice", "one");
  await Promise.all(Array.from({ length: 4 }, () => countWalk(db, "alice", "one")));
  assert.equal(await countWalk(db, "alice", "one"), "already-counted");
  const data = (await player("alice")).data();
  assert.equal(data.totalSteps, 100);
  assert.equal(data.totalDistanceMeters, 75);
  assert.equal(data.completedWalkCount, 1);
  assert.match(data.displayName, /^Walker-[0-9a-f]{8}$/);
  assert.deepEqual(Object.keys(data).sort(), ["schemaVersion", "displayName", "totalSteps", "totalDistanceMeters", "completedWalkCount", "updatedAt"].sort());
});

test("concurrent different walks accumulate and users stay separate", async () => {
  await Promise.all([seed("alice", "one", 100, 70), seed("alice", "two", 200, 140), seed("bob", "one", 50, 30)]);
  await Promise.all([countWalk(db, "alice", "one"), countWalk(db, "alice", "two"), countWalk(db, "bob", "one")]);
  assert.equal((await player("alice")).get("totalSteps"), 300);
  assert.equal((await player("alice")).get("totalDistanceMeters"), 210);
  assert.equal((await player("alice")).get("completedWalkCount"), 2);
  assert.equal((await player("bob")).get("totalSteps"), 50);
});

test("invalid and empty walks are excluded, while steps-only walks count", async () => {
  await seed("alice", "bad", -1, 100);
  await seed("alice", "empty", 0, 0);
  assert.equal(await countWalk(db, "alice", "bad"), "invalid");
  assert.equal(await countWalk(db, "alice", "empty"), "empty");
  assert.equal((await player("alice")).exists, false);
  await seed("alice", "steps", 80, 0);
  assert.equal(await countWalk(db, "alice", "steps"), "counted");
});

test("failed totals validation does not commit a receipt or lose the walk", async () => {
  await seed("alice", "one");
  await db.doc(`${PLAYERS}/alice`).set({ totalSteps: Number.MAX_SAFE_INTEGER, totalDistanceMeters: 0, completedWalkCount: 1 });
  await assert.rejects(countWalk(db, "alice", "one"), /overflowing/);
  assert.equal((await db.doc("leaderboardReceipts/alice/walks/one").get()).exists, false);
  await db.doc(`${PLAYERS}/alice`).delete();
  assert.equal(await countWalk(db, "alice", "one"), "counted");
});

test("name refresh uses the latest profile and does not change scores", async () => {
  await db.doc("leaderboardProfiles/alice").set({ displayName: "First Name" });
  await seed("alice", "one");
  await countWalk(db, "alice", "one");
  assert.equal((await player("alice")).get("displayName"), "First Name");
  await db.doc("leaderboardProfiles/alice").set({ displayName: "Latest Name" });
  await Promise.all([refreshDisplayName(db, "alice"), refreshDisplayName(db, "alice")]);
  assert.equal((await player("alice")).get("displayName"), "Latest Name");
  assert.equal((await player("alice")).get("totalSteps"), 100);
});

test("authenticated users can sort both metrics and read their entry outside top 50", async () => {
  const batch = db.batch();
  for (let i = 0; i < 52; i++) batch.set(db.doc(`${PLAYERS}/player${i}`), {
    schemaVersion: 1, displayName: `Player ${i}`, totalSteps: 52 - i,
    totalDistanceMeters: i, completedWalkCount: 1
  });
  await batch.commit();
  const client = environment.authenticatedContext("player0").firestore();
  const distance = await assertSucceeds(getDocs(query(collection(client, PLAYERS), orderBy("totalDistanceMeters", "desc"), limit(50))));
  assert.equal(distance.size, 50);
  assert.equal(distance.docs[0].id, "player51");
  assert.equal(distance.docs.some(doc => doc.id === "player0"), false);
  await assertSucceeds(getDoc(doc(client, `${PLAYERS}/player0`)));
  const steps = await getDocs(query(collection(client, PLAYERS), orderBy("totalSteps", "desc"), limit(50)));
  assert.equal(steps.docs[0].id, "player0");
});

test("clients cannot forge scores, receipts, or read the board signed out", async () => {
  const client = environment.authenticatedContext("alice").firestore();
  await assertFails(setDoc(doc(client, `${PLAYERS}/alice`), { totalSteps: 999 }));
  await assertFails(setDoc(doc(client, "leaderboardReceipts/alice/walks/one"), { countedAt: serverTimestamp() }));
  await assertFails(getDoc(doc(client, "leaderboardReceipts/alice/walks/one")));
  const guest = environment.unauthenticatedContext().firestore();
  await assertFails(getDocs(collection(guest, PLAYERS)));
});

test("nickname writes are owner-only and reject extra score fields and markup", async () => {
  const client = environment.authenticatedContext("alice").firestore();
  const ref = doc(client, "leaderboardProfiles/alice");
  await assertSucceeds(setDoc(ref, { displayName: "Mochi Walker", updatedAt: serverTimestamp() }));
  await assertSucceeds(getDoc(ref));
  await assertFails(setDoc(ref, { displayName: "Mochi", updatedAt: serverTimestamp(), totalSteps: 999 }));
  for (const displayName of ["Hi", "a".repeat(25), "<b>Dog</b>", " Dog", "Dog\n"])
    await assertFails(setDoc(ref, { displayName, updatedAt: serverTimestamp() }));
  await assertFails(setDoc(doc(client, "leaderboardProfiles/bob"), { displayName: "Mochi", updatedAt: serverTimestamp() }));
  await assertFails(getDoc(doc(client, "leaderboardProfiles/bob")));
});

test("existing private walk save/retry rules remain intact", async () => {
  const alice = environment.authenticatedContext("alice").firestore();
  const bob = environment.authenticatedContext("bob").firestore();
  const ref = doc(alice, "users/alice/walks/one");
  const payload = { ...walk("one"), uploadedAt: serverTimestamp() };
  await assertSucceeds(setDoc(ref, payload));
  await assertSucceeds(setDoc(ref, { ...payload, uploadedAt: serverTimestamp() }));
  await assertSucceeds(getDoc(ref));
  await assertFails(getDoc(doc(bob, "users/alice/walks/one")));
  await assertFails(setDoc(ref, { ...payload, steps: 999, uploadedAt: serverTimestamp() }));
});
