using System;

namespace AutoSplitScreenCoop;

internal sealed class ModConfig
{
    /// <summary>Schema version written by this mod version. Configs from 1.0.x have no ConfigVersion (read as 0).</summary>
    internal const int CurrentVersion = 1;

    /// <summary>Config schema version, used to migrate older config files.</summary>
    public int ConfigVersion { get; set; }

    /// <summary>Whether the mod does anything.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Farmhand to load (case-insensitive). Empty = most recently played eligible farmhand.</summary>
    public string FarmhandName { get; set; } = "";

    /// <summary>Controller for the second screen: Auto, One, Two, Three or Four.</summary>
    public string PlayerIndex { get; set; } = "Auto";

    /// <summary>Removed in 1.1.0 (now detected at runtime). Read only so old configs can be migrated; never written.</summary>
    public bool? HostUsesKeyboard { get; set; }

    /// <summary>Json.NET convention: keeps the removed option out of the rewritten config.json.</summary>
    public bool ShouldSerializeHostUsesKeyboard() => false;

    /// <summary>Upgrade an older config in place. Returns true if it changed and should be written back.</summary>
    internal bool Migrate()
    {
        if (this.ConfigVersion >= CurrentVersion)
            return false;

        // 1.0.x default pair (One + HostUsesKeyboard true) gave the host's pad to player 2 on gamepad hosts.
        if (string.Equals(this.PlayerIndex?.Trim(), "One", StringComparison.OrdinalIgnoreCase) && this.HostUsesKeyboard != false)
            this.PlayerIndex = "Auto";

        this.HostUsesKeyboard = null;
        this.ConfigVersion = CurrentVersion;
        return true;
    }
}
