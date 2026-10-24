# Refined Storage Control overview TODO

Updated October 23, 2026. Status: **P0 closed by owner decision; P1 next.** Checks skipped during P0 are deferred to the stage that depends on them (see [P0-FINDINGS.md](P0-FINDINGS.md#p0-closure-october-23-2026)). This overview follows [SPECS.md](SPECS.md); rationale and the original RSC task IDs are in [the design](RefinedStorage-Design.md).

## Roadmap

| Stage | Outcome | Dependency |
|---|---|---|
| P0 | Prove the native integrations and recovery approach | First |
| P1 | Playable centralized physical storage | P0 storage/topology findings |
| P2 | Powered disks, permissions, and progression | P1 and media/access decisions |
| P3 | Manual terminal crafting and unattended accepted jobs | P1/P2 and P0 station/claim findings |
| P4 | Validate and package v1 | P1-P3 complete |
| P5 | Design and build v2 pattern automation | Separate scope after v1 |

P0 investigates crafting and claim activity early to expose architectural constraints. P1 remains focused on the primary product value: centralized storage.

## P0 - Feasibility and foundation

- [x] Establish the C# mod/build skeleton and modular boundaries for 3.3.0 b18. Keep game assemblies external to distributable files. **MOD-01 through MOD-03**
- [x] Prototype vanilla-style wires, an unpowered data network, blue advanced-network wires, and relay-like hubs. **RSC-04; NET-02/03** *(blue-wire re-test, wire limits, reload deferred to P1)*
- [x] Prove one host-authorized container transfer, exact item preservation, native access checks, and safe rejection of competing requests. **RSC-06; A01-A03/A05** *(in-use/locked rejection and modded items deferred to P1)*
- [x] Prototype portable disk identity/save records and determine recovery guarantees at actual game save boundaries. **RSC-08/15; SAVE-01/02** *(tag survival and crash guarantee deferred to P2)*
- [x] Prove a native station queue request and job-scoped claim activity; test fuel, power, unload/reload, completion, and observer cleanup. **RSC-11/12/13; CRAFT-01/RUN-01** *(completion, fuel gating, absent catch-up and observer deferred to P3)*
- [x] Record findings and resolve prototype-dependent blockers before committing later modules to an unsupported approach. **F10/F12**

**Exit:** supported hooks and limits are documented; transfer, station, media, and claim prototypes identify a viable approach or a concrete unresolved blocker.

## P1 - Centralized physical storage

- [ ] Implement the network registry, simple controller, interfaces, hubs, and terminals. Enforce one controller (wire rejection + Conflict state) and the vanilla-wiring rule. **RSC-04/05; NET-01 through NET-06**
- [ ] Add vanilla container providers, excluding vehicle/drone adapters; preserve native inventory and destruction rules. **RSC-06; PHY-01 through PHY-04** *(built: interface serves adjacent player-placed containers; host transfer service with `rsc net` commands; awaiting in-game checks)*
- [ ] Apply native permissions to both per-viewer inventory displays and host-side transfers. **RSC-06; ACCESS-01/02**
- [ ] Implement deposit-only filters, configurable priority 0-100 (default 50), deterministic deposit/withdraw ordering, and safe full-storage behavior. **RSC-02; ROUTE-01 through ROUTE-05** *(built: routing order, filters, saved interface settings, `rsc net priority/filter`; awaiting in-game checks)*
- [ ] Enforce the simple-tier two-transfer/second budget (round-robin, deposit matching counted) and the advanced-tier technical cap. **RSC-07; PROG-04/06** *(built with the deposit queue; awaiting in-game checks)*
- [ ] Build a vanilla-like terminal (keyboard/mouse) with search, favorites, categories/sort, deposit matching, and individual equipment variants. **RSC-16; UI-01 through UI-05** *(built: search, categories, sort by name/count/quality, favorites, paging, deposit matching/all, exact variants, interface settings window, co-op client packages. Host tested October 23, 2026; categories/sort/favorites and co-op awaiting in-game tests)*

**Exit:** players can connect containers and safely view/deposit/withdraw through the terminal. Includes the P0-deferred checks: in-use/locked container rejection, modded-item transfer, second-controller rejection, wire limits, reload, blue wires. Pass applicable A01-A05/A15 scenarios, including six-player contention checks.

## P2 - Powered digital storage and progression

- [ ] Implement the advanced controller and native power integration; block all advanced storage access during outages. **RSC-10; PWR-01/02/04**
- [ ] Add drive cabinets and disks of **1k, 10k, 100k, 250k, 500k, and 1m individual items**. Preserve portable data and exact item state. **RSC-08; DISK-01 through DISK-03** *(built: disk store + disks.dat with world binding, six disk items, drive cabinet block, F01 power values, cabinet window with insert/remove for host and co-op. disks as storage providers with ACL and power checks, cabinet priority/filter, destruction rolls and empty-only pickup. Next: recipes)*
- [ ] Implement owner plus explicit player allowlist with independent deposit/withdraw/craft/configure permissions. Finish ownership lifecycle rules before shipping disks. **RSC-01; ACCESS-03/04** *(built: owner on first insert, allowlist editing in the cabinet window, ownership transfer, `rsc disk owner`; deposit/withdraw checks arrive with disks in routing; awaiting in-game checks)*
- [ ] Apply configurable priority/filter behavior to digital providers alongside physical containers. **RSC-02; ROUTE-01 through ROUTE-04** *(built: per-cabinet priority/filter in shared routing; awaiting in-game checks)*
- [ ] Implement per-disk destruction survival and filter/upgrade loss, without duplicate drops or stale media resurrection. **RSC-09; DISK-04 through DISK-06** *(built: survival rolls, exactly-once cleanup, empty-only pickup, filter loss for rebuilt blocks; awaiting in-game checks)*
- [ ] Add Advanced Engineering rank 2/5 gates, approved Wiring 101 thresholds, and recipes using existing components. **RSC-03; PROG-01/02/MOD-03**

**Exit:** mixed physical/digital storage passes access, capacity, power, portability, and destruction scenarios A02/A03/A06-A08.

## P3 - v1 manual crafting

- [ ] Add workbench, forge, and campfire adapters; require a connected workbench for terminal backpack/general crafting. **RSC-11/12; CRAFT-01**
- [ ] Validate native unlocks, costs, tools, quality, immediate ingredients, and existing forge reserves; leave fueling and reserve maintenance to the player. **RSC-11/12; CRAFT-02/03**
- [ ] Implement ingredient/queue reservations, five-second pre-dispatch cancellation, and host revalidation before dispatch. **RSC-11; CRAFT-04/06**
- [ ] Dispatch each request to one station; allow separate requests on separate stations without splitting a request. Resolve stats, XP, queue order, and disconnect/outage behavior. **RSC-11; CRAFT-05/F05-F07**
- [ ] Return attributable products to currently accessible storage, with safe native-output fallback when full/unavailable. Preserve dispatched native jobs through RSC outages. **RSC-10/11; CRAFT-07/08/PWR-03**
- [ ] Keep the claimed area active only while accepted native crafting remains; persist/recover jobs and release activity resources correctly. Stop time when the host closes. **RSC-13; CRAFT-09/RUN-01 through RUN-04**

**Exit:** A09-A14 pass for all three station types, including absent players, full output destinations, power loss, and save/restart.

## P4 - v1 verification and release preparation

- [ ] Add structured logs, read-only inspection commands, bounded journal/reconciliation, and owner/admin disk export. **RSC-15; SAVE-02/03**
- [ ] Run all [acceptance scenarios A01-A15](SPECS.md#11-acceptance-scenarios), including permissions, item conservation, failures, and recoverable interruption cases.
- [ ] Benchmark **24 interfaces, two terminals, two cabinets, three players, one active claimed base**; measure and agree budgets. Verify six-player support separately. **RSC-14; ENV-02/F12**
- [ ] Finalize original visuals, localization, input bindings, recipes, balance/configuration, and documented limitations. **RSC-03/16/21; F01/F11**
- [ ] Write install, configuration, backup/export, update, and uninstall instructions; package the mod without game assemblies. **RSC-15/21; ENV-03/05**
- [ ] Prepare Nexus-ready metadata and artifacts. Upload/publish only as a separate authorized step. **RSC-21**

**Exit:** v1 requirements are verified, unresolved release-blocking choices are closed, and the package is ready for review.

## P5 - v2 backlog

- [ ] Design minimal creator-bound patterns and latest-known offline creator-stat records; resolve buffs, XP, and missing profiles. **RSC-17; AUTO-01/02/F08**
- [ ] Define standalone/station crafter execution and tier progression; decide pattern capacity, concurrency, and whether multiblocks are wanted. **RSC-18; AUTO-03/F09**
- [ ] Implement pattern dependencies and full cancellation after the v2 execution model is agreed. **RSC-17/18/19; AUTO-01 through AUTO-05**
- [ ] Add dependency previews, job-monitoring UI, and source-container lookup. **RSC-19; AUTO-05**
- [ ] Scope vehicle adapters, wireless, and more complex networks independently. Other automation extensions need their own scope decisions. **RSC-20**

**Exit:** define a separate v2 acceptance plan; do not expand v1 to absorb this backlog.

## Decisions still needed

- [x] Before P1: resolve network conflicts, routing defaults/filter direction, transfer accounting, and initial input support. **F02/F03/F11/F12** *(decided October 23, 2026; F12 performance budgets stay with the P4 benchmark)*
- [x] Before P2: resolve balance values, media movement/ownership, cabinet pickup, and survival rules. **F01/F04** *(decided October 23, 2026)*
- [ ] During P0 and before P3: resolve claim bounds/removal; crafting stats/XP, station selection, and pending countdown behavior. **F05/F06/F07/F10**
- [ ] Before P5: resolve creator-stat details and crafter architecture. **F08/F09**

Full decision descriptions are in [SPECS.md](SPECS.md#12-open-decisions). Do not count a proposed default as an accepted answer.

## Maintaining this list

Check a task only when its result and relevant verification are complete. Update the specification when behavior changes and record the decision/rationale in the design. Keep detailed implementation subtasks beside their module or test evidence rather than expanding this overview into a duplicate of every source-code change.
