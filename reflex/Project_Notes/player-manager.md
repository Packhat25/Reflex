## Player Manager

### Current State
- `PlayerManager` owns core player runtime state such as health, death, attack flags, temporary card buffs, soul essence, and immortality.
- `isImmortal` is a testing/debug flag that causes `TakeDamage()` to return before applying incoming damage.
- Serialized player instances currently store `currentHealth: 0`, so `PlayerManager.Awake()` repairs invalid alive-player HP immediately and `Start()` restores full HP after saved upgrades are applied.
- `MaxHealth` prefers the assigned `PlayerData` asset but has safe fallback values to avoid build crashes if the data reference is missing.
- Damage/death telemetry is wrapped safely so an emotion-system issue cannot prevent lethal damage from reaching game-over handling.
- `PlayerDied` subscribers are invoked individually, and `InGameUIManager.ShowGameOver()` is attempted afterward as a direct fallback.
- Death now attempts direct game-over presentation before notifying subscribers.
- If no `InGameUIManager` exists at death time, `PlayerManager` creates a runtime fallback manager for emergency game-over UI.

### Debug Controls
- Press `=` during Play Mode to toggle `isImmortal`.
- The key is serialized as `immortalToggleKey` in `PlayerManager` and defaults to `Key.Equals`.
- Toggling writes a console log showing whether immortality is enabled or disabled.

### Files
- `Assets/Scripts/Player/PlayerManager.cs`
- `Assets/Scripts/Game/UpgradeManager.cs`
- `Assets/Scripts/Visuals/UI/InGameUIManager.cs`

### Testing Status
- C# build passes with `dotnet build Assembly-CSharp.csproj -nologo`.
- Unity Play Mode/player-build validation is still required to confirm startup HP is full and the key toggles immortality/damage handling as expected.
