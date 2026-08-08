using PugMod;
using UnityEngine;

namespace AutoRailBridges
{
    /// <summary>
    /// Mod bootstrap. The Pugstorm mod loader instantiates this class on game
    /// start and calls the IMod lifecycle methods. Harmony patch classes are
    /// auto-discovered by the loader — there is no PatchAll() call.
    /// </summary>
    public sealed class AutoRailBridgesMod : IMod
    {
        public void EarlyInit() { }

        public void Init()
        {
            Debug.Log("[AutoRailBridges] Mod initialized.");
        }

        public void ModObjectLoaded(Object obj) { }

        public void Shutdown() { }

        public void Update() { }
    }
}
