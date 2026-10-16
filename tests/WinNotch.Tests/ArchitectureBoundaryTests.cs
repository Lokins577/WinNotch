using System.Reflection;
using WinNotch.Models;
using WinNotch.Plugins;
using WinNotch.Services;
using Xunit;

namespace WinNotch.Tests;

public sealed class ArchitectureBoundaryTests
{
    [Fact]
    public void Core_DoesNotReferenceOtherWinNotchAssemblies()
    {
        AssertWinNotchReferences(typeof(AppSettings).Assembly);
    }

    [Fact]
    public void PluginSdk_DoesNotReferenceOtherWinNotchAssemblies()
    {
        AssertWinNotchReferences(typeof(IPlugin).Assembly);
    }

    [Fact]
    public void PluginHost_ReferencesOnlyPluginSdk()
    {
        AssertWinNotchReferences(typeof(PluginManager).Assembly, "WinNotch.PluginSdk");
    }

    [Fact]
    public void WindowsPlatform_ReferencesOnlyCore()
    {
        AssertWinNotchReferences(typeof(VolumeService).Assembly, "WinNotch.Core");
    }

    private static void AssertWinNotchReferences(Assembly assembly, params string[] expected)
    {
        var actual = assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .Where(name => name?.StartsWith("WinNotch", StringComparison.Ordinal) == true)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expected.OrderBy(name => name, StringComparer.Ordinal), actual);
    }
}
