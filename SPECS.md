# Refined Storage Control specification

Baseline: October 19, 2026. Derived from [the current design and user decisions](RefinedStorage-Design.md). Implementation has not started.

This document organizes confirmed behavior into requirements with stable IDs. The design remains the decision record and detailed plan. Recommendations and unresolved choices are explicitly identified rather than treated as approved defaults. [TODO.md](TODO.md) tracks implementation at milestone level.

## 1. Purpose and release scope

The primary purpose is centralized access to physical and digital storage in 7 Days to Die. Pattern autocrafting is secondary and follows the first release.

| Release | Included |
|---|---|
| v1 | Wired storage networks; simple and powered controller tiers; physical-container interfaces; digital disks; terminal inventory interaction; manual terminal crafting through native workstations |
| v2 | Creator-bound patterns; standalone and machine-connected crafters; tiered crafter progression; full crafting cancellation; dependency previews; job-monitoring UI; source-container lookup |
| Later scope decisions | Vehicle adapters, wireless, complex networks, stock targets, auto-fueling, automatic forge feeding, farming/harvesting/salvage, world construction, turret supply, bulk fluid systems |

The last row is outside v1. Deferral is not a commitment to include every feature in v2. v1 does not automatically manufacture missing intermediate ingredients.

## 2. Platform and integration requirements

| ID | Requirement |
|---|---|
| ENV-01 | Target 7 Days to Die **3.3.0 b18**, Windows PC, solo and Windows player-hosted co-op. |
| ENV-02 | Support up to **six players**. The normal performance benchmark uses three players; it does not lower the supported player count. |
| ENV-03 | Require installation on all participating clients/host and allow EAC to be disabled. Dedicated servers and other operating systems are not first-release requirements. |
| ENV-04 | New saves are acceptable; existing-save support is not required. Development test worlds may be reset. |
| ENV-05 | Prepare for eventual Nexus Mods distribution. Packaging does not itself authorize publishing. |
| MOD-01 | Separate topology, storage providers, permissions, routing/transfers, persistence, power, recipes/stations, crafting, claim activity, content, and UI responsibilities. |
| MOD-02 | Provide extension points for additional containers, recipes/items, and stations without rewriting unrelated core or UI behavior. No specific external-mod compatibility is promised initially. |
| MOD-03 | Keep recipes, progression gates, and balance values data-driven/configurable where appropriate. Use existing game components and ingredients for initial recipes. |

Runtime API inspection has identified integration candidates, not proven their behavior. Native wire behavior, container permissions, workstation queues, portable-media saves, and unattended processing require feasibility prototypes.

## 3. Controllers, wiring, and progression

| ID | Requirement |
|---|---|
| NET-01 | A usable v1 network has exactly one controller. Containers, terminals, hubs, and interfaces join through connected wires. |
| NET-02 | Wire placement and connection interaction resemble vanilla electrical wiring. Wires in a Refined Storage Controller network appear blue. |
| NET-03 | Supply a custom **network-hub** with relay-like connection behavior for extending the network. |
| NET-04 | A wire that would put two controllers on one network is rejected with a tooltip, on the host and for remote clients. Re-wiring a branch to another network is allowed when the result has at most one controller. If a two-controller network still arises (old save, multiplayer race), both controllers enter a **Conflict** state that blocks storage access until a wire is removed; no block or item is destroyed or dropped. Placement never creates wires, so no placement indicator is needed (F02, decided October 23, 2026). |
| NET-06 | Only a controller may be wired from a vanilla power device (generator, battery, relay). Any other RSC block may only be wired from an RSC block, and RSC blocks may only have RSC children. Disallowed wires are rejected like NET-04 (decided October 23, 2026). |
| NET-05 | Detect topology changes, including cable breaks and conflicting connections. Recheck network validity before accepting a transfer; stale UI cannot authorize disconnected access. |
| PROG-01 | Advanced Engineering **rank 2** unlocks Simple Storage Control; **rank 5** unlocks Refined Storage Control. |
| PROG-02 | Gate stage components through **Wiring 101** progression: simple-tier parts at **25**, advanced controller and parts at **55**, disks 1k/10k/100k/250k/500k/1m at **55/60/70/80/90/100** (F01, decided October 23, 2026; values configurable). |
| PROG-03 | Simple Storage Control operates without electricity and exposes connected physical storage. |
| PROG-04 | Simple-controller transfers share a configurable limit of **two completed stack transfers per second per controller** across all users. One transfer is one move out of one source slot, up to a full stack, whatever the count. Deposit-matching counts each stack moved. Players are served round-robin; crafting and internal moves (P3) use the same budget, after player requests (F12, decided October 23, 2026). |
| PROG-06 | Advanced-controller transfers have no gameplay limit, only a configurable per-network technical cap (initially **20 transfers per second**), finalized by the P4 benchmark (F12). |
| PROG-05 | Digital drive providers require the advanced tier and valid power. Connecting them through simple-tier equipment cannot bypass this requirement. |

