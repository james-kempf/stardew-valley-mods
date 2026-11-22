using System;
using System.Collections.Generic;

namespace DodgeRoll;

/// <summary>Game-independent roll math (no SMAPI/XNA types) so it can be unit tested without the game.</summary>
internal static class RollMath
{
    /// <summary>Stamina for one pickaxe swing. Mirrors SDV 1.6 <c>Pickaxe.DoFunction</c>:
    /// <c>2 * (toolPower + 1) - MiningLevel * 0.1</c>. Pickaxes can't be charged, so toolPower is always 0
    /// and the upgrade level doesn't matter. Clamped to 0.1 so heavy buffs can't make rolls free.</summary>
    public static float PickaxeSwingCost(int miningLevel)
        => Math.Max(0.1f, 2f - miningLevel * 0.1f);

    /// <summary>Game states that end a roll in progress. Festivals count: they run with <c>Game1.eventUp</c> set.</summary>
    public static bool RollInterrupted(bool worldReady, bool eventUp)
        => !worldReady || eventUp;

    /// <summary>A roll may start only when nothing would interrupt it on the next tick, so stamina is never charged
    /// for a roll that's cancelled at once. Screen fades are refused too: a pending warp ends the roll on arrival.</summary>
    public static bool CanStartRoll(bool worldReady, bool eventUp, bool screenFading)
        => !RollInterrupted(worldReady, eventUp) && !screenFading;

    public static float RollCost(int miningLevel, float swings, bool useFlat, float flatCost)
        => Math.Max(0f, useFlat ? flatCost : swings * PickaxeSwingCost(miningLevel));

    /// <summary>With <paramref name="allowLow"/>, a roll may overdraw stamina (like tools do) but not start while already exhausted.</summary>
    public static bool CanAfford(float stamina, float cost, bool allowLow)
        => allowLow ? stamina > 0f : stamina >= cost;

    /// <summary>Cubic ease-out on a clamped 0..1 progress.</summary>
    public static float EaseOut(float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        float inv = 1f - t;
        return 1f - inv * inv * inv;
    }

    /// <summary>Unit roll direction in screen space (Y down). Priority: left stick (raw, Y up) outside the deadzone,
    /// then held movement directions (0 up, 1 right, 2 down, 3 left), then <paramref name="facing"/>.</summary>
    public static (float X, float Y) ResolveDirection(float stickX, float stickY, IReadOnlyCollection<int> movementDirections, int facing, float deadzone)
    {
        if (TryNormalize(stickX, -stickY, deadzone, out var stick))
            return stick;

        float x = 0f, y = 0f;
        foreach (int d in movementDirections)
        {
            var (dx, dy) = Offset(d);
            x += dx;
            y += dy;
        }
        if (TryNormalize(x, y, 0.001f, out var keys))
            return keys;

        return Offset(facing);
    }

    /// <summary>Cardinal facing (0 up, 1 right, 2 down, 3 left) for a screen-space vector.</summary>
    public static int FacingFor(float x, float y)
        => Math.Abs(x) > Math.Abs(y) ? (x > 0 ? 1 : 3) : (y < 0 ? 0 : 2);

    /// <summary>One clockwise turn through the four facings over the roll, ending on <paramref name="startFacing"/>.</summary>
    public static int SpinFacing(int startFacing, float progress)
    {
        int step = Math.Clamp((int)(progress * 4f), 0, 4);
        return (startFacing + step) % 4;
    }

    private static (float X, float Y) Offset(int direction) => direction switch
    {
        0 => (0f, -1f),
        1 => (1f, 0f),
        2 => (0f, 1f),
        3 => (-1f, 0f),
        _ => (0f, 1f),
    };

    private static bool TryNormalize(float x, float y, float minLength, out (float X, float Y) result)
    {
        float len = MathF.Sqrt(x * x + y * y);
        bool ok = len > 0f && len >= minLength;
        result = ok ? (x / len, y / len) : default;
        return ok;
    }
}
