# P0 feasibility findings

Started October 19, 2026. Source: a local decompile of 3.3.0 b18 `Assembly-CSharp.dll` (in the gitignored `Investigation/` folder, never committed or shipped). **These are code-reading findings. Nothing here has been confirmed in game yet**, and each item lists the in-game check still needed. Requirement IDs refer to [SPECS.md](SPECS.md).

## 1. Wiring and topology (RSC-04; NET-02/03)

- The native wire tool (`ItemActionConnectPower`) only connects `TileEntityPowered` blocks. It calls `child.CanHaveParent(parent)` (virtual, base returns `true`), rejects a parent with `ChildCount > 8` (so at most 9 children), and uses `MaxWireLength` (default 15) from the wire tool's properties. It then calls `SetParentWithWireTool`, which **re-parents** the child. Each node has exactly one parent, so the graph is a tree with no loops.
- Power data is a global graph (`PowerManager`, saved to `power.dat` plus `.bak` every 120 s on a background thread and at cleanup). It is loaded at world start, independent of chunk loading. Each `PowerItem` knows its `Parent`, `Children`, `Position` and `BlockID`.
- Plain `PowerItem` (type `Consumer`) passes power to its children and draws `RequiredPower` from the block XML.
- **Implication:** RSC devices can be `BlockPowered` blocks with plain `TileEntityPowered`/`Consumer` power items. The data network is the native wire tree restricted to RSC blocks. A hub is a 0 W consumer that acts as a relay. The advanced tier gets native, per-device power draw (PWR-01) by wiring the controller to a power source. The simple tier needs no power, because membership does not depend on `IsPowered`.
- Duplicate-controller and network-join checks (NET-04, RSC-05) can hook `TileEntityPowered.CanHaveParent`, which needs a Harmony prefix because our blocks keep the vanilla TE class. Re-parenting means a single wire can move a whole subtree between networks, so the check must evaluate the moved subtree.
- Wire colours are hardcoded in `TileEntityPowered.DrawWires`; only electric-fence and trip-wire relays get special colours. Blue RSC wires (NET-02) need a Harmony postfix on `DrawWires`.
- Vanilla consumers could be wired as children of RSC devices. Decided October 23, 2026: blocked; only a controller may connect to vanilla wiring (NET-06).
- Placing a block creates no wires, so a newly placed controller is always isolated. The two-controller case (NET-04, RSC-05) can only arise when a wire is added. Wire checks therefore cover it; the placement-preview indicator only matters if placement ever auto-links.
- The wire tool's `CanHaveParent` check runs on the client. A remote client's wire then arrives as `NetPackageWireActions`, which the server applies with no check. RSC therefore enforces its rule in both places: `CanHaveParent` on the host, and the package handler on the server for remote clients.
- In-game checks: chain a 0 W node; confirm the wire limits; derive topology from `PowerManager` after save/reload.

## 2. Custom tile entities and RSC save data

- `TileEntity` instantiation on load switches over the closed `TileEntityType` enum. A custom `TileEntityPowered` subclass would be reloaded as the vanilla class and lose its extra data.
- **Implication:** keep vanilla tile entities. Store RSC node config (priority, filters, cabinet slots) and disk contents in our own versioned file in the save directory, keyed by block position or disk id, following the `power.dat`/`.bak` pattern.

## 3. Containers and native access (RSC-06; ACCESS-01/02)

- 3.3 containers are `TileEntityComposite` objects with features. There is no `TileEntityLootContainer` or `TileEntitySecureLootContainer` class. Items live in `TEFeatureStorage.itemGrid` (`ItemStackGrid`). Locking is handled by `TEFeatureLockable` (`locked`, owner, `allowedUserIds`, password hash), with optional `TEFeatureLockPickable` and a `isJammed` flag.
- **Native permission checks happen on the client** (`TEFeatureStorage.OnBlockActivated`). The server's `OnLockRequestServer` does not check permissions. The host must therefore apply the same rule itself:
  `accessible = !(lockable != null && lockable.IsLocked() && !lockable.IsUserAllowed(user)) && !isJammed && !(lockpick != null && lockpick.NeedsLockpicking())`.
- **An unlocked container owned by another player is natively open to everyone.** Decided October 19, 2026: RSC follows this exactly (ACCESS-01).
- Opening a container takes an exclusive `LockManager` lock on the **storage feature object** (not the composite). While the player has it open, their client uploads the whole tile entity state (`NetPackageTileEntity` to server). If the host changes a container that someone has open, those edits could be overwritten or items duplicated.
- **Rule:** a host transfer may touch a container only when `LockManager.Instance.IsLockedServer(storageFeature)` is false, checked in the same main-thread step as the change. Containers in use are skipped or rejected (A01).
- To change contents: edit the `ItemStackGrid` slots on the host, then call `SetModified()`. That marks the chunk dirty and broadcasts the tile entity to nearby clients.
- Vanilla `RemoveItems` and `HasItem` match by item class or type, not exact item state. RSC needs its own exact-match slot logic.
- In-game checks: host transfer with and without the container open; locked/password/jammed cases; client view refresh.

