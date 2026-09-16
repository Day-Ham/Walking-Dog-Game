"use strict";

const { initializeApp } = require("firebase-admin/app");
const { getFirestore } = require("firebase-admin/firestore");
const { onDocumentCreated, onDocumentWritten } = require("firebase-functions/v2/firestore");
const { countWalk, refreshDisplayName } = require("./leaderboard");

initializeApp();
// Verify the database location before deployment. This matches the documented
// Singapore setup; keep the functions near Firestore.
const options = { region: "asia-southeast1", retry: true, maxInstances: 5 };

exports.countLeaderboardWalk = onDocumentCreated(
  { ...options, document: "users/{uid}/walks/{walkId}" },
  event => countWalk(getFirestore(), event.params.uid, event.params.walkId));

exports.updateLeaderboardName = onDocumentWritten(
  { ...options, document: "leaderboardProfiles/{uid}" },
  event => refreshDisplayName(getFirestore(), event.params.uid));
