# Auto Rail Bridges

A small Core Keeper mod that **lays a bridge under a rail for you**.

Placing a rail across a pit or over water normally does nothing at all: the
rail has no ground to sit on, so the game refuses the placement. This mod
makes that single right-click do both steps — it takes a bridge from your
inventory, puts it down, and places the rail on top.

Personal-use, non-commercial (Pugstorm EULA).

## Install

- **mod.io:** subscribe to the mod; Core Keeper downloads it on next launch.
- **Local build:** see `CLAUDE.md` → *Build and deploy*.

Requires **CoreLib** and **Mod Settings Menu**.

## Usage

Carry rails and at least one kind of bridge, then place rails as you always
have. Over a pit or water, a bridge appears underneath automatically and one
bridge is deducted from your inventory.

When several bridge types are in your inventory, the first available one wins
in this order:

1. Glass Bridge
2. Stone Bridge
3. Wood Bridge
4. Scarlet Bridge
5. Coral Bridge
6. Galaxite Bridge
7. Gleam Wood Bridge
8. Metal Grate
9. Industrial Bridge

The order is fixed in this version and will become configurable once Mod
Settings Menu supports list-valued settings.

**Carrying no bridge changes nothing for the better.** The rail is placed
without a substrate and drops as a pickup item, exactly as it would if the
mod deducted a bridge it does not have. Nothing is lost — pick the rail back
up and bring bridges.

There is one setting, an on/off toggle, in the in-game mod settings. It takes
effect after a restart, because half of this mod's work happens while the
world's object database is being built.

## Compatibility

Works both on its own and alongside **PlacementPlus**, including its grid
placement: drag a rectangle of rails across a pit and every single one gets
its own bridge, as long as your inventory holds out.

This is not a coincidence but the reason for the design — see `CLAUDE.md` and
`docs/adrs/001-hook-the-tile-convergence-point.md` for why the mod reacts to
tiles being queued rather than to clicks being handled.

## How it works

See `CLAUDE.md` for the full architecture. In short, two independent halves:

- At world load, the mod adds `Pit` and `Water` to the rail's list of things
  it may be placed on. That is what makes the game accept the placement at
  all — without it, nothing downstream is ever reached.
- At runtime, every tile queued for placement passes through one utility.
  When a rail arrives there for a position with neither ground nor bridge
  under it, the mod queues a bridge ahead of it and debits it.