## 4. Exact item identity (A05; DISK-03)

- `ItemValue.Write(PooledBinaryWriter)` serializes the full state: type, quality, durability, modifications, cosmetic mods, typed `Metadata`, seed and texture. These bytes become `ItemKey`.
- Caveats:
  - Item type ids are numeric and specific to each world's mapping, which matters for cross-world disks (F04).
  - `Metadata` is a `Dictionary`, so two logically equal items could serialize differently if keys were added in a different order. That only splits a display entry; nothing is lost.
- Disk identity can live in `ItemValue` metadata (`SetMetadata(key, string)`), which travels with native item serialization through pickup, drop and save. Contents stay in the RSC save file (section 2).
- In-game check: metadata survives inventory moves, drop/pickup, containers and save/reload.

## 5. Save boundaries (SAVE-01/02)

- Container and workstation contents are saved with chunks: `World.Save()` → `ChunkCache.Save()`, plus chunk unload and `SaveRandomChunks`. Power is saved separately (`power.dat`), and `World.Save` finishes with `SaveDataUtils.SaveDataManager.CommitAsync()`.
- There is no shared transaction across these files, so any RSC file cannot be atomic with chunk saves.
- **Proposed guarantee:** state is consistent after a clean save or exit. After a crash, physical containers and RSC disk data each revert to their own last save, so items moved between physical and digital storage after the last save may be lost or duplicated. To shrink the window, save the RSC file right after `World.Save` and keep the bounded journal (SAVE-03) for diagnosis.
- Accepted October 19, 2026 (SAVE-02). The forced-crash test is deferred to P2.

## 6. Native workstations (RSC-11/12; CRAFT-01..08)

- `TileEntityWorkstation.queue` is a `RecipeQueueItem[]` (`Recipe`, `Multiplier`, `CraftingTimeLeft`, `OneItemCraftTime`, `Quality`, `StartingEntityId`). The **last** element is the one being crafted.
- **The crafting UI consumes ingredients before queueing; the tile entity never does.** RSC dispatch is therefore: take the validated inputs from network storage, then write one queue entry, then `setModified()`.
- Crafting pauses while a player has the station open (`bUserAccessing`) and, for fuelled stations, while it is not burning.
- Products go into a shared `output[]` array. When it is full, crafting waits.
- No per-item job tag exists. `CraftCompleteList` records `StartingEntityId` per completed item and is a possible attribution signal for CRAFT-08.
- Opening a station takes an exclusive `LockManager` lock, so the same in-use rule as containers applies.
- **Elapsed time** comes from world ticks: `GameTimer.Instance.ticks - lastTickTime`, where `lastTickTime` is saved. When an unloaded station's chunk reloads, it catches up on the gap, limited by fuel. World ticks do not advance while the host is closed.
- **Implication:** native crafting may progress for absent players without any residency, and RUN-03 (no shut-down catch-up) appears native. Residency would then matter mainly for timely output collection and fuel/power interplay. This may simplify RSC-13 and F10. Needs in-game confirmation.

## 7. Chunk residency (RSC-13; RUN-01..04)

- `ChunkManager.AddChunkObserver(position, bBuildVisualMeshAround, viewDim, entityIdToSendChunksTo)` registers an observer. The active chunk set is the union of all observers' `chunksAround`.
- `World.OnUpdateTick` ticks tile entities of active chunks every 20 ticks, alternating halves, so each chunk updates about every 2 s. An observer therefore both loads and ticks chunks.
- Pass `false` for the mesh flag and `-1` for the entity id so no client receives the chunks. `RemoveChunkObserver(observer)` releases it.
- Land claims: `PersistentPlayerList.m_lpBlockMap` / `GetLandProtectionBlockOwner(pos)`; size comes from `GameStats.GetInt(EnumGameStats.LandClaimSize)`.
- In-game checks: whether an observed chunk with no players runs workstation, fuel and electricity correctly, and the observer's memory/CPU cost.

## Prototype status

Host-only console commands under `rsc proto`, in `Source/Game/Prototypes/`.

