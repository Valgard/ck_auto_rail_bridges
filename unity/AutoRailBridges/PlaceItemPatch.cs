using HarmonyLib;
using Inventory;
using PlayerEquipment;
using PugTilemap;
using Unity.Entities;
using Unity.Mathematics;

namespace AutoRailBridges
{
    /// <summary>
    /// Hooks 2 and 3, plus a clearing postfix.
    ///
    /// Why not simply do the work in a PlaceItem prefix: PlaceItem runs more often than a
    /// placement happens, and it protects itself INSIDE the method — !canPlaceObject
    /// (Pug.Other:311322), the tilePlacementTimer guard inside CanPlaceItem (called at :311332;
    /// the timer checks themselves live at :311538-:311555, not :295533 — that line is unrelated
    /// code in a different method entirely), the "same tile within 1s" duplicate check (:311337),
    /// CanConsumeEntityInSlot (:311349) and the creative/placeable-prefab check (:311353). A
    /// prefix runs before ALL of them, so debiting a bridge there loses one item per discarded
    /// input tick.
    ///
    /// EntityUtility.AddTile (called at :311379, immediately before vanilla's own
    /// ConsumeEntityAt at :311382) is the first point past every one of those guards. It is not
    /// the point at which a tile becomes real — AddTile (:256440) only appends to
    /// TileUpdateBuffer and validity is re-judged when the buffer is applied — but "past every
    /// guard" is the property this design needs.
    ///
    /// Ordering: both tiles go into one TileUpdateBuffer, bridge first. The buffer is reversed
    /// TWICE on its way into the world — UpdateSubMapCommon.FilterUpdates (:240546) walks it
    /// backwards while building addList, and ApplyAdd (:241602) walks addList backwards — so
    /// insertion order survives and the bridge is applied first. That is what
    /// GetNeededTile(rail) requires: a rail needs `ground` or `bridge` (Pug.Base:18124). A
    /// single added reversal anywhere in that chain would invert this; re-check after game
    /// updates.
    /// </summary>
    [HarmonyPatch]
    public static class PlaceItemPatch
    {
        private struct PendingBridge
        {
            public bool active;
            public Entity player;
            public Entity inventoryBufferEntity;
            public int slotIndex;
            public ObjectID bridgeID;
            public int tileset;
            public int2 position;
            public bool isCreative;
            public bool godMode;
            public float3 playerPosition;
            public int variation;
        }

        // All three hooks run in the same tick of the same UpdateEquipment call, on the same
        // thread (the targets are reached through managed calls — that is why Harmony can bind
        // them at all). ThreadStatic keeps the record from leaking across worlds when the
        // client-prediction and server passes run on different threads.
        [System.ThreadStatic]
        private static PendingBridge _pending;

        // Captured alongside the record so Hook 3, which receives no lookup data, can still
        // reach the inventory-change buffer. Also ThreadStatic: a plain static here would let
        // one thread's Hook 3 read a value written moments earlier by a different thread's Hook
        // 2, even though that other thread's own `_pending.active` (correctly) never sees it —
        // the read itself would still race. Same rationale as `_pending` above.
        [System.ThreadStatic]
        private static BufferLookup<InventoryChangeBuffer> _pendingInventoryBuffer;

