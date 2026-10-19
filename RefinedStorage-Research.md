# Refined Storage for 7 Days to Die: research and integration draft

**Historical research draft.** The current requirements and release boundaries are in [RefinedStorage-Design.md](RefinedStorage-Design.md), updated October 19 after the user's questionnaire answers. That design supersedes recommendations and unresolved decisions below, particularly the original automation-first emphasis and milestone scope.

Planning date: October 18, 2026, America/Chicago.

Status: research and proposed design. Confirmed requirements are separated from recommendations. No gameplay code or loadable mod has been installed by this planning work.

## Confirmed intent

1. The references are Simple Storage Network by Lothrazar and Refined Storage for Minecraft.
2. The main objective is Refined Storage-style progression and automation.
3. The intended audience is solo play and private co-op.
4. Progression starts with connected physical crates and later adds powered digital storage.
5. Digital disks and connected physical containers coexist in advanced networks.
6. Automation uses actual crafting stations and preserves their requirements, fuel use, and crafting time.
7. Devices connect through physical data cables and adapters. Wireless access is a later upgrade.
8. Players encode recipes they know onto pattern items.
9. Automation should continue when players leave the area, while the game or server remains running.

The remaining questions are in [RefinedStorage-Questions.md](RefinedStorage-Questions.md). Recommendations below are not additional approved requirements.

## What the Minecraft references provide

### Simple Storage Network

The author describes a shared network over existing inventories, with remote access. Its current 1.21.1 source guide describes a network root, link cables, inventory terminals, and request tables with crafting access. Physical inventories supply capacity. Filtered links, import/export connections, priorities, and stock controls organize transfers. Processing cables describe sending configured inputs to a machine and collecting its outputs. Several remotes offer inventory, crafting, collection, or building functions. The guide explicitly discusses loaded roots and disconnected unloaded endpoints.

Use this as inspiration for the initial physical-storage tier. Its processing features also inform station adapters; it is more than a chest-search interface.

Sources:

