## Game Over UI

### Current State
- `InGameUIManager` owns game-over presentation through the authored `UI Manager` child `Game Over Canvas`.
- `PlayerManager.Die()` records death telemetry, notifies death subscribers, and directly attempts to show game over.
- `Return to Lobby` resets player run state and loads `Lobby`.
- The authored `Game Over Canvas` prefab object starts inactive, so runtime show logic must activate its GameObject before changing `CanvasGroup` or scale.

### Fallback Safety
- If the authored `Game Over Canvas` cannot be bound, `InGameUIManager` builds a minimal emergency game-over canvas at runtime.
- The authored screen remains the preferred path; the fallback exists only to prevent a silent death-flow failure.
- A missing return button no longer prevents the game-over screen from appearing, though it logs a warning.
- If no `InGameUIManager` exists when the player dies, `PlayerManager` creates a runtime fallback manager so the emergency canvas can still be shown.

### Files
- `Assets/Scripts/Visuals/UI/InGameUIManager.cs`
- `Assets/Scripts/Player/PlayerManager.cs`

### Testing Status
- C# build passes with `dotnet build Assembly-CSharp.csproj -nologo`.
- Unity Play Mode/player-build validation is still required for lethal damage -> Game Over Canvas -> Return to Lobby.
