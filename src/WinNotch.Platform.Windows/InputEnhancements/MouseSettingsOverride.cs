using System.Runtime.InteropServices;
using WinNotch.InputEnhancements;

namespace WinNotch.Platform.Windows.InputEnhancements;

public sealed class MouseSettingsOverride : IDisposable
{
    private const uint GetMouse = 0x0003;
    private const uint SetMouse = 0x0004;
    private const uint GetMouseSpeed = 0x0070;
    private const uint SetMouseSpeed = 0x0071;
    private int[]? _originalAcceleration;
    private int _originalSpeed;
    private bool _applied;

    public void Apply(InputEnhancementPreferences preferences)
    {
        if (!preferences.OverrideMouseAcceleration)
        {
            Restore();
            return;
        }
        if (!_applied)
        {
            var original = new int[3];
            if (!SystemParametersInfo(GetMouse, 0, original, 0))
                throw new InvalidOperationException("Could not read Windows mouse acceleration settings.");
            int speed = 0;
            if (!SystemParametersInfo(GetMouseSpeed, 0, ref speed, 0))
                throw new InvalidOperationException("Could not read Windows mouse speed.");
            _originalAcceleration = original;
            _originalSpeed = speed;
            _applied = true;
        }
        var values = new[]
        {
            preferences.AccelerationThreshold1,
            preferences.AccelerationThreshold2,
            preferences.AccelerationMode
        };
        try
        {
            if (!SystemParametersInfo(SetMouse, 0, values, 0))
                throw new InvalidOperationException("Windows rejected the mouse acceleration override.");
            int requestedSpeed = preferences.MouseSpeed;
            if (!SystemParametersInfo(SetMouseSpeed, 0, ref requestedSpeed, 0))
                throw new InvalidOperationException("Windows rejected the mouse speed override.");
        }
        catch
        {
            Restore();
            throw;
        }
    }

    public void Restore()
    {
        if (!_applied || _originalAcceleration == null) return;
        SystemParametersInfo(SetMouse, 0, _originalAcceleration, 0);
        int speed = _originalSpeed;
        SystemParametersInfo(SetMouseSpeed, 0, ref speed, 0);
        _applied = false;
        _originalAcceleration = null;
    }

    public void Dispose() => Restore();

    [DllImport("user32.dll", SetLastError = true, EntryPoint = "SystemParametersInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint action, uint parameter, int[] value, uint flags);
    [DllImport("user32.dll", SetLastError = true, EntryPoint = "SystemParametersInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint action, uint parameter, ref int value, uint flags);
}
