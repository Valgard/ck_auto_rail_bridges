using ModSettingsMenu.Settings;
using PugMod;
using UnityEngine;

namespace AutoRailBridges
{
    /// <summary>
    /// Bootstrap. Harmony patch classes are auto-discovered by the loader, so Init does no
    /// manual PatchAll.
    ///
    /// Open question, not yet settled by reading alone: whether a BurstDisabler is needed for
    /// the placement-path targets (PlacementHandler.UpdatePlaceablePosition,
    /// PlaceObjectSlot.PlaceItem, EntityUtility.AddTile). Expectation is that none is needed —
    /// they are reached through managed calls despite sitting inside a Burst-compiled system,
    /// the same way the sibling mod reusable-cattle-box patches PlaceObjectSlot.PlaceItem with
    /// no BurstDisabler and ships working; PlaceItem is called from the same UpdateEquipment as
    /// our targets, and PlaceItem itself calls UnityEngine.Debug.LogWarning — Burst cannot
    /// compile a managed call, so it falls back to Mono for that method, meaning [BurstCompile]
    /// on the caller is an intent, not a guarantee for every callee. Counter-evidence pointing
    /// the other way: EquipmentUpdateSystem (Pug.Other:419765) and its nested UpdateJob
    /// (:419770) both carry [BurstCompile] (:419761, :419767), and Execute() calls
    /// PlaceObjectSlot.UpdateEquipment (:419898-419899), which calls our targets every tick. The
    /// in-game check settles it: if the prefix binds (no "Undefined target method" in
    /// Player.log) but never visibly fires, the target is Burst-inlined after all here. Fallback
    /// in that case is BurstDisabler.DisableBurstForSystem&lt;EquipmentUpdateSystem&gt;() in
    /// Init, before the patch needs to bind.
    ///
    /// The settings are registered in Init, not EarlyInit: `enabled` is read per placement at
    /// runtime, never during the database bake, so there is no bake-time ordering requirement.
    /// </summary>
    public class AutoRailBridgesMod : IMod
    {
        public void EarlyInit() { }

        public void Init()
        {
            // EquipmentUpdateSystem does its work in a nested UpdateJob that carries its own
            // [BurstCompile] (Pug.Other:419767); that job is what calls PlaceObjectSlot
            // .UpdateEquipment (:419898). The plain DisableBurstForSystem un-Bursts only the system
            // shell, leaving the job Bursted, so every Harmony patch on a method reached from the
            // job stays dead. Verified in-game 2026-08-08: with the plain variant no hook fired at
            // all; with ...AndJobs the log shows "BurstDisabler: Patched OnUpdate on
            // EquipmentUpdateSystem for job burst disabling" and every hook fires.
            BurstDisabler.DisableBurstForSystemAndJobs<EquipmentUpdateSystem>();

            ModSettings
                .Section(this)
                .Hint("Placing a rail where it cannot go lays a bridge from your inventory underneath it first.")
                .Toggle(out var en, "enabled", true)
                .Build();
            ModConfig.Instance.Bind(en);
            Debug.Log($"[AutoRailBridges] Init - enabled={ModConfig.Instance.enabled}");
        }

        public void ModObjectLoaded(Object obj) { }

        public void Shutdown() { }

        public void Update() { }
    }
}
