# Testing Kiln Auto Loot in the game

The offline tests (`dotnet test tests/Offline`) and the lab test mods (`tests/LabTest`, `tests/LabSmash`) cover the
rules and what a dedicated server can show without a player. Everything below needs a real player in a real world.

## Setup
1. Back up your save (stack sizes and felled trees change it).
2. `dotnet build -c Release -t:KilnPack -p:KilnPackageOutputPath=dist/`, then import `dist/kiln-auto-loot-<version>.zip`
   in the Kiln Launcher's Library and switch it on in your profile (Kiln 0.2.0-dev.30.6 or later).
3. Set `"counterLog": true` in `kiln-auto-loot-server.json` (it is not in the F8 window): in the Kiln Launcher, Kiln
   Auto Loot > Configs > kiln-auto-loot-server, or by hand in your profile's config folder (`kiln/config/` without a
   profile). The mod then writes its counters every 5 s to `kiln/data/kiln-auto-loot/kiln-auto-loot.log`, from the
   moment Kiln picks the change up (also while the game runs), and its whole classification (the classification dump,
   same folder) when a world starts with it on.
4. Host a world (a test world, or a copy) and walk somewhere with loot: a ruin with barrels, a camp, bushes, a farm.

## 1. Window and keys
- [ ] F8 opens "Kiln Auto Loot <version>" with eight tabs (Salvage is new); switching tabs keeps the width; F8, Esc, the X and Close all
      close it; Esc does not also open the pause menu; while it is open the camera and movement don't react.
- [ ] The status line says whether auto loot is on and names the F9 key; F9 flips it with a short note, also with the
      window open. Rebinding F8/F9 in Kiln's Keys page works and the texts name the new key.
- [ ] Every setting has a tooltip; changes save at once (`kiln-auto-loot-client.json`); editing that file by hand
      updates the open window.
- [ ] With the game in German: the window, the notes and the item names are German and nothing is cut off.
- [ ] The Filters tab right after loading: the item list fills by itself (no "still loading" that stays).
- [ ] Start-up: in `kiln/logs/kiln.log`, `mod kiln-auto-loot <version> loaded in` takes well under a second, and the
      `game data:` line comes a moment later. After a Kiln or game update it says `read from the game ... on a worker`,
      followed by `cache ... written on a worker`; on later starts it says `cache`.

## 2. Loose things
- [ ] Rocks, sticks, ore, mushrooms and dropped items within the radius are picked up without pressing E; each thing
      once (no doubled amounts on stones, mushrooms, fireflies, frogs).
- [ ] Each group switch (ground loot, stones, collectibles, plants, critters, crops in the world, crops in a base, farm
      animal produce, fish) stops its group at once; pressing or holding E still takes a switched-off thing, also
      beyond its group radius and with auto loot off (F9); the radius slider and a group radius larger than the general
      one both work.
- [ ] Old books the world placed (village and ruin furniture that gives Old Books) stay where they are with "Old
      books" off (the default). With it on they are picked up away from any base but stay while you stand in or near
      one, also with a base built around them. E takes them either way.
- [ ] "Take things where they lie" off: things are still picked up (no fly-in), mushrooms and fireflies only right
      next to you.
- [ ] Inventory full: the "inventory full" note appears (at most once a minute) and nothing stutters while loot lies
      around.

## 3. Containers
- [ ] A fresh corpse waits about a second, then empties; elites and bosses bring their bonus loot (jewels, runes).
- [ ] The piles creatures leave are taken: skeleton hound and skeleton bones (also in a Hollow Halls), bat fur,
      critter parts.
- [ ] "Bosses" off: boss, mini boss and elite corpses stay for you (try a Fogger Berserker or Heavy Mage corpse too).
- [ ] World chests, coffins, cupboards and heaps empty; your own chests and locked chests are never touched.
- [ ] Quest items (map pieces, lore scrolls, instruments, eternal ammunition, the farmer hat) stay in containers while
      "leave quest items" is on, and come along when it is off.
- [ ] While a chest, crafting station, dialogue, the inventory, the map or the pause menu is open, nothing is taken
      and the open window never closes or switches by itself; with a controller too.
- [ ] "Pause near enemies": nothing is taken while an enemy is close, it carries on about 2 s after the last one.

