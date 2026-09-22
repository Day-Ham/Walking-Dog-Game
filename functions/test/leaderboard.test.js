"use strict";

const { test, before, beforeEach, after } = require("node:test");
const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const { initializeTestEnvironment, assertFails, assertSucceeds } = require("@firebase/rules-unit-testing");
const { initializeApp, deleteApp } = require("firebase-admin/app");
const { getFirestore } = require("firebase-admin/firestore");
const { doc, setDoc, getDoc, getDocs, collection, query, orderBy, limit, serverTimestamp,
  runTransaction, writeBatch, deleteDoc } = require("firebase/firestore");
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
  assert.deepEqual(Object.keys(data).sort(), ["schemaVersion", "displayName", "totalSteps", "totalDistanceMeters", "completedWalkCount", "lastWalkId", "updatedAt"].sort());
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
  await assertSucceeds(getDoc(doc(client, "leaderboardReceipts/alice/walks/one")));
  await assertFails(getDoc(doc(client, "leaderboardReceipts/bob/walks/one")));
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

// Same transaction protocol as the Unity writer, executed under actual client
// rules instead of Admin privileges. These tests are the Spark security boundary.
async function countClient(client, uid, id) {
  return runTransaction(client, async tx => {
    const receiptRef = doc(client, `leaderboardReceipts/${uid}/walks/${id}`);
    if ((await tx.get(receiptRef)).exists()) return "already-counted";
    const walkData = (await tx.get(doc(client, `users/${uid}/walks/${id}`))).data();
    const playerRef = doc(client, `${PLAYERS}/${uid}`);
    const old = (await tx.get(playerRef)).data() || { totalSteps: 0, totalDistanceMeters: 0, completedWalkCount: 0 };
    const profile = (await tx.get(doc(client, `leaderboardProfiles/${uid}`))).data();
    tx.set(playerRef, { schemaVersion: 1, displayName: profile?.displayName || old.displayName || "Walker-Test",
      totalSteps: old.totalSteps + walkData.steps, totalDistanceMeters: old.totalDistanceMeters + walkData.distanceMeters,
      completedWalkCount: old.completedWalkCount + 1, lastWalkId: id, updatedAt: serverTimestamp() });
    tx.set(receiptRef, { countedAt: serverTimestamp() });
    return "counted";
  });
}

test("Spark client can count a saved walk once and retry without duplication", async () => {
  const client = environment.authenticatedContext("alice").firestore();
  await seed("alice", "one");
  await assertSucceeds(countClient(client, "alice", "one"));
  assert.equal(await countClient(client, "alice", "one"), "already-counted");
  assert.equal((await player("alice")).get("totalSteps"), 100);
  assert.equal((await player("alice")).get("completedWalkCount"), 1);
});

test("Spark concurrent walks preserve totals, including fractional distance", async () => {
  const client = environment.authenticatedContext("alice").firestore();
  await Promise.all([seed("alice", "one", 100, 12.345), seed("alice", "two", 200, 56.789)]);
  await assertSucceeds(Promise.all([countClient(client, "alice", "one"), countClient(client, "alice", "two")]));
  assert.equal((await player("alice")).get("totalSteps"), 300);
  assert.equal((await player("alice")).get("totalDistanceMeters"), 12.345 + 56.789);
});

function forgedBatch(client, id, overrides = {}, withReceipt = true, uid = "alice") {
  const batch = writeBatch(client);
  batch.set(doc(client, `${PLAYERS}/${uid}`), { schemaVersion: 1, displayName: "Walker-Test", totalSteps: 100,
    totalDistanceMeters: 75, completedWalkCount: 1, lastWalkId: id, updatedAt: serverTimestamp(), ...overrides });
  if (withReceipt) batch.set(doc(client, `leaderboardReceipts/${uid}/walks/${id}`), { countedAt: serverTimestamp() });
  return batch;
}

test("Spark rejects missing walk, missing receipt, forged delta and foreign ownership", async () => {
  const client = environment.authenticatedContext("alice").firestore();
  await assertFails(forgedBatch(client, "missing").commit());
  await seed("alice", "one");
  await assertFails(forgedBatch(client, "one", {}, false).commit());
  await assertFails(forgedBatch(client, "one", { totalSteps: 999 }).commit());
  await assertFails(forgedBatch(client, "one", { totalDistanceMeters: 999 }).commit());
  await assertFails(forgedBatch(client, "one", { completedWalkCount: 2 }).commit());
  await seed("bob", "one");
  await assertFails(forgedBatch(client, "one", {}, true, "bob").commit());
  assert.equal((await player("alice")).exists, false);
});

