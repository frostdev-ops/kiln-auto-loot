// SPDX-License-Identifier: MIT
// The player's settings file as SettingsLink uses it: the values in use, one entry written at a time (all or
// nothing per entry, as Kiln's TrySet), and the last write failure. Kiln's ModConfig in the game; a fake in the offline
// tests, so a switch whose writes fail can be shown.
using Kiln;
using KilnAutoLoot.Core;

namespace KilnAutoLoot.Client;

internal interface IPersonalConfig
{
    PersonalSettings Value { get; }
    bool TrySet(string path, object? value, out string error);
    string? LastWriteError { get; }
}

internal sealed class KilnPersonalConfig(ModConfig<PersonalSettings> config) : IPersonalConfig
{
    public ModConfig<PersonalSettings> Config => config;
    public PersonalSettings Value => config.Value;
    public bool TrySet(string path, object? value, out string error) => config.TrySet(path, value, out error);
    public string? LastWriteError => config.LastWriteError;
}