| Prototype | Command(s) | Status |
|---|---|---|
| Wire topology dump from `PowerManager` | `rsc proto power x y z` | **Confirmed in game** (Oct 23): parent/root/children correct; `required` reflects block XML watts |
| RSC network status from the wire tree | Dev blocks `rscProtoControllerSimple`, `rscProtoControllerAdvanced`, `rscProtoHub`, `rscProtoInterface` (creative menu); `rsc proto net x y z` | **Confirmed in game** (Oct 23): simple network of 3 (no power needed) and powered advanced network both report correct controller, tier and members |
| Wire rules: joining two controllers rejected (NET-04); only a controller on vanilla power, storage blocks never feed vanilla devices (NET-06) | Host: vanilla "can't have parent" tooltip. Remote clients: server rejection with a specific tooltip | Moved to P1 (October 23); built and unit-tested, awaiting in-game run |
| Blue wires on advanced networks | Visual: wires from RSC node to RSC node under an advanced controller | **Failed in game** (Oct 23): wires stayed vanilla black/yellow. Fixed (colour now applied to the material, pooled nodes reset); awaiting re-test |
| Container inspection and access rule | `rsc proto container x y z` | **Partly confirmed** (Oct 23): unlocked owned crate reports grid, owner, `access=True`, slots and keys. Locked/jammed cases still to run |
| Host container transfer with access and lock checks | `rsc proto move sx sy sz slot dx dy dz` | **Partly confirmed** (Oct 23): plain stack (21 scrap iron) moved crate to crate with `identical=True`. Still to run: modded/damaged item, in-use rejection, locked container |
| Disk identity via item metadata | `rsc proto disk tag` / `rsc proto disk show` (`tag` is limited to the host's own player) | **Partly confirmed** (Oct 23): tag written and read back on a held item. Survival through containers, drop/pickup and save/reload not logged |
| Station inspection | `rsc proto station x y z` | **Confirmed in game** (Oct 23): workbench report incl. tick timing |
| Station queue probe (does **not** consume ingredients) | `rsc proto queue x y z recipe count` | **Partly confirmed** (Oct 23): workbench accepted `armorLumberjackGloves x1` with native quality and starter id. Completion/output, campfire and forge fuel gating not logged |
| Chunk observer residency | `rsc proto observe x y z viewDim` / `unobserve id` / `observers` | Built; awaiting in-game run |
| Crosshair helper | `rsc proto look` (block under the host's crosshair; chains `net`+`power` for RSC nodes, `station` for workstations) | **Confirmed in game** (Oct 23) |

All prototypes need in-game runs before any P0 box is checked.

## In-game results (October 23, 2026, solo creative)

Evidence: `[RSC proto]` lines in the game's `output_log_client__2026-10-23__15-56-41.txt`.

- **Wiring is confirmed as the network layer.** Controller -> hub -> interface chains built with the vanilla wire tool are read back correctly from `PowerManager`. A simple network works unpowered.
- **Per-device power draw works (PWR-01).** The advanced controller (5 W) and interface (1 W) report `required=5`/`required=1`, and the generator showed the matching load. The earlier "0 W" reading was the unpowered simple network, where every device is 0 W or unpowered.
- **Blue wires failed.** The `DrawWires` postfix set `FastWireNode.wireColor` after vanilla had already called `BuildMesh`/`TogglePulse`, so the material never changed; the yellow is the pulse colour, which was never set. Fixed to call `SetWireColor`/`SetPulseColor`/`TogglePulse` directly. Wire nodes are pooled and `Reset()` does not restore `wireColor`, so the patch also returns recycled blue nodes to vanilla black. Needs re-test.
- **Host container transfer works for a plain stack** with exact identity preserved.
- Disk metadata tag is written and read back on a held item (session `16-26-21`).
- A workbench accepts a queue entry written by the host with native quality and the requester's entity id.

## P0 closure (October 23, 2026)

P0 was closed by the project owner's decision: the core approach (vanilla wire tree as the network, per-device power, host container edits, item-metadata disk identity, host-written station queues) works in game. The checks below were not run and are **deferred to the stage that depends on them**, not dropped:

| Deferred check | Risk if wrong | Must pass before |
|---|---|---|
| Blue wire re-test after the colour fix | Cosmetic (NET-02) | P1 exit |
| Second-controller wire rejected; subtree move between networks | Two controllers on one network (NET-04, RSC-05) | P1 topology work (also needs F02) |
| Wire limits (9 children, 15 m) with 0 W hubs | Network size/layout limits | P1 exit |
| Topology identical after save/reload | Networks lost on reload | P1 exit |
| Transfer of a damaged/modded item keeps exact state | Item state loss (A05) | P1 transfers |
| Transfer rejected while a player has the container open; locked/password/jammed containers | **Item duplication/loss** (A01, ACCESS-01) | P1 transfers (highest priority) |
| Disk tag survives container, drop/pickup and save/reload | Disk contents orphaned (DISK-03) | P2 disks |
| Crash/forced-quit recovery guarantee | Loss/dup across physical/digital (SAVE-01/02) | P2 disks |
| Queued job completes into output; campfire/forge pause without fuel | Crafting dispatch (CRAFT-01..08) | P3 |
| Absent-player catch-up and chunk observer residency | Unattended jobs (RSC-13, RUN-01..04, F10) | P3 |
