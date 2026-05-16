## 2026-05-17 - One-Time Lobby Entry (No Lobby Between Floors)

### Summary
Updated progression flow so Lobby is only used at game start. After a floor is completed, the run now advances directly into the next floor's stage 1 without returning to Lobby.

### Files Affected
- Assets/Scripts/LevelGeneration/LevelRunManager.cs

### Systems Affected
- Floor transition flow
- Generated run graph destination labeling

### Gameplay Changes
- Final stage transition no longer loads Lobby.
- Floor completion now increments floor immediately and loads next floor stage 1 scene directly.
- Node `0` transitions from final stages are now interpreted as a `Next Floor` transition marker.
- Door label for node `0` destination now displays `Next Floor` instead of `Lobby`.

### Build/Test
- `dotnet build reflex.sln` succeeded.
- Existing warning remains unrelated:
  - `Assets/Scripts/Movement/PlayerMovementManagement.cs(30,18) CS0649 isSprinting is never assigned`.

## 2026-05-17 - Floor Debug HUD + Auto Docking

### Summary
Added a dedicated floor debug HUD that displays the current floor and stage, and auto-docks to avoid overlap when another debug HUD is visible.

### Files Affected
- Assets/Scripts/AI/FloorDebugHUD.cs
- Assets/Scripts/AI/FloorDebugHUD.cs.meta
- Assets/Scripts/AI/EmotionDebugHUD.cs

### Systems Affected
- Runtime debug UI overlays
- Floor progression visibility and QA observability

### Gameplay/UI Changes
- Added `FloorDebugHUD` (toggle key: `F5`) with:
  - Current floor
  - Current stage / stages per floor
  - Floor difficulty multipliers (HP, damage, spawn, respawn)
- Added auto-docking behavior:
  - If `EmotionDebugHUD` is visible, floor HUD docks to the right when possible, otherwise below.
  - Final position is clamped to screen bounds.
- Added screen-size-aware scaling for readability across resolutions.
- Exposed visible-area information from `EmotionDebugHUD` so other debug overlays can position relative to it.

### Build/Test
- `dotnet build reflex.sln` succeeded.
- Existing warning remains unrelated:
  - `Assets/Scripts/Movement/PlayerMovementManagement.cs(30,18) CS0649 isSprinting is never assigned`.

## 2026-05-17 - Randomized Stage Order Per Floor

### Summary
Updated floor progression so stage order randomizes every floor instead of always following a fixed scene sequence.

### Files Affected
- Assets/Scripts/LevelGeneration/LevelRunManager.cs

### Systems Affected
- Floor run generation and per-floor scene ordering

### Gameplay Changes
- Enabled per-floor stage-order randomization.
- Each new floor now gets a fresh shuffled stage sequence.
- Boss scenes are still pinned to the final stage of each floor when available, preserving a strong floor climax.
- Added profile-aware scene pooling for randomized generation:
  - Non-boss and boss scene candidates are derived from `LevelGenerationProfile.RoomScenes`.
  - Falls back to `roomSceneNames` if profile candidates are unavailable.

### Build/Test
- `dotnet build reflex.sln` succeeded.
- Existing warning remains unrelated:
  - `Assets/Scripts/Movement/PlayerMovementManagement.cs(30,18) CS0649 isSprinting is never assigned`.

## 2026-05-17 - Floor Loop Progression (Stage 1-5 per Floor)

### Summary
Implemented floor-based progression where each floor runs through stages 1-5, then returns to Lobby and advances to the next floor. Added floor-scaled enemy difficulty so higher floors are harder.

### Files Affected
- Assets/Scripts/LevelGeneration/LevelRunManager.cs
- Assets/Scripts/AI/States/SpawnControl/EnemySpawner.cs
- Assets/Scripts/AI/EnemyController.cs
- Assets/Scripts/Interactables/RewardManager.cs
- Assets/Resources/LevelGeneration/Default Level Generation Profile.asset

### Scenes Affected
- Assets/Scenes/Lobby.unity
- Assets/Scenes/Level_1_Scene.unity
- Assets/Scenes/Level_2_Scene.unity
- Assets/Scenes/Level_3_Scene.unity
- Assets/Scenes/Level_4_Scene.unity
- Assets/Scenes/Final Boss Level.unity

