# Island Mining Tycoon — Unity pre-release audit

Audit date: 20 September 2026  
Unity: 6000.4.9f1  
Scope: read-only Editor/Play Mode audit before Android and iOS builds

## Executive result

**Release readiness: NOT READY.** One Android signing blocker, one confirmed runtime UI failure, and an incomplete automated PlayMode gate must be resolved or explicitly accepted before release certification. iOS signing and real-device monetization still require platform builds.

The audit did not edit game source, scenes, prefabs, assets, or ProjectSettings, and no commit was made. The repository was already dirty with tracked and untracked development changes; those changes were preserved.

## Test and coverage summary

| Area | Result | Confidence |
|---|---|---|
| EditMode tests | **1,565 passed, 0 failed, 0 skipped** | Confirmed |
| PlayMode tests | Runner remained at **0 tests** during transition; job was manually stopped | Confirmed blocker/gap |
| Authored progression catalogue | `Chapters.Count = 8`, `Stages.PerChapter = 4`, `Stages.Count = 32` | Static confirmation |
| Fresh start | New empty save launched; initial UI appeared after a prolonged blank/gradient period | Editor observation |
| Live UI | Main HUD, Settings, Developer Tools, Store, Contracts, Daily Rewards, Captains, Master panel path, Upgrade path | Editor observation |
| Real Android/iOS build | Not run: no device/Xcode/signing environment available | Not tested |
| Live IAP, rewarded ads, push notifications | Not testable in Editor without platform services | Not tested |

The complete 8-chapter/32-stage progression, real device touch behavior, offline clock behavior across app restarts, platform purchases, ad callbacks, achievements, events, leaderboards, pets, inventory, and Sea gameplay could not be certified end-to-end because the project has no project PlayMode test folder and the PlayMode runner stalled before executing tests.

## Confirmed issues

### AUD-001 — Android custom keystore points to a machine-local macOS path

Severity: **Blocker**  
Location: `ProjectSettings/ProjectSettings.asset:275-288`  
Status: Confirmed from project settings

`AndroidKeystoreName` is set to:

`/Users/macbookair/Desktop/intake/islandminingtycoon/topsecretfckngislandmining.keystore`

`androidUseCustomKeystore: 1` is enabled. This path cannot resolve on the current Windows workstation or a clean build agent, so a signed Android release build will fail unless the keystore is supplied through the build environment and the setting is overridden. The keystore itself was not touched.

### AUD-002 — PlayMode release gate does not execute

Severity: **Blocker for QA/release certification**  
Status: Confirmed

Reproduction: Unity MCP `run_tests(mode=PlayMode)` was started from a ready Editor. The job stayed at 0 tests while Unity transitioned into Play Mode, then became stale and had to be stopped. Console reported:

`[TestJobManager] Clearing stale job e11a65cb96884ad9a025b4b7bce85da6`

There is also no `Assets/Scripts/Tests/PlayMode` directory; the project test inventory is EditMode-only. This leaves scene wiring, runtime save flows, UI navigation, platform callbacks, and actual gameplay without an automated runtime gate.

### AUD-003 — UI panels become invisible after Captain/Master navigation

Severity: **High / release blocker**  
Status: Confirmed in live Editor Play Mode

Reproduction:

1. Start from a fresh save.
2. Open Captains, close it, open the Master panel path, and close it.
3. Open Settings or Upgrade.

Expected: the selected panel is visible and usable.  
Actual: the game world is dimmed by the modal overlay, but the panel contents are not rendered. Buttons remain present and invoke actions, but the user cannot see the panel. Evidence: [main-after-masters.png](../Assets/AuditEvidence/Screenshots/main-after-masters.png), [settings-after-ui-close.png](../Assets/AuditEvidence/Screenshots/settings-after-ui-close.png), and [upgrade-after-ui-regression.png](../Assets/AuditEvidence/Screenshots/upgrade-after-ui-regression.png).

### AUD-004 — Fresh-start screen remains blank/gradient for an extended interval

Severity: **High UX/performance risk**  
Status: Confirmed observation; device impact unverified

Reproduction: enter Play Mode with no save file. At approximately 12 seconds the captured screen was still a blank gradient; the usable main scene appeared only after roughly 32 seconds. Evidence: [fresh-start-main.png](../Assets/AuditEvidence/Screenshots/fresh-start-main.png) and [fresh-start-after-32s.png](../Assets/AuditEvidence/Screenshots/fresh-start-after-32s.png).

Expected: a loading state, progress indicator, or usable scene within an acceptable startup window.  
Actual: an unexplained blank screen with no visible loading affordance.

### AUD-005 — Developer/test controls are exposed in the Editor settings flow

Severity: **High if a Development Build is submitted; otherwise release-hardening risk**  
Location: `Assets/Scripts/UI/SettingsUI.cs:114-125, 389-528`  
Status: Confirmed in Editor; retail-build exclusion is conditional on build symbols

Reproduction: Settings → `DEV`. The exposed panel includes tutorial replay, automatic/forced wear, repair, time controls, test mode, and a “max upgrade” action. Evidence: [settings.png](../Assets/AuditEvidence/Screenshots/settings.png) and [developer-tools-exposed.png](../Assets/AuditEvidence/Screenshots/developer-tools-exposed.png).

Expected: no economy/progression manipulation surface in a release build.  
Actual: the controls are reachable in the Editor and any Development Build because the feature is compiled under `UNITY_EDITOR || DEVELOPMENT_BUILD`. A release build must be verified explicitly with `DEVELOPMENT_BUILD` disabled.

### AUD-006 — Mixed language appears in one user flow

