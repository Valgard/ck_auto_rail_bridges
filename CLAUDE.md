# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with
code in this repository.

## What this repo is

A Core Keeper mod that places a bridge from the player's inventory under a rail
when the target tile cannot carry one — one right-click, both tiles. Built
against Pugstorm's `CoreKeeperModSDK`. No content of its own; depends on CoreLib
and Mod Settings Menu. Personal-use, non-commercial (Pugstorm EULA).

The parent `../CLAUDE.md` holds the mod-agnostic SDK/CrossOver guidance shared
with the sibling mods.

## Build and deploy

```bash
source .envrc           # or, from a worktree: source ../../../.envrc && source .envrc
../utils/build.sh       # Unity batchmode build; on Darwin auto-runs install-macos.sh
```

**One-time per clone: `direnv allow .`** — without it `build.sh` aborts with
`UNITY_BIN: must be set in the mods .envrc`, which reads like a missing variable
but is direnv refusing an unapproved file. In a non-interactive shell direnv does
not auto-load at all, so scripted builds need `direnv exec . ../utils/build.sh`.

Unity Editor must be closed (it locks the project). `utils/link.sh` symlinks the
repo's `unity/` mirror into `$SDK_PATH/Assets/`; `build.sh` invokes it
idempotently on every run, so worktree switches and repo moves self-heal.

**Concurrent-build / shared-SDK caveat:** all sibling mods share one
`CoreKeeperModSDK` clone with a single `UnityLockfile`. If another session is
building, wait for the lock to release — do not kill it.

No automated tests — verification is a manual in-game check: carry rails and
bridges, right-click a rail onto a pit, and confirm a bridge appears underneath
with exactly one bridge deducted. Re-check on solid ground (must behave exactly
as vanilla) and with no bridge carried (the rail must drop as a pickup item, not
vanish).

## Architecture

Two halves that are independent of each other. Permission is granted at bake
time; the bridge is placed at runtime. Neither works without the other, and they
fail differently — a broken bake means nothing happens at all, a broken runtime
half means rails drop as items.

- **`AutoRailBridgesMod` (`IMod`)** — bootstrap. Registers the settings section
  in `EarlyInit` (see below) and calls
  `BurstDisabler.DisableBurstForSystemAndJobs<EquipmentUpdateSystem>()` in
  `Init`, followed by a manual `BurstDisabler.AddWorld` pass over `World.All`
  (see below — without it the hooks are dead on a dedicated server). Harmony
  patch classes are auto-discovered; there is no `PatchAll`.
- **`RailPlacementPropertyPatch`** — prefix on
  `PugDatabasePostConverter.PostConvert`, adds `ObjectID.Pit` and
  `ObjectID.Water` to the rail prefab's `canBePlacedOnObjects`.
- **`PlaceItemPatch`** — a context prefix on `PlaceObjectSlot.UpdateEquipment`
  and the working prefix on `EntityUtility.AddTile`, plus a postfix that clears
  the context.
- **`BridgeSelector`** — the fixed priority list and the inventory scan.
- **`ModConfig`** — singleton adapter over the Mod Settings Menu handle.

### Why the permission comes from `canBePlacedOnObjects`, not the placement flags

`PlacementHandler.ShouldCheckPlaceObjectOnTile` grants permission through two
OR-ed routes: the `PlacementCD` bool flags (`canPlaceOnPit`, `canPlaceOnWater`,
…) and an object list checked by `ObjectCanBePlacedOnObject`. **Bridges use the
second route.** Measured live: on `WoodBridge` those bool flags are `false`, both
on the DB prefab and in `PlacementCD` while the bridge is equipped, while its
`canBePlacedOnObjects` reads `[Water, Pit, Lava]`.

An earlier version of this mod copied the bridge's flags onto the held rail. It
could never work — there was nothing to copy — and it was removed once the bake
patch landed. Note the two list properties are passed **crossed** into
`ShouldCheckPlaceObjectOnTile`: hash `-789473209` is the allow list,
`1757427560` the veto list. Reading them in declaration order gets it backwards.

The authoring-side converter is the wrong hook: `PlaceableObjectConverter.Convert`
is editor-only and fires **zero** times in the shipped game. `PostConvert` is
what runs per world/database conversion.

### Why the context comes from `UpdateEquipment` and the work happens in `AddTile`

