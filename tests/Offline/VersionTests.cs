// SPDX-License-Identifier: MIT
// The version the mod says it is (kiln.log's "Kiln Auto Loot <version>: world up", the F8 title) is the package's.
using Xunit;

namespace KilnAutoLoot.Offline;

public sealed class VersionTests
{
    [Fact]
    public void The_version_constant_is_the_package_version() =>
        Assert.Equal(typeof(AutoLootMod).Assembly.GetName().Version!.ToString(3), AutoLootMod.Version);

    [Fact]
    public void This_release_is_0_2_13() => Assert.Equal("0.2.13", AutoLootMod.Version);
}
