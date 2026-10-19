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
- Candidate recipe table: simple components at 25, advanced controller/components at 55, disks at 55/60/70/80/90/100. Exact mapping remains F01.
- A transfer action moves at most one native stack, or one nonstacking item. Bulk operations queue individual transfers; they cannot bypass throttling by requesting an unlimited quantity in one action.
- Queue fairness should prevent one bulk deposit from blocking every other player indefinitely. A simple round-robin of pending player requests is the starting recommendation.
- The scope of the transfer budget for crafting dispatch and automatic output collection needs to be specified alongside those flows; internal transfers must not become an accidental way to bypass the intended simple-tier limit.
- Advanced operation may avoid the simple-tier gameplay throttle, but still needs a configurable technical request/transfer budget.

## Network topology and wiring

Confirmed:

- One controller per network in v1.
- Wire interactions should behave like vanilla electrical wiring. Wires connected to an advanced RSC network appear blue.
- Add a **network-hub** block following the wire-relay interaction model to extend connections between network devices.
- A controller placement that would create a network with two controllers should show a red placement indicator. If placement nevertheless occurs, break the rejected block.

Recommended model:

- An authoritative host-side network registry containing stable network and node identifiers, with wire edges exposed through a topology adapter.
- Reuse the vanilla wire-tool interaction and range/connection model where compatible. Do not assume native powered nodes can supply an unpowered data network without custom behavior.
- A controller grants network membership to connected interfaces, terminals, hubs, and drive cabinets. Data routing and electrical supply are distinct concerns even when their wire interactions resemble one another.
- On controller removal, surviving storage is not erased; disconnected providers simply become unavailable through that terminal network.
- Revalidate only affected components on cable changes. A component lacking a controller is disconnected; a component with conflicting controllers is invalid.
- Validate new wire links as well as new blocks, because two existing controller networks can be joined by wiring. Recommend rejecting the new conflicting wire rather than destroying either established controller. Final conflict behavior and item recovery are F02.
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

**TODO RSC-01 is required before digital storage can ship:** implement that framework and finish ownership assignment/transfer, media portability, and offline-owner rules. Those lifecycle details remain open; the owner-plus-allowlist permission model is decided.

### Routing policy

1. Exclude disconnected, unpowered where applicable, inaccessible, filtered-out, and full/nonmatching providers as appropriate to the operation.
2. Apply integer priority **0-100**, with the lowest value preferred.
3. Within equal priority, prefer a provider already holding the matching item.
4. Otherwise choose the first provider with capacity, in a deterministic order.

Every attached storage interface needs a configurable priority field. Drive priority is also needed to implement the requested shared routing policy. Recommend a stable node identifier as the tie-breaker. Default priority, filter direction, and extraction ordering remain F03.

Rejected or excess items remain at the source. No automatic disposal is included in v1. Different-quality equipment must not merge into a single indistinguishable entry.

### Destruction

- Terminal: owns no stored items, so terminal destruction loses no network contents.
- Storage interface: may lose filters/upgrades randomly; underlying container follows its native destruction rules.
- Drive cabinet: roll survival once per installed disk. Surviving disks drop intact with their data. Destroyed disks and their stored contents are permanently lost.
- Controlled pickup of a disk is distinct from destructive loss. Cleanup must preserve surviving disk records and remove destroyed disk records exactly once.
- Survival probabilities, filter/upgrade recovery behavior, and controlled cabinet pickup rules remain F04.

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

