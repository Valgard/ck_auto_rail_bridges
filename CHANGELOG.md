# Changelog

All notable changes to this mod are documented here. The publish pipeline
reads the topmost `## [x.y.z]` entry as the version to publish.

## [1.0.1] - 2026-08-10

### Fixed
- **No bridge was laid on a dedicated server.** Rails could still be aimed at a
  pit or water, but the bridge underneath never appeared and the rail dropped as
  an item: the placement hooks never became active on the server side. Playing
  alone or as host was unaffected. Update the mod on the server as well — the
  fix has to run there.

## [1.0.0] - 2026-08-09

### Added
- Placing a rail over a pit or over water now lays a bridge from the player's
  inventory underneath it first, in one right-click, and deducts that bridge.
- Bridge type is chosen by availability in a fixed order: Glass, Stone, Wood,
  Scarlet, Coral, Galaxite, Gleam Wood, Metal Grate, Industrial Bridge.
- An on/off toggle in the in-game mod settings (applies after a restart).
- Works alongside PlacementPlus, including its grid placement — every rail in
  a dragged rectangle gets its own bridge.
- Requires **CoreLib** and **Mod Settings Menu**.
