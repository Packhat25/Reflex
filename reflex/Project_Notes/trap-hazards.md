## Trap Hazards

### Current State
- `TrapHazardIndicator` handles visual warning tint/emission for trap geometry and tilemaps.
- `DmgArea` applies entry, dash, and stay damage to `PlayerManager`.
- `TrapStateController` toggles trap damage areas and renderers based on configured conditions.

### Build Safety
- `TrapHazardIndicator` must not create Unity engine objects from field initializers or constructors.
- Its `MaterialPropertyBlock` is created lazily during lifecycle/editor callbacks before applying renderer properties.
- `OnValidate()` clamps tuning values and re-applies indicators only after the property block exists.

### Files
- `Assets/Scripts/Player/TrapHazardIndicator.cs`
- `Assets/Scripts/Player/DmgArea.cs`
- `Assets/Scripts/Player/TrapStateController.cs`

### Testing Status
- C# build passes with `dotnet build Assembly-CSharp.csproj -nologo`.
- Unity build preprocessing should be rerun to confirm Visual Scripting AOT no longer reports `TrapHazardIndicator` constructor or null property-block errors.