        [HarmonyPatch(
            typeof(PlaceObjectSlot),
            "PlaceItem",
            new[] { typeof(EquipmentUpdateAspect), typeof(EquipmentUpdateSharedData), typeof(LookupEquipmentUpdateData) },
            new[] { ArgumentType.Ref, ArgumentType.Normal, ArgumentType.Normal }
        )]
        [HarmonyPrefix]
        public static bool BeforePlaceItem(
            in EquipmentUpdateAspect equipmentUpdateAspect,
            EquipmentUpdateSharedData equipmentUpdateSharedData,
            LookupEquipmentUpdateData equipmentUpdateLookupData
        )
        {
            _pending = default(PendingBridge);

            if (!ModConfig.Instance.enabled)
                return true;

            if (!BridgeSelector.IsRailEquipped(in equipmentUpdateAspect))
                return true;

            ref PlacementCD placement = ref equipmentUpdateAspect.placementCD.ValueRW;
            if (!placement.canPlaceObject)
                return true;

            int3 target = placement.bestPositionToPlaceAt;
            int2 pos = new int2(target.x, target.z);

            // Mirror exactly what ApplyAdd will check for a rail: `ground` OR `bridge`
            // (Pug.Base:18124). IsWalkableTile() would be a wider set — it also accepts floor,
            // rug, litFloor, looseFlooring and rail itself.
            TileAccessor tiles = equipmentUpdateSharedData.tileAccessor;
            if (tiles.HasType(pos, TileType.ground) || tiles.HasType(pos, TileType.bridge))
            {
                return true; // vanilla can place the rail unaided
            }

            if (!BridgeSelector.TryFind(in equipmentUpdateAspect, in equipmentUpdateLookupData, out int slotIndex, out ObjectID bridgeID))
            {
                // No substrate and no bridge: abort the whole placement. Without this, a stale
                // canPlaceObject could let vanilla queue a rail over an unbridged pit.
                return false;
            }

            _pending = new PendingBridge
            {
                active = true,
                player = equipmentUpdateAspect.entity,
                inventoryBufferEntity = equipmentUpdateSharedData.inventoryUpdateBufferEntity,
                slotIndex = slotIndex,
                bridgeID = bridgeID,
                tileset = PugDatabase.GetEntityObjectInfo(bridgeID, equipmentUpdateSharedData.databaseBank.databaseBankBlob).tileset,
                position = pos,
                isCreative = equipmentUpdateSharedData.worldInfoCD.IsWorldModeEnabled(WorldMode.Creative),
                godMode = equipmentUpdateLookupData.godModeLookup.IsComponentEnabled(equipmentUpdateAspect.entity),
                playerPosition = equipmentUpdateLookupData.localTransformLookup[equipmentUpdateAspect.entity].Position,
                variation = equipmentUpdateAspect.equippedObjectCD.ValueRO.containedObject.objectData.variation,
            };
            _pendingInventoryBuffer = equipmentUpdateLookupData.inventoryUpdateBuffer;

            return true;
        }

        [HarmonyPatch(
            typeof(PlaceObjectSlot),
            "PlaceItem",
            new[] { typeof(EquipmentUpdateAspect), typeof(EquipmentUpdateSharedData), typeof(LookupEquipmentUpdateData) },
            new[] { ArgumentType.Ref, ArgumentType.Normal, ArgumentType.Normal }
        )]
        [HarmonyPostfix]
        public static void AfterPlaceItem()
        {
            // Safety net: if vanilla returned between Hook 2 and Hook 3, the record must not
            // leak into the next tick.
            _pending = default(PendingBridge);
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
            if (!_pending.active)
                return;

            if (tileType != TileType.rail || !math.all(position == _pending.position))
                return;

            PendingBridge p = _pending;
            _pending = default(PendingBridge); // consume before doing work — never fire twice

            // Bridge first: two reversals downstream preserve insertion order (class comment).
            EntityUtility.AddTile(p.tileset, TileType.bridge, p.position, p.isCreative, tileUpdateBuffer);

            // optionalTargetObjectID is mandatory here, not optional. InventoryUtility
            // .ConsumeEntityAt (Pug.Other:409860) compares the slot's objectID against it ONLY
            // when it is set; left at None, a queued inventory operation that changed that slot
            // first would make this consume whatever is now there. With it set, a mismatch
            // fails the consume instead — the bridge tile is then placed without a debit, which
            // is the acceptable direction for this failure.
            DynamicBuffer<InventoryChangeBuffer> buffer = _pendingInventoryBuffer[p.inventoryBufferEntity];
            buffer.Add(
                new InventoryChangeBuffer
                {
                    inventoryChangeData = Create.ConsumeEntityAt(
                        p.player,
                        p.slotIndex,
                        1,
                        destroy: true,
                        dontConsume: p.godMode,
                        p.playerPosition,
                        p.variation,
                        default(float3),
                        p.bridgeID
                    ),
                    playerEntity = p.player,
                }
            );
        }
    }
}
