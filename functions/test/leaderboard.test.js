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

async function followPlayer(client, from, to, following = true) {
  const batch = writeBatch(client);
  const outgoing = doc(client, `social/${from}/following/${to}`);
  const incoming = doc(client, `social/${to}/followers/${from}`);
  if (following) {
    const data = { createdAt: serverTimestamp() };
    batch.set(outgoing, data); batch.set(incoming, data);
  } else { batch.delete(outgoing); batch.delete(incoming); }
  return batch.commit();
}
const shareWalks = (client, uid, shareActivity) => setDoc(doc(client, `socialProfiles/${uid}`),
  { shareActivity, updatedAt: serverTimestamp() });

const sharedRoute = () => ({ schemaVersion: 1, walkId: "route-walk",
  routeJson: JSON.stringify({ points: [{ lat: 14.5, lng: 121, gap: true }, { lat: 14.501, lng: 121.001, gap: false }] }),
  updatedAt: serverTimestamp() });

test("routes: missing route reads succeed for owner and eligible follower on fresh installs", async () => {
  const { alice, bob } = await friendAccounts();
  assert.equal((await assertSucceeds(getDoc(doc(bob, "users/bob/sharedRoutes/route-walk")))).exists(), false);
  await seed("bob", "route-walk");
  await followPlayer(alice, "alice", "bob");
  await shareWalks(bob, "bob", true);
  assert.equal((await assertSucceeds(getDoc(doc(alice, "users/bob/sharedRoutes/route-walk")))).exists(), false);
});

test("routes: explicitly shared GPS loads across devices and follows sharing revocation", async () => {
  const { alice, bob, mallory } = await friendAccounts();
  await seed("bob", "route-walk");
  const own = doc(bob, "users/bob/sharedRoutes/route-walk");
  const followed = doc(alice, "users/bob/sharedRoutes/route-walk");
  await assertFails(setDoc(own, sharedRoute()));
  await shareWalks(bob, "bob", true);
  await assertSucceeds(setDoc(own, sharedRoute()));
  await assertFails(getDoc(followed));
  await followPlayer(alice, "alice", "bob");
  assert.equal(JSON.parse((await assertSucceeds(getDoc(followed))).data().routeJson).points.length, 2);
  const newDevice = environment.authenticatedContext("bob").firestore();
  await assertSucceeds(getDoc(doc(newDevice, "users/bob/sharedRoutes/route-walk")));
  await assertFails(getDoc(doc(mallory, "users/bob/sharedRoutes/route-walk")));
  await assertFails(getDoc(doc(environment.unauthenticatedContext().firestore(), "users/bob/sharedRoutes/route-walk")));
  await assertFails(getDocs(collection(alice, "users/bob/sharedRoutes")));
  await followPlayer(alice, "alice", "bob", false);
  await assertFails(getDoc(followed));
  await followPlayer(alice, "alice", "bob");
  await shareWalks(bob, "bob", false);
  await assertFails(getDoc(followed));
  await assertSucceeds(getDoc(own));
  await assertSucceeds(deleteDoc(own));
  await shareWalks(bob, "bob", true);
  assert.equal((await assertSucceeds(getDoc(followed))).exists(), false);
});

test("routes: reject forged ownership, unsynced walks and invalid payload envelopes", async () => {
  const { alice, bob } = await friendAccounts();
  await shareWalks(bob, "bob", true);
  const own = doc(bob, "users/bob/sharedRoutes/route-walk");
  await assertFails(setDoc(own, sharedRoute()));
  await seed("bob", "route-walk");
  await assertFails(setDoc(doc(alice, "users/bob/sharedRoutes/route-walk"), sharedRoute()));
  for (const patch of [{ walkId: "wrong" }, { schemaVersion: 2 }, { extra: true },
    { routeJson: "" }, { routeJson: "x".repeat(200001) }, { routeJson: 4 }, { updatedAt: new Date(0) }])
    await assertFails(setDoc(own, { ...sharedRoute(), ...patch }));
  await assertSucceeds(setDoc(own, sharedRoute()));
  await assertFails(deleteDoc(doc(alice, "users/bob/sharedRoutes/route-walk")));
});

test("social: one-way follows, follow back, counts and unfollow remain independent", async () => {
  const { alice, bob } = await friendAccounts();
  await assertSucceeds(followPlayer(alice, "alice", "bob"));
  assert.equal((await getDocs(collection(bob, "social/bob/followers"))).size, 1);
  assert.equal((await getDocs(collection(bob, "social/bob/following"))).size, 0);
  await assertSucceeds(followPlayer(bob, "bob", "alice"));
  await assertSucceeds(followPlayer(alice, "alice", "bob", false));
  assert.equal((await getDoc(doc(bob, "social/bob/following/alice"))).exists(), true);
  assert.equal((await getDoc(doc(bob, "social/bob/followers/alice"))).exists(), false);
  await assertSucceeds(followPlayer(alice, "alice", "bob", false));
});