test("Spark forbids receipt replacement/deletion and counting a walk twice", async () => {
  const client = environment.authenticatedContext("alice").firestore();
  await seed("alice", "one");
  await countClient(client, "alice", "one");
  await assertFails(forgedBatch(client, "one", { totalSteps: 200, totalDistanceMeters: 150, completedWalkCount: 2 }).commit());
  await assertFails(deleteDoc(doc(client, "leaderboardReceipts/alice/walks/one")));
  await assertFails(deleteDoc(doc(client, `${PLAYERS}/alice`)));
  await assertFails(setDoc(doc(client, "leaderboardReceipts/alice/walks/one"), { countedAt: serverTimestamp() }));
});

test("Spark rejects two receipts paired with only one score increment", async () => {
  const client = environment.authenticatedContext("alice").firestore();
  await seed("alice", "one");
  await seed("alice", "two");
  const batch = forgedBatch(client, "one");
  batch.set(doc(client, "leaderboardReceipts/alice/walks/two"), { countedAt: serverTimestamp() });
  await assertFails(batch.commit());
});

test("Spark nickname and public label update atomically without changing scores", async () => {
  const client = environment.authenticatedContext("alice").firestore();
  await seed("alice", "one");
  await countClient(client, "alice", "one");
  const profileRef = doc(client, "leaderboardProfiles/alice");
  await assertFails(setDoc(profileRef, { displayName: "New Walker", updatedAt: serverTimestamp() }));
  const batch = writeBatch(client);
  batch.set(profileRef, { displayName: "New Walker", updatedAt: serverTimestamp() });
  batch.set(doc(client, "friendCodes/alice"), { displayName: "New Walker", updatedAt: serverTimestamp() });
  batch.update(doc(client, `${PLAYERS}/alice`), { displayName: "New Walker", updatedAt: serverTimestamp() });
  await assertSucceeds(batch.commit());
  assert.equal((await getDoc(doc(client, "friendCodes/alice"))).data().displayName, "New Walker");
  assert.equal((await player("alice")).get("displayName"), "New Walker");
  assert.equal((await player("alice")).get("totalSteps"), 100);
  const cheat = writeBatch(client);
  cheat.set(profileRef, { displayName: "Cheat Walker", updatedAt: serverTimestamp() });
  cheat.update(doc(client, `${PLAYERS}/alice`), { displayName: "Cheat Walker", totalSteps: 999, updatedAt: serverTimestamp() });
  await assertFails(cheat.commit());
});

test("Spark cannot count a newly invented walk and score in the same batch", async () => {
  const client = environment.authenticatedContext("alice").firestore();
  const batch = forgedBatch(client, "one");
  batch.set(doc(client, "users/alice/walks/one"), { ...walk("one"), uploadedAt: serverTimestamp() });
  await assertFails(batch.commit());
});

async function registerFriend(client, uid) {
  return setDoc(doc(client, `friendCodes/${uid}`), { displayName: `Walker ${uid}`, updatedAt: serverTimestamp() });
}
function friendBatch(client, owner, other, requestedBy, status = "pending") {
  const batch = writeBatch(client);
  const data = { requestedBy, status, updatedAt: serverTimestamp() };
  batch.set(doc(client, `friends/${owner}/links/${other}`), data);
  batch.set(doc(client, `friends/${other}/links/${owner}`), data);
  return batch;
}
function removeFriend(client, owner, other) {
  const batch = writeBatch(client);
  batch.delete(doc(client, `friends/${owner}/links/${other}`));
  batch.delete(doc(client, `friends/${other}/links/${owner}`));
  return batch.commit();
}
async function friendAccounts() {
  const alice = environment.authenticatedContext("alice").firestore();
  const bob = environment.authenticatedContext("bob").firestore();
  const mallory = environment.authenticatedContext("mallory").firestore();
  await Promise.all([registerFriend(alice, "alice"), registerFriend(bob, "bob")]);
  return { alice, bob, mallory };
}

test("friend codes support exact lookup, prohibit browsing, and protect ownership", async () => {
  const { alice, bob } = await friendAccounts();
  await assertSucceeds(getDoc(doc(bob, "friendCodes/alice")));
  await assertFails(getDocs(collection(bob, "friendCodes")));
  await assertFails(registerFriend(bob, "alice"));
  await assertFails(setDoc(doc(alice, "friendCodes/alice"), { displayName: "<script>", updatedAt: serverTimestamp() }));
  const guest = environment.unauthenticatedContext().firestore();
  await assertFails(getDoc(doc(guest, "friendCodes/alice")));
});

