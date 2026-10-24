# Refined Storage Control

A storage-network mod for 7 Days to Die 3.3.0 b18, inspired by Minecraft's Simple Storage Network and Refined Storage. Windows PC, solo and player-hosted co-op (up to six players), EAC disabled.

Centralized storage access is the primary goal. v1 covers wired physical storage, powered digital disks, and manual terminal crafting through native stations. Pattern automation and tiered crafters are planned for v2.

## Status

P1 (physical storage), P2 (digital disks) and P3 (terminal crafting) are built and host-tested in game; co-op and the full acceptance run are still to do. P4 (verification and release preparation) is in progress. Blocks still use placeholder models and "(dev)" names.

## What it does

- **Network:** a controller, hubs, interfaces, terminals and drive cabinets joined with the vanilla wire tool. A simple controller needs no power and moves 2 stacks per second; an advanced controller needs power, moves 20 per second and enables disks.
- **Physical storage:** any player-placed container touching an interface joins the network, with its normal lock and access rules. Interfaces have a priority and a deposit filter.
- **Digital storage:** disks from 1k to 1m items go into drive cabinets, with an owner and a per-player access list (deposit, withdraw, craft, configure).
- **Terminal:** search, categories, sorting and favorites; take and store from one window.
- **Crafting:** the terminal's Craft mode queues recipes on a workbench, forge or campfire touching an interface, using the station's own rules. Finished items return to storage, and a land claim keeps the base running while you are away.

## Install

1. Turn off EAC: in the launcher, choose to start the game without EasyAntiCheat.
2. Copy the `RefinedStorageControl` folder into `<7 Days To Die>/Mods/`, so that `Mods/RefinedStorageControl/ModInfo.xml` exists.
3. In co-op, the host and every player need the same version installed.
4. Start a new world. Existing saves load, but the mod is tested only on new ones.

## Settings

`Mods/RefinedStorageControl/Settings.xml` holds the transfer rates, the disk and filter survival chances, the crafting countdown and largest request, the lifetime of dropped items, and the journal size. Each entry explains its range. The host's file decides in co-op. A bad value falls back to its default and is reported in the game log; `rsc settings` in the F1 console shows the values in use.

Block power use, recipes and unlock levels are ordinary game XML in `Config/blocks.xml`, `Config/recipes.xml` and `Config/progression.xml`.

## Backup and recovery

The mod keeps its world data next to the save, in `<save folder>/RefinedStorageControl/`:

| File | Holds |
|---|---|
| `disks.dat` | every disk and its contents, and which cabinet slot each disk is in |
| `nodes.dat` | interface and cabinet priority and filter |
| `jobs.dat` | terminal crafting jobs in progress and XP waiting for its player |
| `journal.log` (+ `.1`) | a readable record of transfers, disk changes, crafting and admin commands |

Each `.dat` file keeps a `.bak` of the previous save. Back up the whole save folder, including this subfolder, while the game is closed.

- **Disk export:** `rsc disk export <id>` (or `all`) writes a readable list of a disk's owner, access list and items to `RefinedStorageControl/export/`. It is a record only; nothing is imported automatically.
- **Journal:** `rsc journal [lines] [text]` shows recent events, for example `rsc journal 50 craft`.
- **After a crash:** each file returns to its last save independently, so items moved between containers and disks after the last save may be lost or duplicated. Saving before risky actions limits this.
- **Unreadable files:** an unreadable file is renamed with an `.unreadable-<time>` suffix and the world starts with defaults instead of overwriting it.

## Update

Close the game, save a copy of your `Settings.xml` if you changed it, replace the mod folder, then put your settings back. The world files are versioned and read by newer builds.

## Uninstall

Remove `Mods/RefinedStorageControl`. Worlds that used the mod lose its blocks and disk items, and **everything stored on disks is lost**. Move items back into ordinary containers first. Items in containers stay where they are.

## Console commands (F1)

| Command | Use |
|---|---|
| `rsc net list x y z` / `rsc net config x y z` | items reachable through the network of a block; an interface's priority, filter and containers |
| `rsc disk list` / `rsc disk owner <id> <player>` / `rsc disk export <id\|all>` | disks, ownership, export |
| `rsc craft jobs` | crafting countdowns, jobs, keepalive and banked XP |
| `rsc journal [lines] [text]` | recent events |
| `rsc settings` | values in use |

## Building from source

`pwsh ./Tools/Build.ps1` builds, runs the test harness and packages `Artifacts/RefinedStorageControl`; `-Install` also copies it to `Mods/RefinedStorageControl_Dev`. See [AGENTS.md](AGENTS.md).

## Planning documents

- [Specification](SPECS.md): requirements, release boundaries, acceptance scenarios and open decisions.
- [Overview TODO](TODO.md): milestone checklist.
- [Design and decision record](RefinedStorage-Design.md): the source of truth for decisions.
- [P0 findings](P0-FINDINGS.md), and in-game test guides for [P2](P2-TEST-GUIDE.md) and [P3](P3-TEST-GUIDE.md).
- [Original research](RefinedStorage-Research.md) and [answered questionnaire](RefinedStorage-Questions.md), kept for history.