test("social: rejects forged, unpaired, self and unknown-player follows", async () => {
  const { alice, bob } = await friendAccounts();
  await assertFails(setDoc(doc(alice, "social/alice/following/bob"), { createdAt: serverTimestamp() }));
  await assertFails(setDoc(doc(alice, "social/bob/followers/alice"), { createdAt: serverTimestamp() }));
  await assertFails(followPlayer(bob, "alice", "bob"));
  await assertFails(followPlayer(alice, "alice", "alice"));
  await assertFails(followPlayer(alice, "alice", "missing"));
  await assertSucceeds(followPlayer(alice, "alice", "bob"));
  await assertFails(deleteDoc(doc(alice, "social/alice/following/bob")));
  await assertFails(followPlayer(bob, "alice", "bob", false));
  await assertFails(setDoc(doc(alice, "social/alice/following/bob"), { createdAt: serverTimestamp(), forged: true }));
  const guest = environment.unauthenticatedContext().firestore();
  await assertFails(getDocs(collection(guest, "social/bob/followers")));
  await assertFails(followPlayer(guest, "alice", "bob"));
});

test("social: summaries require both a follow and sharing opt-in; revocation applies to queries", async () => {
  const { alice, bob } = await friendAccounts();
  await seed("bob", "shared-walk", 1234, 950);
  const walks = () => getDocs(query(collection(alice, "users/bob/walks"), orderBy("endedAtUtc", "desc"), limit(10)));
  await assertFails(walks());
  await assertSucceeds(followPlayer(alice, "alice", "bob"));
  await assertFails(walks());
  await assertSucceeds(shareWalks(bob, "bob", true));
  assert.equal((await assertSucceeds(walks())).size, 1);
  await assertFails(getDoc(doc(environment.authenticatedContext("charlie").firestore(), "users/bob/walks/shared-walk")));
  await assertFails(setDoc(doc(alice, "users/bob/walks/shared-walk"), walk("shared-walk")));
  await assertFails(getDoc(doc(alice, "users/bob/wallet/main")));
  await assertSucceeds(shareWalks(bob, "bob", false));
  await assertFails(walks());
  await assertSucceeds(shareWalks(bob, "bob", true));
  await assertSucceeds(followPlayer(alice, "alice", "bob", false));
  await assertFails(walks());
  await assertSucceeds(getDocs(collection(bob, "users/bob/walks")));
});

test("social: sharing settings are owner-only and cannot expose GPS fields", async () => {
  const { alice, bob } = await friendAccounts();
  await assertFails(shareWalks(alice, "bob", true));
  await assertFails(shareWalks(bob, "bob", "yes"));
  await assertFails(setDoc(doc(bob, "socialProfiles/bob"), { shareActivity: true, updatedAt: serverTimestamp(), route: [] }));
  await assertFails(setDoc(doc(bob, "users/bob/walks/gps"), { ...walk("gps"), uploadedAt: serverTimestamp(), route: [{lat:14,lon:121}] }));
  await assertSucceeds(shareWalks(bob, "bob", true));
  await assertFails(deleteDoc(doc(alice, "socialProfiles/bob")));
  await assertSucceeds(deleteDoc(doc(bob, "socialProfiles/bob")));
});

const walletPath = uid => `users/${uid}/wallet/main`;
const pointReceipt = (uid, id) => `users/${uid}/pointReceipts/${id}`;
const walletFields = (earned = 0, spent = 0, id = "", claimed = 0) => ({ schemaVersion: 1,
  balance: earned - spent, totalEarned: earned, totalSpent: spent, lastWalkId: id,
  milestoneRollsClaimed: claimed, updatedAt: serverTimestamp() });

const dogFields = (patch = {}) => ({ schemaVersion: 1, speed: 40, stamina: 40, acceleration: 40,
  trainingCount: 0, lastTrainingId: '', updatedAt: serverTimestamp(), ...patch });
async function trainDog(client, uid, stat, id) {
  for (let attempt = 0; ; attempt++) {
    try { return await runTransaction(client, async tx => {
      const receiptRef = doc(client, `users/${uid}/dogTrainingReceipts/${id}`);
      const dogRef = doc(client, `users/${uid}/dog/main`);
      const walletRef = doc(client, walletPath(uid));
      const receipt = await tx.get(receiptRef), dog = await tx.get(dogRef), wallet = await tx.get(walletRef);
      if (receipt.exists()) return;
      const before = dog.data(), cash = wallet.data();
      tx.set(walletRef, walletFields(cash.totalEarned, cash.totalSpent + 20, cash.lastWalkId, cash.milestoneRollsClaimed || 0));
      tx.set(dogRef, dogFields({ ...before, [stat]: before[stat] + 5, trainingCount: before.trainingCount + 1,
        lastTrainingId: id, updatedAt: serverTimestamp() }));
      tx.set(receiptRef, { stat, cost: 20, trainedAt: serverTimestamp() });
    }); } catch (error) {
      if (error.code !== 'permission-denied' || attempt >= 2) throw error;
      await new Promise(resolve => setTimeout(resolve, 100 * (attempt + 1)));
    }
  }
}

