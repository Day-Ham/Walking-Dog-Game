"use strict";

// Explicit project and --apply prevent accidental production writes. Default:
// read-only inventory. Admin credentials stay outside the Unity project.
const { initializeApp } = require("firebase-admin/app");
const { getFirestore, FieldPath } = require("firebase-admin/firestore");
const { countWalk, validWalk } = require("./leaderboard");

async function main() {
  const args = process.argv.slice(2);
  const projectArg = args.find(arg => arg.startsWith("--project="));
  if (!projectArg) throw new Error("Provide --project=<id> and optionally --apply");
  const projectId = projectArg.slice("--project=".length);
  if (!/^[a-z][a-z0-9-]+$/.test(projectId)) throw new Error("Invalid project ID");
  initializeApp({ projectId });
  const db = getFirestore();
  const apply = args.includes("--apply");
  const counts = {};
  let cursor;
  for (;;) {
    let query = db.collectionGroup("walks").orderBy(FieldPath.documentId()).limit(200);
    if (cursor) query = query.startAfter(cursor);
    const page = await query.get();
    if (page.empty) break;
    for (const doc of page.docs) {
      const parts = doc.ref.path.split("/");
      // Exclude receipts and any other collection also named walks.
      if (parts.length !== 4 || parts[0] !== "users" || parts[2] !== "walks") continue;
      const data = doc.data();
      const result = apply ? await countWalk(db, parts[1], parts[3])
        : !validWalk(data, parts[3]) ? "invalid" : data.steps === 0 && data.distanceMeters === 0 ? "empty" : "eligible";
      counts[result] = (counts[result] || 0) + 1;
    }
    cursor = page.docs[page.docs.length - 1];
  }
  console.log({ projectId, mode: apply ? "apply" : "dry-run", counts });
}
main().catch(error => { console.error(error); process.exitCode = 1; });
