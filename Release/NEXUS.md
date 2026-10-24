# Nexus Mods page draft

Draft only. Publishing is a separate step that needs the owner's go-ahead (ENV-05, RSC-21). Placeholders in [brackets] need final answers before upload.

## Basics

| Field | Value |
|---|---|
| Name | Refined Storage Control |
| Game | 7 Days to Die |
| Game version | 3.3.0 b18 |
| Mod version | [from ModInfo.xml at release] |
| Category | [Gameplay / Items and objects] |
| Requirements | EAC off; installed on host and every client |
| Permissions | [owner to decide: reuse, conversions, assets] |
| Includes | Harmony (MIT), with its license file |

## Short description

A wired storage network for 7 Days to Die: reach every connected container and storage disk from one terminal, and craft at connected workbenches, forges and campfires without carrying ingredients around.

## Description

Inspired by Minecraft's Refined Storage and Simple Storage Network.

**Build a network with the wire tool**
- A storage controller, hubs, interfaces, terminals and drive cabinets, wired like any electrical device.
- The simple controller runs without power; the advanced controller uses electricity, moves items faster and unlocks storage disks.

**Use the storage you already have**
- Any of your containers touching an interface joins the network and keeps its lock and access rules.
- Give interfaces a priority and an allow/block filter so items go where you want.

**Storage disks**
- Six disk sizes from 1,000 to 1,000,000 items, installed in drive cabinets.
- Each disk has an owner and a per-player access list: deposit, withdraw, craft, configure.
- Disks keep exact items: quality, durability and mods stay intact.
- A destroyed cabinet may lose some of its disks, so protect your base.

**One terminal for everything**
- Search, categories, sorting and favorites.
- Store your whole backpack, or only items the network already holds.

**Craft from the terminal**
- Queue recipes on a connected workbench, forge or campfire with your own perks and quality.
- Use a five-second countdown to cancel before anything is taken.
- Finished items go straight back into storage.
- With a land claim, your base keeps crafting while you are away.

**Progression**
- Unlocked through Wiring 101 and Advanced Engineering.
- Crafted at the workbench from vanilla materials.

**Configurable:** transfer rates, survival chances, the crafting countdown and more, in `Settings.xml`.

## Installation

See the README included in the download (install, settings, backup, update, uninstall).

## Known limitations

- [Placeholder block models and icons until final art is done.]
- Dedicated servers, other operating systems and EAC are not supported in this release.
- After a crash, items moved between containers and disks since the last save may be lost or duplicated, because the game and the mod save separately.
- Removing the mod loses everything stored on disks.

## Files to upload

- `RefinedStorageControl-<version>.zip` containing the `RefinedStorageControl` folder from `Artifacts/` (built by `Tools/Build.ps1`).

## Screenshots needed

Terminal (storage mode), terminal (craft mode), drive cabinet window, a wired base, Wiring 101 page.
