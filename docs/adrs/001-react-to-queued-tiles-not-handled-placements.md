# React to tiles being queued, not to placement being handled

- Status: accepted
- Date: 2026-08-09

## Context and Problem Statement

This mod has to notice "a rail is being placed here" in order to slip a bridge
underneath it. The natural hook is `PlaceObjectSlot.PlaceItem` — the vanilla
method that performs a placement, reached from `PlaceObjectSlot.UpdateEquipment`
inside `EquipmentUpdateSystem`.

That hook works only as long as vanilla owns the placement pipeline. **PlacementPlus**
(mod.io 3400322, installed by this mod's author and by a large share of the
player base) prefixes `UpdateEquipment` with `return false` and runs its own
`ObjectPlacementLogic.PlaceItemGrid` instead. Vanilla's `PlaceItem` is then never
called at all.

The observable failure was worse than "the mod does nothing": the separate
bake-time half of this mod still granted rails permission to sit on pits, so
rails *were* placed over chasms, found no substrate, and dropped as pickup items.
A player running both mods lost nothing but got a shower of rails on the floor —
strictly worse than not installing this mod. Measured 2026-08-09: with
PlacementPlus active, a prefix on `PlaceItem` fired **zero** times during rail
placement.

## Decision Drivers

- The author runs 25+ mods, PlacementPlus among them; "works standalone" is not
  a useful definition of working.
- No dependency on upstream changes to a third-party mod. PlacementPlus's
  repository (`limoka/CoreKeeperMods`) is committed to but has merged no PRs
  since early 2025.
- No second implementation of placement rules — CK's own logic must stay the
  single source of truth for *whether* and *where* something may be placed.
- Must not introduce item loss under any failure mode.
- Sandbox-clean (no `System.IO`, no reflection, no `HarmonyLib.AccessTools`).

## Considered Options

1. **Hook the tile convergence point** — take the placement context from
   `UpdateEquipment` (ordered ahead of foreign prefixes) and do the work in
   `EntityUtility.AddTile`.
2. **Conflict detection** — detect PlacementPlus at load and disable this mod's
   bake patch, with a warning in the log.
3. **A standalone ECS system** — read input and aim position directly, place the
   bridge before anything else does, with no Harmony involvement in the
   placement path (the approach `Tool Resizer` uses to sidestep such conflicts).
4. **Exclude rails from PlacementPlus** via its user-editable `ExcludeItems`
   config, making it fall back to vanilla for rails.

## Decision Outcome

Chosen option: **"Hook the tile convergence point"**.

`EntityUtility.AddTile` is the one utility that writes into the
`TileUpdateBuffer`, and queuing a tile *is* writing into that buffer.
PlacementPlus calls it itself (`ObjectPlacementLogic.cs:276` and `:555`). A mod
can replace the decision of *whether and where* to place without replacing the
act of placing — doing otherwise would mean reimplementing CK's tilemap layer.
That makes `AddTile` a natural chokepoint: not a special case for one foreign
mod, but the point every placement passes through regardless of origin.

`AddTile`'s parameters carry neither player nor inventory, so the context comes
from a prefix on `UpdateEquipment` marked `[HarmonyPriority(Priority.First)]`,
which runs ahead of PlacementPlus's prefix and does nothing but capture aspect,
shared data and lookups into a `[ThreadStatic]` struct. The matching postfix
still runs when a prefix returned `false`, because Harmony skips remaining
*prefixes* only — that is what bounds the context's lifetime to one call.

### Consequences

- One code path serves both worlds; there is no PlacementPlus-specific branch
  anywhere in the mod.
- The decision moves from per-click to **per tile**, which covers grid placement
  for free: each rail in a dragged rectangle arrives as its own `AddTile` call
  and gets its own bridge until the inventory runs out. Vanilla cannot do this
  at all.
- A missing bridge is deliberately **not** treated as a veto. Both vanilla
  (`Pug.Other:311379`/`311382`) and PlacementPlus (`:276`/`:283`) debit the item
  *after* calling `AddTile`, so suppressing the tile would still cost the rail —
  turning a cosmetic annoyance into real item loss. The mod does nothing in that
  case and the rail drops as a pickup.
- The mod remains dependent on Harmony prefix ordering at exactly one point. A
  foreign mod that both replaced `UpdateEquipment` *and* bypassed `AddTile` would
  defeat it, but that requires reimplementing the tilemap write path.

### Confirmation

Verified in-game 2026-08-09 with PlacementPlus 2.1.8 active: single rails over a
pit receive a bridge, and a dragged rectangle of rails receives one bridge per
rail. Previously confirmed standalone with PlacementPlus disabled through
`state.json:disabledMods`. Both tile types the bake patch grants — `Pit` and
`Water` — were covered throughout; water was tested alongside pits in every
round, not inferred from the shared code path.

## Pros and Cons of the Options

### 1. Hook the tile convergence point

- Good, because it uses a chokepoint that follows from CK's architecture rather
  than from one mod's current implementation.
- Good, because grid placement works without any code aimed at it.
- Good, because the change was confined to one file.
- Bad, because it relies on Harmony priority for the context capture.

### 2. Conflict detection

- Good, because it reliably prevents the rail-shower failure mode.
- Bad, because it does not let the author use the mod, which is the entire
  point of having written it. Rejected on that ground.

### 3. Standalone ECS system

- Good, because it is independent of foreign Harmony prefixes.
- Bad, because it would have to *anticipate* placement — detecting the click and
  resolving the aim position itself — duplicating logic that vanilla and
  PlacementPlus each implement separately.
- Bad, because it does not resolve the conflict so much as double it: two
  systems would independently decide about the same tile.
- Bad, because `Tool Resizer` is no precedent — it only adjusts numeric values
  and places nothing.

### 4. Exclude rails from PlacementPlus

- Good, because it needs no code at all.
- Bad, because it disables PlacementPlus's grid placement for rails, taking away
  a feature to work around a conflict rather than resolving it.
- Bad, because it depends on per-user configuration that the mod cannot ensure.

## More Information

The companion finding is that CK grants placement permission through
`canBePlacedOnObjects` on the prefab, not through the `PlacementCD` bool flags —
those read `false` even on real bridges. The bake-time half of this mod
(`RailPlacementPropertyPatch`) rests on that; `CLAUDE.md` § *Architecture* has
the detail, including the crossed property hashes.
