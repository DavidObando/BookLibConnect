using System;
using System.IO;
using System.Threading.Tasks;
using Oahu.Aux;
using Oahu.Cli.App.Core;
using Xunit;

namespace Oahu.Cli.Tests.App;

/// <summary>
/// Tests for <see cref="CoreEnvironment"/>'s path-override behaviour. These run
/// before any other test that touches <see cref="ApplEnv"/> paths sticks the
/// process to a real-name root, so they live in their own file and use a
/// deliberately-unique application name.
/// </summary>
public class CoreEnvironmentTests
{
    [Fact]
    public void Initialize_With_Same_Name_Is_Idempotent()
    {
        // The first test to call Initialize wins for the lifetime of the
        // process (xunit runs each assembly in one AppDomain). Use the same
        // name across repeated calls and assert no throw.
        var name = ApplEnv.ApplName;
        // Either the default name (assembly) or one set by an earlier test —
        // both cases are exercised by re-applying the current name.
        CoreEnvironment.Initialize(name);
        CoreEnvironment.Initialize(name);
        Assert.Equal(name, ApplEnv.ApplName);
    }

    [Fact]
    public void Initialize_With_Different_Name_Throws_When_Already_Initialized()
    {
        // Force a known initial state by initializing with the current name.
        CoreEnvironment.Initialize(ApplEnv.ApplName);
        var ex = Assert.Throws<InvalidOperationException>(
            () => CoreEnvironment.Initialize("definitely-not-the-current-applname"));
        Assert.Contains("already initialized", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OverrideApplName_Reroutes_Local_Application_Directory()
    {
        // Spot-check ApplEnv directly: changing ApplName must update derived paths.
        var prior = ApplEnv.ApplName;
        try
        {
            ApplEnv.OverrideApplName("oahu-cli-test-marker");
            Assert.Equal("oahu-cli-test-marker", ApplEnv.ApplName);
            Assert.EndsWith(
                Path.Combine("oahu-cli-test-marker", "settings"),
                ApplEnv.SettingsDirectory,
                StringComparison.Ordinal);
            Assert.EndsWith("oahu-cli-test-marker", ApplEnv.LocalApplDirectory, StringComparison.Ordinal);
        }
        finally
        {
            // Restore so other tests in the same assembly aren't affected.
            ApplEnv.OverrideApplName(prior);
        }
    }
}