Network conflicts (F02) and transfer accounting/fairness (F12) were decided October 23, 2026. Measured performance budgets remain part of the P4 benchmark.

## 4. Terminal and physical storage

| ID | Requirement |
|---|---|
| UI-01 | Provide a centralized, searchable terminal for reading quantities, depositing, and withdrawing from eligible connected storage. |
| UI-02 | Include favorites, categories, sorting, and deposit matching. Retain a vanilla 7 Days visual resemblance. |
| UI-03 | Display equipment variants individually. Preserve quality, durability, installed modifications, and other item state when selecting/transferring items. |
| UI-04 | Do not introduce automatic item-type exclusions from v1 storage or bulk deposit. Native item rules, permissions, capacity, and configured filters still apply. |
| PHY-01 | A physical-container interface reads and changes the original container inventory. It does not copy inventory into a controller-owned store. |
| PHY-02 | Cover eligible vanilla container blocks in v1; exclude vehicle and drone inventory adapters. Workstation adapters are specified separately below. |
| PHY-03 | Preserve native/container-provider rules for movement, pickup, and destruction of external containers. |
| PHY-04 | Disconnecting or destroying a terminal/interface/controller does not itself erase the independent contents of an external container. The container's own destruction behavior remains authoritative. |

| ID | Input |
|---|---|
| UI-05 | v1 targets keyboard and mouse. Gamepad support is not promised; revisit after v1 (F11, decided October 23, 2026). |

## 5. Digital storage and destruction

| Tier | Capacity in individual items |
|---|---:|
| 1 | 1,000 |
| 2 | 10,000 |
| 3 | 100,000 |
| 4 | 250,000 |
| 5 | 500,000 |
| 6 | 1,000,000 |

| ID | Requirement |
|---|---|
| DISK-01 | Provide the six capacities above, counted as individual items rather than native stack slots or distinct item types. |
| DISK-02 | Disks retain data when removed and transported. Their contents are usable only while installed in a drive cabinet on a usable advanced network. |
| DISK-03 | Preserve exact stored item state across insertion, extraction, media movement, and supported save/reload operations. |
| DISK-04 | Cabinet destruction rolls survival separately for each installed disk, default **50%** each (configurable). Surviving disks drop intact with contents; destroyed disks and their contents are permanently lost. |
| DISK-05 | Terminal destruction loses no network contents because the terminal owns none. Interface destruction loses its filter settings with a default **50%** chance (configurable) while leaving the underlying container to its native rules. |
| DISK-06 | Apply destruction/drop cleanup once; surviving media must retain their data, and destroyed media must not reappear through a stale reference or repeated event. |

| DISK-07 | A drive cabinet holds **6** disks (F01, decided October 23, 2026). |
| DISK-08 | Disks are bound to the world they were created in. A disk carried into another world shows "data unavailable" and cannot be used there (F04, decided October 23, 2026). |
| DISK-09 | A drive cabinet can be picked up only when it holds no disks; otherwise pickup is refused with "remove the disks first" (F04, decided October 23, 2026). |

## 6. Access and routing

