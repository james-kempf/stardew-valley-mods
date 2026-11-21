namespace AutoSplitScreenCoop;

internal sealed class ModConfig
{
    /// <summary>Whether the mod does anything.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Farmhand to load (case-insensitive). Empty = most recently played eligible farmhand.</summary>
    public string FarmhandName { get; set; } = "";

    /// <summary>Controller for the second screen: One, Two, Three or Four.</summary>
    public string PlayerIndex { get; set; } = "One";

    /// <summary>Host plays with keyboard/mouse, so controller One can go to the second screen.</summary>
    public bool HostUsesKeyboard { get; set; } = true;
}