### Systems Affected
- Run generation and scene progression
- Floor/stage labeling and reward context mapping
- Enemy spawn pressure scaling
- Enemy stat scaling

### Gameplay Changes
- Run path now executes as stage ladder per floor:
  - Stage 1: `Level_1_Scene`
  - Stage 2: `Level_2_Scene`
  - Stage 3: `Level_3_Scene`
  - Stage 4: `Level_4_Scene`
  - Stage 5: `Final Boss Level`
- After clearing stage 5 and returning to Lobby, floor increments (`Floor 1 -> Floor 2 -> Floor 3 ...`) and a new floor run is generated.
- Difficulty increases with floor:
  - Enemy health scales up per floor.
  - Enemy damage scales up per floor.
  - Spawn counts scale up per floor.
  - Respawn delay scales down per floor (with minimum clamp).
- Door destination labels now display `Floor X - Stage Y`.
- Fallback scene order is now deterministic (sequential) if profile scene candidates are unavailable.

### Build/Test
- `dotnet build reflex.sln` succeeded.
- Existing warning remains unrelated:
  - `Assets/Scripts/Movement/PlayerMovementManagement.cs(30,18) CS0649 isSprinting is never assigned`.

### Known Limitations
- Runtime playtesting is still required for balance tuning of higher-floor scaling values.
- Floor progression currently resets when a fresh session starts (no cross-session persistence yet).

## 2026-05-17 - Room_2 Removal and Flow Repair

### Summary
Removed `Room_2` from the active level flow and fixed a broken default generation profile asset that had unresolved Git merge markers, which was causing fallback behavior and incorrect lobby returns.

### Files Affected
- Assets/Resources/LevelGeneration/Default Level Generation Profile.asset
- Assets/Scripts/LevelGeneration/LevelRunManager.cs
- ProjectSettings/EditorBuildSettings.asset

### Scenes Affected
- Assets/Scenes/Lobby.unity
- Assets/Scenes/Level_1_Scene.unity
- Assets/Scenes/Level_2_Scene.unity
- Assets/Scenes/Level_3_Scene.unity
- Assets/Scenes/Level_4_Scene.unity
- Assets/Scenes/Final Boss Level.unity
- Assets/Scenes/SampleScene.unity

### Systems Affected
- Level generation profile loading
- Deterministic progression pathing
- Build scene inclusion list

### Gameplay Changes
- Rebuilt `Default Level Generation Profile.asset` as valid Unity YAML (removed unresolved merge conflict markers).
- Updated deterministic run path to remove `Room_2` while preserving the 6-step flow:
  - Lobby -> Level 1 -> Level 2 -> Level 3 -> Level 4 -> Level 5 (`Level_4_Scene` reuse) -> Final Boss -> Lobby.
- Removed `Room_2` from build scenes.
- Corrected fallback room scene name typo in `LevelRunManager`:
  - `Level_4_scene` -> `Level_4_Scene`.

### Build/Test
- `dotnet build reflex.sln` succeeded.
- Existing warning remains unrelated:
  - `Assets/Scripts/Movement/PlayerMovementManagement.cs(30,18) CS0649 isSprinting is never assigned`.

### Known Limitations
- A dedicated `Level_5` scene is still pending; `Level_4_Scene` is currently reused for floor 5.
- Runtime playtesting in Unity Editor is still required to validate full traversal feel.

## 2026-05-16 - PatrolState NavMesh Guard Fix

### Summary
Fixed a runtime AI crash caused by reading `NavMeshAgent.remainingDistance` when the agent is not currently on a NavMesh.

### Files Affected
- Assets/Scripts/AI/States/PatrolState.cs

### Systems Affected
- Enemy AI state machine
- Patrol navigation safety checks

### Gameplay Changes
- `PatrolState` now verifies the agent is non-null, active, and on a NavMesh before:
  - setting patrol destinations in `OnEnter()`
  - evaluating arrival via `remainingDistance` in `Tick()`
- Enemies that are temporarily off NavMesh no longer throw exceptions from patrol logic.

### Build/Test
- Pending in-editor Play Mode validation for spawn placements and patrol transitions.

