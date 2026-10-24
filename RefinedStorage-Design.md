# Refined Storage Control: current design

Updated October 19, 2026, America/Chicago. This is the requirements source of truth. It supersedes the recommendations in RefinedStorage-Research.md. Implementation has not started.

## Product goal and supported environment

Centralized access to physical and digital storage is the primary goal. Autocrafting is a secondary feature delivered later. The Minecraft references span 1.16-1.21: Simple Storage Network, Refined Storage, and the expanded-crafter progression associated with Reborn Storage.

- Name: **Refined Storage Control**.
- First target: **7 Days to Die 3.3.0 b18**.
- Windows PC, solo and Windows player-hosted co-op. Maximum six players; usually two or three.
- No support requirement for existing saves. Development worlds may be reset.
- All clients install the mod; EAC disabled is accepted.
- Eventual distribution on Nexus Mods. Publishing is a future milestone, not an instruction to upload now.
- No initial named compatibility list. Storage, topology, progression, power, recipes, stations, and presentation must be replaceable or extendable without rewriting the core.

## Confirmed release boundaries

### v1: centralized storage and manual terminal crafting

- Simple Storage Control and powered Refined Storage Control progression.
- Storage interfaces, vanilla-style wire connections, a relay-like network-hub, and terminals.
- Read, deposit into, and withdraw from connected eligible vanilla containers. No vehicle or drone interfaces.
- Powered digital drive cabinets and portable storage disks, alongside physical storage.
- Favorites, categories, sorting, and deposit-matching interaction.
- Show equipment variants individually; no automatic exclusions from storage or bulk deposit.
- Simple-tier networks expose physical containers; digital drive providers require the advanced tier and valid power, so a simple controller cannot bypass their power gate.
- Manual recipe requests through connected workbench, forge, and campfire adapters, using real station queues and native requirements/times.
- Return finished products to storage accessible under the crafting request's permissions. If no eligible destination has room, leave products safely in the native station output.
- A connected workbench is required for backpack/general crafting from a terminal.
- Five-second cancellation countdown before a manual request dispatches its ingredients. Full post-dispatch cancellation is deferred.
- One available station per request; distinct requests may use separate compatible stations. A request is not automatically split between stations.
- Accepted jobs finish while the host runs. Keep the controller's claimed area active only while accepted crafting jobs remain, subject to a runtime feasibility test.

### v2: patterns and advanced automation

- Pattern encoder, creator-bound patterns, standalone and station-connected crafters, and tiered crafter progression.
- Store creator identity on patterns; use their latest known crafting statistics, including when the creator is offline. Improvements become available as creator statistics are updated.
- No item-type exclusion list for automation; normal recipe unlocks, station requirements, access checks, and resource requirements still apply.
- Vehicle adapters, potentially differentiated by vehicle class.
- Full cancellation flow, dependency previews, job-monitoring interface, and source-container lookup.
- Wireless access, remote loot handling, and more complex network models are deferred to this version for later design.
- Keep-in-stock, auto-fueling, automatic forge-feed management, world interaction, farming, harvesting, salvage, turret supply, and bulk fluid systems are outside v1. Deferral does not imply that every feature is committed for v2.

Native station progress and current inventories remain the authority in v1. Moving automation to v2 does not remove the need for internal job identifiers, ingredient reservations, persistence, recovery states, and diagnostics in v1.

Manual crafting in v1 requires its immediate ingredients to exist already in accessible network storage or the selected forge's internal reserves. Automatically producing missing intermediate ingredients is pattern automation and belongs to v2.

## Progression and resource choices

Confirmed:

- Advanced Engineering rank **2** unlocks Simple Storage Control.
- Advanced Engineering rank **5** unlocks Refined Storage Control.
- Stage components are gated by Wiring 101 progression, analogous to electrical equipment.
- Use existing ingredients and components. Keep recipes data-driven for future extensions.
- Simple Storage Control requires no electricity.
- Simple controller transfers are limited to **two completed stack transfers per second per controller**, shared across all players and configurable. Deposit-matching consumes the same transfer budget.
- Advanced equipment requires electricity, with demand scaling by attached interfaces, crafters, and other devices.
- Advanced storage access is totally unavailable without power.
- Native station jobs already dispatched continue under their own normal fuel/tools/processing rules during an RSC outage. Advanced network transfers, including output collection, wait for power to return.
- Equipment adds no heat or noise. This does not remove the native effects of connected stations or generators.

Proposed implementation details, not yet balance commitments:

