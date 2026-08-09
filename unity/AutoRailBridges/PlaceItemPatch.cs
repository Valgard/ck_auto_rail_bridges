using HarmonyLib;
using Inventory;
using PlayerEquipment;
using PugTilemap;
using Unity.Entities;
using Unity.Mathematics;

namespace AutoRailBridges
{
    /// <summary>
    /// Two hooks: one captures the placement context, the other reacts to the rail tile actually
    /// being queued.
    ///
    /// Why the context comes from UpdateEquipment and not PlaceItem: PlaceItem is the natural
    /// place, but it is not a place every mod passes through. PlacementPlus (mod.io 3400322)
    /// prefixes PlaceObjectSlot.UpdateEquipment with `return false` and runs its own
    /// ObjectPlacementLogic.PlaceItemGrid instead, so vanilla's PlaceItem is never called and a
    /// prefix there is silently dead. Measured 2026-08-09: with PlacementPlus active, a prefix on
    /// PlaceItem fired zero times while rails were being placed.
    ///
    /// EntityUtility.AddTile, however, is unavoidable. Queuing a tile means writing into the
    /// TileUpdateBuffer, and that is the one utility that does it — PlacementPlus calls it too
    /// (ObjectPlacementLogic.cs:276 and :555). A mod can replace the decision of *whether and
    /// where* to place without replacing the act of placing. So AddTile is where this mod does its
    /// work, and UpdateEquipment — which runs in both worlds, ours and PlacementPlus's, because
    /// HarmonyPriority.First puts our prefix ahead of theirs — is only there to hand it the
    /// context AddTile's own parameters do not carry: which player, which inventory, which tiles.
    ///
    /// Deciding per tile rather than per click is what makes PlacementPlus's grid mode work for
    /// free: every rail in a dragged rectangle arrives as its own AddTile call and gets its own
    /// bridge, until the inventory runs out.
    ///
    /// Ordering: both tiles go into one TileUpdateBuffer, bridge first. The buffer is reversed
    /// TWICE on its way into the world — UpdateSubMapCommon.FilterUpdates (:240546) walks it
    /// backwards while building addList, and ApplyAdd (:241602) walks addList backwards — so
    /// insertion order survives and the bridge is applied first. That is what GetNeededTile(rail)
    /// requires: a rail needs `ground` or `bridge` (Pug.Base:18124). A single added reversal
    /// anywhere in that chain would invert this; re-check after game updates.
    ///
    /// When no bridge is carried, this mod deliberately does nothing and lets the rail drop as a
    /// pickup item. Suppressing the AddTile call would be worse, not better: both vanilla
    /// (Pug.Other:311379/311382) and PlacementPlus (:276/:283) debit the item *after* calling
    /// AddTile, so a blocked tile would still cost the rail — turning a cosmetic annoyance into
    /// actual item loss.
    /// </summary>
    [HarmonyPatch]
    public static class PlaceItemPatch
    {
        private struct PlacementContext
        {
            public bool active;
            public EquipmentUpdateAspect aspect;
            public EquipmentUpdateSharedData sharedData;
            public LookupEquipmentUpdateData lookupData;
        }

        // Both hooks run within the same UpdateEquipment call on the same thread (the targets are
        // reached through managed calls — that is why Harmony can bind them at all). ThreadStatic
        // keeps the context from leaking across worlds when the client-prediction and server
        // passes run on different threads. The struct holds ECS refs and lookups that are valid
        // only for the duration of that call, which is exactly the lifetime the postfix enforces.
        [System.ThreadStatic]
        private static PlacementContext _ctx;

