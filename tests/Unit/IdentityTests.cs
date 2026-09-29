using Ut4Recon.Core;

namespace Ut4Recon.UnitTests;

public class IdentityTests
{
    [Fact]
    public void ObjectIdentityIsStableAndClassSensitive()
    {
        var first = Identity.ObjectId("/Game/Maps/Test", "Test.PersistentLevel.Floor", "/Script/Engine.StaticMeshActor");
        Assert.Equal(first, Identity.ObjectId("/Game/Maps/Test", "Test.PersistentLevel.Floor", "/Script/Engine.StaticMeshActor"));
        Assert.NotEqual(first, Identity.ObjectId("/Game/Maps/Test", "Test.PersistentLevel.Floor", "/Script/Engine.Brush"));
        Assert.Equal(64, first.Length);
    }

    [Theory]
    [InlineData("../../../UnrealTournament/Content/Maps/Glass/Glass.umap", "/Game/Maps/Glass/Glass")]
    [InlineData("UnrealTournament/Content/Maps/Glass/Blueprints/Block.uasset", "/Game/Maps/Glass/Blueprints/Block")]
    [InlineData("Engine/Content/BasicShapes/Cube.uasset", "/Engine/BasicShapes/Cube")]
    public void ConvertsPakPathsToPackagePaths(string input, string expected) => Assert.Equal(expected, PackagePaths.FromInternalPath(input));
}
