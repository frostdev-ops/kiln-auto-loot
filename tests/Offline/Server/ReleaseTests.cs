// SPDX-License-Identifier: MIT
// The "released" rule (src/Server/Release.cs).
using KilnAutoLoot.Core;
using KilnAutoLoot.Server;
using Xunit;

namespace KilnAutoLoot.Offline.LootServerTests;

public sealed class ReleaseTests
{
    [Theory]
    [InlineData(Group.Corpse, true)]
    [InlineData(Group.Chest, true)]
    [InlineData(Group.Plant, true)]
    [InlineData(Group.Farm, true)]
    [InlineData(Group.Ground, true)]
    [InlineData(Group.Boss, false)]
    [InlineData(Group.Collectible, false)]
    [InlineData(Group.Fish, false)]
    [InlineData(Group.Unknown, false)]
    public void Container_targets_are_released_by_their_group(Group g, bool released) =>
        Assert.Equal(released, Release.ContainerReleased(Fixtures.Rule(g), dead: false));

    [Fact]
    public void An_enemy_is_released_only_when_dead()
    {
        var enemy = Fixtures.Rule(Group.Enemy);
        Assert.False(Release.ContainerReleased(enemy, dead: false));
        Assert.True(Release.ContainerReleased(enemy, dead: true));
    }

    [Fact]
    public void Critters_take_the_pickup_path_like_mushrooms_even_when_their_pickup_names_no_item()
    {
        Assert.False(Release.IsContainerTarget(Fixtures.Rule(Group.Critter, TemplateFlags.Swarm), hasPickupItem: true));
        Assert.False(Release.IsContainerTarget(Fixtures.Rule(Group.Critter, TemplateFlags.Swarm), hasPickupItem: false));
        Assert.True(Release.PickupReleased(Fixtures.Rule(Group.Critter, TemplateFlags.Swarm)));
        Assert.False(Release.IsContainerTarget(Fixtures.Rule(Group.Plant), hasPickupItem: true));
        Assert.True(Release.IsContainerTarget(Fixtures.Rule(Group.Chest), hasPickupItem: false));
    }

    [Fact]
    public void Only_living_non_animal_enemies_pause()
    {
        Assert.True(Release.IsPausingEnemy(Fixtures.Rule(Group.Enemy, TemplateFlags.Boss), dead: false));
        Assert.False(Release.IsPausingEnemy(Fixtures.Rule(Group.Enemy, TemplateFlags.Animal), dead: false));
        Assert.False(Release.IsPausingEnemy(Fixtures.Rule(Group.Enemy), dead: true));
        Assert.False(Release.IsPausingEnemy(Fixtures.Rule(Group.Boss), dead: false));
    }

    [Fact]
    public void Pickups_are_released_except_boss_and_enemy()
    {
        Assert.True(Release.PickupReleased(Fixtures.Rule(Group.Ground)));
        Assert.False(Release.PickupReleased(Fixtures.Rule(Group.Boss)));
    }
}
