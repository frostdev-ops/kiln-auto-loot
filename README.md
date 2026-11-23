# Kiln Auto Loot

Automatic looting for Enshrouded, as a [Kiln](https://github.com/frostdev-ops/kiln) mod. Walk through the world and the
mod picks up what lies around you, empties corpses and chests, harvests plants and ripe crops, and breaks barrels and
crates in ruins. Quest items stay where they are, your own storage is never touched, and nothing is broken inside a
base.

The looting runs on the server world: a dedicated server, or your own game while you host. So it works for every
player on a server that runs the mod, single player and co-op alike. The settings window, the toggle key and the short
notes run in each player's game.

## What it does
- **Picks things up around you** (radius 1-50 m, 15 m by default): loose things in the open (rocks, sticks, ore,
  mushrooms, dropped potions and arrows), finds you collect (fossils, urns, gnomes ...), what grows on plants
  (berries, herbs, fibre, reeds), the critters you use as fishing bait (fireflies, moths, grasshoppers, scarabs,
  pillbugs, frogs, maggots; never animals or pets), ripe crops from farm beds, and fish if you want them. Each group
  has its own switch and, if you like, its own radius. Ripe crops have two switches: out in the world, and in or near
  a base, so the fields in your base can stay as decoration.
- **Collects from your farm animals** (off by default, switch **Farm animal produce** on): the milk, wool and other
  produce your grown farm animals have ready, within your main radius, as the game's Collect does (with its pickup
  note). Babies are never grown up and no animal is petted: the mod takes only what waits in the animal.
- **Empties containers**: corpses of defeated enemies (bosses and elites too, or leave those to you), chests, coffins,
  cupboards and heaps out in the world. A fresh container waits a moment before it is opened (1 s by default), so the
  game has time to fill in its special loot.
- **Leaves things behind on purpose**: quest items stay in containers for you to take, and a skip list keeps any item
  you name (by name, part of a name, or a whole group) on the ground and in containers. A plant whose harvest would
  bring a skipped item is left alone. Old books lying in villages and ruins stay put unless you switch **Old books** on,
  and even then not in or near a base, where they may be part of the décor.
- **Smashes** barrels, crates, pots and wrecked furniture around you once a second, never in a base. Optional:
  furniture players could build (only well away from a base), bee hives, and, experimental, trees.
- **Bigger stacks**: every stackable item up to 9999 by default (at most 65535, 0 = the game's own sizes).
- **Pauses** while a game window is open (a chest, crafting, a dialogue, the inventory, the map, the pause menu), and,
  if you switch it on, while an enemy is near.
- **E is yours**: whatever the switches say, anything you press or hold E on is taken as in the game without the mod.
- **Profiles**: switch your whole setup in one go, in the window or with a key (give **Next profile** a key in Kiln's
  Keys page to cycle them; any profile can have its own key). Built in: Everything, Wood & sticks, Stone & ore,
  Plants & crops, Plunder, Treasure hunter; make your own from any setup.
- **Skip list or only list**: leave chosen items behind, or take nothing but the ones you list (exact items, parts of
  names, whole categories). A plant is harvested whole once anything it gives is wanted.
- **Gather focus**: take one item by hand, press **F11**, and only that item is collected around you until you press
  F11 again.
- **Rarity**: weapons, armor, rings and shields below a rarity you choose stay where they are.
- **Height window**: only what is within a few meters above or below you (4 m each by default), so a cave's loot stays
  in the cave and the cave doesn't empty the hill above it. Critters (fishing bait) are the exception: they hover above
  the ground, so they are taken anywhere within your main radius, above or below you.
- **Salvage** (new in 0.2.0): turn gear into runes, as many as the game's own Trash gives. In F8's Salvage tab, mark
  what can go (marked rows are red) and salvage it in one go, with a confirm that lists the items and the runes. Keep
  protects an item for good. Rules for new gear (off until you add one): weapons, shields, armor, rings or all gear up to
  a rarity and level, marked or salvaged at once, only for gear you pick up from the world from then on. Worn gear, the
  action bar, kept and quest items, gear with a gem and everything in chests are never touched. Give "Salvage marked
  gear" a key in Kiln's Keys page if you like (no key by default).
- **Bag-aware**: top up only the stacks you already carry, keep some bag slots free, or stop at a set amount of an item
  or a whole category ("500 wood logs").

## Using it
- **F8** opens the settings window; **F9** turns auto loot on and off; **F11** turns the gather focus on and off.
  Next profile has no key until you give it one. All keys can be changed in Kiln's Keys page.
- Every change is saved at once and takes effect at once, except switching the whole mod off and on (that needs the
  world to start again).
- Your own settings are in `kiln/config/kiln-auto-loot-client.json` (the active profile), your profiles in
  `kiln/data/kiln-auto-loot/profiles.json`, the world's settings in `kiln-auto-loot-server.json`.
- When you host, the World tab sets the stack size, the backpack cheat and the limits for your players. On a dedicated
  server those come from the server's file, and the server can cap what each player chooses (the window shows it).
- Players who join without the mod get the server's default settings.

## Good to know
- **Salvage** cannot be undone. Each salvaged item gets a line in `kiln/logs/kiln.log`. The runes follow the game's own
  Trash formula; the mod checks it against a Trash by hand and switches salvage off in that world if they differ.
  A player who leaves or crashes in the moment between an item's take and its runes may, on a server, come back with
  both (the character file and the world are saved apart); the item is never lost.
- **Stacks**: if you remove the mod or lower the stack size, the game cuts every stack above its own size back the next
  time it handles it, and the rest is lost. Split big stacks first.
- **Felled trees** may not grow back. Try tree felling in a test world, or back up your save.
- **Pulling items in**: with "take things where they lie" off, there is no flying-to-you animation (the game draws that
  in the player's own game); things are still picked up.
- **Line of sight** ("only take what I can see") needs Kiln's line-of-sight checks, which a hosting game does not have
  yet in 0.2.0-dev.28; the window says so. The height window covers caves meanwhile.

## Requirements
Kiln 0.2.0-dev.30.6 or later, on the server and (for the window, the keys and the notes) in each player's game.

## Building
```sh
ln -s <Kiln release>/sdk kiln-sdk            # the release's sdk/ folder
dotnet build                                 # build
dotnet test tests/Offline                    # the offline tests
dotnet build -c Release -t:KilnPack -p:KilnPackageOutputPath=dist/   # the zip the Kiln Launcher's Library imports
```
`tests/LabTest` and `tests/LabSmash` are test mods for a Kiln lab server (`kiln test`).

## Credits
Salvage: ideas by **netjunk** (rules for new pickups, mark and salvage in bulk, the confirm) and **HadToRegister**
(marked items shown in red, the optional "Are you sure?", the Salvage chest still to come).

Inspired by **Auto Loot Plus** by cw (Nexus Mods, Enshrouded mod 170), whose open source showed how automatic looting
can work in Enshrouded and served as our guide for its behaviour.

## Licence
MIT, see `LICENSE`.