## 4. Plants and crops
- [ ] Berry bushes, herbs, fibre and reeds are harvested, each once per visit.
- [ ] Critters (fishing bait): fireflies at night, frogs in the wetlands, maggots, moths, grasshoppers, scarabs and
      pillbugs are picked up as you walk past (no need to stand still), with "Plants" off too; "Critters" off leaves
      them. Animals and pets around you are never touched. kiln.log has one "critter:" line per swarm: "loot sent", or
      why it was left; a "still there ... did not hand the bait over" line means the game refused the take.
- [ ] "Ripe crops in a base" off: ripe beds in your base stay while you stand in or near it; ripe crops out in the
      world are still harvested. "Ripe crops in the world" off and "in a base" on: the other way round.
- [ ] 0.2.6: "Ripe crops in a base" off also leaves planted algae, wheat and pumpkins in your base (they grow into the
      world's own plants); the same plants out in the world still follow "Plants and bushes" (pumpkins: ground loot).
- [ ] Settings from 0.2.2 carry over: with "Ripe crops" off before, both crop switches are off after the update; with
      "Plants" off before, "Critters" is off too (kiln.log: "carried over from the setting it replaces").
- [ ] Farm beds: only ripe crops; a bed in an early stage (Nettle, Rooibos, sugar cane, plant fibre at stage 1) is left
      to grow.

## 5. Skip list
- [ ] Type 3 letters: suggestions in the window's language; Enter takes the first; a group can be skipped whole and
      taken again; entries show their x; "Clear the list" asks once more ("Keep it" in its place, "Really clear all
      N?" beside it) and then clears; Undo brings the list back, also after closing and opening the window; an unknown
      "=Name" is marked.
- [ ] A skipped item stays on the ground and in chests and corpses, with a short note (at most 3 times per item); a
      bush that would give a skipped item is not harvested; pressing or holding E on it takes it.

## 6. Smashing
- [ ] Barrels, crates, pots and broken furniture in ruins break, up to four a second, nearest first, and their loot is
      picked up. 0.2.12: in a ruin with a room next door or a cellar below full of props, those in view still break
      within a few seconds; so do barrels on a stack or a shelf; nothing behind a wall or under the floor breaks. With
      two players each in a ruin like that, both get smashing.
- [ ] Inside your base nothing breaks; near your base buildable furniture stays whole.
- [ ] "Fell trees" on (test world!): trees, stumps and logs come down and drop wood; harvest trees (hazelnut, coffee)
      stay. "Smash bee hives": hives break, honey is picked up.
- [ ] "Stones" off: rock boulders stay whole.
- [ ] F9 (auto loot off) with "Smash things around me" still on: nothing breaks around you (0.1.7 kept smashing);
      F9 again: smashing comes back.
- [ ] Chandeliers, telescopes, showcases, doors, ladders and anything of a quest stay whole.

## 7. World tab (hosting)
- [ ] In the main menu, before loading a save: F8, World tab says no world is running and its fields can be changed
      (0.1.6 left them read-only, "Set by the server you play on."); change the stack size there, then load a save:
      `world up ... maxStack=<the new value>` in kiln.log and the World tab shows it in the world.
- [ ] In the main menu: switch a Looting switch in F8, quit the game from the main menu, start it again: the switch kept
      its new value. The skip list's item picker lists items in the main menu.
- [ ] Stack size 9999 (default): wood and stone stack to 9999; 0 brings back the game's sizes; the number field and the
      -/+ buttons work.
- [ ] Backpack cheat on: every backpack recipe gives the best backpack for 1 of each ingredient; off restores them.
- [ ] Lowering "furthest radius" to 20 shows that the server caps your 40 m radius.
- [ ] "enabled" off, back to the menu, host again: the mod does nothing in that world; on again, it is back.

## 8. Profiles, lists, focus and the bag
- [ ] Next profile has no key at first (F10 does nothing here, so another loader's F10 window opens; the profile
      combo's tooltip says to set one on Kiln's Keys page); once given one there, it cycles the profiles with a short
      note; the profile bar shows the active one; New, Rename (with a key such as Ctrl+1, which then switches to it),
      Delete (asks twice) and Reset on a built-in work.
- [ ] "Wood & sticks" takes only logs and sticks (twig bushes are harvested whole, their fibre comes along); "Stone &
      ore" only stone, flint and ore; "Plunder" only corpses and chests; "Treasure hunter" leaves gear below Rare.
- [ ] Only list: "Only this" on an item switches to it; the skip list is kept and comes back when you switch to skip.
- [ ] Gather focus: take a stick by hand, press F11: "Gathering only: ..."; only sticks are collected; F11 again ends
      it; F11 before taking anything by hand shows the hint.
- [ ] Line of sight (Only what I can see, on by default): standing on a hill above a cave, nothing in the cave is taken
      or smashed; inside the cave, nothing on the surface above; a corpse down a slope in plain view is taken.
- [ ] Height (the fallback: switch Only what I can see off, set Depth below me 4): nothing more than 4 m below is taken
      or smashed. 0 = no limit.
- [ ] Fill carried stacks: with 30 of 50 sticks in your bag, a pickup of 15 is taken, one of 25 stays; from a chest of
      40 sticks, 20 move and 20 stay.
- [ ] Keep 2 free slots: when only 2 are left, only stacks you carry are topped up.
- [ ] Carry limit "=<logs>:100" (from the editor): the mod stops at 100 logs.
- [ ] A broken `profiles.json` (edit it badly by hand) does not stop the game; it is kept as `profiles.json.bad`.

## 9. Optional: a dedicated server
- [ ] Join a server with the mod: your settings apply there, the World tab is read-only with the server's values, a
      skip list with English or German names works, opening a chest pauses only you.
- [ ] A player without the mod gets the server's defaults.

## 10. Salvage (0.2.0)
Use a test world or a copy: salvaged gear is gone. Grep `kiln/logs/kiln.log` for `salvage`.
- [ ] World start: `salvage: yield data seed 8, creep 0.15, trash 5..20, factors 1/0.75/0.5, rune item <id>`.
- [ ] **Calibration first:** trash one upgradeable piece of gear by hand (the game's item menu). kiln.log:
      `salvage check: <name> trashed by hand gave N runes, the formula said N: match`. Try a few: a common level-1
      weapon, an epic armour piece, a ring, one with an upgrade unlocked. Any `MISMATCH` line: salvage turns itself off
      in that world (the tab says so); send the line.
- [ ] F8, Salvage tab: the backpack's gear is listed with rarity, level and runes; worn gear and the action bar are not
      listed; gear with a gem shows "has a gem" and has no Mark button.
- [ ] Mark two items: their rows turn red and go to the top; "Salvage marked (2, N runes)" appears; Unmark works;
      "Clear marks" clears all.
- [ ] Keep an item: it shows "kept", has no Mark button, and a rule never touches it. Click Kept again to free it.
- [ ] "Salvage marked": the confirm lists the items and the runes, "No" sits where the button was and cancels; "Yes":
      the items are gone, the runes are in the backpack (with the game's "+N" note), the tab says
      "Salvaged 2: +N runes", and kiln.log has one `salvage: player ... salvaged <name> ... -> N runes` line per item.
- [ ] Switch "Ask before salvaging" off: the button salvages at once. Switch it back on.
- [ ] Full backpack with no room for runes: nothing is taken ("no room for the runes").
- [ ] Rules: add "Weapons up to Uncommon" (action Mark): kill an enemy or open a world chest that drops a common
      weapon: it is marked (red) in the tab; kiln.log `salvage: rule marks ...`. Gear moved in from your own chest,
      a crafted weapon and what you already carried stay unmarked. Action "Salvage it at once": the next such drop is
      salvaged straight away (`rule salvages`).
- [ ] Key: give "Salvage marked gear" a key in Kiln's Keys page (no default): it opens the confirm.
- [ ] Save and quit right after a salvage, load again: no item back, no runes twice; kiln.log shows no "open salvage"
      or says how they were settled.
- [ ] World tab, "Salvage allowed" off: the tab says the server switched it off.
- [ ] Joined (dedicated server with 0.2.0): the same list and buttons work; a server on 0.1.x: the tab says the server
      does not run this version.

### For netjunk and HadToRegister (test build)
1. Back up your save. Import the zip in the Kiln Launcher (needs Kiln 0.2.0-dev.29.18 or later on both sides).
2. Trash one piece of gear by hand first, then send the `salvage check` line from `kiln/logs/kiln.log`.
3. F8 > Salvage: mark a few pieces, salvage them, check the runes. Try a rule for new pickups (Mark first).
4. Not in this build: red slots in the game's own inventory, a Salvage button in the chest screen, the Salvage chest
   (they need Kiln changes or come next); the tab's list stands in for them.

## What to send back
`kiln/logs/kiln.log`, `kiln/data/kiln-auto-loot/kiln-auto-loot.log` and the classification dump, plus a note of which
boxes failed.