- Separate controller unlocks from component-tier unlocks; controller crafting requires its specified perk rank and magazine gate, while support components use their configured magazine thresholds.
- Begin component availability near the existing electrical thresholds. Installed XML currently gates generators/relays at electrician skill 25, a second electrical tier at 45, and battery banks at 55.
- Recipe gates (F01, decided October 23, 2026): simple components at 25, advanced controller/components at 55, disks at 55/60/70/80/90/100, mirroring vanilla relays (25), battery banks (55) and quality car batteries (55-100). Wiring 101 goes to 100 in 3.3.
- A transfer action moves at most one native stack, or one nonstacking item. Bulk operations queue individual transfers; they cannot bypass throttling by requesting an unlimited quantity in one action.
- Queue fairness should prevent one bulk deposit from blocking every other player indefinitely. A simple round-robin of pending player requests is the starting recommendation.
- The scope of the transfer budget for crafting dispatch and automatic output collection needs to be specified alongside those flows; internal transfers must not become an accidental way to bypass the intended simple-tier limit.
- Advanced operation may avoid the simple-tier gameplay throttle, but still needs a configurable technical request/transfer budget.

## Network topology and wiring

Confirmed:

- One controller per network in v1.
- Wire interactions should behave like vanilla electrical wiring. Wires connected to an advanced RSC network appear blue.
- Add a **network-hub** block following the wire-relay interaction model to extend connections between network devices.
- ~~A controller placement that would create a network with two controllers should show a red placement indicator. If placement nevertheless occurs, break the rejected block.~~ Superseded by F02 (October 23, 2026): placing a block never creates a wire, so conflicts are prevented at wire time instead.

Recommended model:

- An authoritative host-side network registry containing stable network and node identifiers, with wire edges exposed through a topology adapter.
- Reuse the vanilla wire-tool interaction and range/connection model where compatible. Do not assume native powered nodes can supply an unpowered data network without custom behavior.
- A controller grants network membership to connected interfaces, terminals, hubs, and drive cabinets. Data routing and electrical supply are distinct concerns even when their wire interactions resemble one another.
- On controller removal, surviving storage is not erased; disconnected providers simply become unavailable through that terminal network.
- Revalidate only affected components on cable changes. A component lacking a controller is disconnected; a component with conflicting controllers is invalid.
- Validate new wire links, because two existing controller networks can be joined by wiring. Conflicting wires are rejected; neither controller is destroyed (F02, decided).
- The reject/break path must run once on the host and cannot duplicate a placed controller or its drop.

## Storage, routing, permissions, and destruction

### Physical providers

An interface reads and changes the connected container's actual inventory. It does not migrate or copy the contents into the controller. Container movement, breakage, and other behavior follow vanilla or the provider mod's rules.

v1 covers eligible vanilla container blocks and workstation adapters; it excludes vehicle/drone inventory providers. All item types are eligible for storage. A normal item being portable in a vehicle is separate from having a network adapter for that vehicle.

Each viewer sees and operates only on providers that their current container access permits. The host rechecks permissions for every transfer. Network topology alone never grants access to a locked or foreign container. Cache/index contents must also be filtered per viewer to avoid exposing inaccessible inventories.

### Digital providers

Confirmed disk capacities, counted in individual items:

| Tier | Capacity |
|---|---:|
| 1 | 1,000 |
| 2 | 10,000 |
| 3 | 100,000 |
| 4 | 250,000 |
| 5 | 500,000 |
| 6 | 1,000,000 |

Disks retain their data and are portable. Their contents are accessible only while installed in a drive cabinet connected to a usable network. Providers preserve exact item state, including quality, durability, modifications, and custom properties.

Implementation recommendation: use durable media identifiers and versioned records, with sparse item records rather than preallocating one slot per capacity unit. A million identical items need not create a million UI entries. Aggregate counters must be wide enough for multiple full disks; native stack limits still apply to transfers.

Confirmed digital RBAC baseline: an owner plus an explicit per-player allowlist, with distinct **deposit, withdraw, craft, and configure** permissions. Physical containers continue to use their native access rules. Host-side checks apply to every operation; inserting a disk does not make its contents public.

**TODO RSC-01 is required before digital storage can ship:** implement that framework with the ownership lifecycle decided under F04 (see below).

### Routing policy

1. Exclude disconnected, unpowered where applicable, inaccessible, filtered-out, and full/nonmatching providers as appropriate to the operation.
2. Apply integer priority **0-100**, with the lowest value preferred.
3. Within equal priority, prefer a provider already holding the matching item.
4. Otherwise choose the first provider with capacity, in a deterministic order.

Every attached storage interface needs a configurable priority field. Drive priority is also needed to implement the requested shared routing policy. Default priority, tie-breaker, filter direction, and extraction ordering are decided under F03 (October 23, 2026).

Rejected or excess items remain at the source. No automatic disposal is included in v1. Different-quality equipment must not merge into a single indistinguishable entry.

### Destruction

