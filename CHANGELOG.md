# Changelog

## 0.1.6
- The game no longer waits for Kiln Auto Loot while it starts: the mod reads the game's data, sorts it and writes its
  cache in the background. On a hard disk this held the main menu's first frame for over 5 s after a Kiln or game
  update (the first start reads the game's data instead of the cache). Only the item names are still looked up on the
  game's thread, after such an update. The item list in F8 fills a moment after loading.
- The classification dump (counterLog) is written in the background as well.

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