| ID | Requirement |
|---|---|
| ACCESS-01 | Physical storage follows the requesting player's current native container access. Being connected to the same network does not grant additional container permissions. Native access is followed exactly: an unlocked container is reachable by any network user, as it is in person (decided October 19, 2026). The host re-applies the native rule because the game checks it only on the client. |
| ACCESS-02 | Filter displayed inventories per viewer and validate permissions on the host for each mutation. Permission changes invalidate later unauthorized operations. |
| ACCESS-03 | Digital storage uses an **owner plus an explicit per-player allowlist**, with separate **deposit, withdraw, craft, and configure** permissions. Inserting a disk does not make it public. |
| ACCESS-04 | A disk's owner is the player who first inserts it blank into a cabinet; inserting or removing a disk never changes its owner. The owner can transfer ownership to a player already on the disk's allowlist; the host can reassign any disk by console command (covers players who left). The allowlist keeps working while the owner is offline. New allowlist entries get **deposit + withdraw**; craft and configure (which includes editing the allowlist) are granted separately (F04, decided October 23, 2026). Tracked as **RSC-01**. |
| ROUTE-01 | Apply provider eligibility and configured filters before destination selection. Exclude inaccessible/disconnected providers and advanced providers lacking power. |
| ROUTE-02 | Give each storage interface/provider a configurable integer priority from **0 to 100**, **default 50**, with lower numbers preferred for deposits. Implement priority fields/configuration under **RSC-02**. |
| ROUTE-03 | Deposits: lowest priority number first; within equal priority, prefer a provider already holding the exact item; remaining ties use a fixed node-position order. Withdrawals: highest priority number (least preferred) first, with the same fixed tie order. |
| ROUTE-05 | Each interface may have an allow list or a block list. Filters control **deposits only**; withdrawals see every accessible provider. |
| ROUTE-04 | Full storage, rejected filters, or invalid destinations leave unaccepted items recoverable at the source. v1 performs no automatic disposal. |

Routing defaults, filter direction, extraction order, and the tie-breaker were decided October 23, 2026 (F03).

## 7. Power behavior

| ID | Requirement |
|---|---|
| PWR-01 | Advanced equipment requires electricity, with consumption scaling by attached interfaces, crafters, and other devices. Defaults (configurable): advanced controller **10 W**, interface **2 W**, terminal **3 W**, drive cabinet **5 W**, hub **0 W** (F01, decided October 23, 2026). |
| PWR-02 | Advanced terminal/storage interaction is inaccessible without power. Automatic network output collection also waits for network power. |
| PWR-03 | An already dispatched native workstation job continues under normal native processing/fuel rules during an RSC outage. Do not cancel, refund, restart, or duplicate it merely because controller power failed. |
| PWR-04 | RSC equipment adds no heat or noise. Do not suppress the native effects of connected generators or workstations. |

Pending requests still in their five-second pre-dispatch window need an agreed outage policy under F07.

## 8. v1 manual crafting

| ID | Requirement |
|---|---|
| CRAFT-01 | Support manual terminal requests through native **workbench, forge, and campfire** adapters. A connected workbench is required for backpack/general crafting from the terminal. |
| CRAFT-02 | Preserve native recipe unlocks, required stations/tools, effective ingredient costs, quality rules, and processing times. Immediate inputs must already exist in accessible storage or the selected forge's internal material reserves. |
| CRAFT-03 | Fuel remains the user's responsibility. v1 does not automatically refuel stations, smelt raw items into forge reserves, or produce missing intermediate recipes. |
| CRAFT-04 | A request has a **five-second cancellation countdown before ingredients are dispatched**. Cancelling during that window releases reservations without consuming ingredients. Full post-dispatch cancellation is deferred to v2. |
| CRAFT-05 | Use **one compatible station per request**. Separate requests may run on separate stations. Do not automatically split one request across multiple stations. |
| CRAFT-06 | Revalidate access, input availability, native queue capacity, station state, and applicable power at dispatch. Competing requests cannot spend the same inputs. |
| CRAFT-07 | Return finished request products to currently accessible eligible network storage. Leave unaccepted products safely in native station output if capacity/access/power prevents collection. |
| CRAFT-08 | Attribute outputs to the dispatched request; do not collect unrelated manual crafting outputs as if they belonged to it. Retry blocked collection safely when the station/network is available. |
| CRAFT-09 | Persist sufficient internal job state to handle accepted work and claim activity without duplicated inputs or outputs. Deferring the job-monitoring UI does not defer internal job tracking. |

### Required request lifecycle

