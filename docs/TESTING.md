# Testing Kiln Auto Loot in the game

The offline tests (`dotnet test tests/Offline`) and the lab test mods (`tests/LabTest`, `tests/LabSmash`) cover the
rules and what a dedicated server can show without a player. Everything below needs a real player in a real world.

## Setup
1. Back up your save (stack sizes and felled trees change it).
2. `dotnet build -c Release -t:KilnPack -p:KilnPackageOutputPath=dist/`, then import `dist/kiln-auto-loot-<version>.zip`
   in the Kiln Launcher's Library and switch it on in your profile (Kiln 0.2.0-dev.28 or later).
3. Set `"counterLog": true` in `kiln-auto-loot-server.json` (it is not in the F8 window): in the Kiln Launcher, Kiln
   Auto Loot > Configs > kiln-auto-loot-server, or by hand in your profile's config folder (`kiln/config/` without a
   profile). The mod then writes its counters every 5 s to `kiln/data/kiln-auto-loot/kiln-auto-loot.log`, from the
   moment Kiln picks the change up (also while the game runs), and its whole classification (the classification dump,
   same folder) when a world starts with it on.
4. Host a world (a test world, or a copy) and walk somewhere with loot: a ruin with barrels, a camp, bushes, a farm.

## 1. Window and keys
- [ ] F8 opens "Kiln Auto Loot <version>" with six tabs; switching tabs keeps the width; F8, Esc, the X and Close all
      close it; Esc does not also open the pause menu; while it is open the camera and movement don't react.
- [ ] The status line says whether auto loot is on and names the F9 key; F9 flips it with a short note, also with the
      window open. Rebinding F8/F9 in Kiln's Keys page works and the texts name the new key.
- [ ] Every setting has a tooltip; changes save at once (`kiln-auto-loot-client.json`); editing that file by hand
      updates the open window.
- [ ] With the game in German: the window, the notes and the item names are German and nothing is cut off.
- [ ] The Filters tab right after loading: the item list fills by itself (no "still loading" that stays).

## 2. Loose things
- [ ] Rocks, sticks, ore, mushrooms and dropped items within the radius are picked up without pressing E; each thing
      once (no doubled amounts on stones, mushrooms, fireflies).
- [ ] Each group switch (ground loot, stones, collectibles, plants, crops, fish) stops its group at once; pressing or
      holding E still takes a switched-off thing, also beyond its group radius and with auto loot off (F9); the radius
      slider and a group radius larger than the general one both work.
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
- [ ] Berry bushes, herbs, fibre, reeds and fireflies are harvested, each once per visit.
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
- [ ] Barrels, crates, pots and broken furniture in ruins break once a second and their loot is picked up.
- [ ] Inside your base nothing breaks; near your base buildable furniture stays whole.
- [ ] "Fell trees" on (test world!): trees, stumps and logs come down and drop wood; harvest trees (hazelnut, coffee)
      stay. "Smash bee hives": hives break, honey is picked up.
- [ ] "Stones" off: rock boulders stay whole.
- [ ] Chandeliers, telescopes, showcases, doors, ladders and anything of a quest stay whole.

## 7. World tab (hosting)
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
- [ ] Height: standing on a hill above a cave, nothing in the cave is taken or smashed; inside the cave, nothing on the
      surface above. 0 = no limit.
- [ ] Fill carried stacks: with 30 of 50 sticks in your bag, a pickup of 15 is taken, one of 25 stays; from a chest of
      40 sticks, 20 move and 20 stay.
- [ ] Keep 2 free slots: when only 2 are left, only stacks you carry are topped up.
- [ ] Carry limit "=<logs>:100" (from the editor): the mod stops at 100 logs.
- [ ] A broken `profiles.json` (edit it badly by hand) does not stop the game; it is kept as `profiles.json.bad`.

## 9. Optional: a dedicated server
- [ ] Join a server with the mod: your settings apply there, the World tab is read-only with the server's values, a
      skip list with English or German names works, opening a chest pauses only you.
- [ ] A player without the mod gets the server's defaults.

## What to send back
`kiln/logs/kiln.log`, `kiln/data/kiln-auto-loot/kiln-auto-loot.log` and the classification dump, plus a note of which
boxes failed.