test('dogs: profiles start at baseline, stay private, and cannot be freely upgraded or reset', async () => {
  const alice = environment.authenticatedContext('alice').firestore();
  const bob = environment.authenticatedContext('bob').firestore();
  const profile = doc(alice, 'users/alice/dog/main');
  await assertFails(setDoc(profile, dogFields({ speed: 45 })));
  await assertSucceeds(setDoc(profile, dogFields()));
  await assertSucceeds(getDoc(profile));
  await assertFails(getDoc(doc(bob, 'users/alice/dog/main')));
  await assertFails(setDoc(doc(bob, 'users/alice/dog/main'), dogFields()));
  await assertFails(setDoc(profile, dogFields({ speed: 50 })));
  await assertFails(deleteDoc(profile));
  await assertFails(setDoc(profile, dogFields()));
});

test('dogs: training pays and upgrades atomically, preserves milestones, and retries without double charging', async () => {
  const alice = environment.authenticatedContext('alice').firestore();
  await db.doc(walletPath('alice')).set({ ...walletFields(2000, 0, 'activity', 1), updatedAt: new Date() });
  await setDoc(doc(alice, 'users/alice/dog/main'), dogFields());
  const id = 'a'.repeat(32);
  await assertSucceeds(trainDog(alice, 'alice', 'speed', id));
  await assertSucceeds(trainDog(alice, 'alice', 'speed', id));
  const dog = (await getDoc(doc(alice, 'users/alice/dog/main'))).data();
  const wallet = (await getDoc(doc(alice, walletPath('alice')))).data();
  assert.equal(dog.speed, 45); assert.equal(dog.stamina, 40); assert.equal(dog.acceleration, 40);
  assert.equal(dog.trainingCount, 1); assert.equal(wallet.balance, 1980); assert.equal(wallet.milestoneRollsClaimed, 1);
  const receipt = doc(alice, `users/alice/dogTrainingReceipts/${id}`);
  await assertFails(setDoc(receipt, { stat: 'stamina', cost: 20, trainedAt: serverTimestamp() }));
  await assertFails(deleteDoc(receipt));
});

test('dogs: insufficient points and forged upgrades leave both wallet and dog unchanged', async () => {
  const alice = environment.authenticatedContext('alice').firestore();
  await db.doc(walletPath('alice')).set({ ...walletFields(10), updatedAt: new Date() });
  const profile = doc(alice, 'users/alice/dog/main');
  await setDoc(profile, dogFields());
  await assertFails(trainDog(alice, 'alice', 'stamina', 'b'.repeat(32)));
  assert.equal((await getDoc(profile)).data().stamina, 40);
  assert.equal((await getDoc(doc(alice, walletPath('alice')))).data().balance, 10);
  assert.equal((await getDoc(doc(alice, `users/alice/dogTrainingReceipts/${'b'.repeat(32)}`))).exists(), false);
  await db.doc(walletPath('alice')).set({ ...walletFields(100), updatedAt: new Date() });
  const forged = writeBatch(alice), id = 'c'.repeat(32);
  forged.set(profile, dogFields({ speed: 50, trainingCount: 1, lastTrainingId: id }));
  forged.set(doc(alice, walletPath('alice')), walletFields(100, 20));
  forged.set(doc(alice, `users/alice/dogTrainingReceipts/${id}`), { stat: 'speed', cost: 20, trainedAt: serverTimestamp() });
  await assertFails(forged.commit());
  await assertFails(setDoc(doc(alice, `users/alice/dogTrainingReceipts/${id}`), { stat: 'speed', cost: 20, trainedAt: serverTimestamp() }));
});

test('dogs: training is capped at 100 and independent sessions accumulate correctly', async () => {
  const alice = environment.authenticatedContext('alice').firestore();
  await db.doc(walletPath('alice')).set({ ...walletFields(1000), updatedAt: new Date() });
  await setDoc(doc(alice, 'users/alice/dog/main'), dogFields());
  await Promise.all([trainDog(alice, 'alice', 'speed', 'd'.repeat(32)), trainDog(alice, 'alice', 'acceleration', 'e'.repeat(32))]);
  for (let i=1; i<12; i++) await trainDog(alice, 'alice', 'speed', i.toString(16).padStart(32,'0'));
  const profile = doc(alice, 'users/alice/dog/main');
  const before = (await getDoc(profile)).data();
  assert.equal(before.speed, 100); assert.equal(before.acceleration, 45); assert.equal(before.trainingCount, 13);
  await assertFails(trainDog(alice, 'alice', 'speed', 'f'.repeat(32)));
  assert.equal((await getDoc(profile)).data().speed, 100);
  assert.equal((await getDoc(doc(alice, walletPath('alice')))).data().balance, 740);
});

