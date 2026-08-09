# Auto Rail Bridges

**Run your rails straight across the chasm.**

Laying track across a pit is a chore: place a bridge, place a rail on it,
repeat — one tile at a time, two clicks each. Aim a rail at open water or a
pit without a bridge already there and nothing happens at all.

This mod collapses that into a single click. Put down a rail where it cannot
go, and a bridge from your inventory goes down underneath it first.

![Rails being laid straight across a chasm, wood bridges appearing underneath](https://raw.githubusercontent.com/Valgard/ck_auto_rail_bridges/main/sources/ck_auto_rail_bridges_wood.gif)

## What it does

- Places a bridge from your inventory automatically whenever you place a rail
  over a pit or over water, and deducts that one bridge.
- Picks the bridge type for you when you carry several: Glass, then Stone,
  then Wood, then Scarlet, Coral, Galaxite, Gleam Wood, Metal Grate and
  Industrial Bridge.
- Leaves ordinary placement completely untouched — on solid ground, rails
  behave exactly as they always have.
- Can be switched off in the in-game mod settings.

Carrying stone bridges instead? Then those are what gets placed — the mod
always reaches for the highest-priority bridge you actually have on you:

![The same crossing built with stone bridges instead of wood](https://raw.githubusercontent.com/Valgard/ck_auto_rail_bridges/main/sources/ck_auto_rail_bridges_stone.gif)

## Good to know

- Carrying no bridge simply means no bridge is placed; the rail drops as a
  pickup item and can be collected again. Nothing is consumed or lost.
- The bridge order is fixed in this version. Making it configurable is
  planned for a later release.
- The on/off setting applies after restarting the game, because part of the
  mod's work happens while the world is being loaded.

## Compatibility

Works alongside **PlacementPlus**, including its grid placement — drag a
rectangle of rails across a pit and each one gets its own bridge, for as long
as your bridges last.

## Requirements

Requires **CoreLib** and **Mod Settings Menu** — mod.io will prompt you to
install them when you subscribe.

In multiplayer, this mod has to be installed on the server as well as on the
clients, because it changes what the world considers a valid placement.

---

*Built with the official Pugstorm Core Keeper Mod SDK. Personal-use,
non-commercial (Core Keeper EULA). Not affiliated with or endorsed by
Pugstorm.*
