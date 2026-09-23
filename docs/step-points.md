# Step points preview

The walking UI shows one point per ten steps, rounded down. This is a preview
derived from the current step totals, not a saved or spendable wallet.

`StepCountAndGpsManager.StepPoints` reads the current overall step total;
`WalkingSessionPoints` reads the active or most recent walk's steps, including
recovered steps. Both are calculated by the manager and work without a visible UI.
The existing `getPoint()` method returns `StepPoints` for compatibility. The
temporary `setPoint()` method was removed because a derived value should not be
overwritten by a display or mistaken for a currency balance.

`UIStepController` only displays the appropriate property, preserving its prefix
and session-selection settings. A missing manager displays zero.

Before using points for gacha, implement an account-owned saved balance, earning
receipts keyed by completed walk ID, and atomic spending/reward operations.
Do not subtract purchases from this preview: it is recalculated from steps.