test("only recipient can accept; strangers cannot see or change friendships", async () => {
  const { alice, bob, mallory } = await friendAccounts();
  await assertSucceeds(friendBatch(alice, "alice", "bob", "alice").commit());
  await assertSucceeds(getDocs(collection(alice, "friends/alice/links")));
  await assertFails(getDocs(collection(bob, "friends/alice/links")));
  await assertFails(getDoc(doc(mallory, "friends/alice/links/bob")));
  await assertFails(friendBatch(alice, "alice", "bob", "alice", "accepted").commit());
  await assertFails(friendBatch(mallory, "alice", "bob", "alice", "accepted").commit());
  await assertSucceeds(friendBatch(bob, "alice", "bob", "alice", "accepted").commit());
  assert.equal((await getDoc(doc(alice, "friends/alice/links/bob"))).data().status, "accepted");
  assert.equal((await getDoc(doc(bob, "friends/bob/links/alice"))).data().status, "accepted");
  await assertFails(removeFriend(mallory, "alice", "bob"));
  await assertSucceeds(removeFriend(bob, "alice", "bob"));
  assert.equal((await getDoc(doc(alice, "friends/alice/links/bob"))).exists(), false);
  assert.equal((await getDoc(doc(bob, "friends/bob/links/alice"))).exists(), false);
});

test("friend requests reject self, unknown players, forged sender and unilateral changes", async () => {
  const { alice } = await friendAccounts();
  await assertFails(friendBatch(alice, "alice", "alice", "alice").commit());
  await assertFails(friendBatch(alice, "alice", "missing", "alice").commit());
  await assertFails(friendBatch(alice, "alice", "bob", "bob").commit());
  await assertFails(friendBatch(alice, "alice", "bob", "alice", "accepted").commit());
  await assertFails(setDoc(doc(alice, "friends/alice/links/bob"), { requestedBy: "alice", status: "pending", updatedAt: serverTimestamp() }));
  await assertSucceeds(friendBatch(alice, "alice", "bob", "alice").commit());
  await assertFails(deleteDoc(doc(alice, "friends/alice/links/bob")));
});

test("cancel and decline clear both copies; crossed sends cannot overwrite a request", async () => {
  const { alice, bob } = await friendAccounts();
  await friendBatch(alice, "alice", "bob", "alice").commit();
  await assertFails(friendBatch(bob, "bob", "alice", "bob").commit());
  await assertSucceeds(removeFriend(alice, "alice", "bob"));
  await assertSucceeds(friendBatch(bob, "bob", "alice", "bob").commit());
  await assertSucceeds(removeFriend(alice, "alice", "bob"));
  await assertFails(friendBatch(alice, "bob", "alice", "bob", "accepted").commit());
  assert.equal((await getDoc(doc(bob, "friends/bob/links/alice"))).exists(), false);
});

test("friend acceptance cannot change the sender or leave mismatched records", async () => {
  const { alice, bob } = await friendAccounts();
  await friendBatch(alice, "alice", "bob", "alice").commit();
  await assertFails(friendBatch(bob, "alice", "bob", "bob", "accepted").commit());
  await assertFails(setDoc(doc(bob, "friends/bob/links/alice"), { requestedBy: "alice", status: "accepted", updatedAt: serverTimestamp() }));
  await assertSucceeds(friendBatch(bob, "alice", "bob", "alice", "accepted").commit());
  await assertFails(friendBatch(alice, "alice", "bob", "alice").commit());
});

function reserveCode(client, uid, code) {
  const batch = writeBatch(client);
  batch.set(doc(client, `friendCodeOwners/${uid}`), { code });
  batch.set(doc(client, `friendCodeLookup/${code}`), { playerId: uid });
  return batch.commit();
}

test("fresh account registers its profile and short code, then saves and restores a photo", async () => {
  const uid = "fresh-install";
  const client = environment.authenticatedContext(uid).firestore();
  const profile = doc(client, `friendCodes/${uid}`);
  // Mirror RegisterCodeAsync: both private profile and public directory are absent.
  await assertSucceeds(runTransaction(client, async tx => {
    assert.equal((await tx.get(doc(client, `leaderboardProfiles/${uid}`))).exists(), false);
    assert.equal((await tx.get(profile)).exists(), false);
    tx.set(profile, { displayName: "Walker-Fresh", photoUrl: "", updatedAt: serverTimestamp() }, { merge: true });
  }));
  assert.equal((await getDocs(collection(client, `friends/${uid}/links`))).empty, true);
  const reservation = doc(client, `friendCodeOwners/${uid}`);
  const lookup = doc(client, "friendCodeLookup/ABCD2345");
  await assertSucceeds(runTransaction(client, async tx => {
    assert.equal((await tx.get(reservation)).exists(), false);
    assert.equal((await tx.get(lookup)).exists(), false);
    tx.set(reservation, { code: "ABCD2345" });
    tx.set(lookup, { playerId: uid });
  }));
  // A second client installation recovers the same account code from the server.
  const reinstall = environment.authenticatedContext(uid).firestore();
  assert.equal((await getDoc(doc(reinstall, `friendCodeOwners/${uid}`))).data().code, "ABCD2345");
  for (const photoUrl of ["data:image/jpeg;base64,/9j/AAAA", "https://lh3.googleusercontent.com/a/photo=s96-c", ""]) {
    await assertSucceeds(setDoc(profile, { photoUrl, updatedAt: serverTimestamp() }, { merge: true }));
    const saved = (await getDoc(doc(reinstall, `friendCodes/${uid}`))).data();
    assert.equal(saved.photoUrl, photoUrl);
    assert.equal(saved.displayName, "Walker-Fresh");
  }
  assert.equal((await getDoc(doc(client, `${PLAYERS}/${uid}`))).exists(), false);
});