Recommended priority order, awaiting F11: inventory correctness and current access rules; dependable centralized storage; clear UI; modular maintainability; progression balance; automation breadth. Safety checks are part of a working storage feature, not an optional polish pass.

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
- [ ] **RSC-02:** add priority 0-100 configuration to every storage interface and drive provider; define defaults and filter direction.
- [ ] **RSC-03:** confirm exact Advanced Engineering and Wiring 101 unlock table, recipes, and player-facing progression text.
- [ ] **RSC-04:** prototype vanilla-style data wires without requiring power for Simple Storage Control, advanced blue appearance, and relay-like network-hub.
- [ ] **RSC-05:** handle duplicate controllers at preview, placement, and wire-join time without destroying established networks or duplicating drops.
- [ ] **RSC-06:** prototype native permission checks and per-viewer indexing for all eligible vanilla container types.
- [ ] **RSC-07:** implement the shared configurable two-stack-transfer budget, bulk-operation accounting, and fair requests.
- [ ] **RSC-08:** implement portable disk identity, exact item serialization, capacities, versioning, and same-world versus cross-world policy.
- [ ] **RSC-09:** decide and implement survival probabilities and exactly-once cleanup/drop behavior for destroyed media and interface upgrades.
- [ ] **RSC-10:** define watts per device, connection limits, and power propagation; preserve dispatched native jobs during outages and suspend advanced network output collection until power returns.
- [ ] **RSC-11:** define manual-crafting actor stats, XP, queue selection, and countdown disconnect/failure behavior; implement the confirmed return-to-accessible-storage flow with safe station-output fallback.
- [ ] **RSC-12:** validate forge internal reserves and native queues without adding automatic smelting/fueling.
- [ ] **RSC-13:** prototype job-scoped claimed-area activity, update coverage, ownership/claim changes, cleanup, and restart reconstruction.
- [ ] **RSC-14:** measure the selected small-base profile and test six-player correctness separately.
- [ ] **RSC-15:** implement diagnostic commands, bounded journal, disk export, and development-world recovery procedure.
- [ ] **RSC-16:** confirm keyboard/mouse versus gamepad support; the prior hardware-controller question was answered as a network core block.
- [ ] **RSC-17 (v2):** design minimal creator-bound patterns with persisted latest-known stats and offline behavior.
- [ ] **RSC-18 (v2):** design crafter tiers and clarify standalone execution versus connected-workbench requirements.
- [ ] **RSC-19 (v2):** design full cancellation, dependency preview, job UI, and source lookup.
- [ ] **RSC-20 (v2):** scope vehicle adapters, wireless, and more complex networks independently.
- [ ] **RSC-21:** prepare original assets, metadata, installation instructions, and a package for eventual Nexus distribution.

## Remaining decisions

These are refinements, not another round of the original 36 questions. Accepted choices above stay accepted.

- **F01 - Balance table:** approve or change proposed magazine gates (simple components 25; advanced components 55; disks 55/60/70/80/90/100), cabinet slots, device power costs, and disk/interface survival chances.
- **F02 - Network conflicts:** should a wire joining two existing controller networks be rejected? Should a forcibly placed duplicate controller drop itself intact when broken?
- **F03 - Routing:** approve default priority 50, lower-first ordering, matching-item preference only within equal priority, stable ties, and filters controlling insertion; specify whether filters must also hide/block withdrawal.
- **F04 - Media:** may a disk move between separate save worlds? May a cabinet be picked up while loaded? Decide ownership transfer when a teammate inserts or removes a disk.
- **F05 - Manual crafting:** use the requesting player's stats in v1, and who receives crafting XP? Output destination is decided: return to accessible network storage, with safe station-output fallback when no eligible capacity is available.
- **F06 - Queueing:** choose a station with space and the shortest queue, or fill one station before choosing another? Should new terminal jobs append behind manual station jobs? Cancel an undispatched countdown if its player disconnects?
- **F07 - Power loss:** should undelivered inputs remain reserved or should pending countdown requests be cancelled? Already dispatched native jobs continuing on their own fuel, with advanced output collection waiting for power, is decided.
- **F08 - Creator stats for v2:** persistent progression only, or temporary buffs too? Who receives XP? What happens if the creator's saved profile becomes unavailable?
- **F09 - Crafter tiers:** are increasing pattern slots and dispatch concurrency sufficient, or is a multiblock desired? What work may a standalone crafter perform while preserving the connected-workbench requirement?
- **F10 - Claims:** must all linked devices/stations be within the controller's claim? What happens to accepted work when that claim/controller is removed? Would a claim be required only for unattended crafting or for all network use?
- **F11 - Priorities and input:** approve the recommended product-priority order; clarify whether gamepad input is needed for the initial PC release.

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
