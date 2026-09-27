using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace AutoRailBridges
{
    /// <summary>
    /// Grants a rail the placement permission a bridge already has over pit and water, by adding
    /// ObjectID.Pit and ObjectID.Water to the rail prefab's own
    /// PlaceableObjectAuthoring.canBePlacedOnObjects list (Pug.ECS.Authoring:3251).
    ///
    /// Why that list: PlacementHandler.ShouldCheckPlaceObjectOnTile (Pug.Other:305252) treats the
    /// tile under the cursor as a virtual object — pit and water map to ObjectID.Pit/.Water — and
    /// asks ObjectCanBePlacedOnObject (Pug.Other:305307, body 305430-305462). That runs two list
    /// scans on the placement prefab's ObjectPropertiesCD, veto list first (hash 1757427560,
    /// read at Pug.Other:305111) and allow list second (hash -789473209, PugProperties:862, read at
    /// Pug.Other:305115), then a reverse check against the target object's own allow list. A hit in
    /// the rail's allow list is enough. WoodBridge's list is [Water, Pit, Lava]; the rail's is
    /// [ConveyorBelt, ElectricalDoor] and its veto list is empty, which is why rails already cross
    /// conveyor belts but not pits. The PlacementCD bool flags (canPlaceOnPit, …) are the other
    /// route, and they are false on real bridges — copying them was an earlier approach that could
    /// never work.
    ///
    /// Why PostConvert, and when the edit takes effect: PlaceableObjectConverter.Convert
    /// (Pug.ECS.Conversion:2967) is what copies the list into the object's properties
    /// (SetPropertyList, :3035). It runs in the shipped game, but ConversionManager runs every
    /// converter before any final-world post-converter (PugConversion:766-805), and
    /// PugDatabasePostConverter is one of those (CanRunInStagingWorld => false, Pug.Other:3502). So
    /// an edit made here lands after the current world has already captured Rail's properties, and
    /// takes effect in the NEXT conversion that reads the same prefab. That is enough in practice:
    /// ECSManager converts the Default World first (Pug.Other:2345-2356), then the ServerWorld and
    /// ClientWorld during StartEcs — so the worlds a player actually plays in see the edited list.
    /// The Default World itself keeps the unpatched one, and so does anything that reads Rail's
    /// properties from it. Verified on 1.3.0.2 with a host session; whether a dedicated server also
    /// converts the Default World first is not verified.
    ///
    /// Finding the rail: vanilla PostConvert reads the prefabs from
    /// ScriptableData.GetDataBlocks&lt;EntityAuthoringDataBlock&gt;() (Pug.Other:3513) and the
    /// ObjectInfo off each block's prefab (IEntityMonoBehaviourData, Pug.Other:3529, .ObjectInfo at
    /// :3535). This prefix walks the same list and filters on ObjectInfo.objectID
    /// (Pug.Base:4623), an enum compare rather than a name compare. PlaceableObjectAuthoring sits
    /// on that same prefab. Before 1.3 the walk went through DatabaseConversionUtility and
    /// ObjectInfo.prefabInfos; neither exists any more.
    ///
    /// Idempotency: PostConvert runs once per world against the same persistent prefab, and
    /// "add if not present" needs no guard beyond the Contains check.
    ///
    /// What this does NOT do: TileType.rail still needs a ground or bridge substrate when its tile
    /// update is applied (TileType.GetNeededTile, Pug.Base:19957-20000, case rail at :19970;
    /// enforced in ApplyAdd, Pug.Other:248696-248738). A bare pit has neither, so this permission
    /// alone would make the cursor lie. PlaceItemPatch.BeforeAddTile supplies the bridge.
    /// </summary>
    [HarmonyPatch(typeof(PugDatabasePostConverter), nameof(PugDatabasePostConverter.PostConvert))]
    internal static class RailPlacementPropertyPatch
    {
        static RailPlacementPropertyPatch()
        {
            Debug.Log("[AutoRailBridges] RailPlacementPropertyPatch loaded.");
        }

        [HarmonyPrefix]
        private static bool Prefix(GameObject authoring)
        {
            if (!ModConfig.Instance.enabled)
                return true;
            if (authoring == null)
                return true;
            if (!authoring.TryGetComponent<PugDatabaseAuthoring>(out _))
                return true;

            // 1.3 removed DatabaseConversionUtility and emptied PugDatabaseAuthoring, which now
            // only marks the object. Vanilla reads the prefabs from ScriptableData instead
            // (Pug.Other:3513) and returns without building the database when the list is null
            // or empty (:3515), so there is nothing to patch in that case either.
            IReadOnlyList<EntityAuthoringDataBlock> blocks = ScriptableData.GetDataBlocks<EntityAuthoringDataBlock>();
            if (blocks == null)
                return true;

            bool railPatched = false;
            foreach (EntityAuthoringDataBlock block in blocks)
            {
                GameObject blockPrefab = block.prefab;
                if (blockPrefab == null)
                    continue;
                IEntityMonoBehaviourData entityData = blockPrefab.GetComponent<IEntityMonoBehaviourData>();
                if (entityData == null)
                    continue;
                ObjectInfo objectInfo = entityData.ObjectInfo;
                if (objectInfo == null || objectInfo.objectID != ObjectID.Rail)
                    continue;

                // Found the rail. From here on every early exit has to say why: a silent skip
                // would leave the log saying nothing at all, while rails quietly stop crossing pits.
                if (!blockPrefab.TryGetComponent(out PlaceableObjectAuthoring placeable))
                {
                    Debug.LogWarning("[AutoRailBridges] The rail prefab has no PlaceableObjectAuthoring — rails will not be placeable over pits.");
                    continue;
                }
                if (placeable.canBePlacedOnObjects == null)
                {
                    Debug.LogWarning("[AutoRailBridges] The rail's canBePlacedOnObjects list is null — rails will not be placeable over pits.");
                    continue;
                }

                AddIfMissing(placeable.canBePlacedOnObjects, ObjectID.Pit);
                AddIfMissing(placeable.canBePlacedOnObjects, ObjectID.Water);
                railPatched = true;
                Debug.Log("[AutoRailBridges] Rail may now be placed on: " + string.Join(",", placeable.canBePlacedOnObjects));
            }

            if (!railPatched)
            {
                Debug.LogWarning("[AutoRailBridges] No rail was patched during the database bake — rails will not be placeable over pits.");
            }

            return true; // always run the vanilla bake
        }

        private static void AddIfMissing(List<ObjectID> list, ObjectID id)
        {
            if (!list.Contains(id))
                list.Add(id);
        }
    }
}