- [Author's project description](https://www.curseforge.com/minecraft/mc-mods/simple-storage-network)
- [Author's repository](https://github.com/Lothrazar/Storage-Network)
- [Source guide and English descriptions, trunk/1.21.1](https://raw.githubusercontent.com/Lothrazar/Storage-Network/trunk/1.21.1/src/main/resources/assets/storagenetwork/lang/en_us.json)

Version qualification: the source branch can contain work beyond a published release. Exact mechanics and feature names differ between Minecraft versions. We should choose a reference era before treating any particular recipe, range, or upgrade as canonical.

### Refined Storage

Refined Storage combines a common Grid interface with item/fluid disks, storage blocks, and external inventories. Its powered network supports filters, storage priorities, controlled input/output, and wireless access. Current documentation also describes an optional configuration without energy requirements; power is a design choice rather than an unavoidable property of every Minecraft installation.

Patterns define crafting and machine-processing recipes. Autocrafting resolves intermediate recipes, previews requirements, and manages active jobs. Processing recipes dispatch inputs to external machines and wait for outputs. Stock-maintenance setups can trigger production automatically. Wireless handheld access and transmitter/receiver links solve different problems: remote player access versus extending the device network. Security devices provide player permissions. Constructors and destructors automate world block placement and breaking.

Sources:

- [Overview](https://refinedmods.com/refined-storage/index.html)
- [Getting started and power configuration](https://refinedmods.com/refined-storage/getting-started.html)
- [Grid interaction, search, and sorting](https://refinedmods.com/refined-storage/viewing-resources/grid.html)
- [External storage, filters, and priorities](https://refinedmods.com/refined-storage/storage/external-storage.html)
- [Transfer devices and world interaction](https://refinedmods.com/refined-storage/guides/getting-resources-in-and-out.html)
- [Autocrafting dependencies and task management](https://refinedmods.com/refined-storage/guides/getting-started-with-autocrafting.html)
- [Crafting and processing patterns](https://refinedmods.com/refined-storage/autocrafting/pattern.html)
- [Stock maintenance](https://refinedmods.com/refined-storage/guides/keeping-resources-in-stock.html)
- [Wireless Grid](https://refinedmods.com/refined-storage/wireless-networking/wireless-grid.html)
- [Distant network links and chunk-loading requirement](https://refinedmods.com/refined-storage/guides/networks-over-long-distances.html)
- [Player permissions](https://refinedmods.com/refined-storage/security/security-manager.html)

Version qualification: these are the site's current documentation pages, whose selector includes a latest/snapshot view. This draft borrows concepts, not an exact specification for a particular released Minecraft version or addon pack.

## Proposed translation into 7 Days

| Reference concept | Proposed 7 Days implementation | Status |
|---|---|---|
| Network root/controller | Early logistics hub; advanced powered controller | Tiered progression confirmed; names and recipes open |
| Link cable/external storage | Adapter explicitly attaches a player container to data cables | Physical connection confirmed; eligible containers open |
| Inventory Grid | Searchable terminal displaying network quantities and exact item variants | Recommended first playable feature |
| Crafting Grid | Recipe browser using network ingredients and valid crafting destinations | Station rules confirmed; interface details open |
| Disks and drive | Crafted storage media in a powered cabinet, coexisting with crates | Confirmed concept; capacity and portability open |
| Importer/exporter | Filtered station/container adapter with configurable transfer direction | Recommended |
| Patterns/autocrafter | Pattern encoder plus scheduler routing requests to real stations | Confirmed direction |
| Processing patterns | Station-specific adapters, including separate forge smelting support | Recommended implementation approach |
| Keep-in-stock | Optional production targets such as ammunition, concrete, or medical supplies | Scope and limits open |
| Wireless Grid | Bound handheld terminal unlocked after wired logistics | Later wireless confirmed; range and costs open |
| Transmitter/receiver | Optional connections between distant bases | Open |
| Fluid network | Initially treat bottled liquids and gasoline as ordinary items | Recommendation; bulk fluid storage open |
| Constructor/destructor | Optional world building, harvesting, or salvage equipment | Open; separate scope from core storage |
| Security Manager | Owner and authorized co-op users, with optional distinct permissions | Recommended; policy open |

Do not assume Minecraft remotes imply access to unloaded storage in 7 Days. The desired unattended processing exceeds the loaded-chunk behavior described in the reference guides and needs a deliberate 7 Days implementation.

## Evidence from the local installation

The latest local client log inspected, dated October 16, reports **V 3.3.0 (b18)**. This is a provisional development target pending your confirmation, not a claim about the newest public game version.

The existing Project-Coruscant source demonstrates a local C# mod using IModApi, Harmony, .NET Framework 4.8, and references to the installed game assemblies. Its README instructs users to disable EAC. This is a useful local development precedent; it does not prove the new storage hooks yet.

Installed game XML provides concrete integration targets:

- `Data/Config/XUi_InGame/windows.xml` defines crafting lists, crafting queues, and container UI.
- `Data/Config/blocks.xml` defines workbench crafting areas and forge tool, fuel, output, and material-input modules.
- `Data/Config/recipes.xml` includes crafting areas, times, learnable recipes, material-based forge recipes, and quality-dependent ingredient effects.
- Electrical blocks and `RequiredPower` properties provide a starting point for power integration.

Inference: the requested network, shared terminal, persistent disks, synchronized transfers, and scheduler call for custom C# behavior plus XML/XUi definitions. Plan for a client-and-host/server installation with EAC disabled unless an implementation spike establishes a narrower requirement. Exact runtime hooks, supported server operating systems, and packaging remain unverified.

## Proposed architecture

### 1. Network topology and ownership

Represent hubs, cables, storage adapters, terminals, and station adapters as a bounded graph. Give each network a stable identifier independent of controller position. Rebuild affected portions after placement, destruction, loading, or upgrade. Define explicit behavior for cable breaks, network splits, merged networks, and controller replacement.

Authorize every operation on the host/server. Linking a locked or foreign container must not silently grant access. Player identity, allowed operations, and source ownership must be checked when transferring or scheduling, not only when opening a screen.

### 2. Storage providers and terminal

Use separate providers for physical containers and disk storage behind one interface. The terminal displays an index; it does not own another copy of the items. An external crate stays a real crate.

Preserve item data, including quality, durability, installed modifications, ammunition state, custom properties, and nested contents where supported. Aggregate visually only when variants are equivalent or when the user can expand the result and select a specific variant.

Recommend default insertion behavior that fills matching stacks and follows configured priorities. Leave rejected or excess items at their source. Any destructive overflow mode must be a deliberate player setting if included.

### 3. Transfers and persistence

Serialize competing changes on the host/server and revalidate source, destination, capacity, ownership, and item identity at commit. Keep transfer and crafting records sufficient to reconcile interrupted operations. An accepted request needs a stable identifier so retries do not repeat it.

For disks, choose whether portable media carries the contents or references a world-owned store. Either approach needs stable identity, save-version migration, and explicit rules for pickup, destruction, duplication attempts, and moving between worlds.

Crash recovery across player inventories, native containers, and custom storage is a design task, not something guaranteed by an in-memory lock. The first prototype must establish available save boundaries and the supported recovery guarantee.

### 4. Pattern and crafting service

Encoding validates that the player knows the recipe. Preserve enough context to distinguish recipe, output quality, and crafting destination. Decide separately whose skill bonuses, ingredient reductions, and crafting experience apply when a pattern is executed later.

Build a dependency planner using the game's effective recipe rules. It must account for batch output counts, existing intermediates, shared ingredients, missing patterns, circular paths, and alternative recipes. Present required inputs and missing prerequisites before confirmation.

Separate planning from execution. Use a persistent job state machine: planned, reserved, queued, processing, collecting, completed, blocked, or cancelled. Reserve stock to prevent two players or stock rules spending the same resources. Cancellation must distinguish unused inputs, material already consumed by a station, and completed outputs.

### 5. Real station adapters

Start with one workbench recipe. Add other station types through explicit adapters. A vanilla workbench's output-module layout is not evidence of a generic input inventory: route validated jobs through the native queue mechanism rather than inventing input slots.

Forges need special handling because item smelting and recipes using internal material units are separate operations. Track material conversion, fuel, required tools, output limits, and whether intermediate smelted material is accessible only at that forge.

Keep native station timers and resource costs as the authority wherever possible. Define how manual queue changes, picked-up stations, power failures, and multiple simultaneous stations interact with network jobs.

### 6. Processing when the base is unattended

Confirmed requirement: continue while the host/server is running even after players leave the base. Closed-game or shut-down-server catch-up is not yet requested.

First measure native behavior with an instrumented test: station queueing, chunk unload, elapsed-time advancement, fuel consumption, save/load, and reload reconciliation. Then select a supported execution strategy:

1. Prefer native unattended station progress if it is sufficiently exposed and consistent.
2. Otherwise evaluate a bounded persisted simulation for registered network stations, with exclusive authority and reconciliation when native stations reload.
3. Evaluate keeping selected areas active only if necessary and acceptable after measuring memory, CPU, and world-simulation effects.

Never let both a detached scheduler and native reload catch-up produce the same output. Remote container access needs its own plan: continuing a committed crafting job is different from modifying arbitrary unloaded containers. Unattended stock maintenance also needs authoritative quantities and safe capacity reservations.

If no acceptable approach preserves the requested station semantics, report the concrete limitation and resolve that requirement before building later features around an invalid assumption.

### 7. Power, interface, and extensibility

Use the existing electricity system for advanced equipment, subject to prototype validation. Decide which devices consume power, how consumption scales, and how jobs behave during outages. Recommend keeping data connectivity separate from power connectivity, consistent with the confirmed cable choice.

Expose search, sorting, deposit/withdraw, disk capacity, device health, job status, and actionable blocked-job messages. Use original 7 Days-oriented names and art. Add extension points for additional containers, stations, recipes, and configuration after native behavior is stable.

## Proposed implementation milestones

| Milestone | Concrete result | Exit criteria |
|---|---|---|
| 0. Feasibility | Confirm target build; inspect runtime APIs; prototype one transfer, one native station job, and unattended progression | Ownership, item identity, save boundaries, and unload behavior understood; major constraints documented |
| 1. Wired physical storage | Hub, cable, crate adapter, searchable terminal | Two players can safely transfer from shared crates; cable breaks and save/reload behave predictably |
| 2. Powered disks | Controller, cabinet, disk tiers, capacity display | Mixed crate/disk storage preserves exact items and survives restart; pickup/destruction rules tested |
| 3. Station logistics | Filtered transfers and native workbench jobs; additional station adapters incrementally | Correct costs, time, tools, fuel, manual-queue interaction, and unattended behavior |
| 4. Pattern automation | Encoder, dependency preview, reservations, queue and cancellation | Multi-step job completes once; missing inputs, cycles, interruptions, and co-op conflicts recover safely |
| 5. Advanced logistics | Stock rules, wireless access, upgrades, optional distant links | Range, power, permissions, unattended behavior, and performance meet agreed targets |
| 6. Release preparation | Assets, progression balance, configuration, migration, packaging | Repeatable install/upgrade instructions and agreed acceptance scenarios pass |

Milestone 0 must investigate unattended automation immediately, even though players encounter it later in progression. Digital storage and station adapter development can then be reordered if prototype results justify it.

## Acceptance scenarios to agree before implementation

- Two players withdraw the final stack simultaneously; accepted totals never exceed available stock.
- A transfer preserves a damaged, modified weapon as the same weapon.
- A player deposits into a full network; unaccepted items remain recoverable at the source.
- A cable breaks or permission changes after opening a terminal; stale UI cannot authorize a transfer.
- A controller is destroyed; physical crate contents remain governed by crate behavior, and disk consequences follow the agreed policy.
- A requested recipe chain handles batch sizes and reports a missing pattern or circular dependency.
- A forge recipe uses native material units and consumes fuel/tools correctly.
- Manual station activity does not steal reserved job inputs or count unrelated outputs as job completion.
- Players leave and return to an operating base; jobs progress once with correct fuel and output capacity.
- The host saves/restarts during a transfer or job; recovery behavior matches the documented guarantee.
- An update or mod removal follows the agreed migration/export policy.
- A representative large co-op base meets a measured server-time and network-traffic budget.

No playtest, runtime hook validation, or storage implementation has been performed during this research pass.