### Known Limitations
- This change prevents the crash, but enemies spawned off-bake may still idle until placed back on a valid NavMesh area.

## 2026-05-16 - Lobby Entry Flow + Linear Run to Boss

### Summary
Wired the game to use `Lobby` as the true entry scene with a deterministic level path and a reachable boss endpoint, while keeping `SampleScene` available for testing.

### Files Affected
- Assets/Scripts/LevelGeneration/LevelDoorAutoBinder.cs
- Assets/Scripts/LevelGeneration/LevelRunManager.cs
- Assets/Scripts/LevelGeneration/LevelGenerationProfile.cs
- Assets/Resources/LevelGeneration/Default Level Generation Profile.asset
- ProjectSettings/EditorBuildSettings.asset

### Scenes Affected
- Assets/Scenes/Lobby.unity
- Assets/Scenes/Level_1_Scene.unity
- Assets/Scenes/Level_2_Scene.unity
- Assets/Scenes/Level_3_Scene.unity
- Assets/Scenes/Room_2.unity
- Assets/Scenes/Final Boss Level.unity
- Assets/Scenes/SampleScene.unity

### Systems Affected
- Build scene bootstrap order
- Generated run graph and door routing
- Scene transition fallback behavior

### Gameplay Changes
- Build scene list now includes `Final Boss Level` and retains `SampleScene` as a test scene.
- Default run profile is now linear and deterministic:
  - Lobby -> Level 1 -> Level 2 -> Level 3 -> Level 4 (`Level_4_Scene`) -> Level 5 (`Room_2`) -> Final Boss -> Lobby.
- Door binder now supports directional fallback names (for example `North`, `Walls_North`) when explicit `Door/Doors` objects are not present.
- When a node has exactly one route, all detected door candidates map to that same route for clearer progression.
- Added safe auto-advance fallback for scenes with no door candidates:
  - Lobby can enter the run even without explicit door objects.
  - Non-lobby nodes only auto-advance after the room is cleared.
- Run door binding/auto-advance now only applies when the active scene matches the current generated node, so direct test-scene launches (for example `SampleScene`) are not force-routed into the run.
- Corrected a scene-name/build-settings mismatch that caused `Room_2` load failures by aligning profile depth mapping with the available scene set and adding `Room_2` explicitly to build scenes.

### Build/Test
- `dotnet build reflex.sln` succeeded.
- Existing warning remains unrelated:
  - `Assets/Scripts/Movement/PlayerMovementManagement.cs(30,18) CS0649 isSprinting is never assigned`.

### Known Limitations
- A dedicated `Level_5` scene is not yet present; current flow uses `Room_2` as level 5.
- In-editor Unity Play Mode validation is still required for final interaction feel and exit readability.

## 2026-05-16 - Emotion Loop Refinement

### Summary
Improved the emotion adaptation loop so game responses update continuously from aggression score and confidence, not only when calm/aggressive state flips.

### Files Affected
- Assets/Scripts/AI/EmotionEngine.cs
- Assets/Scripts/AI/EmotionDirector.cs
- Assets/Scripts/AI/EmotionDebugHUD.cs
- Assets/Scripts/AI/States/SpawnControl/EnemySpawner.cs

### Systems Affected
- Emotion analysis and signaling
- Emotion director adaptation logic
- Spawn count and respawn timing adaptation
- Debug observability

### Gameplay Changes
- Added `EmotionProfileUpdated` event emitted during each evaluation pass.
- Director now computes a continuous blend between calm and aggressive tuning based on aggression score and confidence.
- Director updates are now bounded in logging to reduce spam while still reporting meaningful adaptation shifts.
- Respawn delay scaling can now use continuous score/confidence blending.
- Debug HUD now displays director blend/confidence to make adaptation behavior easier to tune.

### Design Notes
- Maintains existing calm/aggressive strategy framing while making response intensity smoother.
- Confidence dampens overreaction early in a room when evidence is weak.

### Build/Test
- `dotnet build reflex.sln` succeeded.
- One existing warning remains unrelated to this change:
  - `Assets/Scripts/Movement/PlayerMovementManagement.cs(30,18) CS0649 isSprinting is never assigned`.