test("short codes require paired unique immutable reservations and exact authenticated lookup", async () => {
  const { alice, bob } = await friendAccounts();
  await assertFails(setDoc(doc(alice, "friendCodeOwners/alice"), { code: "ABCDEFGH" }));
  await assertFails(setDoc(doc(alice, "friendCodeLookup/ABCDEFGH"), { playerId: "alice" }));
  await assertFails(reserveCode(alice, "bob", "ABCDEFGH"));
  await assertFails(reserveCode(alice, "alice", "ABCD0123"));
  await assertSucceeds(reserveCode(alice, "alice", "ABCDEFGH"));
  assert.equal((await getDoc(doc(bob, "friendCodeLookup/ABCDEFGH"))).data().playerId, "alice");
  await assertFails(reserveCode(bob, "bob", "ABCDEFGH"));
  await assertFails(reserveCode(alice, "alice", "ZYXWVU32"));
  await assertFails(getDoc(doc(bob, "friendCodeOwners/alice")));
  await assertFails(getDocs(collection(bob, "friendCodeLookup")));
  await assertFails(deleteDoc(doc(alice, "friendCodeLookup/ABCDEFGH")));
  await assertFails(getDoc(doc(environment.unauthenticatedContext().firestore(), "friendCodeLookup/ABCDEFGH")));
});

test("concurrent claims for the same short code cannot bind two players", async () => {
  const { alice, bob } = await friendAccounts();
  const outcomes = await Promise.allSettled([reserveCode(alice, "alice", "ABCDEFGH"), reserveCode(bob, "bob", "ABCDEFGH")]);
  assert.equal(outcomes.filter(result => result.status === "fulfilled").length, 1);
  const winner = (await getDoc(doc(alice, "friendCodeLookup/ABCDEFGH"))).data().playerId;
  assert.equal((await db.doc(`friendCodeOwners/${winner}`).get()).data().code, "ABCDEFGH");
  assert.equal((await db.doc(`friendCodeOwners/${winner === "alice" ? "bob" : "alice"}`).get()).exists, false);
});

test("public photos are owner controlled, bounded, and preserved by nickname merges", async () => {
  const { alice, bob } = await friendAccounts();
  const ref = doc(alice, "friendCodes/alice");
  for (const photoUrl of ["https://lh3.googleusercontent.com/a/photo=s96-c", "data:image/jpeg;base64,/9j/AAAA"]) {
    await assertSucceeds(setDoc(ref, { photoUrl, updatedAt: serverTimestamp() }, { merge: true }));
    assert.equal((await getDoc(doc(bob, "friendCodes/alice"))).data().photoUrl, photoUrl);
    await assertSucceeds(setDoc(ref, { displayName: "New Name", updatedAt: serverTimestamp() }, { merge: true }));
    assert.equal((await getDoc(ref)).data().photoUrl, photoUrl);
  }
  for (const photoUrl of ["http://lh3.googleusercontent.com/a", "https://evil.test/a", "https://lh3.googleusercontent.com.evil.test/a", "data:image/jpeg;base64,/9j/" + "A".repeat(32768), "data:image/png;base64,AAAA", 42])
    await assertFails(setDoc(ref, { photoUrl, updatedAt: serverTimestamp() }, { merge: true }));
  await assertFails(setDoc(doc(bob, "friendCodes/alice"), { photoUrl: "", updatedAt: serverTimestamp() }, { merge: true }));
  await assertSucceeds(setDoc(ref, { photoUrl: "", updatedAt: serverTimestamp() }, { merge: true }));
  await assertFails(setDoc(ref, { totalSteps: 123, updatedAt: serverTimestamp() }, { merge: true }));
});