1. Validate the requested recipe, immediate ingredients, authority, and compatible native station.
2. Reserve ingredients and queue capacity without removing inputs during the countdown.
3. Display the five-second window and permit cancellation.
4. Revalidate and dispatch once to one native station, or reject safely.
5. Track native processing under native timing, tools, reserves, and user-maintained fuel.
6. Collect only attributable outputs into eligible storage; leave blocked outputs in the station.
7. Persist/reconcile request state and release claim activity when native work completes.

Requester stats/XP, station-selection order, coexistence with existing manual queues, and countdown disconnect/failure behavior remain F05-F07. Reservations must also account for direct native-container interaction; feasibility work must establish how to prevent or safely reject races at dispatch.

## 9. Unattended processing and persistence

| ID | Requirement |
|---|---|
| RUN-01 | Accepted crafting jobs continue when players leave the base while the host game remains running. v1 does not start stock-maintenance or pattern-generated work. |
| RUN-02 | Start with job-scoped activity for the controller's claimed area. Retain activity only while accepted native crafting work remains; waiting indefinitely for output capacity/power must not retain the claim forever. |
| RUN-03 | No wall-clock production catch-up after the host closes the game. Reopening restores saved state rather than manufacturing shut-down-time output. |
| RUN-04 | Track overlapping job/claim activity correctly and release activity resources on completion, invalidation, or world shutdown. Restore only valid unfinished work after loading. |
| SAVE-01 | Host-side inventory mutations and request retries must preserve item totals and exact item identity during normal operation and supported recovery paths. |
| SAVE-02 | Establish save boundaries, versioned media/job records, and interruption-recovery guarantees in P0. Do not promise cross-file crash atomicity without testing available game hooks. Accepted guarantee (October 19, 2026): state is consistent after a clean save or exit. After a crash, physical containers and RSC data each revert to their own last save, so physical/digital moves made after that save may be lost or duplicated. Mitigation: save RSC data immediately after the world save, and keep the SAVE-03 journal. |
| SAVE-03 | Provide structured diagnostics, a bounded transfer/job journal, and owner/admin media export before public release. Recovery tools must not blindly duplicate already committed transfers. |

Claim residency is a chosen prototype strategy, not a validated API result. It stays the chosen strategy even if native workstation catch-up on chunk reload is confirmed (re-confirmed October 19, 2026), so outputs are collected promptly. See [P0-FINDINGS.md](P0-FINDINGS.md). `ChunkManager.AddChunkObserver`/`RemoveChunkObserver` are candidates. Test actual station, fuel, electrical, and world-update behavior rather than equating chunk residency with correct simulation. Claim boundaries, outside stations, claim removal, and controller removal remain F10.

## 10. v2 requirements and design boundary

| ID | Requirement |
|---|---|
| AUTO-01 | Players encode recipes they know into patterns. Authorized teammates may use those patterns. |
| AUTO-02 | Patterns retain creator identity and use that creator's latest known crafting statistics, including while offline. Creator progression improvements must be usable without requiring an immutable full-stat copy on every pattern. |
| AUTO-03 | Support standalone and station-connected crafters with tiered progression. Their relationship to the connected-workbench requirement remains F09. |
| AUTO-04 | Do not introduce an item-type exclusion list for automation; recipe, access, resource, and station rules still apply. |
| AUTO-05 | Add full cancellation, dependency previews, job-monitoring UI, and source-container lookup in the v2 scope. |

Proposed, not approved: shared versioned creator-stat records, freezing effective costs at job acceptance, and crafter tiers increasing pattern slots/dispatch concurrency. Exact tier values, multiblocks, temporary buffs, missing creator profiles, and XP attribution remain F08-F09. Do not implement these as implied v1 requirements.

## 11. Acceptance scenarios

