// SPDX-License-Identifier: MIT
// Stack size and the backpack cheat, through Kiln.Content.
// Both are this mod's kiln.content layer: Content.Revert() takes them off (the stack guard, which kiln.content turns
// on while any max stack is raised, keeps bigger stacks from being cut until the process ends). Applied live: a new
// value takes effect at once for new stacking; stacks already bigger are left as they are.
using Kiln;
using KilnAutoLoot.Core;

namespace KilnAutoLoot.Server;

internal static class WorldData
{
    private readonly record struct Wanted(int StackSize, bool BestBackpack);

    private static Wanted? _wanted, _applied;
    private static bool _hooked;

    /// <summary>The stack size this mod's content layer applies now (0 = none: the game's own sizes, also while
    /// kiln.content is not there or has not applied it yet). Tick thread.</summary>
    public static int AppliedStackSize { get; private set; }

    /// <summary>Tick thread, server side. Idempotent: LootServer calls it at ServerStartedEvent and after every change
    /// of the server settings.</summary>
    public static void Apply(ServerSettings s)
    {
        _wanted = new Wanted(Math.Clamp(s.MaxStack, 0, WorldDataRules.MaxStack), s.BackpackCheat);
        if (!Content.Available)
        {
            KilnLog.Warn("kiln-auto-loot: kiln.content is not available: stack size and backpack recipes stay the game's own");
            return;
        }
        if (!_hooked)
        {
            _hooked = true;
            // runs after every content application (world load, pack reload), and at the next step if content is
            // ready already; as this mod, so the layer is this mod's
            Content.OnReady(_ => { _applied = null; ApplyNow(); });
            return;
        }
        if (Content.Ready) ApplyNow();
    }

    /// <summary>At ServerStoppingEvent.</summary>
    public static void Reset()
    {
        _wanted = null;
        AppliedStackSize = 0;
        if (_applied is not null && Content.Available) Content.Revert();
        _applied = null;
    }

    private static void ApplyNow()
    {
        if (_wanted is not { } w || _applied == w) return;
        Content.Revert();                                         // this mod's layer only: back to the game's values
        AppliedStackSize = 0;
        _applied = w;
        int raised, recipes;
        try
        {
            raised = ApplyStacks(w.StackSize);
            recipes = w.BestBackpack ? ApplyBackpack() : 0;
            AppliedStackSize = w.StackSize;
        }
        catch (Exception e)
        {
            KilnLog.Warn($"kiln-auto-loot: stack size / backpack recipes not applied: {e.Message}");
            Content.Revert();
            AppliedStackSize = 0;
            return;
        }
        KilnLog.Info($"kiln-auto-loot: stack size {(w.StackSize == 0 ? "the game's own" : w.StackSize.ToString())}: " +
                     $"{raised} items raised; best backpack {(w.BestBackpack ? $"on ({recipes} recipes)" : "off")}; " +
                     $"stack guard {(Content.StackGuard ? "on" : "off")}");
    }

    // every item whose game max is > 1 and below the wish gets the wish
    private static int ApplyStacks(int wish)
    {
        if (wish <= 0) return 0;
        var raised = 0;
        foreach (var item in Content.Items)
        {
            var original = (int)item.Original("maxStackSize").Integer;
            var target = WorldDataRules.StackTarget(original, wish);
            if (target == original || item.MaxStack == target) continue;
            item.MaxStack = target;
            raised++;
        }
        return raised;
    }

    // every Special_Backpack_* recipe gives Backpack_T3_NEW x1 for 1 of each ingredient
    private static int ApplyBackpack()
    {
        var best = RecipeStack.Of(WorldDataRules.BestBackpack, 1);
        var count = 0;
        foreach (var recipe in Content.Recipes)
        {
            if (!WorldDataRules.IsBackpackRecipe(recipe.Name)) continue;
            List<RecipeStack> outputs = recipe.Outputs.Count == 0 ? [best] : recipe.Outputs.Select(_ => best).ToList();
            recipe.SetOutputs(outputs);
            recipe.SetInputs(recipe.Inputs.Select(i => i with { Count = WorldDataRules.BackpackInputCount(i.Count) }).ToList());
            count++;
        }
        return count;
    }
}
