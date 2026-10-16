using System.IO;
using WinNotch.QuickTools;
using Xunit;

namespace WinNotch.Tests;

public sealed class RadialMenuTests
{
    [Fact]
    public void Normalize_TrimsBoundsAndRepairsIdentifiers()
    {
        var duplicate = Guid.NewGuid();
        var configuration = new RadialMenuConfiguration
        {
            Items = Enumerable.Range(0, 15)
                .Select(index => new RadialMenuItem
                {
                    Id = index < 2 ? duplicate : Guid.Empty,
                    Label = $"  Action {index}  ",
                    Kind = RadialActionKind.CommandBar,
                    Target = " ignored "
                })
                .Append(new RadialMenuItem { Label = " " })
                .ToList()
        };

        configuration.Normalize();

        Assert.Equal(RadialMenuConfiguration.MaximumItems, configuration.Items.Count);
        Assert.Equal(configuration.Items.Count, configuration.Items.Select(item => item.Id).Distinct().Count());
        Assert.All(configuration.Items, item =>
        {
            Assert.NotEqual(Guid.Empty, item.Id);
            Assert.Equal(item.Label.Trim(), item.Label);
            Assert.Null(item.Target);
        });
    }

    [Fact]
    public void Normalize_EmptyConfigurationRestoresDefaults()
    {
        var configuration = new RadialMenuConfiguration
        {
            Items = [new RadialMenuItem { Label = " " }]
        };

        configuration.Normalize();

        Assert.Equal(6, configuration.Items.Count);
        Assert.Contains(configuration.Items, item => item.Kind == RadialActionKind.CommandBar);
    }

    [Theory]
    [InlineData("https://example.com/path", true)]
    [InlineData("http://example.com", true)]
    [InlineData("file:///C:/Windows", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("relative/path", false)]
    public void TargetPolicy_AllowsOnlyHttpUrls(string target, bool expected)
    {
        bool valid = RadialActionTarget.TryResolve(
            RadialActionKind.Url, target, out _, out _);

        Assert.Equal(expected, valid);
    }

    [Fact]
    public void TargetPolicy_DistinguishesApplicationsFromFolders()
    {
        string root = Path.Combine(Path.GetTempPath(), "winnotch-radial-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string file = Path.Combine(root, "tool.exe");
        File.WriteAllText(file, "fixture");
        try
        {
            Assert.True(RadialActionTarget.TryResolve(
                RadialActionKind.Application, $"  {file}  ", out var application, out _));
            Assert.Equal(Path.GetFullPath(file), application);
            Assert.False(RadialActionTarget.TryResolve(
                RadialActionKind.Application, root, out _, out _));
            Assert.True(RadialActionTarget.TryResolve(
                RadialActionKind.File, root, out var folder, out _));
            Assert.Equal(Path.GetFullPath(root), folder);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
