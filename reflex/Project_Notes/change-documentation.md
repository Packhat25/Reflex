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
  - Lobby -> Level 1 -> Level 2 -> Level 3 -> Level 4 (`Room_2`) -> Level 5 (`Level_3_Scene` reuse) -> Final Boss -> Lobby.
- Door binder now supports directional fallback names (for example `North`, `Walls_North`) when explicit `Door/Doors` objects are not present.
- When a node has exactly one route, all detected door candidates map to that same route for clearer progression.
- Added safe auto-advance fallback for scenes with no door candidates:
  - Lobby can enter the run even without explicit door objects.
  - Non-lobby nodes only auto-advance after the room is cleared.
- Run door binding/auto-advance now only applies when the active scene matches the current generated node, so direct test-scene launches (for example `SampleScene`) are not force-routed into the run.

### Build/Test
- `dotnet build reflex.sln` succeeded.
- Existing warning remains unrelated:
  - `Assets/Scripts/Movement/PlayerMovementManagement.cs(30,18) CS0649 isSprinting is never assigned`.

### Known Limitations
- A dedicated `Level_4` and `Level_5` scene is not yet present; current flow reuses available scenes.
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