async function claimMilestone(client, uid, id) {
  return runTransaction(client, async tx => {
    const receipt = doc(client, `users/${uid}/milestoneReceipts/${id}`);
    if ((await tx.get(receipt)).exists()) return;
    const wallet = doc(client, walletPath(uid));
    const old = (await tx.get(wallet)).data();
    const claimed = (old.milestoneRollsClaimed || 0) + 1;
    tx.set(wallet, walletFields(old.totalEarned, old.totalSpent, id, claimed));
    tx.set(receipt, { milestone: claimed, claimedAt: serverTimestamp() });
  });
}

test("milestones: legacy wallet upgrades, claims once, and preserves claims during step sync and spending", async () => {
  const alice = environment.authenticatedContext("alice").firestore();
  const legacy = walletFields(); delete legacy.milestoneRollsClaimed;
  await assertSucceeds(setDoc(doc(alice, walletPath("alice")), legacy));
  await assertFails(claimMilestone(alice, "alice", "too-early"));
  await assertSucceeds(syncActivity(alice, "alice", streamA, 10000));
  await assertSucceeds(claimMilestone(alice, "alice", "first"));
  await assertSucceeds(claimMilestone(alice, "alice", "first"));
  assert.equal((await getDoc(doc(alice, walletPath("alice")))).data().milestoneRollsClaimed, 1);
  await assertFails(claimMilestone(alice, "alice", "second"));
  await assertSucceeds(syncActivity(alice, "alice", streamA, 20000));
  await assertSucceeds(setDoc(doc(alice, walletPath("alice")), walletFields(2000, 500, 'activity', 1)));
  await assertSucceeds(claimMilestone(alice, "alice", "second"));
  const result = (await getDoc(doc(alice, walletPath("alice")))).data();
  assert.equal(result.milestoneRollsClaimed, 2);
  assert.equal(result.balance, 1500);
  assert.equal(result.totalEarned, 2000);
  await seed("alice", "old-walk", 100);
  await assertSucceeds(awardPoints(alice, "alice", "old-walk"));
  assert.equal((await getDoc(doc(alice, walletPath("alice")))).data().milestoneRollsClaimed, 2);
});

test("milestones: forged claims, resets, unrelated wallet writes and editable receipts are denied", async () => {
  const alice = environment.authenticatedContext("alice").firestore();
  const bob = environment.authenticatedContext("bob").firestore();
  await syncActivity(alice, "alice", streamA, 20000);
  const wallet = doc(alice, walletPath("alice"));
  await assertFails(setDoc(wallet, walletFields(2000, 0, 'forged', 1)));
  await assertFails(setDoc(wallet, walletFields(2000, 0, 'activity', -1)));
  await assertFails(setDoc(wallet, walletFields(2000, 0, 'activity', 0.5)));
  const skipped = writeBatch(alice);
  skipped.set(wallet, walletFields(2000, 0, 'skip', 2));
  skipped.set(doc(alice, 'users/alice/milestoneReceipts/skip'), { milestone: 2, claimedAt: serverTimestamp() });
  await assertFails(skipped.commit());
  const wrongPair = writeBatch(alice);
  wrongPair.set(wallet, walletFields(2000, 0, 'right', 1));
  wrongPair.set(doc(alice, 'users/alice/milestoneReceipts/wrong'), { milestone: 1, claimedAt: serverTimestamp() });
  await assertFails(wrongPair.commit());
  await assertFails(setDoc(doc(alice, 'users/alice/milestoneReceipts/alone'), { milestone: 1, claimedAt: serverTimestamp() }));
  await assertFails(claimMilestone(bob, "alice", "stolen"));
  await claimMilestone(alice, "alice", "valid");
  await assertFails(setDoc(wallet, walletFields(2000, 0, 'activity', 0)));
  await assertFails(setDoc(wallet, walletFields(2000, 1, 'activity', 0)));
  const legacy = walletFields(2000, 1, 'activity'); delete legacy.milestoneRollsClaimed;
  await assertFails(setDoc(wallet, legacy));
  const receipt = doc(alice, 'users/alice/milestoneReceipts/valid');
  await assertFails(setDoc(receipt, { milestone: 2, claimedAt: serverTimestamp() }));
  await assertFails(deleteDoc(receipt));
  await assertFails(getDoc(doc(bob, 'users/alice/milestoneReceipts/valid')));
});

