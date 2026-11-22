using StardewModdingAPI.Utilities;

namespace DodgeRoll;

internal sealed class ModConfig
{
    public bool Enabled { get; set; } = true;

    /// <summary>Default is the left stick click (L3).</summary>
    public KeybindList RollKey { get; set; } = KeybindList.Parse("LeftStick");

    public float RollDistanceTiles { get; set; } = 2.5f;

    public int RollDurationMs { get; set; } = 350;

    /// <summary>Invincibility window from the start of the roll. Capped at the roll duration.</summary>
    public int IFrameDurationMs { get; set; } = 250;

    /// <summary>Delay after a roll ends before the next one can start.</summary>
    public int CooldownMs { get; set; } = 300;

    /// <summary>Roll cost as a number of the player's current pickaxe swings.</summary>
    public float StaminaCostSwings { get; set; } = 2f;

    public bool UseFlatStaminaCost { get; set; }

    public float FlatStaminaCost { get; set; } = 4f;

    /// <summary>Allow rolling with less stamina than the cost (overdraws like tools do), as long as the player isn't exhausted.</summary>
    public bool AllowRollWithLowStamina { get; set; }

    public bool PlaySound { get; set; } = true;
}
