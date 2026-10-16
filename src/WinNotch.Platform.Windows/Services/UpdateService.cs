using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using WinNotch.Platform.Windows.ProductHardening;
using System.Threading;
using System.Threading.Tasks;

namespace WinNotch.Services;

/// <summary>Outcome of an update check.</summary>
public enum UpdateCheckResult
{
    /// <summary>The installed version is the latest.</summary>
    UpToDate,

    /// <summary>A newer version is available and ready to download.</summary>
    UpdateAvailable,

    /// <summary>The check could not be completed (network/parse error). See update.log.</summary>
    CheckFailed
}

/// <summary>
    /// Self-update service. Detects new releases on GitHub and stays within the
    /// installed channel: signed MSIX via App Installer or signed Inno in-place update.
/// </summary>
public sealed class UpdateService
{
    private const string GitHubApiUrl = "https://api.github.com/repos/N3uralCreativity/WinNotch/releases/latest";

    private static readonly HttpClient _http = new()
    {
        DefaultRequestHeaders =
        {
            { "User-Agent", "WinNotch-Updater" },
            { "Accept", "application/vnd.github+json" }
        },
        Timeout = TimeSpan.FromSeconds(15)
    };

    // Separate client for large binary downloads — no API headers, longer timeout.
    private static readonly HttpClient _downloadHttp = new(new HttpClientHandler
    {
        AllowAutoRedirect = true,
        MaxAutomaticRedirections = 10
    })
    {
        DefaultRequestHeaders =
        {
            { "User-Agent", "WinNotch-Updater" }
        },
        Timeout = TimeSpan.FromMinutes(10)
    };

    // Update artifacts and logs live under %LOCALAPPDATA%\WinNotch (stable, per-user,
    // writable without elevation, and not subject to %TEMP% cleanup mid-update).
    private static readonly string AppDataRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinNotch");
    private static readonly string UpdatesDir = Path.Combine(AppDataRoot, "Updates");
    private static readonly string LogPath = Path.Combine(AppDataRoot, "Logs", "update.log");

    public string CurrentVersion { get; }
    public string? LatestVersion { get; private set; }
    public string? InstallerDownloadUrl { get; private set; }
    public bool UsesMsixUpdate => PackageIdentityService.HasIdentity;

    public bool IsUpdateAvailable => LatestVersion != null
        && Version.TryParse(LatestVersion, out var remote)
        && Version.TryParse(CurrentVersion, out var local)
        && remote > local;

    public UpdateService(string currentVersion)
    {
        CurrentVersion = Version.TryParse(currentVersion, out var parsed)
            ? parsed.ToString(3)
            : "0.0.0";
    }

    /// <summary>
    /// Queries the GitHub releases API for the latest release and records its
    /// version and installer asset URL.
    /// </summary>
    public async Task<UpdateCheckResult> CheckForUpdateAsync(CancellationToken ct = default)
    {
        try
        {
            Log($"Checking for updates (installed v{CurrentVersion})...");

            var release = await _http.GetFromJsonAsync<GitHubRelease>(GitHubApiUrl, ct);
            if (release?.TagName == null)
            {
                Log("Check failed: release response contained no tag_name.");
                return UpdateCheckResult.CheckFailed;
            }

            // Strip leading 'v' from the tag (e.g. "v0.7.0" -> "0.7.0").
            LatestVersion = release.TagName.TrimStart('v', 'V');

            // Packaged builds stay in the MSIX update path; unpackaged builds use Inno.
            InstallerDownloadUrl = null;
            if (release.Assets != null)
            {
                string? selectedName = SelectAssetName(
                    release.Assets.Where(asset => asset.Name != null).Select(asset => asset.Name!),
                    UsesMsixUpdate);
                InstallerDownloadUrl = release.Assets.FirstOrDefault(asset =>
                    string.Equals(asset.Name, selectedName, StringComparison.OrdinalIgnoreCase))?.BrowserDownloadUrl;
            }

            Log($"Latest release: v{LatestVersion}; installer asset: {InstallerDownloadUrl ?? "<none>"}.");

            if (!IsUpdateAvailable)
                return UpdateCheckResult.UpToDate;

            if (InstallerDownloadUrl == null)
            {
                // A newer version exists but the release has no installer attached —
                // treat as a failure so the user isn't offered a broken update.
                Log("A newer version exists but no compatible installer asset is attached to the release.");
                return UpdateCheckResult.CheckFailed;
            }

            return UpdateCheckResult.UpdateAvailable;
        }
        catch (Exception ex)
        {
            Log($"Check failed: {ex.GetType().Name}: {ex.Message}");
            return UpdateCheckResult.CheckFailed;
        }
    }