test("routes: automatic transaction preserves existing routes and stops when sharing is OFF", async () => {
  const { alice, bob } = await friendAccounts();
  await seed("bob", "route-walk");
  await shareWalks(bob, "bob", true);
  await followPlayer(alice, "alice", "bob");
  const own = doc(bob, "users/bob/sharedRoutes/route-walk");
  async function automaticUpload() {
    return runTransaction(bob, async tx => {
      const settings = await tx.get(doc(bob, "socialProfiles/bob"));
      const summary = await tx.get(doc(bob, "users/bob/walks/route-walk"));
      const existing = await tx.get(own);
      if (!settings.data().shareActivity || !summary.exists()) return false;
      if (!existing.exists()) tx.set(own, sharedRoute());
      return true;
    });
  }
  await assertSucceeds(automaticUpload());
  const original = (await getDoc(own)).data().updatedAt;
  await assertSucceeds(automaticUpload());
  assert.equal((await getDoc(own)).data().updatedAt.isEqual(original), true);
  await assertSucceeds(getDoc(doc(alice, "users/bob/sharedRoutes/route-walk")));
  await shareWalks(bob, "bob", false);
  assert.equal(await automaticUpload(), false);
  await assertFails(getDoc(doc(alice, "users/bob/sharedRoutes/route-walk")));
  await assertFails(setDoc(own, sharedRoute()));
});

async function awardPoints(client, uid, id) {
  for (let attempt = 0; ; attempt++) {
    try { return await runTransaction(client, async tx => {
    const receipt = doc(client, pointReceipt(uid, id));
    if ((await tx.get(receipt)).exists()) return;
    const saved = await tx.get(doc(client, `users/${uid}/walks/${id}`));
    const wallet = doc(client, walletPath(uid));
    const old = await tx.get(wallet);
    const points = Math.floor(saved.data().steps / 10);
    tx.set(wallet, walletFields((old.exists() ? old.data().totalEarned : 0) + points,
      old.exists() ? old.data().totalSpent : 0, id, old.exists() ? old.data().milestoneRollsClaimed || 0 : 0));
    tx.set(receipt, { points, awardedAt: serverTimestamp() });
    }); } catch (error) {
      if (error.code !== "permission-denied" || attempt >= 2) throw error;
      await new Promise(resolve => setTimeout(resolve, 100 * (attempt + 1)));
    }
  }
}

test("points: fresh account creates a private zero wallet", async () => {
  const client = environment.authenticatedContext("alice").firestore();
  await assertSucceeds(setDoc(doc(client, walletPath("alice")), walletFields()));
  assert.equal((await getDoc(doc(client, walletPath("alice")))).data().balance, 0);
  const other = environment.authenticatedContext("bob").firestore();
  await assertFails(getDoc(doc(other, walletPath("alice"))));
  await assertFails(setDoc(doc(other, walletPath("alice")), walletFields()));
  await assertFails(getDoc(doc(environment.unauthenticatedContext().firestore(), walletPath("alice"))));
});

test("points: historical walks, rounding, zero rewards, retries and another installation", async () => {
  const client = environment.authenticatedContext("alice").firestore();
  for (const [id, steps] of [["old", 129], ["short", 9], ["empty", 0]]) {
    await seed("alice", id, steps, 0);
    await assertSucceeds(awardPoints(client, "alice", id));
  }
  const second = environment.authenticatedContext("alice").firestore();
  await assertSucceeds(awardPoints(second, "alice", "old"));
  assert.equal((await getDoc(doc(second, walletPath("alice")))).data().balance, 12);
  assert.equal((await getDoc(doc(client, pointReceipt("alice", "short")))).data().points, 0);
});

test("points: concurrent walks and repeated claims preserve exact integer totals", async () => {
  const client = environment.authenticatedContext("alice").firestore();
  await seed("alice", "one", 123, 10.1);
  await seed("alice", "two", 239, 20.2);
  await Promise.all([awardPoints(client, "alice", "one"), awardPoints(client, "alice", "two"), awardPoints(client, "alice", "one")]);
  assert.equal((await getDoc(doc(client, walletPath("alice")))).data().balance, 35);
});

test("points: rejects forged balances, missing receipts, missing walks and overspending", async () => {
  const client = environment.authenticatedContext("alice").firestore();
  await seed("alice", "one", 100, 0);
  await assertFails(setDoc(doc(client, walletPath("alice")), walletFields(100)));
  await assertFails(setDoc(doc(client, walletPath("alice")), walletFields(10, 0, "one")));
  for (const [id, earned, points] of [["missing", 10, 10], ["one", 999, 999], ["one", 10, 999]]) {
    const batch = writeBatch(client);
    batch.set(doc(client, walletPath("alice")), walletFields(earned, 0, id));
    batch.set(doc(client, pointReceipt("alice", id)), { points, awardedAt: serverTimestamp() });
    await assertFails(batch.commit());
  }
  await awardPoints(client, "alice", "one");
  await assertSucceeds(setDoc(doc(client, walletPath("alice")), walletFields(10, 5, "one")));
  await assertFails(setDoc(doc(client, walletPath("alice")), walletFields(10, 11, "one")));
  await assertFails(setDoc(doc(client, walletPath("alice")), walletFields(10, 0, "one")));
  await assertFails(setDoc(doc(client, walletPath("alice")), walletFields()));
  await assertFails(deleteDoc(doc(client, walletPath("alice"))));
});