Severity: **Medium**  
Status: Confirmed observation

The Settings panel rendered in English while the Developer Tools panel rendered Turkish (`EĞİTİMİ TEKRAR OYNAT`, `KİR`, `ONAR`, `KAPAT`) during the same session. This is inconsistent with the selected language and indicates that developer-only strings are not localized or are bypassing the localization table.

### AUD-007 — Store purchase controls are not operational in the audit environment

Severity: **High potential monetization risk**  
Status: Potential; Editor limitation not yet separated from production behavior

The Store rendered its product cards, but the purchase controls displayed `—` and were disabled. Evidence: [store.png](../Assets/AuditEvidence/Screenshots/store.png). IAP uses Unity Purchasing 5.4.2 and must be tested on Google Play Internal Testing and an App Store sandbox account before release. No purchase, restore, receipt, or failure callback was certified.

### AUD-008 — Scrollable panels show clipped bottom content in the captured viewport

Severity: **Medium responsive-layout risk**  
Status: Potential; expected scrolling behavior was not fully testable through the MCP interaction path

The Store’s `MINE BOSS` card and the lower Captain roster content are cut off at the bottom of the captured portrait viewport. Evidence: [store.png](../Assets/AuditEvidence/Screenshots/store.png) and [captains.png](../Assets/AuditEvidence/Screenshots/captains.png). Verify on the smallest supported Android and iPhone safe-area sizes that content is reachable by touch and not merely clipped.

## Warnings and maintenance findings

- EditMode completed successfully but emitted obsolete `GetInstanceID` warnings in `Assets/Scripts/Tests/EditMode/StationForemenTests.cs:131,135`.
- The test runner emitted a notification warning stating that test notification spacing is enabled during tests. The serialized Bootstrap value is `notificationTestSpacingSeconds: 0` (`Assets/Scenes/Bootstrap.unity:476`), so this was not confirmed as a shipping runtime defect.
- Live Play Mode logging showed Analytics session start, island vehicle cleanup, and camera framing messages. No runtime exception or error was captured during the live UI pass.
- `Assets/Data/AdsConfig.asset:15` has `useTestAds: 0` and both Android/iOS rewarded IDs are populated. This is a positive static finding, but ad initialization and reward callbacks remain untested on devices.

## Build and release blockers

### Android

Confirmed blocker:

1. Custom keystore is configured to a non-portable macOS absolute path (`AUD-001`).

Release verification still required:

1. Produce a signed Android Release/IL2CPP build with the real keystore and verify install, launch, save migration, ads, IAP, notifications, back navigation, and safe-area behavior.
2. Validate the custom Gradle templates and dependency resolution. The project uses custom Gradle templates, AGP 9.0.0, AdMob 11.3.0, Unity Purchasing 5.4.2, AndroidX/Jetifier, and Google Mobile Ads 25.4.0 dependencies.
3. Confirm the arm64-only configuration is intentional and acceptable for the target device policy.

### iOS

Potential signing blocker:

1. `appleDeveloperTeamID`, manual provisioning profile ID, and manual profile type are empty in `ProjectSettings/ProjectSettings.asset:248-252`. Automatic signing is enabled, but Xcode team selection and signing credentials were not available for verification.

Release verification still required:

1. Export an Xcode project, resolve signing, archive, and validate on a physical iPhone.
2. Exercise ATT consent, localized `NSUserTrackingUsageDescription`, StoreKit purchase/restore, AdMob, notifications, privacy manifest, and App Store validation.
3. Verify portrait-only/full-screen behavior on current iPhone safe areas. Settings show iOS target 15.0 and `iOSRequireFullScreen: 1`.

## Screens and systems inspected

The live pass inspected the fresh-start main HUD, Settings, Developer Tools, Store, Contracts, Daily Rewards (including Claim), Captains, Master-panel path, and Upgrade-panel path. Static configuration and tests were also reviewed for chapters, stages, economy, ads, IAP, notifications, localization, privacy, Android dependencies, and iOS post-processing.

Screenshots captured during the audit are stored in [Assets/AuditEvidence/Screenshots](../Assets/AuditEvidence/Screenshots). They include the Store, Contracts, Daily Rewards, Captains, Settings, Developer Tools, fresh-start loading state, and the post-navigation invisible-panel state.

## Save and preference protection

The development save was snapshotted before testing. After Unity rotated the save during the first Play Mode launch, the preserved main save copy was restored as `save.dat`; final elevated verification reported SHA-256 `A4D2BC5CE20D8827DCE735EFD6C7C948FF1039C787AAF643F0CB522D14C20418` for both `save.dat` and `save.dat.bak`. PlayerPrefs were re-imported and verified for language, SFX, music, vibration, and language-selected values.

Because Unity had already rotated the older backup during the initial launch, `save.dat.bak` is now a safe duplicate of the restored main save rather than a distinct historical backup revision. No save file was intentionally deleted beyond removing the audit-generated fresh-start files during restoration.

## Areas not certified

The following remain unverified and should be explicit release-gate items: Android/iOS signed builds, device performance and touch input, real IAP and restore flows, rewarded ads and reward delivery, notification scheduling on device, App Store/Google Play validation, complete manual progression through all 32 stages, chapter reset after completion, offline earnings across real process termination, achievements, events, leaderboards, pets, inventory, and Sea combat end-to-end.

## Recommended release gate

Do not create submission builds until AUD-001 through AUD-003 are resolved or formally waived. Then run signed Android and iOS device smoke tests, add/repair a runtime PlayMode test gate, and replay this audit on clean builds with a disposable test account while keeping the restored development save untouched.
