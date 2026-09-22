using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace AutoRailBridges
{
    /// <summary>
    /// Hook 1's real replacement. Grants a rail the same PLACEMENT PERMISSION a bridge already
    /// has over pit/water — not by borrowing anything from a bridge at runtime (that premise is
    /// what an earlier in-game diagnosis disproved: WoodBridge's own canPlaceOnPit / canPlaceOnWater
    /// / canBePlacedOnLava PlacementCD-side properties read False even while a bridge is
    /// equipped), but by editing Rail's OWN pre-bake placement-property list, permanently, at
    /// database bake time.
    ///
    /// The mechanism bridges actually use: PlacementHandler.ShouldCheckPlaceObjectOnTile
    /// (Pug.Other:295775) treats the tile under the cursor as a virtual "object" —
    /// PugDatabase.TryGetTileItemInfo maps TileType.pit/.water to ObjectID.Pit/.Water — and asks
    /// ObjectCanBePlacedOnObject (Pug.Other:295898-295930) whether the PLACEMENT PREFAB's own
    /// "PlaceableObject/canBePlacedOnObjects" list (property hash -789473209, PugProperties:362)
    /// contains that virtual ObjectID. That function is a plain membership scan with one veto
    /// ahead of it: canNotBePlaceOnObjects (hash 1757427560) is checked first and blocks
    /// unconditionally on a hit; only then is canBePlaceOnObjects checked, and a hit there
    /// returns true immediately — no further condition. WoodBridge's list holds exactly
    /// [Water, Pit, Lava]; that alone is why a bridge can be dropped on a hole. Rail's own list,
    /// unmodified, holds [ConveyorBelt, ElectricalDoor] (real objects, no tile types) and nothing
    /// under the veto hash — which is why rails already cross conveyor belts but not pits, and
    /// why adding Pit/Water to Rail's own list is sufficient by itself to flip this one check.
    ///
    /// Where the edit has to land: before bake, the list is a plain mutable
    /// `public List&lt;ObjectID&gt; canBePlacedOnObjects` field on PlaceableObjectAuthoring
    /// (Pug.ECS.Authoring:3150) — a MonoBehaviour on the prefab GameObject, not any ObjectInfo
    /// class.
    ///
    /// Corrected hook: this used to prefix PlaceableObjectConverter.Convert
    /// (SingleAuthoringComponentConverter&lt;PlaceableObjectAuthoring&gt;,
    /// Pug.ECS.Conversion:2825) — measured in-game to fire ZERO times, because that converter is
    /// part of the EDITOR-side asset-conversion pipeline; the shipped game's database is already
    /// baked and never runs it again. The method that DOES run at world/database conversion time
    /// in the shipped game is PugDatabasePostConverter.PostConvert(GameObject authoring)
    /// (Pug.Other:3442, 3504), proven by the sibling mod RebalanceKeyCrafting's
    /// KeyRecipeCostPatch, which patches exactly that method to rewrite vanilla recipe costs and
    /// ships working. PostConvert does not read canBePlacedOnObjects itself — it never needed to
    /// — but it walks every prefab's authoring GameObject on its way to building the immutable
    /// PugDatabaseBank blob. Game 1.3 turned that walk around: it used to start from the database
    /// component (PugDatabaseAuthoring.prefabList -> DatabaseConversionUtility.GetPrefabList ->
    /// PrefabData.ObjectInfo -> ObjectInfo.prefabInfos -> PrefabInfo.ecsPrefab), and none of those
    /// exist any more. It now starts from the data blocks and reads the ObjectInfo off the prefab
    /// (Pug.Other:3513: ScriptableData.GetDataBlocks&lt;EntityAuthoringDataBlock&gt;() ->
    /// block.prefab -> prefab.GetComponent&lt;IEntityMonoBehaviourData&gt;() (Pug.Other:3529) ->
    /// .ObjectInfo (Pug.Other:3535)). That prefab is exactly the GameObject a
    /// PlaceableObjectAuthoring component would sit on as a sibling, so the reachable set is
    /// unchanged — only the direction is. Editing the list there, before this same PostConvert call
    /// finishes, is early enough: PlaceableObjectConverter.Convert already ran during the
    /// original SDK bake and froze its own snapshot into a byte[] property blob
    /// (PugProperties:961-983) irrespective of what this prefix does — but ObjectCanBePlacedOnObject
    /// reads that frozen list value directly off the still-live PlaceableObjectAuthoring field at
    /// prefab level (Pug.Other:295775-295930 reads through PugDatabase.objectPropertiesCD, whose
    /// backing property values are (re)serialized from the authoring components once per world's
    /// own PostConvert pass), so mutating canBePlacedOnObjects here — before this PostConvert call
    /// returns — is still ahead of the point where THIS world's ObjectPropertiesCD blob for Rail
    /// gets finalized.
    ///
    /// Identifying Rail: unlike Convert (which only ever received the isolated
    /// PlaceableObjectAuthoring instance, forcing a fragile ObjectAuthoring.objectName string
    /// compare — the assumption that broke last), PostConvert's own prefab walk exposes
    /// ObjectInfo.objectID directly (Pug.Base:4623) on the ObjectInfo read off that prefab. So
    /// this patch filters on `objectInfo.objectID == ObjectID.Rail` — an enum comparison, not a
    /// string comparison — and then takes PlaceableObjectAuthoring off that very same GameObject,
    /// the one vanilla PostConvert fetched IEntityMonoBehaviourData from a moment earlier. Since
    /// 1.3 that is a single object rather than a chain to walk, which is why the loop body no
    /// longer iterates prefabInfos.
    ///
    /// Idempotency: no HashSet-of-processed-instances guard, unlike KeyRecipeCostPatch. Scaling a
    /// number is not naturally idempotent (run it twice, it halves twice), but "add to a list if
    /// not already present" is idempotent forever with nothing more than a Contains check, even
    /// though PostConvert can run more than once per world/database conversion against the same
    /// persistent authoring instance.
    ///
    /// Logs once per bake what the rail is now allowed to sit on, and warns loudly if the rail
    /// confirmation the new hook actually fires and locates Rail — remove once verified.
    ///
    /// What this patch does NOT fix, and was never asked to: TileType.rail still requires a
    /// ground or bridge SUBSTRATE at the moment its tile update is actually applied
    /// (TileType.GetNeededTile, Pug.Base:18111-18154 — case TileType.rail needs
    /// [ground, bridge]; enforced in ApplyAdd, Pug.Other:241598-241640). A bare pit satisfies
    /// neither, so this permission fix alone would make the cursor lie. That gap is already
    /// closed by PlaceItemPatch.cs's Hook 2/3 (untouched by this task), which inject a bridge
    /// tile ahead of the rail's own tile update whenever the destination lacks one — they were
    /// simply unreachable before, because Hook 1 never actually made `placement.canPlaceObject`
    /// true over a pit, so Hook 2's own `if (!placement.canPlaceObject) return true;` guard
    /// always fired first. This patch's only job is to make that one flag true for the right
    /// reason, the same permission a bridge already has.
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
            // only marks the object. Vanilla's own PostConvert reads the prefabs from
            // ScriptableData instead (Pug.Other:3513), so this walks the same source it does.
            // PrefabInfo.ecsPrefab became PrefabInfo.authoring in the same release
            // ([FormerlySerializedAs("ecsPrefab")], Pug.Base:4736).
            IReadOnlyList<EntityAuthoringDataBlock> blocks = ScriptableData.GetDataBlocks<EntityAuthoringDataBlock>();
            if (blocks == null)
                return true;

            bool railFound = false;
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
                railFound = true;

                // 1.3 also dropped ObjectInfo.prefabInfos, so there is no list to walk: the data
                // block's own prefab is the authoring object that used to sit in
                // prefabInfos[].ecsPrefab, and it is what vanilla reads the ObjectInfo off above.
                if (!blockPrefab.TryGetComponent(out PlaceableObjectAuthoring placeable))
                    continue;
                if (placeable.canBePlacedOnObjects == null)
                    continue;

                AddIfMissing(placeable.canBePlacedOnObjects, ObjectID.Pit);
                AddIfMissing(placeable.canBePlacedOnObjects, ObjectID.Water);
                Debug.Log("[AutoRailBridges] Rail may now be placed on: " + string.Join(",", placeable.canBePlacedOnObjects));
            }

            if (!railFound)
            {
                Debug.LogWarning("[AutoRailBridges] ObjectID.Rail not found during the database bake — rails will not be placeable over pits.");
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
