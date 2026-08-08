using ModSettingsMenu.Settings;

namespace AutoRailBridges
{
    /// <summary>
    /// Mod configuration adapter. The single player-facing knob `enabled` is a live in-game
    /// setting from the Mod Settings Menu framework, bound once in AutoRailBridgesMod.Init
    /// via Bind(). The RoslynCSharp sandbox blocks System.IO; the framework persists the value
    /// via CoreLib, so the mod's own code touches no file API.
    /// </summary>
    internal sealed class ModConfig
    {
        // Live handle set once by AutoRailBridgesMod.Init via Bind(); null only in the brief
        // pre-Bind window at mod load -> the hardcoded default (true) applies. The hooks fire
        // during gameplay, strictly after Bind, and the framework is a hard dependency.
        private SettingHandle<bool> _enabledHandle;

        public void Bind(SettingHandle<bool> enabled)
        {
            _enabledHandle = enabled;
        }

        // Master switch (default true). When false, all three hooks return immediately and
        // placement behaves exactly as vanilla.
        public bool enabled => _enabledHandle != null ? _enabledHandle.Value : true;

        private static readonly ModConfig _instance = new ModConfig();
        public static ModConfig Instance => _instance;
    }
}