test("points: receipts are private, immutable, and cannot be created without wallet credit", async () => {
  const client = environment.authenticatedContext("alice").firestore();
  await seed("alice", "one");
  const receipt = doc(client, pointReceipt("alice", "one"));
  await assertFails(setDoc(receipt, { points: 10, awardedAt: serverTimestamp() }));
  await awardPoints(client, "alice", "one");
  await assertFails(setDoc(receipt, { points: 10, awardedAt: serverTimestamp() }));
  await assertFails(deleteDoc(receipt));
  await assertFails(getDoc(doc(environment.authenticatedContext("bob").firestore(), pointReceipt("alice", "one"))));
});

test("points: exact delta preserves backend spending and cannot overflow the wallet", async () => {
  const client = environment.authenticatedContext("alice").firestore();
  await seed("alice", "one", 100);
  await db.doc(walletPath("alice")).set({ ...walletFields(100, 30), updatedAt: new Date() });
  await awardPoints(client, "alice", "one");
  assert.equal((await getDoc(doc(client, walletPath("alice")))).data().balance, 80);
  await seed("alice", "two", 100);
  await db.doc(walletPath("alice")).set({ ...walletFields(Number.MAX_SAFE_INTEGER), updatedAt: new Date() });
  await assertFails(awardPoints(client, "alice", "two"));
  assert.equal((await db.doc(pointReceipt("alice", "two")).get()).exists, false);
});

before(async () => {
  environment = await initializeTestEnvironment({ projectId,
    firestore: { rules: fs.readFileSync(path.join(__dirname, "../../firestore.rules"), "utf8") } });
  app = initializeApp({ projectId }, "leaderboard-tests");
  db = getFirestore(app);
});
beforeEach(async () => environment.clearFirestore());
after(async () => { await environment?.cleanup(); if (app) await deleteApp(app); });

// Mirrors the Unity transaction; these tests exercise the actual Firestore rules.
async function syncActivity(client, uid, streamId, total) {
  for (let attempt = 0; ; attempt++) {
    try { return await runTransaction(client, async tx => {
      const streamRef = doc(client, `users/${uid}/stepStreams/${streamId}`);
      const activityRef = doc(client, `users/${uid}/activity/main`);
      const walletRef = doc(client, walletPath(uid));
      const playerRef = doc(client, `${PLAYERS}/${uid}`);
      const stream = await tx.get(streamRef);
      const activity = await tx.get(activityRef);
      const wallet = await tx.get(walletRef);
      const player = await tx.get(playerRef);
      const profile = await tx.get(doc(client, `leaderboardProfiles/${uid}`));
      const previous = stream.exists() ? stream.data().totalSteps : 0;
      if (total <= previous) return;
      const delta = total - previous;
      const oldTotal = activity.exists() ? activity.data().totalSteps : 0;
      const next = oldTotal + delta;
      const earned = (wallet.exists() ? wallet.data().totalEarned : 0) + Math.floor(next / 10) - Math.floor(oldTotal / 10);
      const spent = wallet.exists() ? wallet.data().totalSpent : 0;
      const old = player.exists() ? player.data() : { totalSteps: 0, totalDistanceMeters: 0, completedWalkCount: 0, displayName: 'Walker-test' };
      tx.set(streamRef, { totalSteps: total, updatedAt: serverTimestamp() });
      tx.set(activityRef, { totalSteps: next, lastStreamId: streamId, updatedAt: serverTimestamp() });
      tx.set(walletRef, walletFields(earned, spent, 'activity', wallet.exists() ? wallet.data().milestoneRollsClaimed || 0 : 0));
      tx.set(playerRef, { schemaVersion: 1, displayName: profile.exists() ? profile.data().displayName : old.displayName,
        totalSteps: old.totalSteps + delta, totalDistanceMeters: old.totalDistanceMeters, completedWalkCount: old.completedWalkCount,
        pointsBalance: earned - spent, lastWalkId: 'activity', updatedAt: serverTimestamp() });
    }); } catch (error) {
      if (error.code !== 'permission-denied' || attempt >= 2) throw error;
      await new Promise(resolve => setTimeout(resolve, 100 * (attempt + 1)));
    }
  }
}
const streamA = 'a'.repeat(32), streamB = 'b'.repeat(32);

