# Refined Storage intent questionnaire

**Answered discovery questionnaire, retained for reference.** Answers received October 19 are recorded in [RefinedStorage-Design.md](RefinedStorage-Design.md). Do not reopen these questions as unanswered. The remaining decisions have new F-prefixed identifiers in that document; the original questions below retain their numbering for traceability.

## Scope and reference behavior

1. Which Minecraft versions or modpacks best represent the experience you want? Which three Refined Storage features are essential, and which behaviors do you dislike?
2. Is 7 Days V3.3.0 b18 the intended first target? Must this work on existing saves, and is a test world acceptable during development?
3. Will private co-op use a player-hosted game, a dedicated server, or both? What host/server operating systems and maximum player count should we support?
4. Which other mods must coexist with this one, especially UI, inventory, stack-size, crafting-from-storage, and overhaul mods? Should it interact with Project-Coruscant in any specific way?
5. Is installing the mod on every participating PC/server and running with EAC disabled acceptable? Do you want an eventual public release, source repository, and original mod name, or a private project initially?
6. What exact first playable experience would feel like success: searchable crates, powered disks, or a complete automated crafting chain? Give one example task you want to perform.

## Progression and survival balance

7. When should each tier become available: opening days, first workbench, electricity, or late game? What should unlock it: magazines, perks, schematics, quests, traders, or a new research system?
8. Should components mostly use existing electrical/mechanical parts and metals, or add processors, silicon, circuit boards, and other manufacturing steps? How involved should that production chain be?
9. Should early connected storage work without electricity? Which advanced devices should consume power, and should demand scale with capacity, connected devices, or active transfers/crafting?
10. During a power outage, should advanced equipment become inaccessible, offer emergency withdrawal, or operate briefly on a buffer? Should queued jobs pause and resume?
11. Should network equipment generate heat, noise, or another zombie-attraction cost? What should zombie destruction do to controllers, drives, cables, and their contents?
12. Should disks and loaded cabinets be movable with their contents, including in vehicles? When a full drive is destroyed, should contents be recoverable, dropped, or lost? Is cross-world disk transport desired?

## Physical network and storage rules

13. What should data cables look like: full blocks, thin surface conduits, or concealed connections? Should they share a block space with walls where feasible, and should there be length, device, or network-size limits?
14. Which inventories may connect: player crates, secure chests, cabinets, vehicle storage, drones, loot containers, and modded containers? Should linking require ownership and an explicit adapter on each inventory?
15. How should co-op access work: owner only, party, allies, or an explicit list? Should deposit, withdraw, craft, configure, and dismantle permissions be separate?
16. Can multiple controllers or hubs belong to one network? What should happen when cables merge two networks or split one network in half, and how should early hubs upgrade without rebuilding the base?
17. How should disk capacity be measured: individual items, stack-equivalent units, item types, or another rule? Give an approximate desired capacity for the first and final disk tiers.
18. Which routing controls matter: preferred destination, filters, reserved slots, per-item caps, insertion/extraction priority, and a bulk deposit chest? On full storage, should transfers stop, use an overflow container, or optionally discard filtered excess?

## Recipes, stations, and automation

19. After a knowledgeable player encodes a pattern, may authorized teammates use it without knowing that recipe? Whose crafting quality, speed, material discounts, and XP rules should apply, including while that player is offline?
20. Should patterns encode a fixed output quality and specific recipe, or adapt to the requester? Must every intermediate recipe have a pattern, as in Refined Storage?
21. Which stations are required for the first automation release: workbench, forge, campfire, chemistry station, cement mixer, or others? Should simple backpack recipes execute through a workbench or a separate basic crafting unit?
22. Should automation feed fuel, smelt raw materials into forge reserves, and collect products automatically? May players reserve some fuel/materials for manual use, and may the network expose smelted forge reserves as visible stock?
23. Should one request use several compatible stations in parallel? How should manual jobs and automated jobs share station queues, and should the system automatically choose an alternative recipe or station?
24. Do you want automatic stock targets such as "keep 1,000 rounds"? Which items should it protect from automatic consumption, and how should player requests, stock rules, missing ingredients, output-full conditions, and cancellation be prioritized?

## Unattended operation and wireless access

25. While the host/server is running, must every feature continue at an empty base, including new stock-triggered jobs, forge smelting, fueling, and import/export, or is completing already accepted jobs sufficient?
26. If unloaded-area support needs a tradeoff, would you prefer a carefully reconciled background simulation, keeping a limited base area active, or choosing after benchmark results? What CPU/memory budget or scale limit is acceptable?
27. When the game/server is shut down, should time stop or should it simulate elapsed real time on the next launch? In a player-hosted co-op game, is stopping when the host closes the game acceptable?
28. Should wireless access cover the base, expandable radio coverage, or eventually the whole map? Should it require handheld charge, controller power, a consumable, or per-operation costs? Do you also want device links between distant bases?
29. Should field access allow withdrawal and crafting, depositing loot, automatic pickup forwarding, or building/repairing from network materials? Which of these would make looting trips too easy?
30. Are block placement/breaking, farming, harvesting, salvaging, turret ammunition supply, and bulk fluid tanks part of the intended mod, or should some remain outside its scope? Rank any that matter.

## Interface, reliability, and delivery

31. Should the interface resemble vanilla 7 Days, a salvaged industrial terminal, or a more futuristic Refined Storage screen? What display resolution, UI scale, controller support, and languages should we target?
32. Which terminal actions are essential: search/categories, favorites, deposit matching items, deposit all except locked slots, exact quantity withdrawal, recipe preview, job monitoring, source-container lookup, or stock alerts? Should crafting also pull from the player's inventory?
33. How should damaged tools and weapons with different quality/mods appear: individually or grouped with an expandable picker? Which items should bulk deposit and automatic crafting always exclude?
34. What size should we design and benchmark for: connected containers, stored item variants/total quantity, simultaneous crafting jobs, stations, and distant bases? Would you prefer hard limits or server-configurable limits?
35. What recovery and removal behavior do you expect: owner-visible transaction history, admin recovery tools, backup/export, guaranteed safe mod upgrades, and a way to empty disks before uninstalling? Are test-save resets acceptable before the first stable release?
36. What should the finished first release be called, what visual assets can we use or create, and how should we prioritize new features against polish, multiplayer reliability, compatibility, and development time?
