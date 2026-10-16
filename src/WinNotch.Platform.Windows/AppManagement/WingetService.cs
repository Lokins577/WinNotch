using System.Diagnostics;
using System.Text.RegularExpressions;
using WinNotch.AppManagement;

namespace WinNotch.Platform.Windows.AppManagement;

public sealed partial class WingetService
{
    private readonly Func<IReadOnlyList<string>, CancellationToken, Task<CommandResult>> _runner;

    public WingetService(Func<IReadOnlyList<string>, CancellationToken, Task<CommandResult>>? runner = null)
        => _runner = runner ?? RunAsync;

    public async Task<(IReadOnlyList<PackageUpgrade> Upgrades, string? Error)> DiscoverUpgradesAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _runner(
                ["upgrade", "--accept-source-agreements", "--disable-interactivity"],
                cancellationToken).ConfigureAwait(false);
            if (result.ExitCode != 0)
                return ([], string.IsNullOrWhiteSpace(result.Error) ? result.Output : result.Error);
            return (WingetOutputParser.ParseUpgrades(result.Output), null);
        }
        catch (Exception exception)
        {
            return ([], $"WinGet unavailable: {exception.Message}");
        }
    }

    public Process StartUpgrade(PackageUpgrade package)
    {
        ArgumentNullException.ThrowIfNull(package);
        if (!SafePackageId().IsMatch(package.PackageId))
            throw new InvalidOperationException("The package identifier is not safe to hand to WinGet.");
        var start = new ProcessStartInfo("winget") { UseShellExecute = true };
        start.ArgumentList.Add("upgrade");
        start.ArgumentList.Add("--id");
        start.ArgumentList.Add(package.PackageId);
        start.ArgumentList.Add("--exact");
        start.ArgumentList.Add("--interactive");
        start.ArgumentList.Add("--accept-package-agreements");
        start.ArgumentList.Add("--accept-source-agreements");
        return Process.Start(start) ?? throw new InvalidOperationException("WinGet did not start.");
    }

    private static async Task<CommandResult> RunAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo("winget")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)
                            ?? throw new InvalidOperationException("WinGet did not start.");
        Task<string> output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> error = process.StandardError.ReadToEndAsync(cancellationToken);
        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: false); }
            catch { }
            throw;
        }
        return new CommandResult(process.ExitCode, await output, await error);
    }

    public sealed record CommandResult(int ExitCode, string Output, string Error);

    [GeneratedRegex("^[A-Za-z0-9._-]{1,200}$")]
    private static partial Regex SafePackageId();
}
