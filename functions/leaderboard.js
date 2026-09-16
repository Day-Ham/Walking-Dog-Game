"use strict";

const { createHash } = require("node:crypto");
const { FieldValue } = require("firebase-admin/firestore");

const PLAYERS = "leaderboards/allTime/players";
const validName = value => typeof value === "string" && /^[A-Za-z0-9][A-Za-z0-9 _-]{2,23}$/.test(value) && !/[\r\n]/.test(value);
const defaultName = uid => "Walker-" + createHash("sha256").update(uid).digest("hex").slice(0,8);
const displayName = (uid, profile) => validName(profile?.displayName) ? profile.displayName : defaultName(uid);

function validWalk(data, walkId) {
  return data?.schemaVersion === 1 && data.id === walkId &&
    Number.isSafeInteger(data.steps) && data.steps >= 0 && data.steps <= 2147483647 &&
    typeof data.distanceMeters === "number" && Number.isFinite(data.distanceMeters) && data.distanceMeters >= 0 &&
    data.distanceMeters <= 3.4028234663852886e38 &&
    typeof data.durationSeconds === "number" && Number.isFinite(data.durationSeconds) && data.durationSeconds >= 0;
}

// Used by both the creation trigger and historical backfill. Receipt and totals
// commit together: retries, concurrent walks and backfill/trigger races are safe.
async function countWalk(db, uid, walkId) {
  return db.runTransaction(async tx => {
    const walkRef = db.doc(`users/${uid}/walks/${walkId}`);
    const receiptRef = db.doc(`leaderboardReceipts/${uid}/walks/${walkId}`);
    const playerRef = db.doc(`${PLAYERS}/${uid}`);
    const profileRef = db.doc(`leaderboardProfiles/${uid}`);
    const [walk, receipt, player, profile] = await tx.getAll(walkRef, receiptRef, playerRef, profileRef);
    if (receipt.exists) return "already-counted";
    if (!walk.exists || !validWalk(walk.data(), walkId)) return "invalid";
    const data = walk.data();
    // Empty editor/test walks do not place an inactive player on the board.
    if (data.steps === 0 && data.distanceMeters === 0) return "empty";
    const previous = player.data() || { totalSteps: 0, totalDistanceMeters: 0, completedWalkCount: 0 };
    const totalSteps = previous.totalSteps + data.steps;
    const totalDistanceMeters = previous.totalDistanceMeters + data.distanceMeters;
    const completedWalkCount = previous.completedWalkCount + 1;
    if (!Number.isSafeInteger(totalSteps) || totalSteps < 0 ||
        !Number.isFinite(totalDistanceMeters) || totalDistanceMeters < 0 ||
        !Number.isSafeInteger(completedWalkCount) || completedWalkCount < 1)
      throw new Error("Invalid or overflowing leaderboard totals");
    tx.set(playerRef, { schemaVersion: 1, displayName: displayName(uid, profile.data()),
      totalSteps, totalDistanceMeters, completedWalkCount, updatedAt: FieldValue.serverTimestamp() });
    tx.create(receiptRef, { countedAt: FieldValue.serverTimestamp() });
    return "counted";
  });
}

async function refreshDisplayName(db, uid) {
  return db.runTransaction(async tx => {
    const playerRef = db.doc(`${PLAYERS}/${uid}`);
    const [player, profile] = await tx.getAll(playerRef, db.doc(`leaderboardProfiles/${uid}`));
    if (!player.exists) return;
    // Read the latest profile, not the event payload: delivery can be out of order.
    const name = displayName(uid, profile.data());
    if (player.get("displayName") !== name)
      tx.update(playerRef, { displayName: name, updatedAt: FieldValue.serverTimestamp() });
  });
}

module.exports = { PLAYERS, validName, validWalk, countWalk, refreshDisplayName };
