# Walking Dog documentation

Start with [the system documentation](system-documentation.md), organized around
project setup, system responsibilities, fields, methods, execution flows and
dependencies to follow the supplied developer documentation reference. Its
formatted PDF is generated from that editable source.

The [game overview and player guide](game-documentation.md) remains supporting
material for a broader project report. Both describe the repository at `cdc8ebc`,
reviewed October 19, 2026.

## Developer handover outline

1. Project setup and runtime architecture.
2. Authentication and continuous steps.
3. GPS walks, persistence, recovery and history.
4. Territory and map display.
5. Wallet, gacha and collar inventory.
6. Dog training, race simulation and multiplayer rooms.
7. Rankings, friends, following and shared routes.
8. Screen navigation, animation and notifications.
9. Cloud contracts, verification and source index.

Actual Inspector screenshots and current device-test evidence still need to be
added. The reference game's screenshots and architecture are examples rather
than Walking Dog implementation facts.

## Report outline

1. Project overview: background, purpose, objectives and intended users.
2. Scope and requirements: supported features, use cases and quality goals.
3. Game design: gameplay loop, progression, rewards and competition rules.
4. Player guide: how to use each major feature.
5. System design: architecture, components, data and account access.
6. Development and setup: tools, project structure and build preparation.
7. Testing and evaluation: acceptance cases, evidence and results.
8. Limitations and future development.
9. References and appendices: source files, screenshots and project information.

This structure can later be transferred into a school or company template. It
does not assume a research methodology, user study or institutional format.

## How to finish the draft

1. Review the system manual with the team, starting with setup and architecture.
   Use sections 1–3 of the game overview to confirm the intended project story.
2. Supply the short list of project details in Appendix C when available.
3. Run the player flows on the build being documented and capture the screenshots
   listed in Appendix B. Record the build revision and test device.
4. Fill the test results table with observed results and evidence.
5. Review the completed content with the team before formatting a final report.

The text marked **Proposed** is an editorial suggestion. **Pending** means an
input or verification is still needed. Descriptions of implementation do not
establish that a feature passed a phone test.

## Existing technical notes

| Topic | Reference |
| --- | --- |
| Latest integration review | [Current baseline](current-game-baseline.md) |
| Current steps and rewards | [Continuous steps](continuous-steps.md) |
| Legacy wallet behavior | [Step points](step-points.md) |
| Route recording and recovery | [Walking reliability](walk-tracking-reliability.md) |
| Territory geometry and persistence | [Territory claiming](territory-claiming.md) |
| Training and practice racing | [Racing foundation](dog-racing-foundation.md) |
| Multiplayer race rooms | [Race lobby](race-lobby.md) |
| Friends and current rankings | [Friends leaderboard](friends-leaderboard.md) |
| Earlier leaderboard design | [Leaderboard integration](leaderboard-integration.md) |
| Profiles, following and sharing | [Following activity](following-activity.md) |
| Authentication | [Google sign-in](google-sign-in.md) |
| Firebase preparation | [Firebase preparation](firebase-preparation.md) |

Some notes describe earlier versions. Current continuous-step accounting replaces
per-walk reward accounting for new activity; race rooms extend the original
practice-only racing design. Use the main document for the consolidated narrative
and the source files for implementation details.