test('rollout: existing gacha debit, receipt and inventory work alongside continuous rewards', async () => {
  const client = environment.authenticatedContext('alice').firestore();
  await syncActivity(client, 'alice', streamA, 400);
  const purchase = writeBatch(client);
  purchase.set(doc(client, walletPath('alice')), walletFields(40, 10, 'activity'));
  purchase.set(doc(client, 'users/alice/spendReceipts/purchase-one'), { points: 10, spentAt: serverTimestamp() });
  await assertSucceeds(purchase.commit());
  const equip = writeBatch(client);
  equip.set(doc(client, 'users/alice/inventory/main'), { unlockedItemIds: ['Collar_Bronze'], equippedItemId: 'Collar_Bronze', updatedAt: serverTimestamp() });
  equip.set(doc(client, 'leaderboardProfiles/alice'), { displayName: 'Walker-test', equippedItemId: 'Collar_Bronze', equippedCollarColor: '#CD7F32', updatedAt: serverTimestamp() });
  await assertSucceeds(equip.commit());
  await assertSucceeds(syncActivity(client, 'alice', streamA, 500));
  const wallet = (await getDoc(doc(client, walletPath('alice')))).data();
  assert.equal(wallet.balance, 40);
  assert.equal(wallet.totalSpent, 10);
  assert.equal((await getDoc(doc(client, 'leaderboardProfiles/alice'))).data().equippedItemId, 'Collar_Bronze');
  const other = environment.authenticatedContext('bob').firestore();
  await assertSucceeds(getDoc(doc(other, 'leaderboardProfiles/alice')));
  await assertFails(getDoc(doc(other, 'users/alice/inventory/main')));
  await assertFails(setDoc(doc(other, 'users/alice/spendReceipts/purchase-one'), { points: 999 }));
});

test('rollout: public equipment updates cannot smuggle leaderboard score changes', async () => {
  const client = environment.authenticatedContext('alice').firestore();
  await syncActivity(client, 'alice', streamA, 100);
  const ref = doc(client, `${PLAYERS}/alice`);
  await assertSucceeds(setDoc(ref, { equippedCollarColor: '#CD7F32', updatedAt: serverTimestamp() }, { merge: true }));
  await assertFails(setDoc(ref, { equippedCollarColor: '#FFFFFF', totalSteps: 9999, updatedAt: serverTimestamp() }, { merge: true }));
  assert.equal((await getDoc(ref)).data().totalSteps, 100);
});

test('activity: steps before, during and after a territory walk earn exactly once', async () => {
  const client = environment.authenticatedContext('alice').firestore();
  await assertSucceeds(syncActivity(client, 'alice', streamA, 105));
  await assertSucceeds(syncActivity(client, 'alice', streamA, 313));
  const summary = { ...walk('territory', 208, 250), stepAccountingVersion: 1, uploadedAt: serverTimestamp() };
  await assertSucceeds(setDoc(doc(client, 'users/alice/walks/territory'), summary));
  await assertSucceeds(countClient(client, 'alice', 'territory'));
  assert.equal(await countWalk(db, 'alice', 'territory'), 'already-counted');
  await assertFails(awardPoints(client, 'alice', 'territory'));
  await assertSucceeds(syncActivity(client, 'alice', streamA, 360));
  const score = (await player('alice')).data();
  assert.equal(score.totalSteps, 360);
  assert.equal(score.completedWalkCount, 1);
  assert.equal(score.totalDistanceMeters, 250);
  assert.equal((await getDoc(doc(client, walletPath('alice')))).data().balance, 36);
});

test('activity: leftovers combine across syncs and devices, with no walk required', async () => {
  const first = environment.authenticatedContext('alice').firestore();
  const second = environment.authenticatedContext('alice').firestore();
  await assertSucceeds(syncActivity(first, 'alice', streamA, 9));
  await assertSucceeds(syncActivity(second, 'alice', streamB, 9));
  await assertSucceeds(syncActivity(first, 'alice', streamA, 12));
  const score = (await player('alice')).data();
  assert.equal(score.totalSteps, 21);
  assert.equal(score.completedWalkCount, 0);
  assert.equal(score.pointsBalance, 2);
});

test('activity: concurrent territory and activity updates preserve route totals and steps', async () => {
  const client = environment.authenticatedContext('alice').firestore();
  await db.doc('users/alice/walks/loop').set({ ...walk('loop', 80, 230.25), stepAccountingVersion: 1 });
  await Promise.all([syncActivity(client, 'alice', streamA, 120), countClient(client, 'alice', 'loop')]);
  const score = (await player('alice')).data();
  assert.equal(score.totalSteps, 120);
  assert.equal(score.totalDistanceMeters, 230.25);
  assert.equal(score.completedWalkCount, 1);
  assert.equal((await getDoc(doc(client, walletPath('alice')))).data().balance, 12);
});

test('activity: optional backend also skips territory steps on first delivery', async () => {
  await db.doc('users/alice/walks/loop').set({ ...walk('loop', 80, 230), stepAccountingVersion: 1 });
  assert.equal(await countWalk(db, 'alice', 'loop'), 'counted');
  const score = (await player('alice')).data();
  assert.equal(score.totalSteps, 0);
  assert.equal(score.totalDistanceMeters, 230);
});

test('activity: duplicate, stale and concurrent uploads are idempotent', async () => {
  const client = environment.authenticatedContext('alice').firestore();
  await Promise.all([syncActivity(client, 'alice', streamA, 110), syncActivity(client, 'alice', streamA, 100),
    syncActivity(client, 'alice', streamB, 90)]);
  await assertSucceeds(syncActivity(client, 'alice', streamA, 110));
  await assertSucceeds(syncActivity(client, 'alice', streamA, 80));
  assert.equal((await player('alice')).data().totalSteps, 200);
  assert.equal((await getDoc(doc(client, walletPath('alice')))).data().balance, 20);
});