### Known Limitations
- Runtime playtesting in Unity Editor is still needed for final feel tuning.
- `GAME_DEV_RULES.md` appears to contain encoding artifacts and should be normalized to UTF-8.

## 2026-05-16 - Emotion Distinction Pass (Behavior + Visual)

### Summary
Made calm vs aggressive profiles more visibly and behaviorally distinct by wiring existing director fields into live chase movement and scene tinting.

### Files Affected
- Assets/Scripts/AI/States/ChaseState.cs
- Assets/Scripts/AI/EmotionDirector.cs

### Gameplay Changes
- `ChaseState` now uses `GetDirectorChaseDestination(...)` as a tactical destination source.
- In `AggressionContainment`, enemies now honor standoff/retreat behavior while still applying local separation.
- Calm chase flow keeps ring-style pressure but now routes through director tactical destination.

### Visual Changes
- Director `worldTint` is now applied to:
  - `RenderSettings.ambientLight` (tint strength controlled)
  - Active camera background colors (tint strength controlled)
- Visual baselines are cached per scene and restored when tinting is disabled or director is disabled.

### Build/Test
- `dotnet build reflex.sln` succeeded.
- Existing warning remains:
  - `Assets/Scripts/Movement/PlayerMovementManagement.cs(30,18) CS0649 isSprinting is never assigned`.

### Known Limitations
- Final tuning still requires in-editor playtesting for perceived intensity and readability.

## 2026-05-17 - Calm Motivation Pass (Relief + Composure Rewards)

### Summary
Implemented a calm-play motivation loop without recovery buffs by adding tactical relief charges and composure essence rewards.

### Files Affected
- Assets/Scripts/AI/EmotionEngine.cs
- Assets/Scripts/AI/EmotionDirector.cs
- Assets/Scripts/AI/EmotionDebugHUD.cs
- Assets/Scripts/Interactables/RewardManager.cs
- Assets/Scripts/Visuals/UI/InGameUIManager.cs

### Gameplay Changes
- Added `EmotionEngine.RoomStarted` event so systems can react exactly when a new combat room starts.
- Added Calm Relief charges in the emotion director:
  - Calm, low-damage, deathless rooms with enough combat actions earn charges.
  - Next room can consume a charge for reduced spawn pressure and slower enemy aggression.
- Added Composure Soul Essence bonus in reward manager:
  - Eligible calm rooms grant extra essence with quality scaling.
  - Optional on-screen status message communicates the reward immediately.

### Design Notes
- Keeps kill-all progression intact while making calm execution strategically valuable.
- Incentive is explicit (extra currency) and practical (easier next-room pressure), encouraging deliberate play instead of panic trading.

### Build/Test
- Pending compile + play-mode pass after this change set.

### Known Limitations
- Status message feedback requires UI references (`statusMessageText` and `statusMessageCanvasGroup`) to be assigned in the scene UI.
- Final threshold tuning still needed in-editor for pacing and fairness.

## 2026-05-17 - Aggression Spike Mitigation (Stacked Enemy Hits)

### Summary
Added burst-aware hit scoring so one punch hitting a stacked pack no longer spikes aggression as if all hits were fully independent actions.

### Files Affected
- Assets/Scripts/AI/EmotionEngine.cs
- Assets/Scripts/AI/EmotionDebugHUD.cs

### Gameplay Changes
- Added effective hit tracking separate from raw hit count.
- Introduced diminishing returns for additional hits in the same burst window.
- Added per-attack effective-hit budget cap to prevent single-swing over-weighting.
- Emotion hit score and confidence action evidence now use effective hits.
- Debug HUD now shows effective hits to support live tuning.

### Design Notes
- Raw combat events are preserved for analytics/readability.
- Emotion adaptation now reacts more to sustained aggressive behavior over time than to one high-density overlap event.

### Build/Test
- `dotnet build reflex.sln` succeeded.
- Existing warning remains:
  - `Assets/Scripts/Movement/PlayerMovementManagement.cs(30,18) CS0649 isSprinting is never assigned`.

### Known Limitations
- Final values for `multiHitBurstWindow`, `additionalHitFalloff`, and `maxEffectiveHitsPerAttack` need in-editor playtest tuning.

## 2026-05-17 - Aggression Tempo Tuning (Slower Build, Faster Cooldown)

