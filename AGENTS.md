# RefinedStorageControl

7 Days to Die mod: a wired storage network for centralized access to connected
containers and powered digital disks, with manual terminal crafting via native
stations. Targets game 3.3.0; managed assembly references come from
`<GameRoot>\7DaysToDie_Data\Managed` via the `GameManaged` msbuild property.

See `SPECS.md` (requirements and IDs), `TODO.md` (milestones), and
`RefinedStorage-Design.md` (decision record).

## Layout

- `Source/` — the mod assembly (`RefinedStorageControl.dll`, net48).
  - `Source/Core/` — network topology registry (pure).
  - `Source/Storage/` — disk records and binary serialization (pure).
  - `Source/Access/` — owner + allowlist ACLs (pure).
  - `Source/Routing/` — transfer budget, stack placement, provider ordering
    and filters (pure).
  - `Source/Game/` — the only layer allowed to touch game/Unity types
    (`IModApi` entry point, `Log`, console commands).
    - `Source/Game/Topology/` — wire tree projection and wire rules.
    - `Source/Game/Providers/` — vanilla container providers, the host
      transfer service, and `rsc net` commands.
    - `Source/Game/Persistence/` — RSC save files (interface settings) and
      the save/removal hooks.
    - `Source/Game/Blocks/` — `BlockRscNode` (E action for terminals and
      interfaces; keeps the vanilla powered tile entity).
    - `Source/Game/UI/` — XUi controllers; layouts live in
      `Mod/Config/XUi_InGame/`.
    - `Source/Game/Prototypes/` — P0 `rsc proto` commands (dev only).
- `Tests/` — net48 console harness compiling `Source/**` **except** `Source/Game/`.
  This is the purity check: Core/Storage/Access/Routing must never use game or
  Unity types. If the test build needs a game reference, the purity boundary
  has been violated.
- `Mod/` — deployable mod files (`ModInfo.xml`, future `Config/`).
- `Artifacts/RefinedStorageControl/` — packaged mod produced by the build
  (gitignored).

## Build and test

```
pwsh ./Tools/Build.ps1
```

Builds both projects, runs the test harness, and assembles the packaged mod.
The build fails if the artifact contains any file that also exists in the
game's Managed directory (no game assemblies may ship).

```
pwsh ./Tools/Build.ps1 -Install
```

Additionally mirrors the artifact into `Mods/RefinedStorageControl_Dev` under
the game root so the mod can be loaded in-game without touching this dev
tree. Close the game first — `RefinedStorageControl.dll` stays locked while
it is running.

## Important

- **Never** place a `ModInfo.xml` at the repo root — this repo sits inside the
  game's Mods folder and the game would load the dev tree. The mod manifest
  lives only in `Mod/`.
- Do not commit build output (`bin/`, `obj/`, `Artifacts/`).
- Open decisions F01-F12 in `SPECS.md` must not be hardcoded as accepted
  defaults — keep them configurable or unimplemented.
