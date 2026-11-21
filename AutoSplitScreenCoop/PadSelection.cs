using System;
using System.Collections.Generic;

namespace AutoSplitScreenCoop;

/// <summary>Picks player 2's gamepad. Pads are <c>PlayerIndex</c> values as ints (0 = One .. 3 = Four); -1 = no pad.</summary>
/// <remarks>Kept free of game/XNA types so the decision logic can be tested outside the game.</remarks>
internal static class PadSelection
{
    internal const int NoPad = -1;
    private static readonly string[] Names = { "One", "Two", "Three", "Four" };

    internal sealed record Result(int? Pad, bool HostGivesUpPad, string? SkipReason, IReadOnlyList<string> Warnings);

    internal static string Name(int pad) => pad >= 0 && pad < Names.Length ? Names[pad] : "none";

    /// <param name="configured">Config value: Auto, One, Two, Three or Four (case-insensitive).</param>
    /// <param name="hostPad">Pad the host screen reads (<c>instancePlayerOneIndex</c>), or -1.</param>
    /// <param name="hostOnKeyboard">Whether the host plays with keyboard/mouse rather than a gamepad.</param>
    /// <param name="isConnected">Whether a pad is connected.</param>
    internal static Result Choose(string? configured, int hostPad, bool hostOnKeyboard, Func<int, bool> isConnected)
    {
        var warnings = new List<string>();
        // A host on a gamepad owns hostPad; giving it to player 2 locks the host out (Steam Deck bug).
        bool hostOwnsPad = !hostOnKeyboard && hostPad != NoPad;

        string value = configured?.Trim() ?? "";
        int explicitPad = Array.FindIndex(Names, n => string.Equals(n, value, StringComparison.OrdinalIgnoreCase));
        if (explicitPad < 0 && !string.Equals(value, "Auto", StringComparison.OrdinalIgnoreCase))
            warnings.Add($"Invalid PlayerIndex '{configured}' in config (use Auto, One, Two, Three or Four); using Auto.");

        if (explicitPad >= 0)
        {
            if (hostOwnsPad && explicitPad == hostPad)
            {
                warnings.Add($"PlayerIndex {Name(explicitPad)} is the host's controller and the host is playing on a gamepad; using Auto instead so the host keeps it.");
            }
            else
            {
                if (!isConnected(explicitPad))
                    warnings.Add($"No controller connected for player {Name(explicitPad)}; starting split-screen anyway. Connect one to control the farmhand.");
                // Keyboard host whose screen still reads this pad: release it, as vanilla LocalCoopJoinMenu does.
                return new Result(explicitPad, explicitPad == hostPad, null, warnings);
            }
        }

        // Auto: same range vanilla auto-join scans (Two..Four), skipping the host's pad.
        for (int pad = 1; pad < Names.Length; pad++)
        {
            if (pad != hostPad && isConnected(pad))
                return new Result(pad, false, null, warnings);
        }

        if (hostOnKeyboard && isConnected(0))
            return new Result(0, hostPad == 0, null, warnings);

        string reason = hostOnKeyboard
            ? "no controller connected for player 2"
            : $"no free controller for player 2 (the host is using controller {Name(hostPad)}); connect a second controller";
        return new Result(null, false, reason, warnings);
    }
}