        // No explicit argument-type array: naming the overload that way would require matching
        // argumentVariations for its `in` parameters. UpdateEquipment is unambiguous, so Harmony
        // resolves it from the name alone and binds our subset of parameters by name.
        [HarmonyPatch(typeof(PlaceObjectSlot), nameof(PlaceObjectSlot.UpdateEquipment))]
        [HarmonyPriority(Priority.First)]
        [HarmonyPrefix]
        public static void BeforeUpdateEquipment(
            in EquipmentUpdateAspect equipmentUpdateAspect,
            in EquipmentUpdateSharedData equipmentUpdateSharedData,
            in LookupEquipmentUpdateData equipmentUpdateLookupData
        )
        {
            _ctx = default(PlacementContext);

            if (!ModConfig.Instance.enabled)
                return;

            // Cheap gate: without a rail in hand no AddTile call can concern us, and skipping the
            // capture keeps every other placement in the game untouched.
            if (!BridgeSelector.IsRailEquipped(in equipmentUpdateAspect))
                return;

            _ctx = new PlacementContext
            {
                active = true,
                aspect = equipmentUpdateAspect,
                sharedData = equipmentUpdateSharedData,
                lookupData = equipmentUpdateLookupData,
            };
        }

        // Runs even when PlacementPlus's prefix returns false — Harmony skips remaining prefixes
        // in that case, but never the postfixes.
        [HarmonyPatch(typeof(PlaceObjectSlot), nameof(PlaceObjectSlot.UpdateEquipment))]
        [HarmonyPostfix]
        public static void AfterUpdateEquipment()
        {
            _ctx = default(PlacementContext);
        }

        [HarmonyPatch(
            typeof(EntityUtility),
            "AddTile",
            new[] { typeof(int), typeof(TileType), typeof(int2), typeof(bool), typeof(DynamicBuffer<TileUpdateBuffer>) }
        )]
        [HarmonyPrefix]
        public static void BeforeAddTile(
            int tileSet,
            TileType tileType,
            int2 position,
            bool isWorldModeCreative,
            DynamicBuffer<TileUpdateBuffer> tileUpdateBuffer
        )
        {
            if (!_ctx.active)
                return;

            // Also the recursion guard: the bridge this method adds re-enters here as
            // TileType.bridge and stops at this line.
            if (tileType != TileType.rail)
                return;

            // Mirror exactly what ApplyAdd will check for a rail: `ground` OR `bridge`
            // (Pug.Base:18124). IsWalkableTile() would be a wider set — it also accepts floor,
            // rug, litFloor, looseFlooring and rail itself.
            TileAccessor tiles = _ctx.sharedData.tileAccessor;
            if (tiles.HasType(position, TileType.ground) || tiles.HasType(position, TileType.bridge))
                return;

            // No bridge carried: leave the rail alone. It will drop as a pickup item (class
            // comment explains why suppressing the tile would be worse).
            if (!BridgeSelector.TryFind(in _ctx.aspect, in _ctx.lookupData, out int slotIndex, out ObjectID bridgeID))
                return;

            int bridgeTileset = PugDatabase.GetEntityObjectInfo(bridgeID, _ctx.sharedData.databaseBank.databaseBankBlob).tileset;

            // Bridge first: two reversals downstream preserve insertion order (class comment).
            EntityUtility.AddTile(bridgeTileset, TileType.bridge, position, isWorldModeCreative, tileUpdateBuffer);

            // optionalTargetObjectID is mandatory here, not optional. InventoryUtility
            // .ConsumeEntityAt (Pug.Other:409860) compares the slot's objectID against it ONLY
            // when it is set; left at None, a queued inventory operation that changed that slot
            // first would make this consume whatever is now there. With it set, a mismatch fails
            // the consume instead — the bridge tile is then placed without a debit, which is the
            // acceptable direction for this failure.
            Entity player = _ctx.aspect.entity;
            DynamicBuffer<InventoryChangeBuffer> buffer = _ctx.lookupData.inventoryUpdateBuffer[_ctx.sharedData.inventoryUpdateBufferEntity];
            buffer.Add(
                new InventoryChangeBuffer
                {
                    inventoryChangeData = Create.ConsumeEntityAt(
                        player,
                        slotIndex,
                        1,
                        destroy: true,
                        dontConsume: _ctx.lookupData.godModeLookup.IsComponentEnabled(player),
                        _ctx.lookupData.localTransformLookup[player].Position,
                        _ctx.aspect.equippedObjectCD.ValueRO.containedObject.objectData.variation,
                        default(float3),
                        bridgeID
                    ),
                    playerEntity = player,
                }
            );
        }
    }
}
