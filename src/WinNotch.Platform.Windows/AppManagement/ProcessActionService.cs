using System.Diagnostics;
using WinNotch.AppManagement;

namespace WinNotch.Platform.Windows.AppManagement;

public sealed class ProcessActionService
{
    public ProcessActionResult RequestClose(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            if (!ProcessTerminationPolicy.CanTarget(processId, process.ProcessName, out string reason))
                return new ProcessActionResult(false, reason);
            if (!process.CloseMainWindow())
                return new ProcessActionResult(false, "The process exposes no cooperative main-window close action.");
            return new ProcessActionResult(true, "Close requested. The application remains in control of save prompts.");
        }
        catch (Exception exception) { return new ProcessActionResult(false, exception.Message); }
    }

    public ProcessActionResult ForceTerminate(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            if (!ProcessTerminationPolicy.CanTarget(processId, process.ProcessName, out string reason))
                return new ProcessActionResult(false, reason);
            process.Kill(entireProcessTree: false);
            return new ProcessActionResult(true, "The selected process was force-terminated.");
        }
        catch (Exception exception) { return new ProcessActionResult(false, exception.Message); }
    }
}
