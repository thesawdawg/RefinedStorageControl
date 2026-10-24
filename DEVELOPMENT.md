# Developer guide

How to set up a new machine, build, test, change the art, and pick the work back up. Agents start with [AGENTS.md](AGENTS.md); this file has the details it links to.

## 1. Where things stand (October 24, 2026)

| Phase | State |
|---|---|
| P0 feasibility | Closed. [P0-FINDINGS.md](P0-FINDINGS.md) |
| P1 physical storage (network, interfaces, terminal, co-op packages) | Built, host-tested in game |
| P2 digital storage (disks, cabinets, ACLs, destruction, recipes/gates) | Built, host-tested in game |
| P3 terminal crafting (native stations, countdown, jobs, XP, claim keepalive) | Built; crafting and output collection host-tested |
| P4 release prep | Settings file, journal, disk export, docs, final names, icons and block models done |

**Open work, in order** (details in [TODO.md](TODO.md)):

1. The owner's full in-game test run: the [P2](P2-TEST-GUIDE.md) and [P3](P3-TEST-GUIDE.md) guides, the co-op checks, and the [acceptance scenarios](SPECS.md#11-acceptance-scenarios). Fix what it finds.
2. The P4 performance benchmark (24 interfaces, 2 terminals, 2 cabinets, 3 players), which settles F12 (the final advanced transfer cap).
3. A balance review of recipe costs and unlock levels (`Mod/Config/recipes.xml`, `progression.xml`).
4. Optional: world models for dropped and held disks; disks still use the vanilla electrical-parts model.
5. Nexus page: [Release/NEXUS.md](Release/NEXUS.md) needs version, permissions and screenshots. Publishing needs the owner's go-ahead.

Known gaps already accepted are listed under "Known gap" in [RefinedStorage-Design.md](RefinedStorage-Design.md).

## 2. Prerequisites