The obvious hook for "a rail is being placed" is `PlaceObjectSlot.PlaceItem`.
It is also the wrong one, because it is not a place every mod passes through:
PlacementPlus (mod.io 3400322) prefixes `PlaceObjectSlot.UpdateEquipment` with
`return false` and runs its own `ObjectPlacementLogic.PlaceItemGrid` instead.
Measured 2026-08-09: with PlacementPlus active, a prefix on `PlaceItem` fired
zero times while rails were being placed, and rails landed over pits with no
bridge under them.

`EntityUtility.AddTile` is unavoidable by comparison. Queuing a tile means
writing into the `TileUpdateBuffer`, and that is the one utility that does it —
PlacementPlus calls it too (`ObjectPlacementLogic.cs:276` and `:555`). A mod can
replace the decision of *whether and where* to place without replacing the act of
placing, short of reimplementing CK's tilemap layer.

`AddTile`'s parameters carry no player and no inventory, which is what the
`UpdateEquipment` prefix is for — it does nothing but capture aspect, shared data
and lookups into a `[ThreadStatic]` struct. `[HarmonyPriority(Priority.First)]`
puts it ahead of PlacementPlus's prefix; the matching postfix still runs even
when a prefix returned `false`, because Harmony skips remaining *prefixes* only.

Deciding **per tile** rather than per click follows from this and covers grid
placement for free: each rail in a dragged rectangle arrives as its own `AddTile`
call and gets its own bridge until the inventory runs out.

### Why a missing bridge must not veto the placement

Suppressing the `AddTile` call when no bridge is carried looks like the tidy
behaviour and is the harmful one. Both vanilla (`Pug.Other:311379`/`311382`) and
PlacementPlus (`:276`/`:283`) debit the item **after** calling `AddTile`, so a
blocked tile still costs the rail. Letting it through only drops a pickup item.
The mod therefore does nothing in that case.

### Why the tile ordering survives

Both tiles go into one `TileUpdateBuffer`, bridge first. The buffer is reversed
**twice** on its way into the world — `UpdateSubMapCommon.FilterUpdates`
(`:240546`) walks it backwards while building `addList`, and `ApplyAdd`
(`:241602`) walks `addList` backwards — so insertion order survives and the
bridge is applied first. That is what `GetNeededTile(rail)` requires: a rail
needs `ground` or `bridge` (`Pug.Base:18124`). A single added reversal anywhere
in that chain would invert this; re-check after game updates.

### Why `DisableBurstForSystemAndJobs`, not the plain variant

`EquipmentUpdateSystem` (`Pug.Other:419765`) does its work in a nested
`UpdateJob` carrying its own `[BurstCompile]` (`:419767`), and that job is what
calls `PlaceObjectSlot.UpdateEquipment` (`:419898`). The plain
`DisableBurstForSystem` un-Bursts only the system shell, leaving the job Bursted,
so every patch on a method reached from the job stays dead. Verified in-game
2026-08-08: with the plain variant **no** hook fired at all.

The two variants differ by exactly one thing, and it is not a thoroughness bonus
— it is the mechanism. `AndJobs` adds a postfix calling
`state.Dependency.Complete()` (`PugMod.SDK.Runtime:924`, applied via
`CompleteDependencyAfterUpdatePatch` at `:943`). The bypass itself is a *window*:
a prefix/postfix pair on `Unity.Entities.WorldUnmanagedImpl.UpdateSystem`
(`:960`) flips `BurstCompiler.Options.EnableBurstCompilation` off for the
duration of that one system's update and restores it right after. An async job is
*scheduled* inside that window but *runs* after it, once Burst is back on — so it
stays Bursted and patches on anything it reaches never bind. `Complete()` drags
the execution into the window. This is a reading of the code that fits the
2026-08-08 observation, not an independently proven claim; treat it as the
working model.

The corollary decides the variant for any future system: **if the patch target is
the system's own `OnUpdate`, the plain variant suffices and costs almost
nothing** — the shell runs managed, but the jobs it schedules stay Bursted and
keep running async on a worker thread, so the real work never leaves native code.
That is the case in the sibling mods DisableDurability, FasterTalents and
FasterPetTalents, all three of which patch `<System>.OnUpdate` and use the plain
call. Only a target *inside* a Bursted job needs `AndJobs`, and only then is the
sync point paid at all.

