using ModSettingsMenu.Settings;
using PlayerEquipment;
using PugMod;
using Unity.Entities;
using UnityEngine;

namespace AutoRailBridges
{
    /// <summary>
    /// Bootstrap. Harmony patch classes are auto-discovered by the loader, so Init does no
    /// manual PatchAll.
    ///
    /// The settings section registers in EarlyInit, not Init: RailPlacementPropertyPatch reads
    /// `enabled` during Core Keeper's database bake (PugDatabasePostConverter.PostConvert), which
    /// the game runs strictly between EarlyInit and Init — binding any later would leave that bake
    /// reading ModConfig's hardcoded fallback instead of the persisted value, the same bake-timing
    /// bug the sibling mod RebalanceKeyCrafting hit first. PlaceItemPatch's own runtime read of
    /// `enabled` is unaffected by binding earlier — it only needs the handle to exist by the time
    /// gameplay starts, which EarlyInit already guarantees.
    /// </summary>
    public class AutoRailBridgesMod : IMod
    {
        public void EarlyInit()
        {
            // RequiresRestart: honest about the bake-time half (RailPlacementPropertyPatch) even
            // though the runtime half (PlaceItemPatch's Hook 2/3) actually responds immediately —
            // matches RebalanceKeyCrafting's own bake-time `enabled` toggle for the same reason.
            ModSettings
                .Section(this)
                .Hint(
                    "Placing a rail where it cannot go lays a bridge from your inventory underneath it first. "
                        + "Toggling this fully takes effect on the next restart."
                )
                .Toggle(out var en, "enabled", true)
                .RequiresRestart()
                .Build();
            ModConfig.Instance.Bind(en);
            Debug.Log($"[AutoRailBridges] EarlyInit - enabled={ModConfig.Instance.enabled}");
        }

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

            // ...and that registration only takes effect for worlds BurstDisabler.AddWorld has
            // already seen. Its sole caller is ECSManager.StartEcs, which snapshots whatever is
            // registered at that moment, and a dedicated server runs IMod.Init() *after* StartEcs —
            // so there the snapshot was empty, the job stayed Bursted and the hooks were dead
            // again, exactly the 2026-08-08 symptom but only in multiplayer. Note the
            // "Patched OnUpdate ... for job burst disabling" log line does NOT prove the bypass is
            // armed: it comes from the AndJobs variant's dependency patch, which is set regardless.
            // No-op on the client, where Init() runs first; the registry is a set.
            foreach (var world in World.All)
                BurstDisabler.AddWorld(world);
        }

        public void ModObjectLoaded(Object obj) { }

        public void Shutdown() { }

        public void Update() { }
    }
}
