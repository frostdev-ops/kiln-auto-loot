// SPDX-License-Identifier: MIT
// The smash hit: impact/smash_one.kimp, assembled to
// assets/impact/smash_one.bin and registered by kiln.abilities from assets/assets.json, runs at the target with
// Root = the player and applies an ApplyDamage (blunt/wood/stone/metal/explosion 100000, weapon category
// 3832918e-..., no hit reaction, no push-back), so the game's own drop path runs and the loot loop picks the drops up.
using Kiln;

namespace KilnAutoLoot.Server;

internal static class SmashHit
{
    /// <summary>The program GUID in impact/smash_one.kimp and assets/assets.json.</summary>
    public static readonly Guid Program = new("86bdce19-c6fe-4a6e-80d0-70db585360c8");

    /// <summary>The program's variable that RunProgram's configs set to the target.</summary>
    public const string TargetVariable = "Target::value";

    private static bool _ready, _missingLogged, _errorLogged;

    /// <summary>Tick thread. One program instance per hit; it lingers 1 s, then destroys itself.</summary>
    public static void Hit(EntityId player, in SmashTarget target, SmashKind kind)
    {
        if (!Ready()) return;
        var task = Abilities.RunProgram(Program, target.Position, new ImpactOptions
        {
            Root = player,
            Configs = new ImpactConfigs { [TargetVariable] = target.Entity },
            Tag = "kiln-auto-loot-smash",
        });
        if (task.IsFaulted && !_errorLogged)
        {
            _errorLogged = true;
            KilnLog.Warn($"kiln-auto-loot: smash: RunProgram failed (logged once): {task.Exception?.GetBaseException().Message}");
        }
    }

    /// <summary>The program is in the game's registry (kiln.abilities registers it at the first Pre of a server world).</summary>
    public static bool Ready()
    {
        if (_ready) return true;
        if (Abilities.Programs.Info(Abilities.Programs.IdOf(Program)) is not null) return _ready = true;
        if (!_missingLogged)
        {
            _missingLogged = true;
            KilnLog.Warn($"kiln-auto-loot: smash: program {Program} is not registered (assets/assets.json not loaded?); nothing is smashed");
        }
        return false;
    }

    /// <summary>A new server world: log again.</summary>
    public static void Reset() => _ready = _missingLogged = _errorLogged = false;
}
