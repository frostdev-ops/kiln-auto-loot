// SPDX-License-Identifier: MIT
// The stack size widgets: a logarithmic slider 0..65535 that shows "Default" at 0, and a number field
// with steps of 100 and 1000 (ImGui's InputInt: the buttons step 100, with Ctrl 1000), clamped 0..65535. Kiln's UI has
// no InputInt and no log slider, so the field is a text field parsed here and the slider is an int slider over
// positions 0..Positions mapped onto the value. Pure.
using System.Globalization;

namespace KilnAutoLoot.Client;

internal static class StackField
{
    public const int Max = 65535;
    public const int Step = 100, FastStep = 1000;
    /// <summary>Slider positions; 0 = Default (0), the rest spread logarithmically over 1..65535.</summary>
    public const int Positions = 1000;

    public static int Clamp(long v) => v < 0 ? 0 : v > Max ? Max : (int)v;

    /// <summary>The typed text as a stack size: blanks trimmed, an optional sign, digits; clamped to 0..65535, as an
    /// ImGui InputInt does. Null when it is no number (the field keeps the value it had).</summary>
    public static int? Parse(string? text)
    {
        var t = (text ?? "").Trim();
        if (t.Length == 0) return null;
        var digits = t[0] is '+' or '-' ? t[1..] : t;
        if (digits.Length == 0 || !digits.All(char.IsAsciiDigit)) return null;
        if (long.TryParse(t, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var v)) return Clamp(v);
        return t[0] == '-' ? 0 : Max;  // too long for a long: still a number, clamped by its sign
    }

    /// <summary>One press of the minus (-1) or plus (+1) button; with Ctrl the step is 1000.</summary>
    public static int Stepped(int value, int direction, bool ctrl) =>
        Clamp((long)value + Math.Sign(direction) * (ctrl ? FastStep : Step));

    public static int ToPosition(int value)
    {
        value = Clamp(value);
        if (value == 0) return 0;
        var p = (int)Math.Round(Positions * Math.Log(value) / Math.Log(Max), MidpointRounding.AwayFromZero);
        return Math.Clamp(p, 1, Positions);
    }

    public static int FromPosition(int position)
    {
        if (position <= 0) return 0;
        if (position >= Positions) return Max;
        return Clamp((long)Math.Round(Math.Pow(Max, position / (double)Positions), MidpointRounding.AwayFromZero));
    }

    /// <summary>What the slider shows: "Default" (or "Standard") at 0, else the number.</summary>
    public static string Label(int value, string defaultText) =>
        value == 0 ? defaultText : value.ToString(CultureInfo.InvariantCulture);

    public static string Text(int value) => value.ToString(CultureInfo.InvariantCulture);
}
