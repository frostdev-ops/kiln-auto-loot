# Changelog

## Unreleased
- **Farm animal produce** (new switch, off by default): the milk, wool and other produce your grown farm animals
  (goats, yaks, ...) have ready is collected within your main radius, the same way the game's Collect does, with the
  game's own pickup note. The mod takes only what waits in the animal: it never presses E on an animal, so babies are
  never grown up and nothing is petted. The skip list, the only list and the bag rules apply as for chests. Animals are
  looked at once every 5 s (they refill in minutes). On a server, only players whose user group may access inventories
  (canAccessInventories) get produce, the right the game asks for at a chest.
- Built for Kiln 0.2.0-dev.30.6.

## 0.2.6
- **No more multi-second freezes when you arrive somewhere new** (reported by HadToRegister): the first auto-loot look
  around after a fast travel (Flame Altar, with the map open for a while before) held the server for 5.3 s on a slower
  PC, and walking into a village full of props held it 3.6 s while the mod looked for things to smash. The mod now
  spends at most about 4 ms of a server step on its look around: what it has not looked at yet (a whole new place at
  once) is looked at in the next steps, nearest first, so nothing near you waits. Smashing looks at the nearest props
  first and stops once it has the four it hits; a prop's type is read once and kept while it stays in reach.
- If a step is slow anyway, kiln.log says so (at most once a minute): a "slow scan" or "slow smash" line with the time,
  which part took it (finding things around you, reading a new thing's type from the game, the loot rules) and the
  counts. Please send kiln.log if you see one.
- The timing line of counterLog says how many server steps the slowest scan was spread over (steps=).
- **Rules for new gear see locked chests and camp junk piles as world loot** (reported by netjunk): gear from a
  locked world chest you open with a key or lockpicks, and from the searchable heaps in camps and mines (scavenger and
  Vukah trash heaps, wooden junk piles, old tools), was left as "came from a tombstone or a chest that is not world
  loot". Auto Loot itself still leaves these to you (it never opens a locked chest or a heap); only the rules now count
  what you take from them.
- **"Ripe crops in a base" now covers everything you plant there** (reported by netjunk): planted algae was still
  harvested in a base with the switch off. Algae, wheat, pumpkins, papyrus, daylilies, cattails, reeds, taro, ginger,
  shiitake, beans, thyme, pomegranates and the bushes you can plant grow into the same plants the world has, so the
  mod saw them as wild plants ("Plants and bushes", pumpkins as ground loot) and never asked the base switch. In or
  near a base they now count as crops: "Ripe crops in a base" and the crop radius decide. Out in the world nothing
  changes for them.

## 0.2.5
- Fishing bait above or below you is picked up within your main radius; the height limits apply to everything else.

## 0.2.4
- kiln.log now says what happened to each critter (fishing bait swarm: fireflies, moths, grasshoppers, scarabs,
  pillbugs, frogs, maggots) you pass: one line each with its name, distance and height, and either "loot sent" or why
  it was left (Critters off, beyond the main radius, outside the height window, skip list, only list, gather focus, a
  bag rule, not in plain view). If a swarm is still there 2 seconds after its take was sent, one more line says the
  game did not hand the bait over. A bait swarm the mod's table does not know is named once too. At most 200 such
  lines per world. The counter line (counterLog) counts critters as seen/sent/still there.
- kiln.log's "world up" line names the right version again (0.2.3 still said 0.2.2).
- Nothing else changes: this build is for finding out why a player still sees fishing bait left on 0.2.3. Please send
  kiln/logs/kiln.log after walking past some frogs, maggots or fireflies.

## 0.2.3
- **Critters**: fireflies, moths, grasshoppers, scarabs, pillbugs, frogs and maggots (the fishing bait you catch) are
  picked up as you walk past, with their own switch "Critters (fishing bait)", on by default (asked by Tyrion67). The
  mod knew them before but treated them like bushes: it pressed "accept" first and waited up to 3 seconds per try
  before taking them, so you had walked on before they came. They are now taken at once, like mushrooms, which they
  are built like. Only the bait swarms count: animals, pets, farm animals, thrown animal bait and quest things never.
  They use the main radius, and the skip list, only list and gather focus see them as the bait they give.
- **Ripe crops** has two switches now (asked by netjunk and ramabbott): "Ripe crops in the world" and "Ripe crops in
  a base" (while you are in or near a base, where fields may be decoration). Both are on by default; switch the base
  one off to leave your fields to you and E. The Ranges page's crop radius is for both.
- Your settings carry over: whatever "Ripe crops" was before goes to both crop switches, and "Critters" starts as
  "Plants" was (they were part of plants before). This goes for your settings file, your profiles, the server's
  defaults for players without the mod, and a player's game that is still on 0.2.2.
- "Plants and bushes" no longer lists fireflies: they are critters now.

## 0.2.2
- Rules for new gear see what you loot from the world, whatever way it comes. 0.2.1 only took gear whose source the
  mod had watched being opened in the last 3 seconds, so most loot was left ("its source ... is no world loot the mod
  saw"): chests in ruins and dungeons emptied by hand, loot bags, items that spill out as pickups, enemy drops. Now
  every source counts except crafting, another player (trades and gifts), and any tombstone or chest that is not world
  loot (a chest you built, a station): those stay out, also after a tombstone is gone.
- New gear that lands on the action bar is marked by a rule too (it stays there unsalvaged; the mark shows once you
  move it into the backpack). Before, the rule skipped it.
- Grenades and other gear without upgrades no longer fill kiln.log with "rules leave" lines.
- Salvage tab: the Mark and Keep buttons come first in each row, so a long name no longer pushes them out of reach.
  New "Mark all" marks every listed piece that may be salvaged (never kept, worn or quest items, nor gear with a gem);
  the confirm still lists everything before anything is salvaged.

## 0.2.1
- Salvage works for the player who hosts the world. In 0.2.0 "Salvage marked" did nothing there and said "1 skipped
  (moved or changed)": the game records no Steam id for the hosting player (only for players who join over the
  network), and the mod would not salvage for a player it could not name in its ledger. It now names the host, and a
  player without a Steam id by their account id; when neither is known the tab says so instead of "moved or changed".
- Rules for new gear now see what Auto Loot itself takes out of corpses and chests. In 0.2.0 they only saw gear that
  came with the game's pickup note (a loot-all, a take by hand), and most gear arrives through Auto Loot's own slot
  moves, which bring no such note. kiln.log now also says why a rule left a piece of gear ("no rule takes ...").
- Rules also see gear you take out of a world chest yourself, in its screen (dragging an item, Take all): that comes
  with no pickup note either. When you open a world chest or other world loot, the mod notes the gear in it and finds
  it in your backpack afterwards (for 2 minutes). Gear from your own chests, a tombstone, crafting or trades is still
  never touched, nor gear you carried before and put into the chest yourself.
- The level slider of a rule saves when you let it go, not at every step of the drag.
- kiln.log has one line per salvage request: how many were salvaged and skipped, and why.

## 0.2.0
- **Salvage**: turn gear into runes from F8's new Salvage tab (ideas by netjunk and HadToRegister). Mark items (their
  rows turn red), then "Salvage marked" with a confirm that lists the items and the runes ("Ask before salvaging", on by
  default). Keep protects an item from salvage and rules. The runes are what the game's own Trash gives (its formula,
  read from the game's data and settings); the first Trash you do by hand is compared with it in kiln.log, and salvage
  switches itself off in that world if they differ.