- Terminal: owns no stored items, so terminal destruction loses no network contents.
- Storage interface: may lose filters/upgrades randomly; underlying container follows its native destruction rules.
- Drive cabinet: roll survival once per installed disk. Surviving disks drop intact with their data. Destroyed disks and their stored contents are permanently lost.
- Controlled pickup of a disk is distinct from destructive loss. Cleanup must preserve surviving disk records and remove destroyed disk records exactly once.
- Survival probabilities and cabinet pickup are decided under F01/F04 (see below).

## Crafting: native stations first

### v1 manual request flow

1. A player chooses a known recipe and amount in the terminal; the host validates access, effective costs, required connected station, tools, fuel conditions, and output rules.
2. Reserve the required ingredients and target queue capacity. Keep physical ingredients at their source during the five-second confirmation window.
3. Display a five-second countdown with a cancel action. Cancellation before dispatch releases reservations.
4. At the deadline, revalidate the sources, permissions, station, and applicable power state. Dispatch once or return a clear failure without partial item loss.
5. Queue the recipe at a single real compatible station using the native recipe and queue mechanisms. Do not emulate a workbench with an invented generic input inventory.
6. Preserve native time, fuel consumption, quality-dependent cost rules, and required tools. The user maintains fuel; the network does not refuel or fill forge material reserves automatically in v1.
7. Return finished products to eligible accessible network storage using the crafting request's authority, rechecking current permissions, routing filters, capacity, connection, and power. Leave any unaccepted products safely in the station output. Do not collect unrelated manual-job outputs as if they belonged to the request.
8. Persist enough job state to track accepted work and release the claimed-area keepalive when native processing completes. Output awaiting capacity or power must remain recoverable in the native station and must not keep the claim active forever merely to wait for a destination. Retry collection when the station/network is available again.

One request for 100 rounds uses one workbench. A second request may use another workbench. There is no automatic 50/50 split in v1. Recommend choosing an accessible compatible station with queue capacity, then the shortest estimated queue, using stable node order for ties. Queue-order rules remain F06.

Forge crafting uses material already smelted in the selected forge. It must not treat an iron item in a crate as immediately interchangeable with forge iron units. The first forge integration should use existing reserves and native queue validation.

Confirmed outage behavior: an already dispatched native station job continues using its own normal rules and available fuel. The advanced terminal and network remain inaccessible, and network output collection waits until power returns. Do not remove, restart, or refund a native queued job merely because RSC power fails. Behavior for requests still in the pre-dispatch countdown remains F07.

### v2 patterns and crafter tiers

Pattern metadata should reference creator identity and recipe identity plus only the parameters needed to identify the intended output. Maintain one versioned creator-stat record per world/player rather than duplicating their entire profile onto every pattern. Refresh relevant records while the creator plays; the last saved record supports offline execution.

Resolve and freeze effective recipe parameters when a job is accepted, so mid-job stat changes do not alter reserved costs or outputs. Persistent progression versus temporary buffs, XP attribution, and offline record failure behavior remain F08.

The author describes Reborn Storage as a multiblock crafter whose CPU blocks increase crafting speed and storage blocks increase pattern capacity, plus larger storage disks. ExtraStorage explicitly offers upgraded autocrafter tiers. Both inform the requested progression, without requiring an exact port of either implementation.

