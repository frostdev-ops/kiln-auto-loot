# Changelog

## 0.1.0
First release.
- Automatic looting around every player: loose things, finds, plants, ripe crops, fish; corpses, chests and heaps.
- Quest items and a skip list stay where they are; switched-off groups stay put even when you hold E.
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