test('activity: preserves legacy rewards, leaderboard totals and spent points', async () => {
  const client = environment.authenticatedContext('alice').firestore();
  await seed('alice', 'old', 129, 100);
  await awardPoints(client, 'alice', 'old');
  await countWalk(db, 'alice', 'old');
  await db.doc(walletPath('alice')).update({ totalSpent: 5, balance: 7 });
  await assertSucceeds(syncActivity(client, 'alice', streamA, 19));
  assert.equal((await player('alice')).data().totalSteps, 148);
  assert.equal((await getDoc(doc(client, walletPath('alice')))).data().balance, 8);
  await awardPoints(client, 'alice', 'old');
  assert.equal((await getDoc(doc(client, walletPath('alice')))).data().balance, 8);
});

test('activity: private cursors, no isolated writes, rollback or deletion', async () => {
  const client = environment.authenticatedContext('alice').firestore();
  const stream = doc(client, `users/alice/stepStreams/${streamA}`);
  await assertFails(setDoc(stream, { totalSteps: 100, updatedAt: serverTimestamp() }));
  await assertFails(setDoc(doc(client, 'users/alice/activity/main'), { totalSteps: 100, lastStreamId: streamA, updatedAt: serverTimestamp() }));
  await assertSucceeds(syncActivity(client, 'alice', streamA, 100));
  await assertFails(setDoc(stream, { totalSteps: 50, updatedAt: serverTimestamp() }));
  await assertFails(deleteDoc(stream));
  const other = environment.authenticatedContext('bob').firestore();
  await assertFails(getDoc(doc(other, `users/alice/stepStreams/${streamA}`)));
  await assertFails(syncActivity(other, 'alice', streamB, 100));
  await assertFails(setDoc(doc(client, walletPath('alice')), walletFields(999, 0, 'activity')));
});

test('activity: incorrect wallet delta or leaderboard score rejects the entire batch', async () => {
  const client = environment.authenticatedContext('alice').firestore();
  await syncActivity(client, 'alice', streamA, 100);
  for (const [earned, score] of [[999, 200], [20, 999]]) {
    const batch = writeBatch(client);
    batch.set(doc(client, `users/alice/stepStreams/${streamA}`), { totalSteps: 200, updatedAt: serverTimestamp() });
    batch.set(doc(client, 'users/alice/activity/main'), { totalSteps: 200, lastStreamId: streamA, updatedAt: serverTimestamp() });
    batch.set(doc(client, walletPath('alice')), walletFields(earned, 0, 'activity'));
    batch.set(doc(client, `${PLAYERS}/alice`), { schemaVersion: 1, displayName: 'Walker-test', totalSteps: score,
      totalDistanceMeters: 0, completedWalkCount: 0, pointsBalance: earned, lastWalkId: 'activity', updatedAt: serverTimestamp() });
    await assertFails(batch.commit());
  }
  assert.equal((await getDoc(doc(client, `users/alice/stepStreams/${streamA}`))).data().totalSteps, 100);
});

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
  // Live map avatars use public profile reads for signed-in players.
  await assertSucceeds(getDoc(doc(client, "leaderboardProfiles/bob")));
  await assertFails(getDoc(doc(environment.unauthenticatedContext().firestore(), "leaderboardProfiles/bob")));
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
  for (let attempt = 0; ; attempt++) {
    try { return await runTransaction(client, async tx => {
    const receiptRef = doc(client, `leaderboardReceipts/${uid}/walks/${id}`);
    if ((await tx.get(receiptRef)).exists()) return "already-counted";
    const walkData = (await tx.get(doc(client, `users/${uid}/walks/${id}`))).data();
    const playerRef = doc(client, `${PLAYERS}/${uid}`);
    const old = (await tx.get(playerRef)).data() || { totalSteps: 0, totalDistanceMeters: 0, completedWalkCount: 0 };
    const profile = (await tx.get(doc(client, `leaderboardProfiles/${uid}`))).data();
    tx.set(playerRef, { schemaVersion: 1, displayName: profile?.displayName || old.displayName || "Walker-Test",
      totalSteps: old.totalSteps + (walkData.stepAccountingVersion === 1 ? 0 : walkData.steps), totalDistanceMeters: old.totalDistanceMeters + walkData.distanceMeters,
      completedWalkCount: old.completedWalkCount + 1, lastWalkId: id, updatedAt: serverTimestamp() });
    tx.set(receiptRef, { countedAt: serverTimestamp() });
    return "counted";
    }); } catch (error) {
      if (error.code !== 'permission-denied' || attempt >= 2) throw error;
      await new Promise(resolve => setTimeout(resolve, 100 * (attempt + 1)));
    }
  }
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