    /// <summary>
    /// Downloads the latest installer to a stable location and launches it in silent
    /// update mode, then shuts the app down so the installer can replace it in place
    /// and relaunch the new version.
    /// </summary>
    /// <returns>(true, null) on success; otherwise (false, short user-facing error).</returns>
    public async Task<(bool Ok, string? Error)> DownloadAndLaunchInstallerAsync(
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        if (InstallerDownloadUrl == null)
        {
            Log("Update aborted: no installer URL (run a check first).");
            return (false, "No installer found for this release.");
        }

        string fileName = UsesMsixUpdate
            ? $"WinNotch-{LatestVersion}-x64.msix"
            : $"WinNotch-{LatestVersion}-Setup.exe";
        var finalPath = Path.Combine(UpdatesDir, fileName);
        var partPath = finalPath + ".part";

        try
        {
            Directory.CreateDirectory(UpdatesDir);
            Log($"Downloading {InstallerDownloadUrl} -> {finalPath}");

            long totalBytes = -1;
            long bytesRead = 0;

            using (var response = await _downloadHttp.GetAsync(InstallerDownloadUrl,
                HttpCompletionOption.ResponseHeadersRead, ct))
            {
                response.EnsureSuccessStatusCode();

                totalBytes = response.Content.Headers.ContentLength ?? -1;
                await using var contentStream = await response.Content.ReadAsStreamAsync(ct);
                await using var fileStream = new FileStream(partPath, FileMode.Create,
                    FileAccess.Write, FileShare.None, 81920, useAsync: true);

                var buffer = new byte[81920];
                int read;
                while ((read = await contentStream.ReadAsync(buffer, ct)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, read), ct);
                    bytesRead += read;
                    if (totalBytes > 0)
                        progress?.Report((double)bytesRead / totalBytes);
                }

                await fileStream.FlushAsync(ct);
            }

            // Verify the download is complete (detect truncated transfers).
            if (totalBytes > 0 && bytesRead != totalBytes)
            {
                Log($"Download truncated: got {bytesRead} of {totalBytes} bytes.");
                TryDelete(partPath);
                return (false, "Download incomplete — please retry.");
            }

            Log($"Downloaded {bytesRead} bytes.");
            progress?.Report(1.0);

            // Atomically move the completed download into place (same volume).
            File.Move(partPath, finalPath, overwrite: true);

            // Remove the download-zone marker; release assets are independently signed.
            TryRemoveMarkOfTheWeb(finalPath);

            if (UsesMsixUpdate)
            {
                Process.Start(new ProcessStartInfo(finalPath) { UseShellExecute = true });
                Log("Signed MSIX handed to Windows App Installer; current app remains open for user review.");
                return (true, null);
            }

            // Launch the installer in silent update mode. /UPDATE=1 tells the installer
            // to overwrite the app in place (no menus) and relaunch it afterwards.
            Log("Launching installer: /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /UPDATE=1");
            Process? installer;
            try
            {
                installer = Process.Start(new ProcessStartInfo
                {
                    FileName = finalPath,
                    Arguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /UPDATE=1",
                    UseShellExecute = true,
                    WorkingDirectory = UpdatesDir
                });
            }
            catch (Win32Exception wex)
            {
                Log($"Failed to launch installer: Win32 0x{wex.NativeErrorCode:X}: {wex.Message}");
                return (false, "Installer was blocked (antivirus/SmartScreen?) — see update.log.");
            }

            if (installer == null)
            {
                Log("Failed to launch installer: Process.Start returned null.");
                return (false, "Couldn't start the installer — see update.log.");
            }

            Log($"Installer started (pid {installer.Id}). Shutting down to apply update.");

            // Give the installer a moment to spin up, then exit gracefully so our
            // OnExit releases the single-instance mutex before the installer's
            // taskkill fires — allowing the new version to relaunch cleanly.
            await Task.Delay(300, CancellationToken.None);

            var application = System.Windows.Application.Current;
            application?.Dispatcher.BeginInvoke(new Action(application.Shutdown));

            return (true, null);
        }
        catch (OperationCanceledException)
        {
            Log("Update canceled by caller.");
            TryDelete(partPath);
            return (false, "Update canceled.");
        }
        catch (HttpRequestException hex)
        {
            Log($"Download failed: {hex.Message}");
            TryDelete(partPath);
            return (false, "Download failed — check your connection.");
        }
        catch (Exception ex)
        {
            Log($"Update failed: {ex.GetType().Name}: {ex.Message}");
            TryDelete(partPath);
            return (false, "Update failed — see update.log.");
        }
    }

    public static string? SelectAssetName(IEnumerable<string> names, bool packaged)
    {
        string suffix = packaged ? "-x64.msix" : "-Setup.exe";
        return names.FirstOrDefault(name => name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Deletes the NTFS Zone.Identifier alternate data stream (Mark-of-the-Web) if
    /// present, so the unsigned installer launches without a SmartScreen prompt.
    /// </summary>
    private static void TryRemoveMarkOfTheWeb(string path)
    {
        var adsPath = path + ":Zone.Identifier";
        try
        {
            if (File.Exists(adsPath))
            {
                File.Delete(adsPath);
                Log("Removed Mark-of-the-Web (Zone.Identifier).");
            }
        }
        catch (Exception ex)
        {
            Log($"Could not remove Mark-of-the-Web (non-fatal): {ex.Message}");
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Best-effort cleanup only.
        }
    }

    /// <summary>Appends a timestamped line to update.log. Never throws.</summary>
    private static void Log(string message)
    {
        try
        {
            var dir = Path.GetDirectoryName(LogPath);
            if (dir != null)
                Directory.CreateDirectory(dir);
            File.AppendAllText(LogPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}");
        }
        catch
        {
            // Logging must never interfere with the update itself.
        }
    }

    // --- GitHub API DTOs ---

    private sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string? TagName { get; set; }

        [JsonPropertyName("assets")]
        public GitHubAsset[]? Assets { get; set; }
    }

    private sealed class GitHubAsset
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("browser_download_url")]
        public string? BrowserDownloadUrl { get; set; }
    }
}
