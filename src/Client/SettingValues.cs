// SPDX-License-Identifier: MIT
// Reads and writes a setting by its key on the settings classes (the property is the key with a capital first
// letter, which is also how Kiln names the config path), lists what changed between two copies, and formats values
// as the window shows them. Pure, no Kiln calls.
using System.Globalization;
using System.Reflection;
using KilnAutoLoot.Core;

namespace KilnAutoLoot.Client;

internal static class SettingValues
{
    private static readonly Dictionary<(Type, string), PropertyInfo?> Props = new();

    private static PropertyInfo? Prop(Type t, string key)
    {
        lock (Props)
        {
            if (Props.TryGetValue((t, key), out var p)) return p;
            p = key.Length == 0 ? null : t.GetProperty(char.ToUpperInvariant(key[0]) + key[1..], BindingFlags.Public | BindingFlags.Instance);
            Props[(t, key)] = p;
            return p;
        }
    }

    public static object? Get(object settings, string key) => Prop(settings.GetType(), key)?.GetValue(settings);

    public static bool GetBool(object settings, string key) => Get(settings, key) is true;

    public static float GetFloat(object settings, string key) => Get(settings, key) switch
    {
        float f => f,
        int i => i,
        _ => 0f,
    };

    public static void Set(object settings, string key, object? value)
    {
        var p = Prop(settings.GetType(), key) ?? throw new ArgumentException($"no setting '{key}'", nameof(key));
        p.SetValue(settings, To(value, p.PropertyType));
    }

    /// <summary>The value as the property's type; an enum from its value, a number or its name.</summary>
    private static object? To(object? value, Type type)
    {
        if (!type.IsEnum) return Convert.ChangeType(value, type, CultureInfo.InvariantCulture);
        if (value is string name) return Enum.Parse(type, name, ignoreCase: true);
        return Enum.ToObject(type, Convert.ToInt32(value, CultureInfo.InvariantCulture));
    }

    /// <summary>An enum setting's values in order, as their names (the Choice combo).</summary>
    public static IReadOnlyList<string> ChoiceNames(Type owner, string key) =>
        Prop(owner, key) is { PropertyType.IsEnum: true } p ? Enum.GetNames(p.PropertyType) : [];

    /// <summary>The index of an enum setting's value among <see cref="ChoiceNames"/>, 0 when unknown.</summary>
    public static int ChoiceIndex(object settings, string key)
    {
        var v = Get(settings, key);
        if (v is null || !v.GetType().IsEnum) return 0;
        return Math.Max(0, Array.IndexOf(Enum.GetNames(v.GetType()), v.ToString()));
    }

    /// <summary>The camelCase keys (Kiln config paths) whose values differ, with the new value.</summary>
    public static List<(string Path, object? Value)> Changed<T>(T before, T after) where T : class
    {
        var list = new List<(string, object?)>();
        foreach (var p in typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!p.CanRead || !p.CanWrite || !IsScalar(p.PropertyType)) continue;
            var a = p.GetValue(before);
            var b = p.GetValue(after);
            if (!Equals(a, b)) list.Add((char.ToLowerInvariant(p.Name[0]) + p.Name[1..], b));
        }
        return list;
    }

    private static bool IsScalar(Type t) => t.IsPrimitive || t == typeof(string) || t.IsEnum;

    /// <summary>A copy of a limits object (it has no Clone).</summary>
    public static ServerLimits Copy(ServerLimits l) => new()
    {
        MaxRadius = l.MaxRadius, AllowSmash = l.AllowSmash, AllowFelling = l.AllowFelling, AllowHives = l.AllowHives,
        AllowSalvage = l.AllowSalvage,
    };

    // meters "%.0f m" and saved as whole meters, openDelay "%.1f s" saved in 0.1 steps
    public static float RoundMeters(float v) => MathF.Round(v, MidpointRounding.AwayFromZero);
    public static float RoundSeconds(float v) => MathF.Round(v * 10f, MidpointRounding.AwayFromZero) / 10f;

    public static float Round(WidgetKind kind, float v) => kind == WidgetKind.Seconds ? RoundSeconds(v) : RoundMeters(v);

    public static string Format(WidgetKind kind, float v) => kind == WidgetKind.Seconds
        ? RoundSeconds(v).ToString("0.0", CultureInfo.InvariantCulture) + " s"
        : RoundMeters(v).ToString("0", CultureInfo.InvariantCulture) + " m";

    /// <summary>A value for "server applies X": bool as on/off text, numbers in the widget's format.</summary>
    public static string Describe(SettingSpec spec, object settings, bool german) => spec.Kind switch
    {
        WidgetKind.Bool => GetBool(settings, spec.Key) ? (german ? "an" : "on") : (german ? "aus" : "off"),
        WidgetKind.Meters or WidgetKind.Seconds when spec.ZeroText is { } zero && GetFloat(settings, spec.Key) == 0 =>
            Texts.Get(zero, german),
        WidgetKind.Meters or WidgetKind.Seconds => Format(spec.Kind, GetFloat(settings, spec.Key)),
        WidgetKind.Count when spec.ZeroText is { } zero && GetFloat(settings, spec.Key) == 0 => Texts.Get(zero, german),
        WidgetKind.Choice => Texts.Get(spec.Key + "." + Get(settings, spec.Key), german),
        _ => Convert.ToString(Get(settings, spec.Key), CultureInfo.InvariantCulture) ?? "",
    };

    /// <summary>The personal settings whose value the server applies differently (after its limits).</summary>
    public static IEnumerable<SettingSpec> Differing(PersonalSettings mine, PersonalSettings effective) =>
        SettingCatalog.All.Where(s => s.Owner == SettingOwner.Personal && !IsList(s.Kind)
                                      && !Equals(Get(mine, s.Key), Get(effective, s.Key)));

    /// <summary>The list widgets (skip, only, carry limits): the server's copy of those is not shown as a cap.</summary>
    public static bool IsList(WidgetKind kind) => kind is WidgetKind.ItemList or WidgetKind.CarryLimits;
}
