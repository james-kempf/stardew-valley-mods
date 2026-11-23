namespace DeadlyLightning;

/// <summary>Only the host's config matters: the host rolls the strikes and tells the victim's client the rules to re-check.</summary>
internal sealed class ModConfig
{
    public bool Enabled { get; set; } = true;

    /// <summary>Chance per eligible player per 10-minute clock check (107 checks from 6:10 to 23:50).
    /// 0.0005 gives 1 - 0.9995^107, about 5.2%, for one ring outdoors through a whole storm day. Clamped to 0..1.</summary>
    public double StrikeChancePerCheck { get; set; } = 0.0005;

    /// <summary>Multiply the chance by the number of ring slots in use (1 or 2; a combined ring is one slot).</summary>
    public bool ScaleWithRingCount { get; set; }

    /// <summary>Players inside a building (a location that isn't outdoors) can't be struck.</summary>
    public bool IndoorsSafe { get; set; } = true;
}