| ID | Scenario and expected outcome | Requirements |
|---|---|---|
| A01 | Multiple users contend for the final stack: accepted withdrawals never exceed available stock. Repeat with six clients. | ENV-02, ACCESS-02, SAVE-01 |
| A02 | A user lacks or loses container/disk permission: inaccessible stock is not exposed or transferred. Test digital permissions independently. | ACCESS-01 through ACCESS-04 |
| A03 | Deposit into a full, filtered, disconnected, or unpowered destination: unaccepted items remain at source. A filtered-out item is still visible and withdrawable from the provider that holds it. | ROUTE-01 through ROUTE-05, PWR-02 |
| A04 | Concurrent users and deposit-matching share the simple controller's configured two-transfer/second limit: a partial stack costs one transfer, one player's bulk deposit does not starve another (round-robin), and the advanced controller is limited only by its technical cap. | PROG-04, PROG-06 |
| A05 | Withdraw and reload a damaged, modified item: exact state is preserved and variants stay distinguishable. | UI-03, DISK-03 |
| A06 | Fill each disk to its capacity and reject the next item safely. Mixed-provider routing deposits to the lowest priority number first (matching item preferred within equal priority, then fixed tie order) and withdraws from the highest number first. | DISK-01, ROUTE-02 through ROUTE-05 |
| A07 | Move a disk through supported pickup/save/reload paths: its data survives and is usable only in a valid drive/network. | DISK-02, DISK-03 |
| A08 | Destroy a cabinet: evaluate each disk once, drop survivors intact, and permanently remove destroyed media contents. | DISK-04, DISK-06 |
| A09 | Cancel before the countdown ends or invalidate dispatch: no consumed ingredients or stranded reservations. | CRAFT-04, CRAFT-06 |
| A10 | Request native workbench, forge, and campfire recipes: preserve costs, tools, reserves, time, and manual fueling. | CRAFT-01 through CRAFT-03 |
| A11 | Submit two compatible requests to two stations: each request stays on one station and work may proceed concurrently. | CRAFT-05 |
| A12 | Outputs finish with full storage or failed RSC power: they stay safely in station output; later collection occurs once with current access. | CRAFT-07, CRAFT-08, PWR-02, PWR-03 |
| A13 | Leave the claimed base while work runs: accepted jobs finish correctly; claim activity releases after native work completes. | RUN-01, RUN-02, RUN-04 |
| A14 | Save/close/reopen during supported job/media operations: no duplicated state, no leaked claim activity, and no shut-down-time production. | RUN-03, RUN-04, SAVE-01, SAVE-02 |
| A15 | Wire two controller networks together: the wire is rejected for host and remote client. Move a branch between networks: access follows the new network on the next transfer. Wire a vanilla device into a non-controller RSC block: rejected. Force a two-controller network: both enter Conflict and block access until a wire is removed, with nothing destroyed. Remove a controller: no stale access and no external-storage loss. | NET-01, NET-04 through NET-06, PHY-04 |

Primary performance scenario: **24 storage interfaces, two terminals, two drive cabinets, three players, and one active claimed base**. This is a benchmark target, not a hard limit. Measure stored item variants separately from total item quantity. CPU, memory, response-time, and traffic budgets must be measured and agreed; no numerical performance guarantee exists yet.

## 12. Open decisions

Original F identifiers are preserved from the design. F12 makes existing unresolved transfer-accounting details explicit; it adds no confirmed feature.

| ID | Decision needed | Needed by |
|---|---|---|
| ~~F01~~ | Decided decided October 23, 2026: see PROG-02, PWR-01, DISK-04/05/07 | - |
| ~~F02~~ | Decided October 23, 2026: see NET-04 | - |
| ~~F03~~ | Decided October 23, 2026: see ROUTE-02/03/05 | - |
| ~~F04~~ | Decided decided October 23, 2026: see DISK-08/09 and ACCESS-04 | - |
| F05 | v1 requester crafting statistics and XP attribution; output return/fallback is already decided | P3 crafting |
| F06 | Station selection, manual queue coexistence, and countdown disconnect behavior | P3 crafting |
| F07 | Pending reservations/countdowns during an outage; dispatched jobs continuing is already decided | P3 crafting |
| F08 | v2 creator buffs, XP, and missing/offline profile details | P5 pattern design |
| F09 | Crafter tiers/multiblocks and standalone versus workbench execution | P5 crafter design |
| F10 | Claim requirements/bounds, outside stations, and claim/controller removal during jobs | P0 prototype/P3 behavior |
| ~~F11~~ | Decided October 23, 2026: see UI-05 and the design's product priority order | - |
| F12 | Accounting, fairness and the technical cap decided October 23, 2026 (PROG-04/06). Still open: measured CPU/memory/traffic budgets and the final cap value | P4 benchmark |

Pending proposals must not silently become approved behavior. Record resolutions in the design and update this specification and the affected acceptance scenario together.
