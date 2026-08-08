using ModSettingsMenu.Settings;
using PugMod;
using UnityEngine;

namespace AutoRailBridges
{
    /// <summary>
    /// Bootstrap. Harmony patch classes are auto-discovered by the loader, so Init does no
    /// manual PatchAll. No BurstDisabler is needed — the patch targets on the placement path
    /// (PlacementHandler.UpdatePlaceablePosition, PlaceObjectSlot.PlaceItem,
    /// EntityUtility.AddTile) are reached through managed calls.
    ///
    /// The settings are registered in Init, not EarlyInit: `enabled` is read per placement at
    /// runtime, never during the database bake, so there is no bake-time ordering requirement.
    /// </summary>
    public class AutoRailBridgesMod : IMod
    {
        public void EarlyInit() { }

        public void Init()
        {
            ModSettings.Section(this).Toggle(out var en, "enabled", true).Build();
            ModConfig.Instance.Bind(en);
            Debug.Log($"[AutoRailBridges] Init - enabled={ModConfig.Instance.enabled}");
        }

        public void ModObjectLoaded(Object obj) { }

        public void Shutdown() { }

        public void Update() { }
    }
}
