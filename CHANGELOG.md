# Changelog

## 0.1.2
- Loose loot from broken barrels, crates and pots and from some resource nodes is picked up (string, torn cloth, flint
  and the like were left on the ground). Each piece is judged by the item it carries: the skip and only lists, gather
  focus, the Stones switch for stone, the Collectibles switch for finds; quest items stay.
- Counter log: the counters line shows how many of those drops were seen and how many were left (generic=seen/left).

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
