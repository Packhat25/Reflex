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