- [Reborn Storage author's description](https://www.curseforge.com/minecraft/mc-mods/rebornstorage)
- [ExtraStorage author's description](https://www.curseforge.com/minecraft/mc-mods/extrastorage)

Recommendation: single-block crafter tiers first, with increasing pattern capacity and dispatch concurrency, for example 8/16/32/64 pattern slots and 1/2/4/8 dispatch slots. These are proposed numbers only. Station adapters do not accelerate native crafting time. More throughput requires more capable orchestration and available real stations. The role of a standalone crafter versus the connected-workbench requirement remains F09.

## Keeping an unattended base active

Confirmed starting policy: **keep the controller's claimed area active only while accepted crafting jobs remain**. No wall-clock catch-up after the host closes the game. Keepalive must not grant access to a container or override the agreed power rules.

Installed Assembly-CSharp inspection found `ChunkManager.AddChunkObserver` and `RemoveChunkObserver`, with arguments controlling visual-mesh construction and an entity destination. This is an implementation lead, not proof that an observer alone sustains native workstation, electricity, or entity updates correctly.

Initial prototype requirements:

- Register one host-side residency request for the owning claim while jobs are active; avoid forcing remote clients to render that area.
- Cover the actual configured claim bounds, including boundary chunks. Deduplicate overlapping requests and reference-count jobs/networks.
- Determine which simulation/update services must run in addition to residency. Do not invent a fake player until actual API behavior is understood.
- Test native time, forge fuel/materials, workbench queues, electricity, and save/load while all players are away.
- Release observers/residency requests on completion, world shutdown, invalidated jobs, and controller removal; reconstruct only valid unfinished requests after loading.
- Define what happens to machines outside the claim or when a claim is removed, invalidated, or changed. This remains F10.

| Approach | Benefits | Costs and risks | Current status |
|---|---|---|---|
| Job-scoped claimed-area activity | Preserves native station inventories and progression; least duplicated crafting logic | Resident chunks and potentially additional simulation consume resources; update coverage and cleanup must be verified | Chosen starting approach |
| Selective native station progression without full-area activity | Potentially smaller retained world footprint; keeps station semantics | Requires suitable hooks and careful handling of unavailable inventories, power, and saves | Possible later optimization |
| Detached background simulation | Work can scale independently of resident world regions | Must reproduce costs, fuel, power, and outputs; reload reconciliation and compatibility are substantially harder | Deferred alternative |

A land claim supplies the bounds and ownership context. This plan does not assume that placing a vanilla land claim itself keeps all required systems active.

## Modular implementation boundaries

These are proposed responsibilities, not promises of a large plugin framework:

| Module | Responsibility and extension point |
|---|---|
| Core / NetworkRegistry | Stable IDs, network membership, one-controller validation, lifecycle |
| Topology | Vanilla wire integration, connection changes, wire appearance, network-hub |
| Storage | Physical-container and disk providers; capacity and exact item identity |
| Access | Native container permission adapter; digital owner/allowlist with separate deposit, withdraw, craft, and configure permissions |
| Routing / Transfers | Filters, priority, deterministic selection, reservations, throttle, host commits |
| Persistence | Versioned disk records, request identifiers, recovery journal, migrations |
| Power | Native electrical supply adapter and configurable device consumption |
| Recipes / Stations | Effective recipes, station adapters, queue access, creator-stat service |
| Crafting | Manual job dispatch in v1; pattern planning/scheduling added in v2 |
| Residency | Claim bounds, job-scoped activity, observer lifetime and cleanup |
| Presentation | Vanilla-like XUi terminal, configuration screens, localization, input bindings |
| Content | XML blocks/items/recipes/progression, icons/models, balance configuration |

Prefer registered providers and station capabilities over hardcoded lists spread through UI or network code. Host logic owns mutations; clients send intent and display authorized state. Do not publish or redistribute game assemblies in a Nexus package.

## Scale, performance, and developer tools

Chosen primary benchmark profile: **24 storage interfaces, two terminals, two drive cabinets, three players, one active claimed base**. This is a test target, not a hard gameplay limit. The separate six-player support requirement remains; repeat contention/correctness checks with six clients.

Digital capacity, number of distinct stored item states, and visible UI entries are separate measures. Include duplicate resource stacks and individually distinct equipment in the benchmark rather than treating every stored item as a separate network record.

Recommended starting tools, accepted in principle by the user:

- Disposable development worlds and full save copies before destructive/recovery scenarios.
- Structured host logs with operation IDs, provider IDs, and concise rejection reasons.
- Read-only debug commands for network topology, effective access, capacities, reservations, power, and active job/claim counts.
- A small bounded transfer/job journal for diagnosis and restart reconciliation; no automatic restoration that duplicates legitimate transfers.
- An owner/admin disk-export path and versioned save migrations before public release.
- Focused automated tests for routing, item identity, throttling, job transitions, and serialization; in-game tests for the native integrations.

Product priority order (approved October 23, 2026, F11): inventory correctness and current access rules; dependable centralized storage; clear UI; modular maintainability; progression balance; automation breadth. Safety checks are part of a working storage feature, not an optional polish pass.

## Implementation milestones

| Milestone | Scope | Verification |
|---|---|---|
| P0: feasibility | Native wire tool/data links, permission checks, one container transfer, disk identity/persistence, manual station queue, job-scoped claim activity | Record supported APIs and unresolved constraints on 3.3.0 b18 |
| P1: first playable storage | Simple controller, interface, network-hub, terminal, favorites/sort/deposit matching, shared two-transfer budget | Normal three-player profile plus six-player contention; no duplication or unauthorized inventory access |
| P2: advanced storage | Advanced controller/power, cabinet, six disk tiers, RBAC minimum, destruction policy, progression XML | Portable media preserve data; outages block access; deterministic mixed-provider routing |
| P3: v1 crafting | Manual workbench/forge/campfire dispatch, five-second countdown, one station per request, accepted-job residency | Correct queue/time/fuel behavior, cancellation before dispatch, return from an empty base, save/restart |
| P4: v1 release preparation | Original assets, instructions, configurable balance, export/recovery tools, packaging | Full v1 acceptance scenarios and Nexus-ready package; publishing handled separately |
| P5: v2 | Creator-bound patterns, tiered crafters, advanced cancellation, optional vehicle/wireless features | Detailed design and scope decision before implementation |

P0 establishes crafting and residency feasibility early even though P1 centers on storage. No existing Project-Coruscant files are changed for this project.

## TODO register

- [ ] **RSC-01 (release blocker for disks):** implement the confirmed owner-plus-explicit-player-allowlist RBAC with separate deposit/withdraw/craft/configure permissions; finish ownership transfer, offline identities, and default-deny behavior when lifecycle policy is unresolved.
- [ ] **RSC-02:** add priority 0-100 configuration (default 50) and deposit-only allow/block filters to every storage interface and drive provider (F03 decided).
- [ ] **RSC-03:** confirm exact Advanced Engineering and Wiring 101 unlock table, recipes, and player-facing progression text.
- [ ] **RSC-04:** prototype vanilla-style data wires without requiring power for Simple Storage Control, advanced blue appearance, and relay-like network-hub.
- [ ] **RSC-05:** reject wires that would join two controllers (host and remote-client paths); put any two-controller network that still arises into a Conflict state (F02 decided).
- [ ] **RSC-06:** prototype native permission checks and per-viewer indexing for all eligible vanilla container types.
- [ ] **RSC-07:** implement the simple-tier two-transfers/second budget with round-robin fairness and player-first scheduling, and the advanced-tier technical cap (F12 decided).
- [ ] **RSC-08:** implement portable disk identity, exact item serialization, capacities, versioning, and same-world versus cross-world policy.
- [ ] **RSC-09:** decide and implement survival probabilities and exactly-once cleanup/drop behavior for destroyed media and interface upgrades.
- [ ] **RSC-10:** define watts per device, connection limits, and power propagation; preserve dispatched native jobs during outages and suspend advanced network output collection until power returns.
- [ ] **RSC-11:** define manual-crafting actor stats, XP, queue selection, and countdown disconnect/failure behavior; implement the confirmed return-to-accessible-storage flow with safe station-output fallback.
- [ ] **RSC-12:** validate forge internal reserves and native queues without adding automatic smelting/fueling.
- [ ] **RSC-13:** prototype job-scoped claimed-area activity, update coverage, ownership/claim changes, cleanup, and restart reconstruction.
- [ ] **RSC-14:** measure the selected small-base profile and test six-player correctness separately.
- [ ] **RSC-15:** implement diagnostic commands, bounded journal, disk export, and development-world recovery procedure.
- [ ] **RSC-16:** build the terminal for keyboard/mouse; gamepad is not promised for v1 (F11 decided).
- [ ] **RSC-17 (v2):** design minimal creator-bound patterns with persisted latest-known stats and offline behavior.
- [ ] **RSC-18 (v2):** design crafter tiers and clarify standalone execution versus connected-workbench requirements.
- [ ] **RSC-19 (v2):** design full cancellation, dependency preview, job UI, and source lookup.
- [ ] **RSC-20 (v2):** scope vehicle adapters, wireless, and more complex networks independently.
- [ ] **RSC-21:** prepare original assets, metadata, installation instructions, and a package for eventual Nexus distribution.

## Decisions from P0 findings (October 19, 2026)

These follow the code-reading findings in [P0-FINDINGS.md](P0-FINDINGS.md).

- **Unlocked containers:** follow native access exactly. Any network user can reach an unlocked container, as they could in person. Rationale: ACCESS-01 says to follow native access, and a stricter rule would invent a second permission system for physical storage.
- **Crash guarantee (SAVE-02):** accepted. State is consistent after a clean save or exit. After a crash, physical containers (saved with chunks) and RSC data (our own file) each revert to their own last save, so recent physical/digital moves may be lost or duplicated. Rationale: the game offers no shared transaction. Mitigation: save RSC data right after `World.Save` and keep the bounded journal.
- **Residency:** keep job-scoped claim loading even if native workstation catch-up is confirmed. Rationale: outputs are collected promptly, and players see current state.

## Decisions for P1 (October 23, 2026)

Accepted by the project owner after the P0 in-game run. SPECS.md carries the resulting requirements.

- **Wiring rules (P0 follow-up):** only a controller may have a vanilla parent (generator, battery, relay). Every other RSC block may only be wired from an RSC block, and RSC blocks may only have RSC children. Rationale: the network stays a clean, storage-only branch of the wire tree, it never becomes a power-distribution path for vanilla devices, and blue wires mean exactly one thing.
- **F02 - Network conflicts:** reject any wire that would put two controllers on one network, on both the host and the remote-client package path (already prototyped). Re-wiring a branch to another network is allowed when the result has at most one controller; access is rechecked on the next transfer (NET-05). The placement indicator and break-the-controller rule are dropped, because placement never creates a wire. Safety net: if a two-controller network still appears (old save, multiplayer race), both controllers enter a Conflict state that blocks storage access until a wire is removed; nothing is destroyed or dropped. Rationale: no block or item loss, one rule, matches the prototype.
- **F03 - Routing:** priority 0-100, default 50, lower preferred. Deposits go to the lowest number first; within equal priority, a provider already holding the exact item wins; remaining ties use a fixed node-position order. Filters (allow list or block list per interface) control deposits only; withdrawals see every accessible provider. Withdrawals take from the highest number (least preferred) first, with the same fixed tie order. Rationale: predictable, familiar to Refined Storage players, empties overflow first, and avoids "visible but not withdrawable" items.
- **F11 - Priorities and input:** the product priority order above is approved. v1 targets keyboard and mouse; gamepad is not promised, and any navigation the vanilla UI gives for free is a bonus. Revisit after v1.
- **F12 - Transfer budget:** one transfer is one move out of one source slot, up to a full stack, whatever the count. Deposit matching counts each stack moved. The simple controller allows 2 transfers/second (configurable), shared by everyone on the network; players are served round-robin; crafting and internal moves (P3) use the same budget with player requests first. The advanced controller has no gameplay limit, only a configurable per-network technical cap, starting at 20 transfers/second, finalized by the P4 benchmark. Rationale: easy to explain, keeps the simple tier a real progression step, and the advanced tier cannot stall the server.

## P1 implementation notes (October 23, 2026)

Choices made while building the container provider and transfer service. They follow the accepted decisions; change them here if they turn out wrong in play.

- **Which containers an interface serves:** every container in the six blocks touching the interface (multi-block containers resolve to their parent block). A container touching two interfaces counts once. Priority and filters are per interface, so all containers on one interface share them.
- **Eligible containers:** only player-placed containers (`TileEntityComposite.PlayerPlaced`). This keeps the network away from unopened loot and from loot that respawns when a touched world container is emptied. Workstations are excluded; they get their own adapters in P3.
- **Player slot locks** are respected: a locked slot is never filled or emptied by the network.
- **Withdraw size:** one request moves at most one stack. Each container slot it draws from is one transfer; on the simple tier a request may stop early at the budget and report it.
- **Interface settings file:** priority and filter are saved per interface in `<save>/RefinedStorageControl/nodes.dat` (versioned, with a `.bak` of the previous good copy), written right after each world save. Unreadable files are renamed with an `.unreadable-<time>` suffix and the world starts with defaults instead of overwriting them. Removing an interface block clears its settings. A container touching several interfaces uses the settings of the first interface in fixed position order.
- **Configuring:** `rsc net config/priority/filter` on the host console for now; per-player configure permission arrives with the terminal UI. Filter names are exact item names and are validated against the game's item list.
- **Round-robin scheduling:** bulk and backpack deposits queue one request per source slot; the host drains the queue every frame, players taking turns, at each network's budget rate. Single withdrawals run immediately and report when the budget stopped them.
- **First terminal UI (host only):** pressing E on a terminal opens a window with the network's items (search, pages; left click takes a stack, right click takes one) above the player's backpack (click a slot to store it; Store matching / Store all). Pressing E on an interface opens its priority and filter settings (filter built from the held item). Blocks use the `BlockRscNode` class, which keeps the vanilla powered tile entity.
- **Console front end:** `rsc net list/deposit/withdraw` drives the service until the terminal UI exists. Endpoints are containers, not player inventory; player-inventory transfers come with the terminal's network packages.

### Terminal view options (October 23, 2026)

- **Categories:** All, Resources, Weapons & Ammo, Tools, Food & Medical, Armor & Clothing, Building, Science & Books, Other, mapped from the vanilla item `Group` values (crafting-menu variants such as `CF…`, `TC…`, `adv…` included). The category button skips categories with nothing in them.
- **Sort:** Name, Count or Quality, with name then quality as tie-breakers so the order is stable between refreshes.
- **Favorites:** per player, stored on that player's machine (`<user data>/RefinedStorageControl/favorites.txt`) and shared across worlds. A favorite covers every variant of an item and always sorts first. Toggle "Mark favorites", then click items; click the button again to go back to taking items.
- The category and sort choice persist while the game runs; the logic lives in the pure `Source/View/` module with tests.

### Co-op clients (October 23, 2026)

- A client's backpack is owned by the client, so items travel in packages. **Store:** the client removes one stack from its backpack and sends it; the server deposits what fits and returns the rest, which goes back to the same slot when free. **Take:** the client asks only for what fits in its backpack; the server removes it from the network and sends it; anything that no longer fits is dropped at the player's feet.
- The server re-checks every request: the player must be within 8 blocks of a real terminal or interface, and native container access, in-use and power rules apply as for the host.
- Each client has one store in flight; a budget refusal returns the stack and retries the same slot after 0.3 s, so clients share the budget roughly in turn. The host's own bulk stores use the round-robin queue.
- Trust model: like vanilla containers, the server accepts the client's description of the stack it is storing. A modified client could create items; this is the same exposure vanilla co-op already has.
- Known gap: if a client disconnects while a stack is in flight, that one stack can be lost.
- Interface settings for clients: the window asks the server for the current settings and sends changes; the server validates item names and ranges and answers with the stored result.

## Decisions for P2 (decided October 23, 2026)

Accepted by the project owner. All numbers are configurable defaults to tune after playtesting.

- **F01 - Balance:** Wiring 101 gates of 25 (simple parts), 55 (advanced controller and parts) and 55/60/70/80/90/100 (disks 1k-1m). Six disks per cabinet. Power: advanced controller 10 W, interface 2 W, terminal 3 W, drive cabinet 5 W, hub 0 W (a 24-interface, 2-terminal, 2-cabinet base draws about 74 W). Destruction: each disk in a destroyed cabinet survives with 50% chance; a destroyed interface loses its filter settings with 50% chance. Rationale: gates follow existing electrical progression; power is a real but mid-game cost.
- **F04 - Media:** disks are bound to their world (contents live in that world's save and item ids differ between worlds); in another world a disk shows "data unavailable". A cabinet can be picked up only when empty. Rationale: no corrupted cross-world items, no pickup exploits or accidental loss.
- **ACCESS-04 - Ownership:** owner is the first player to insert the disk blank; inserting or removing never changes ownership. The owner can transfer to an allowlisted player; the host can reassign any disk by console command. The allowlist works while the owner is offline. New allowlist entries get deposit + withdraw; craft and configure (including editing the allowlist) are granted separately. Rationale: predictable ownership, owner stays in control, admins can recover abandoned disks.

### P2 implementation notes (October 23, 2026)

- **Build order:** (1) disk store, save file, disk items, cabinet block, power values; (2) cabinet window to insert/remove disks and manage owner/allowlist, host and co-op; (3) disks as providers in deposit/withdraw routing with ACL and power checks; (4) destruction survival rolls, empty-only cabinet pickup, interface filter loss; (5) recipes and Wiring 101 gates.
- **Disk identity:** a disk item carries `rsc.disk` (disk id) and `rsc.world` (world id) in native item metadata. A blank disk has neither; its record and owner are created on first insert. Contents live in `<save>/RefinedStorageControl/disks.dat` with the cabinet slot map; the file's world id is what binds disks to the world.
- **One slot per disk:** the store refuses to install a disk that is already installed anywhere, so a stale copy of a disk item cannot duplicate its contents. Destroyed disks lose their record for good.
- **Ownership transfer:** the previous owner stays on the allowlist with the default deposit + withdraw.
- **Shared save handling:** interface settings and disks both use the .tmp/.bak/.unreadable pattern in `RscSaveFile`.
- **Cabinet window (step 2):** E on a drive cabinet shows its six slots, the selected disk's tier, use, owner and your access, its allowlist with Deposit / Withdraw / Craft / Configure toggles, "Remove from list", "Make owner" (owner only) and "Add player" (known players of this world, default deposit + withdraw), and the backpack (click a disk to insert it into the selected slot or the first free one). Works for the host directly and for co-op clients through `NetPackageRscCabinet` / `NetPackageRscCabinetState`; a refused client insert sends the disk straight back.
- **Who may do what at a cabinet:** anyone in reach may insert a disk into a free slot; a blank disk becomes the inserter's. Taking a disk out and editing its allowlist need the owner or Configure (ACCESS-04: Configure includes editing the allowlist; the access list now enforces that). Making someone else the owner needs the owner. A cabinet works without power or a network; disks only store items on a powered advanced network (step 3).
- **Refused disks:** a disk from another world ("data unavailable") or one whose data was destroyed cannot be installed.
- **Host tools:** `rsc disk list` and `rsc disk owner <diskIdPrefix> <player>`.
- **Disks as storage (step 3):** installed disks join deposit/withdraw routing next to containers through one `StorageProvider` abstraction. Disks count only on an **advanced, powered** network (PROG-05, PWR-02); on a simple network they are ignored. Per player: seeing a disk's items and taking them need **Withdraw**, storing needs **Deposit** (owner has everything). Capacity counts individual items; a withdrawal from a disk is one transfer. A disk stores only exact item keys; withdrawals rebuild the exact item from the key (quality, durability, mods, metadata). Items whose type no longer exists stay recorded but are hidden.
- **Cabinet priority and filter:** a cabinet has its own priority and deposit filter, applying to all its disks, edited through the same settings window as interfaces ("Priority and filter" button in the cabinet window) or `rsc net priority/filter` on the cabinet. Fixed tie order: cabinet position, then slot.
- **Destruction and pickup (step 4):** a cabinet destroyed by damage, an explosion or a collapse rolls each disk once (50% survival); survivors drop intact with their data, the rest lose their records for good. The slot map is cleared before anything drops, so a repeated event cannot drop or delete twice (DISK-06). Every other removal (pickup, admin tools) drops all disks intact. RSC drops last one hour instead of the vanilla 60 seconds.
- **Empty-only pickup (DISK-09):** pickup of a cabinet with disks shows "Remove the disks first". The host checks the store directly; a client asks the server ("Checking the cabinet... try again" on the first attempt, answer trusted for 5 s). If a stale answer still lets a loaded cabinet through, the server drops its disks intact.
- **Settings after destruction (DISK-05):** interface and cabinet settings are tied to their position. When the block is destroyed they are kept for a block rebuilt on the same spot, but the deposit filter is lost with 50% chance; priority is kept. Picking a block up clears its settings, so a block placed there later starts from the defaults.
- **Recipes and gates (step 5):** every RSC block and disk has a workbench recipe from existing materials (forged iron/steel, electrical and mechanical parts, scrap polymers) tagged `learnable`. Wiring 101 unlocks hub, interface and terminal at 25; the simple controller at 25 **and** Advanced Engineering 2; the advanced controller at 55 **and** Advanced Engineering 5; the drive cabinet and 1k disk at 55; disks 10k/100k/250k/500k/1m at 60/70/80/90/100. The "and" is one `RecipeTagUnlocked` effect with an Advanced Engineering requirement. The Wiring 101 page lists them as "Storage Network" (25), "Powered Storage" (55) and one entry per larger disk. Amounts are first-pass balance in `Mod/Config/recipes.xml`.
- **Known gap:** damage a remote co-op client deals with their own tools may be applied on the client, in which case the server only sees the block disappear and treats it as a plain removal (disks drop intact, settings cleared). Zombie, explosion, collapse and host damage run on the server and use the rolls.

## Remaining decisions

These are refinements, not another round of the original 36 questions. Accepted choices above stay accepted.

- **F05 - Manual crafting:** use the requesting player's stats in v1, and who receives crafting XP? Output destination is decided: return to accessible network storage, with safe station-output fallback when no eligible capacity is available.
- **F06 - Queueing:** choose a station with space and the shortest queue, or fill one station before choosing another? Should new terminal jobs append behind manual station jobs? Cancel an undispatched countdown if its player disconnects?
- **F07 - Power loss:** should undelivered inputs remain reserved or should pending countdown requests be cancelled? Already dispatched native jobs continuing on their own fuel, with advanced output collection waiting for power, is decided.
- **F08 - Creator stats for v2:** persistent progression only, or temporary buffs too? Who receives XP? What happens if the creator's saved profile becomes unavailable?
- **F09 - Crafter tiers:** are increasing pattern slots and dispatch concurrency sufficient, or is a multiblock desired? What work may a standalone crafter perform while preserving the connected-workbench requirement?
- **F10 - Claims:** must all linked devices/stations be within the controller's claim? What happens to accepted work when that claim/controller is removed? Would a claim be required only for unattended crafting or for all network use?

## Acceptance checklist

- [ ] Two players competing for the last item cannot over-withdraw; verify six-player contention as well.
- [ ] Each viewer sees only inventory they may currently access; revocation invalidates later transfers.
- [ ] Full storage and rejected filters never consume the unaccepted source items.
- [ ] The simple controller enforces two stack transfers per second across users and bulk deposits.
- [ ] Priorities, matching-stack preference, exact equipment variants, and six disk capacities behave consistently.
- [ ] An unpowered advanced controller cannot expose usable network storage access.
- [ ] Digital owners can grant deposit, withdraw, craft, and configure independently; physical-container access remains governed by native rules.
- [ ] Finished request products return only to currently accessible eligible storage; full storage leaves them safely in station output.
- [ ] RSC power loss does not stop or duplicate dispatched native station jobs; advanced output collection waits for power and resumes safely.
- [ ] Removing a terminal/controller does not erase independent physical containers or surviving disks.
- [ ] Destruction rolls each disk once and permanently removes only destroyed disk contents.
- [ ] A five-second pre-dispatch cancel leaves no consumed ingredients or stranded reservations.
- [ ] Manual workbench, campfire, and forge jobs preserve native requirements, time, and user-maintained fuel.
- [ ] Two requests may use two stations, while a single request remains on one station.
- [ ] Job-scoped claim activity sustains accepted jobs and releases resources at completion.
- [ ] Host shutdown stops time; save/reload does not duplicate ingredients, outputs, media, or claim observers.
- [ ] Selected three-player scale profile works within measured budgets; six-player support is verified separately.

Validation status: documentation, installed XML, and selected assembly methods inspected. No runtime prototype, playtest, or implementation tests have run.
