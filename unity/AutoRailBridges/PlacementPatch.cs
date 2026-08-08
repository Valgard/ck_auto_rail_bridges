using HarmonyLib;
using PlayerEquipment;
using Pug.Properties;
using Unity.Collections;
using Unity.Entities;

namespace AutoRailBridges
{
    /// <summary>
    /// Hook 1. While a rail is held, this makes the vanilla placement check treat a
    /// bridgeable tile as valid — by copying the chosen bridge prefab's placement flags onto
    /// the player's PlacementCD. The mod therefore never enumerates pit/water/lava itself; it
    /// borrows whatever rules the bridge has, and inherits new bridge types or tile types from
    /// future game updates for free.
    ///
    /// Why this method and not PlacementHandler.Activate: Activate receives no player entity
    /// (Pug.Other:296019), so from there the flags could not be conditioned on actually owning
    /// a bridge — the cursor would go green with an empty inventory.
    ///
    /// Why it CLEARS the flags unconditionally, before any other check: the result of this hook
    /// must depend only on the current inventory, never on whatever the four flags happened to
    /// be when this call started. Clearing first — ahead of the enabled check and the bridge
    /// search — turns every return path (mod disabled mid-aim; no bridge found; the found
    /// bridge's own properties failing to resolve) into "vanilla for a rail" (all four false) by
    /// construction, instead of relying on each path to remember to clear individually. This does
    /// NOT rest on any assumption about when vanilla itself re-initialises these flags, nor on
    /// the relative ordering of the two systems: SelectedEquipmentChangeSystem in fact calls
    /// PlacementHandler.Activate on every tick, not only on an equipment change —
    /// OnUpdate schedules EquippedSlotChangeJob unconditionally (Pug.Other:428254-428257, no
    /// change filter on the job's queries), and the Activate call at :427333 sits outside the
    /// job's own equip-change branch, gated only on the equipped slot's type. So vanilla does
    /// reset them too, but this hook is correct whether or not that holds. It is safe because the
    /// hook only clears while a rail is held; holding anything else returns before touching the
    /// flags at all.
    /// </summary>
    [HarmonyPatch]
    public static class PlacementPatch
    {
        [HarmonyPatch(
            typeof(PlacementHandler),
            "UpdatePlaceablePosition",
            new[]
            {
                typeof(Entity),
                typeof(NativeList<PlacementHandler.EntityAndInfoFromPlacement>),
                typeof(EquipmentUpdateAspect),
                typeof(EquipmentUpdateSharedData),
                typeof(LookupEquipmentUpdateData),
            },
            new[] { ArgumentType.Normal, ArgumentType.Ref, ArgumentType.Ref, ArgumentType.Ref, ArgumentType.Ref }
        )]
        [HarmonyPrefix]
        public static void BeforeUpdatePlaceablePosition(
            Entity placementPrefab,
            ref NativeList<PlacementHandler.EntityAndInfoFromPlacement> diggableEntityAndInfos,
            in EquipmentUpdateAspect equipmentUpdateAspect,
            in EquipmentUpdateSharedData equipmentUpdateSharedData,
            in LookupEquipmentUpdateData equipmentUpdateLookupData
        )
        {
            // Anything other than a rail keeps whatever Activate already set for that object:
            // clearing past this point would corrupt a real bridge's own placement flags while
            // the player holds one.
            if (!BridgeSelector.IsRailEquipped(in equipmentUpdateAspect))
                return;

            // Clear first, set later (maybe). Every path below this line holds a rail, and a
            // rail carries none of these properties itself, so "vanilla for a rail" IS "all four
            // false". Clearing unconditionally here — ahead of both the enabled check and
            // TryFind — removes every early-return path that could leave a flag borrowed on a
            // previous, successful tick still set (mod disabled mid-aim; last bridge just spent;
            // the chosen bridge's own properties failing to resolve). That is what makes the hook
            // idempotent instead of merely additive.
            ref PlacementCD placement = ref equipmentUpdateAspect.placementCD.ValueRW;
            placement.canPlaceOnPit = false;
            placement.canPlaceOnWater = false;
            placement.canBePlacedOnLava = false;
            placement.canBePlacedOnLowColliders = false;

            // Deliberately not the first guard: EquippedSlotChangeJob only re-runs Activate on an
            // equipment CHANGE (Pug.Other:427117/:427333), not when Mod Settings flips `enabled`
            // mid-aim. Checking first here would skip the clear above on that path and leave the
            // cursor green until the next equipment change. Clearing before this check means
            // "disabled" already lands on vanilla's values for a rail — the toggle's contract,
            // not just its letter.
            if (!ModConfig.Instance.enabled)
                return;

            if (!BridgeSelector.TryFind(in equipmentUpdateAspect, in equipmentUpdateLookupData, out _, out ObjectID bridgeID))
                return;

            Entity bridgePrefab = PugDatabase.GetPrimaryPrefabEntity(bridgeID, equipmentUpdateSharedData.databaseBank.databaseBankBlob);
            if (!equipmentUpdateLookupData.objectPropertiesLookup.TryGetComponent(bridgePrefab, out ObjectPropertiesCD bridgeProps) || !bridgeProps.IsValid)
            {
                return;
            }

            // The same property hashes PlacementHandler.Activate reads (Pug.Other:296026-296035),
            // named in PugProperties.PropertyID.PlaceableObject (PugProperties:328-345).
            placement.canPlaceOnPit = bridgeProps.Has(-1827158511);
            placement.canPlaceOnWater = bridgeProps.Has(-1324171664);
            placement.canBePlacedOnLava = bridgeProps.Has(-1535225238);
            placement.canBePlacedOnLowColliders = bridgeProps.Has(-1242875768);
        }
    }
}