| Tool | Version used | Needed for |
|---|---|---|
| 7 Days to Die | 3.3.0 b18 (Steam, Windows) | game assemblies to compile against; running the mod |
| .NET SDK | 6.0.4xx or later | `Tools/Build.ps1` (`dotnet build`) |
| .NET Framework 4.8 Developer Pack (targeting pack) | 4.8 | compiling the net48 mod and tests (Visual Studio's .NET desktop workload also installs it) |
| PowerShell | 7.x (`pwsh`) | build scripts |
| Python | 3.11+ | only for ad-hoc doc/edit scripts; not part of the build |
| Blender | 5.0 (4.2+ works) | icons, previews and FBX models (`Tools/Art`) |
| Unity | **2022.3.62** with Windows Build Support, plus an activated licence (Personal is fine) | the model bundle (`Tools/Unity`). Must match the game's Unity minor release: 3.3 b18 ships 2022.3.62f2; 2022.3.62f3 has been used successfully. Check `UnityPlayer.dll` → Properties → Details after a game update. |
| ilspycmd | 8.2.0.7535 (optional) | decompiling the game for research (`Investigation/`) |
| git | any | GitHub remote `origin` (thesawdawg/RefinedStorageControl), branch `develop` |

You only need Blender and Unity to change the art: the icons and `Mod/Resources/rsc.unity3d` are committed, so a code-only machine just needs the game, the .NET SDK and PowerShell.

## 3. Set up a new machine

1. Install the game and run it once.
2. Clone the repo **inside the game's Mods folder**:
   ```
   cd "<Steam>\steamapps\common\7 Days To Die\Mods"
   git clone https://github.com/thesawdawg/RefinedStorageControl.git
   git -C RefinedStorageControl checkout develop
   ```
   `Tools/Build.ps1` finds the game two folders up. A clone elsewhere works with `-GameRoot "<game folder>"`. The repo root has no `ModInfo.xml`, so the game never loads the dev tree itself (keep it that way).
3. Build and install the dev copy:
   ```
   pwsh ./Tools/Build.ps1 -Install
   ```
   Expect `PASS: N checks passed.` and `Installed: ...\Mods\RefinedStorageControl_Dev`.
4. Start the game with EAC off, then make a new Creative world. Check the log for `[RSC] Refined Storage Control ... initialized` and `[RSC] Harmony patches applied`.
5. Optional, for game research (the decompile is not committed):
   ```
   dotnet tool install ilspycmd --version 8.2.0.7535 --tool-path .tools
   .tools\ilspycmd -p -o Investigation\AssemblyCSharp "..\..\7DaysToDie_Data\Managed\Assembly-CSharp.dll"
   ```
   The game's Assembly-CSharp is publicized, so private members show as public with `[PublicizedFrom]`, and mod code may use them.
6. Optional, for art: install Blender and Unity (see section 2), sign in to Unity Hub, and activate a licence once.

## 4. Everyday workflow

| Task | Command |
|---|---|
| Build, unit tests, package | `pwsh ./Tools/Build.ps1` |
| ...and install to `Mods/RefinedStorageControl_Dev` | `pwsh ./Tools/Build.ps1 -Install` (close the game first: the DLL is locked while it runs) |
| Unit tests only | they run as part of the build (`Tests/`, a plain console harness) |
| Change icons/models (Blender only) | `"<Blender>\blender.exe" -b --factory-startup -P Tools/Art/rsc_art.py -- .` |
| Change models and rebuild the game bundle | `pwsh ./Tools/Unity/BuildBundle.ps1` (runs Blender, then Unity in batch mode; `-SkipBlender` reuses `Art/Models`) |
| In-game diagnostics (F1) | `rsc` lists everything: `rsc net list x y z`, `rsc disk list`, `rsc craft jobs`, `rsc journal 50`, `rsc settings`, `rsc proto look` |

**Logs:** `%AppData%\7DaysToDie\logs\output_log_client__*.txt` (newest file). RSC lines start with `[RSC]`.

**Where the mod keeps world data:** `%AppData%\7DaysToDie\Saves\<world>\<save>\RefinedStorageControl\` (`nodes.dat`, `disks.dat`, `jobs.dat`, `journal.log`, `export/`).

**After a rename of block names**, old worlds lose those blocks; use a new world (ENV-04 allows it).

## 5. How the code is organised

See [AGENTS.md](AGENTS.md) for the folder map. The rules that matter most:

- **Purity boundary:** everything outside `Source/Game/` is pure C# with no game or Unity types. `Tests/` compiles all of `Source/` except `Source/Game/`, so a game type there breaks the test build. Put logic in the pure modules and unit-test it; keep `Source/Game/` thin.
- **Host authority:** every mutation happens on the host/server; clients send packages (`Source/Game/Net/`) and the server re-validates.
- **Network packages:** the game finds packages by class name, so names must be unique. Write primitives through `((BinaryWriter)_bw)`, because the game's `PooledBinaryWriter` has span overloads net48 can't resolve.
- **Game references:** `Source/RefinedStorageControl.csproj` references Assembly-CSharp, UnityEngine.CoreModule, UnityEngine.PhysicsModule, LogLibrary and InControl, all with `Private=false`. The build refuses to package any game assembly.
- **XML and localization:** mod XML lives in `Mod/Config/` (validated by the build). The game reads only `Config/Localization.csv`, with the header `Key,File,Type,UsedInMainMenu,NoTranslate,english`.
- **Settings:** balance values live in `Mod/Settings.xml` (`Source/Config/RscSettings.cs` defines names, ranges and defaults).

## 6. Decisions and documents

- [SPECS.md](SPECS.md) holds the requirements, with IDs (NET-, ROUTE-, DISK-, CRAFT-, RUN-, ...), acceptance scenarios A01-A15 and the open-decision table (F-ids).
- [RefinedStorage-Design.md](RefinedStorage-Design.md) is the decision record and implementation notes for each phase. It's the source of truth when something is unclear.
- [TODO.md](TODO.md) holds the milestone checklists. Items are marked "built; awaiting in-game checks" until the owner tests them.
- **Rule:** a decision is never hard-coded until the owner accepts it. When accepted, record it in the design doc **and** SPECS (with the date), and update the affected acceptance scenarios in the same change. Still open: F08/F09 (v2) and the F12 cap value (benchmark).
- Test guides (`P2-TEST-GUIDE.md`, `P3-TEST-GUIDE.md`) are what the owner follows in game; write one for each new feature set.

## 7. Art pipeline

1. `Tools/Art/rsc_art.py` builds every model from primitives in Blender, so there are no hand-edited `.blend` files to merge. Colours: dark steel case and one accent per component (see the `BLOCKS` and `DISK_TIERS` tables). It writes:
   - `Mod/UIAtlases/ItemIconAtlas/<name>.png`: 160×160 icons (shipped; blocks and items use `CustomIcon=<name>`)
   - `Art/Renders/`: 512 px previews and `lineup.png`
   - `Art/Models/<name>.fbx`, plus `Art/rsc_models.blend`
2. `Tools/Unity/BuildBundle.ps1` runs `RscBundleBuilder.Build` in `Tools/Unity/RscBundle` and writes `Mod/Resources/rsc.unity3d`. `blocks.xml` uses `Model=#@modfolder:Resources/rsc.unity3d?Assets/RSC/Prefabs/<block>.prefab`.
3. Commit the regenerated PNGs, FBX files, blend file and bundle together with the script change.

Game conventions that the builder already handles, learned the hard way:

| Convention | Why it matters |
|---|---|
| Prefab pivot at the **block centre**; the model stands on y = -0.5 | The game places a model block's pivot at the block centre for every rotation. A pivot at the model's base made models float half a block. |
| Collider on the **prefab root** | A hit is traced back to its block by comparing against the root transform; a child collider left blocks impossible to interact with. |
| `T_Block` tag set **at runtime** (`BlockRscNode.OnBlockEntityTransformBeforeActivated`) | Bundles store tags as indexes into the building project's tag list, which don't match the game's. |
| Child named `WireOffset` | It's where wires attach (`TileEntityPowered.BlockTransform`). |
| Prefab name equals the asset name in the path | The game logs "Model has a wrong name" otherwise. |
| No coplanar faces between parts | Overlapping surfaces flicker (z-fighting); see the top plates in `rsc_art.py`. |
| Unity minor release matches the game | Bundles from a newer Unity generally don't load in an older player. |

## 8. Other gotchas found during development

- **Item identity:** stackable items are keyed without their seed (`ItemKeys.FromItemValue`). The game stamps a clock-based seed on every new item and ignores it when stacking; keying on it split crafted batches into separate slots.
- **Wire rules:** only a controller may take vanilla power, and RSC blocks never feed vanilla devices (NET-06). The host sees the reason as a swapped tooltip.
- **Blocks that were never wired** have no power item, so they aren't on any network until the wire tool first connects them.
- **Pickup** uses the vanilla land-claim rule (only inside your own claim). Cabinets with disks refuse pickup.
- **Station attribution:** terminal jobs tag their queue entries with a negative crafter id (`CraftTags`), and a Harmony patch keeps those records from merging with players' own crafting.
- **The shell on Windows:** the Bash tool's heredocs mishandle some quoting, and `python -c` is unreliable through the pyenv shim. Write helper scripts to a file and run them with `python -I <file> <args>`.

## 9. Git and commits

- Work on `develop`. `main` still holds only the first planning commit; merging to it is a release decision for the owner. The remote is `origin` on GitHub. Pushing, opening PRs and publishing to Nexus need the owner's go-ahead.
- Keep build output out of git (`bin/`, `obj/`, `Artifacts/`, Unity `Library/`, `Temp/`, `Logs/`, `UserSettings/`, `Assets/RSC/`, `BundleOut/`, `*.blend1`, `Investigation/`, `.tools/`).
- One logical change per commit, with the message explaining why. Agent commits end with the co-author line the harness provides.
