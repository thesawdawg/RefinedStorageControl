# P2 in-game test guide (digital storage)

Build `4aee7fa` or later, installed in `Mods/RefinedStorageControl_Dev`. Restart the game after installing. Every storage action is written to the game log, so results do not need to be copied by hand; just note what looked wrong.

## Setup

1. Start a new Creative world (a fresh world also gets a fresh `disks.dat`).
2. Creative menu, search **RSC** and **Storage Disk**. Take: Advanced Controller, Hub, Terminal, 2 Interfaces, 2 Drive Cabinets, two 1k disks, one 10k disk, plus a Generator Bank, a Gas Can, the Wire Tool and 2 Writable Storage Crates.
3. Place a crate next to each interface. Wire **Generator -> Advanced Controller -> Hub -> Terminal / Interfaces / Cabinets**. Fuel and turn on the generator.

## Test 1: Cabinet window and ownership

1. Look at a cabinet: the prompt says "E Open RSC Drive Cabinet (dev)". Press E.
2. Click slot 1, then click a **1k disk** in your backpack. Expect it in slot 1 and "Owner: <you>, Your access: owner".
3. Insert the second 1k disk and the 10k disk; check slot info (used / capacity).
4. Select a disk and **Take disk out**: it returns to your backpack. Put it back.
5. Console: `rsc disk list` shows each disk with tier, use, owner and cabinet/slot.

## Test 2: Disks as storage

1. Open the Terminal. **Store all** from your backpack.
2. Open the cabinet window: disk use has gone up (crates may also have taken some; both are fine at the default priority).
3. In the cabinet window, **Priority and filter**: set priority **10**. Interfaces stay at 50.
4. Store more items: they now go to the disks first. Take items back: they come from the crates first (highest number is emptied first).
5. Take a **damaged or modded** item that was stored on a disk: quality, durability and mods must be unchanged.
6. Fill a 1k disk past 1,000 items: extra items go to the next disk or crate, and nothing is lost.

## Test 3: Power and tier

1. Turn the generator off. Open the terminal: "network has no power".
2. Turn it back on: items reappear.
3. Optional: build a **simple** network with a cabinet: its disks are ignored (only crates show).

## Test 4: Pickup and destruction

1. Try to pick up a cabinet that has disks (hold E, Take): **"Remove the disks first"**.
2. Take all disks out, then pick it up: it works.
3. Put disks back in the other cabinet. Note the count with `rsc disk list`.
4. Destroy that cabinet (explosives or a lot of damage as the host). Expect some disks on the ground and the rest gone. The log line says "N disk(s) dropped, M lost".
5. Pick up a surviving disk and insert it into a new cabinet: its items are back.
6. Interface settings: set a filter on an interface, then destroy it and place a new interface on the same spot. Its priority is kept and the filter is kept about half the time. Picking an interface up instead clears its settings.

## Test 5: Recipes and gates (needs a non-creative world)

1. Start a normal world (or turn off creative). Open a **workbench**: the RSC recipes are listed but locked.
2. Open the Wiring 101 page (Skills > Crafting): "Storage Network" at 25, "Powered Storage" at 55, and one entry per larger disk.
3. Raise Wiring 101 by reading its magazines (creative menu or loot). At 25 the hub, interface and terminal unlock; the simple controller also needs Advanced Engineering rank 2. At 55 the cabinet and 1k disk unlock; the advanced controller also needs Advanced Engineering 5.

## Test 6 (optional): Co-op permissions

With a second player:

1. As owner, select a disk and **Add player** the friend: they get Deposit + Withdraw.
2. Friend: they see that disk's items in a terminal and can store and take.
3. Turn their **Withdraw** off: the disk's items disappear from their terminal; storing still works.
4. Turn **Deposit** off and Withdraw on: they can take but not store.
5. Friend tries **Take disk out**: refused without Configure. Give them Configure: now they can, and they can edit the list.
6. **Make owner**: ownership moves to the friend; you stay on the list with deposit + withdraw.
7. Friend picks up a loaded cabinet: the first try says "Checking the cabinet... try again", then "Remove the disks first".

## Report back

Which tests passed or failed, anything that looked wrong (layout, text cut off, buttons that did nothing), and whether the game log shows any XML or RSC errors at startup.