Cost in practice here: no perceptible impact (observed, never profiled —
2026-08-11), including while laying rails across pits and water, where the hook
fires on every single placement. Two properties keep it cheap and both must be
re-checked before assuming the same for another system: the query iterates player
entities only (`EquipmentUpdateAspect` requires `ClientInput`, `PlayerStateCD`,
`PlayerGhost` — `Pug.Other:419114`), and the job is scheduled with `Schedule()`,
not `ScheduleParallel()` (`:420660`), so it was single-threaded anyway and
`Complete()` costs only the frame overlap. PlacementPlus un-Bursts the same system
with the same call, and running both at once was equally unremarkable; double
registration is harmless (the registry is a `HashSet`, a second `Complete()` is a
no-op).

Choosing the right variant is only half of it — the registration has to reach the
world as well, which is why `Init` follows it with a manual
`BurstDisabler.AddWorld` pass over `World.All`. `AddWorld`'s sole caller is
`ECSManager.StartEcs`, and it **snapshots** whatever is registered at that
moment; a dedicated server runs `IMod.Init()` *after* `StartEcs`, so the snapshot
was empty, the job stayed Bursted and the hooks were dead again — the exact
2026-08-08 symptom, but only in multiplayer. Beware the misleading evidence here:
the `BurstDisabler: Patched OnUpdate on EquipmentUpdateSystem for job burst
disabling` line **does appear** in the server log even while the bypass is
inactive, because it comes from the `AndJobs` variant's dependency patch, which
is applied regardless of the snapshot. The pass is a no-op in the client
ordering, and `EarlyInit()` is not an alternative — `TypeManager` is not
initialised that early. Full mechanism in the parent `../CLAUDE.md`.

### Why the settings section registers in `EarlyInit`

`RailPlacementPropertyPatch` reads `enabled` during the database bake, which the
game runs strictly between `EarlyInit` and `Init`. Binding any later would leave
that bake reading `ModConfig`'s hardcoded fallback instead of the persisted
value — the same bake-timing bug the sibling mod RebalanceKeyCrafting hit first.
The toggle is therefore declared `RequiresRestart()`, honest about the bake-time
half even though the runtime half responds immediately.

### Why `requiredOn: 3`

Both sides genuinely need this mod. The bake patch changes what the world
considers a valid placement, so a server without it rejects what a modded client
predicts, and a client without it never offers the placement in the first place.
That is the case the parent `../CLAUDE.md` reserves `3` for — unlike the
read-only HUD mods, which take `1` so they never block joining unmodded servers.

`unity/` is the canonical source — a 1:1 mirror of the SDK's `Assets/` tree
holding every file the Editor generates for the mod.

## macOS / CrossOver

Deployed through the fake-mod.io workaround (see parent `../CLAUDE.md`). This
mod's fake mod.io ID is **`9999988`**. Do not open the in-game Mods menu while a
fake-ID install is active; re-run `../utils/build.sh` to restore if the cache is
wiped.

To test coexistence with another installed mod, toggle it through
`state.json:disabledMods` rather than renaming its directory.

## Publishing to mod.io

Published; the real mod ID in
`unity/AutoRailBridges/Editor/AutoRailBridges_modio.asset` is `6295455`.
`../utils/upload.sh` uses the shared
`CoreKeeperModUtils.CLIPublishHelper.Publish` Editor class the same way as every
sibling mod: the version comes from the topmost `## [x.y.z]` entry of
`CHANGELOG.md`. `CK_MODIO_TYPE` is `Quality of Life|World`. The profile logo
is `unity/AutoRailBridges/Editor/logo.png` — a 1024×1024 transparent PNG made
with the family logo pipeline (parent `../CLAUDE.md` § Logo / branding). Its
per-mod gesture is a brass crane on a teal minecart lowering a bridge plank that
already carries a section of rail, onto the open end of a half-built bridge; the
chosen white/black source pair and the rejected candidates are kept in `sources/`
under the family naming convention. Two notes for future logo work here: the rail
on the *hanging* plank is the point of the whole image — without it the picture
reads as "bridges get built" rather than "rails cross chasms" — and this logo
deliberately drops the pale sticker rim most siblings avoid while keeping the
golden radial glow. Note `CLIPublishHelper` only rejects a *missing* logo asset,
never a placeholder one, so the scaffold's empty 0-byte file would have published
silently had it not been replaced. Set the mod.io profile type tag to **`Script`**
(an `Asset` tag silently disables the mod's scripts).

## Conventions

- Commit messages: Conventional Commits (`type(scope): subject`), imperative,
  no emoji.
- Documentation files (`CLAUDE.md`, `README.md`, `docs/`) are English; chat
  answers are German.
- Prefer `git commit --amend` / `git reset --soft` over fix-up commits on a
  personal branch, and `git rebase` over `git merge`.
