// SPDX-License-Identifier: MIT
// The game data load in steps, so the game's thread never waits for the disk. On a hard disk the read, the
// classifiers and the cache write held the main menu's first frame for over 5 s (inbox 50). The steps:
//   1. a worker reads: the cache when it is for this game data, else the game's resources;
//   2. the caller's thread names the items, only after a fresh read (the game's texts are read with a call that only
//      the game's own threads may make);
//   3. a worker classifies;
//   4. the caller takes the result; after a fresh read a worker writes the cache.
// Step() never waits: the caller polls it (each tick) or waits with Finish() (a server world's start). Pure: the work
// is given as functions, so the offline tests run it with plain data.
using KilnAutoLoot.Core;

namespace KilnAutoLoot.Server.GameData;

/// <summary>What the read step found. <see cref="Facts"/> null: the game has not loaded its data yet (try again
/// later). <see cref="Named"/>: the facts came from the cache, names included. <see cref="Key"/>: the cache key they
/// belong to.</summary>
internal sealed record ReadResult(GameFacts? Facts, bool Named, string Source, string Key = "");

internal enum LoadState { NotYet, Pending, Ready, Failed }

internal sealed class StagedLoad<T>(
    Func<bool> available,
    Func<ReadResult> read,
    Func<GameFacts, GameFacts> name,
    Func<GameFacts, T> classify,
    Action<string, GameFacts> save) where T : class
{
    private readonly object _gate = new();
    private Task<ReadResult>? _reading;
    private Task<T>? _classifying;
    private GameFacts? _facts;
    private bool _fresh;
    private string _key = "";

    /// <summary>The classified data once ready, else null.</summary>
    public T? Result { get; private set; }

    /// <summary>The facts the result was made from, once ready.</summary>
    public GameFacts? Facts => Result is null ? null : _facts;

    /// <summary>Where the facts came from (the read step's line), once ready.</summary>
    public string Source { get; private set; } = "";

    /// <summary>How long the caller's thread spent naming the items (0 after a cache hit), and the classifiers on the
    /// worker.</summary>
    public double NameMs { get; private set; }
    public double ClassifyMs { get; private set; }

    /// <summary>The last failure (Step answered Failed); the next Step starts again.</summary>
    public Exception? Error { get; private set; }

    /// <summary>The cache write after a fresh read, or null.</summary>
    public Task? Saving { get; private set; }

    /// <summary>The step a worker runs now, or null.</summary>
    public Task? Running
    {
        get { lock (_gate) return _classifying ?? (Task?)_reading; }
    }

    /// <summary>Moves the load on as far as it can without waiting. Any thread; the naming step runs on the thread that
    /// calls it after the read is done.</summary>
    public LoadState Step()
    {
        lock (_gate)
        {
            if (Result is not null) return LoadState.Ready;
            if (_classifying is null)
            {
                if (_reading is null)
                {
                    if (!available()) return LoadState.NotYet;
                    _reading = Task.Run(read);
                    return LoadState.Pending;
                }
                if (!_reading.IsCompleted) return LoadState.Pending;
                var reading = _reading;
                _reading = null;
                if (!reading.IsCompletedSuccessfully) return Fail(reading.Exception);
                var r = reading.Result;
                if (r.Facts is null) return LoadState.NotYet;
                GameFacts facts;
                var sw = System.Diagnostics.Stopwatch.StartNew();
                try { facts = r.Named ? r.Facts : name(r.Facts); }
                catch (Exception e) { return Fail(e); }
                NameMs = r.Named ? 0 : sw.Elapsed.TotalMilliseconds;
                _facts = facts;
                _fresh = !r.Named;
                _key = r.Key;
                Source = r.Source;
                _classifying = Task.Run(() =>
                {
                    var t = System.Diagnostics.Stopwatch.StartNew();
                    var c = classify(facts);
                    ClassifyMs = t.Elapsed.TotalMilliseconds;
                    return c;
                });
                return LoadState.Pending;
            }
            if (!_classifying.IsCompleted) return LoadState.Pending;
            var classifying = _classifying;
            _classifying = null;
            if (!classifying.IsCompletedSuccessfully) return Fail(classifying.Exception);
            Result = classifying.Result;
            Error = null;
            if (_fresh)
            {
                var (k, f) = (_key, _facts!);
                Saving = Task.Run(() => save(k, f));
            }
            return LoadState.Ready;
        }
    }

    /// <summary>Steps until the data is ready or nothing can run, waiting on this thread for each worker step. For a
    /// server world's start, whose handlers expect the data. A poll on another thread may take a step
    /// meanwhile.</summary>
    public LoadState Finish()
    {
        var state = Step();
        for (var pass = 0; state == LoadState.Pending && pass < 4; pass++)
        {
            try { Running?.Wait(); }
            catch (AggregateException) { /* Step reports it */ }
            state = Step();
        }
        return state;
    }

    private LoadState Fail(Exception? e)
    {
        Error = e is AggregateException { InnerExceptions.Count: 1 } a ? a.InnerException : e;
        Error ??= new InvalidOperationException("a load step stopped without an answer");
        return LoadState.Failed;
    }
}
