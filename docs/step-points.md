# Firebase points wallet

Completed, account-owned walks earn one point per ten recorded steps, rounded
down **per walk**. A 129-step walk earns 12 points; two 9-step walks earn zero.
Existing saved cloud walks are included, as requested. Signed-out local walks
remain local until explicitly assigned to an account through the existing import
flow; this feature does not silently change walk ownership.

## Storage and awarding

- `users/{uid}/wallet/main`: `schemaVersion`, integer `balance`, `totalEarned`,
  `totalSpent`, `lastWalkId`, and server `updatedAt`.
- `users/{uid}/pointReceipts/{walkId}`: integer `points` and server `awardedAt`.

Both paths are private to the owning account. A new account receives a zero
wallet. Uploading a completed summary awards its points before updating the
leaderboard. The wallet and receipt commit in one Firestore transaction. Retrying
a saved walk or uploading it from multiple installations cannot pay twice.
Receipts cannot be overwritten or deleted by clients. Rules permit only the exact
step-derived increase, with unchanged total spent; arbitrary balance edits and
client spending are denied. Wallet totals are capped at 9,007,199,254,740,991.

The existing Firebase/Firestore setup is used, without a new Cloud Function.
These rules validate accounting against saved summaries, not physical activity:
step measurements still originate from the client. A future gacha implementation
needs trusted reward selection and atomic spending/reward grants.

## Recovery, history and offline behavior

On sign-in, `FirebasePointsWalletStore` reconciles existing cloud walks in pages
of 25. The in-memory cursor advances only after successful awards. A later
refresh resumes interrupted work; relaunch/sign-in starts a fresh scan whose
receipts make repeats safe. New uploads award points directly, independently of
the scan. Exact-delta rules may reject a concurrent stale transaction before the
SDK reports a conflict; bounded retries reread the documents. Persistent failures
remain retryable through the local walk-upload queue or history reconciliation.

`FirebaseWalkBootstrap` loads the wallet after initialization, periodically,
after reward acknowledgements and on resume. It clears account state on auth
changes and rejects late results from old requests. Requests have a bounded UI
wait, though native Firebase operations can finish later.

The main points label shows the saved wallet balance. While offline, a balance
already loaded in this session remains visible with a sync-pending indicator.
Before any successful read, the UI shows a pending/unavailable state, not a
fabricated zero. The last displayed balance is not separately persisted locally.
Completed offline walks remain in the existing durable upload queue and earn
points after reconnecting. The indicator also covers pending walk uploads; an
unrelated leaderboard retry can therefore keep it visible after points are saved.

`StepCountAndGpsManager.StepPoints` and `getPoint()` remain current-step previews
for compatibility. `WalkingSessionPoints` previews the current/recovered walk.
They are **not** wallet balances. `UIStepController` normally reads
`FirebaseWalkBootstrap.Wallet.DisplayText`; its optional session mode continues
to show the current walk preview. Future purchasing code must use the saved wallet.

## Validation and rollout

September 23, 2026: deployed and verified the wallet rules on `walky-aa25c`.
The previous live rules matched the repository baseline; the new live rules
exactly match this working copy. All **89/89 Unity EditMode tests passed** in an
isolated copy of the project. Logs are in the ignored repository `Logs` folder:
`wallet-unity-final.xml`, `wallet-emulator-final.log`, `wallet-rules-deploy.log`.

Deploy the repository's Firestore rules before distributing the updated client.
Existing clients keep their previous behavior; installing/running the updated
client triggers wallet creation and historical reward reconciliation. No production
walks or balances are backfilled by a separate admin script.

Emulator coverage includes zero-wallet initialization, account privacy, historical
walk rewards, per-walk rounding, duplicate/multiple-installation claims, concurrent
uploads, forged balances, receipt integrity, spending denial and overflow rollback.
The full suite passes 30/31; the only failure is the already documented fractional-
distance concurrent leaderboard test. All six points tests pass.

Unity tests cover wallet parsing, reward boundaries, offline/timeout recovery,
account switching, late responses, same-account re-login and pending UI state.
Physical-device validation is still needed: finish offline, reconnect, verify the
wallet and receipt in Firestore, restart, then sign in on a second device and
confirm the same balance. Repeating synchronization must not increase it again.

Firebase reference: [transactions and atomic writes](https://firebase.google.com/docs/firestore/manage-data/transactions).