- **Rules for new gear** (none by default): weapons, shields, armor, rings or all gear up to a rarity and level are
  marked, or salvaged at once, when you pick them up from the world (corpses, world chests, the ground). Never what you
  already carry, gear from your own chests, tombstones, crafting, worn gear, kept or quest items, or gear with a gem.
- A salvage stays whole across a save: a ledger saved with the world settles an item taken just before a save (no item
  back, no runes twice; runes that did not fit are given once there is room).
- New key "Salvage marked gear" with no key by default; new world limit "Salvage allowed".
- Needs Kiln 0.2.0-dev.29.18 or later. The channel version is 3: a game with 0.1.x on a 0.2.0 server (or the other way
  round) gets the server's defaults, as without the mod.
- Not yet: red slots in the game's own inventory, a Salvage button in the chest screen, and the Salvage chest; they
  need Kiln changes.

## 0.1.8
- F9 stops smashing too: with auto loot off, barrels, crates, pots, trees and hives around you are left alone, whatever
  the smashing switches in F8 say. Before, F9 only stopped the pickups and things kept breaking around you. F9 again
  brings smashing back as you had it.

## 0.1.7
- The World tab can really be changed in the main menu now: in 0.1.6 it stayed read-only there ("Set by the server you
  play on."). Kiln runs a mod's queued work only while a world runs, and the step that opens the tab for editing was
  queued; on the title screen the mod now runs it itself, each frame.
- Changes in the F8 window in the main menu (switches, lists, profiles) are saved at once. Before, they waited for the
  next world, and quitting from the main menu lost them.
- The F8 item list fills in the main menu again (0.1.6 waited for a world to start).

## 0.1.6
- The game no longer waits for Kiln Auto Loot while it starts: the mod reads the game's data, sorts it and writes its
  cache in the background. On a hard disk this held the main menu's first frame for over 5 s after a Kiln or game
  update (the first start reads the game's data instead of the cache). Only the item names are still looked up on the
  game's thread, after such an update. The item list in F8 fills a moment after loading.
- The classification dump (counterLog) is written in the background as well.
- The World tab (stack size, backpack cheat, the limits) can be changed in the main menu: F8 there edits the settings
  the next world you host starts with, so a save loads with the stack size you want instead of you fixing it inside.
  Inside a world you host it works as before; on a server you joined it shows the server's values.

## 0.1.5
- Clearing the skip list or the only list asks once more: "Clear the list" turns into "Keep it" (where you just
  clicked) and "Really clear all N?" beside it. A stray click while the mouse misbehaves no longer empties a list.
- Undo next to the list puts it back as it was before your last change in the editor (a clear, an x, a group, a
  pick), also after the window was closed and opened again, until the list changes some other way (a profile switch,
  F9, an edit of the file).

## 0.1.4
- Old books lying in villages and ruins (the world's furniture that gives Old Books) are no longer picked up: built
  around, they are part of a base's décor, and the mod took them. A new switch, Old books (F8, Looting), takes them
  again out in the world; even then they stay while you are in or near a base. It is off by default. E takes them
  either way, and books you build yourself were never taken.
- Counter log: the settings line names the oldBooks switch, and the pickups part shows how many books were left near
  a base (decorLeft).

## 0.1.3
- Next profile has no key by default now (it was F10): XHL-Core opens its window on F10, and with Kiln Auto Loot
  loaded F10 never reached it. Give it a key in Kiln's Keys page (Kiln Auto Loot, Next profile) to switch with one
  press; a key you set there already stays as it is.

## 0.1.2
- Loose loot from broken barrels, crates and pots and from some resource nodes is picked up (string, torn cloth, flint
  and the like were left on the ground). Each piece is judged by the item it carries: the skip and only lists, gather
  focus, the Stones switch for stone, the Collectibles switch for finds; quest items stay.
- Counter log: the counters line shows how many of those drops were seen and how many were left because their item
  could not be read or is a quest item (generic=seen/left).

## 0.1.1
- Pressing or holding E takes anything again, whatever your switches, group ranges and F9 say: with a group switched
  off, a single press of E on a stone, a crop or a plant did nothing (and stones followed you). Switched-off groups are
  left by the mod only; holding E pulls them in as in the game without the mod.
- Bones, bat fur and critter parts that creatures leave are looted (hound bones in the Hollow Halls were always left).
- Counter log: the settings line names every group switch, the group ranges and the skip and only lists.

## 0.1.0
First release.
- Automatic looting around every player: loose things, finds, plants, ripe crops, fish; corpses, chests and heaps.
- Quest items and a skip list stay where they are; switched-off groups stay put even when you hold E (changed in
  0.1.1).
- Smashing of barrels, crates and ruins clutter, optional bee hives and tree felling; never inside a base.
- Bigger stacks and an optional backpack cheat.
- F8 settings window (English and German), F9 toggle, short notes; settings per player, limits per server.
- Built from the running game's own data: new crops and items of a game update are known without a mod update.
- Profiles with quick swap (F10, own keys) and six built-in ones; skip list or only list; gather focus on the item you
  last took by hand (F11); minimum gear rarity; a height window (4 m above and below by default); bag-aware taking:
  top up carried stacks only, keep free slots, carry limits per item or category.

Fixed since the test builds:
- Archaic Essence crystals are picked up.
- Boulders and stone mine carts are smashed only while both Stones and Things on the ground are on.
- One key press that reaches the mod twice counts once (the F8 window no longer opens and closes again at once).
- Nothing is looted or smashed for a player until their own settings have arrived, so the world's defaults no longer
  apply in the moment after loading in (a player without the mod gets the defaults after 10 s). A hosted game hands
  the host's settings over until they arrive.