### Summary
Adjusted emotion tempo so aggression climbs less abruptly from isolated attack events and decays faster when combat pressure cools down.

### Files Affected
- Assets/Scripts/AI/EmotionEngine.cs

### Gameplay Changes
- Added directional smoothing:
  - `aggressionRiseSmoothing` for slower upward movement.
  - `aggressionFallSmoothing` for faster downward movement.
- Added passive calm decay after short inactivity:
  - `calmDecayDelay`
  - `calmDecayPerSecond`
- Reduced attack/hit pressure sensitivity in weighted scoring:
  - `attackIntentScale`
  - `hitIntentScale`
- Combat actions now refresh a shared combat-intent timestamp used by decay logic.

### Design Notes
- Keeps responsiveness while reducing one-attack overreaction.
- Encourages emotion state to reflect sustained behavior, not single spikes.

### Build/Test
- `dotnet build reflex.sln` succeeded.
- Existing warning remains:
  - `Assets/Scripts/Movement/PlayerMovementManagement.cs(30,18) CS0649 isSprinting is never assigned`.

### Known Limitations
- Final values still need in-editor feel validation against different weapons and room densities.

## 2026-05-17 - Forgiving Aggression Rebalance

### Summary
Rebalanced emotion thresholds and tempo to make aggression significantly more forgiving for passive/disengage play patterns.

### Files Affected
- Assets/Scripts/AI/EmotionEngine.cs

### Gameplay Changes
- Raised aggressive entry threshold and widened hysteresis:
  - `aggressiveThreshold`: `0.58 -> 0.64`
  - `calmThreshold`: `0.42 -> 0.46`
- Reduced upward response intensity:
  - `aggressionRiseSmoothing`: `0.20 -> 0.14`
  - `attackIntentScale`: `0.75 -> 0.58`
  - `hitIntentScale`: `0.70 -> 0.52`
- Increased cooldown/recovery behavior:
  - `calmDecayDelay`: `0.90 -> 0.55`
  - `calmDecayPerSecond`: `0.07 -> 0.11`
  - `recentBehaviorWeight`: `0.60 -> 0.75`
- Added explicit passive forgiveness model:
  - `passiveRecoveryBoost`
  - `passiveForgivenessBias`
  - Recovery now prioritizes recent calm behavior when recent score drops below lifetime score.
- Raised transition evidence requirement:
  - `minimumEvidenceForChange`: `0.25 -> 0.38`

### Design Notes
- Intent is to prevent “one hit + disengage” loops from drifting aggressive unless pressure is sustained.
- Aggressive state should now represent sustained combat intent, not brief interactions.

### Build/Test
- `dotnet build reflex.sln` succeeded.
- Existing warning remains:
  - `Assets/Scripts/Movement/PlayerMovementManagement.cs(30,18) CS0649 isSprinting is never assigned`.

### Known Limitations
- Requires in-editor playtest to calibrate final feel around specific weapon cadence and room layouts.

## 2026-05-17 - Floor Transition Stability Fix (Emotion Engine)

### Summary
Added progression-stability safeguards so emotion telemetry does not saturate or stall across floors and continues updating reliably after floor transitions.

### Files Affected
- Assets/Scripts/AI/EmotionEngine.cs

### Gameplay Changes
- Emotion engine now listens for floor entry (`LevelRunManager.LevelEntered`).
- On combat floor entry:
  - optionally clears stale active room/spawner state
  - optionally rebases telemetry with configurable carryover factor
  - forces an immediate emotion evaluation to keep HUD/director state fresh
- Added tunables:
  - `rebaseTelemetryOnLevelEntered`
  - `levelCarryoverFactor`
  - `clearRoomStateOnLevelEntered`

### Design Notes
- Prevents lifetime metric saturation from making aggression appear frozen at higher floors.
- Keeps room state consistent when moving between scenes/floors.

### Build/Test
- `dotnet build reflex.sln` succeeded.
- Existing warning remains:
  - `Assets/Scripts/Movement/PlayerMovementManagement.cs(30,18) CS0649 isSprinting is never assigned`.

### Known Limitations
- Needs in-editor multi-floor verification to confirm behavior under all room/spawner combinations.
