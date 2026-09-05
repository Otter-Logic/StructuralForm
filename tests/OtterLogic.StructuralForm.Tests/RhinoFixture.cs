using System.Runtime.CompilerServices;
using Rhino.Runtime.InProcess;
using Xunit;

namespace OtterLogic.StructuralForm.Tests;

/// <summary>
/// Boots the installed Rhino in-process so tests can build real curves.
/// Point3d and friends are managed structs and need none of this; anything
/// backed by native geometry does.
/// </summary>
internal static class RhinoBootstrap
{
    [ModuleInitializer]
    internal static void Initialize() => RhinoInside.Resolver.Initialize();
}

public sealed class RhinoFixture : IDisposable
{
    private readonly RhinoCore _core;

    public RhinoFixture() => _core = new RhinoCore(new[] { "/nosplash" }, WindowStyle.NoWindow);

    public void Dispose() => _core.Dispose();
}

[CollectionDefinition(Name)]
public sealed class RhinoCollection : ICollectionFixture<RhinoFixture>
{
    public const string Name = "Rhino in-process";
}
