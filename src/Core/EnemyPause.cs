// SPDX-License-Identifier: MIT
namespace KilnAutoLoot.Core;

/// <summary>Per player: pause near enemies: paused until 2000 ms after the last near enemy.</summary>
public sealed class EnemyPause
{
    public const long PauseMs = 2000;

    private readonly IClock _clock;
    private long? _lastNear;

    public EnemyPause(IClock clock) => _clock = clock;

    /// <summary>An enemy (group enemy, not animal, alive) within enemyDistance was seen now.</summary>
    public void EnemySeen() => _lastNear = _clock.NowMs;

    /// <summary>True while an enemy was near less than 2000 ms ago and enemyPause is on.</summary>
    public bool Paused(bool enemyPause) =>
        enemyPause && _lastNear is long t && _clock.NowMs - t < PauseMs;
}
