## Player Movement

### Current State
- `PlayerMovementManagement` owns player movement, dash, knockback, dash hazard detection, and dash pass-through collision handling.
- Dash temporarily ignores selected enemy colliders so the player can pass through enemies during the dash.

### Dash Collision Safety
- Dash ignore/restore calls must only use colliders that are part of loaded Unity scenes.
- Cleanup skips stale, prefab-asset, and unloaded-scene colliders before calling `Physics.IgnoreCollision()`.
- Invalid restore attempts are caught and logged as warnings instead of crashing the player build.

### Files
- `Assets/Scripts/Movement/PlayerMovementManagement.cs`

### Testing Status
- C# build passes with `dotnet build Assembly-CSharp.csproj -nologo`.
- Unity player validation is still needed for dash -> death and dash -> scene transition cleanup.
