# P3 in-game test guide (terminal crafting)

Use the latest build, installed in `Mods/RefinedStorageControl_Dev`, and restart the game after installing. Every request, job, collection and keepalive change is written to the game log, so note what looked wrong rather than copying values. `rsc craft jobs` (F1 console) shows countdowns, queued jobs, items waiting in stations, keepalive claims and banked XP at any time.

## Setup

1. Start a new Creative world. Recipes are unlocked in Creative, which keeps the first tests simple.
2. From the Creative menu take an Advanced Controller, a Hub, a Terminal, 3 Interfaces, a Storage Crate, a **Workbench**, a **Forge**, a **Campfire**, a Cooking Pot, a Land Claim Block, a Generator Bank, a Gas Can and the Wire Tool.
3. For ingredients, take 200 Wood, 50 Small Stones, 20 Iron (ore), 20 Clay Soil and 10 Murky Water. For fuel, take 50 more Wood.
4. Place the crate touching interface 1, the workbench touching interface 2, and the forge and campfire touching interface 3. A station counts when any of its blocks touches an interface.
5. Wire **Generator -> Advanced Controller -> Hub -> Terminal / Interfaces**, then fuel the generator and turn it on.
6. Open the terminal and **Store all**, so the ingredients are in the crate.

## Test 1: Craft mode

1. In the terminal, press **Craft** (bottom-right of the second button row). The grid switches to recipes and the button now reads **Storage**.
2. Each cell's number is how many crafts are possible right now; recipes you cannot afford show a dimmed 0.
3. Search, Category and Sort all work. Click a recipe: the line under the grid shows its ingredients and station, or what is missing.
4. Press **Storage** to go back to your items.

## Test 2: Workbench craft

1. Pick a workbench or backpack recipe you can afford, such as the Stone Axe or Wooden Club. Press **+** to set 3, then **Craft**.
2. The bar shows "3x ... in 5s" counting down. When it ends, a tooltip says "Crafting 3x ... at the Workbench". The ingredients are gone from storage.
3. Wait for the craft time. The finished items appear in storage by themselves (Storage mode) and the workbench output is empty. Your XP bar moves.
4. Console: `rsc craft jobs` shows "0 jobs" once it is done.

## Test 3: Cancel and power loss

1. Start a craft and press **Cancel** within 5 seconds. The status says "Cancelled ..." and storage is unchanged.
2. Start another craft and, within 5 seconds, turn the generator off. A tooltip says "Crafting cancelled (network has no power)" and nothing is taken.
3. Turn the generator back on.

## Test 4: Forge

1. Open the forge. Put Iron and Clay Soil in its inputs and Wood in its fuel, light it, and wait until its iron and clay units fill a little. Close it.
2. In terminal craft mode, **Forged Iron** shows how many the forge's own units allow. Craft 2: the forge uses its own units, not anything in the crate.
3. Let the forge run out of fuel. Forged Iron's line now says "the Forge has no fuel" and a request is refused.

## Test 5: Campfire tools

1. Without a Cooking Pot in the campfire, find **Boiled Water**: it says "needs Cooking Pot in the Campfire".
2. Put the pot and some Wood in the campfire, store the Murky Water, and craft 2 Boiled Water. It cooks and lands in storage.

## Test 6: Queue order, two stations, full storage

1. Queue something at the workbench by hand, then close it. Request a craft from the terminal: it runs **after** your manual job and never jumps ahead.
2. Place a second workbench touching an interface. Make two terminal requests in a row: the second goes to the workbench with the shorter queue.
3. Fill the crate completely, then craft something. The finished items wait in the workbench output (`rsc craft jobs` shows "N waiting in the station"). Free some room and they move into storage within a couple of seconds.
4. While a terminal job runs, open the workbench: the job shows in its queue and keeps crafting in the window as usual. Finished items stay in the output while it is open; close it and they reach storage within a couple of seconds.

## Test 7: Away from base (keepalive)

1. Place the Land Claim Block so its area covers the controller and the workbench.
2. Request a long craft (around 10 minutes of craft time). The log says "claim keeps the area active".
3. Fly or teleport at least 300 blocks away and wait. The log says "keepalive started for the claim at ...".
4. Come back after it is done: the items are in storage and the log says "keepalive released".
5. Repeat without the claim block: the job does not progress while you are far away; it catches up natively when you return.

## Test 8: Save and claim removal

1. Request a long craft, then save and quit. Reload the world: `rsc craft jobs` still lists the job, and it finishes and is collected once.
2. Request another long craft, then pick up the Land Claim Block while it runs. The log says the job stopped ("the land claim was removed"); the station keeps crafting by vanilla rules and its output stays there for you.

## Test 9 (optional): Co-op

With a second player:

1. The friend opens the terminal, uses Craft mode and makes a craft. Their countdown, tooltips and XP work, and the finished items reach storage.
2. The friend's craft uses items from a disk only if they have **Craft** permission on that disk (cabinet window).
3. The friend starts a craft and disconnects within 5 seconds: the request is cancelled and nothing is taken.
4. Check that items the friend crafts get **their** quality tier and perk costs, not the host's.

## Report back

Tell me which tests passed or failed, anything that looked wrong (layout, text cut off, buttons that did nothing, wrong counts), and any RSC errors in the game log.
