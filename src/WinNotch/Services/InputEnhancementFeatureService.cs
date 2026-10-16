using WinNotch.Features;
using WinNotch.InputEnhancements;
using WinNotch.Platform.Windows.InputEnhancements;
using ThreadingTimer = System.Threading.Timer;

namespace WinNotch.Services;

public sealed class InputEnhancementFeatureService : IFeatureModule, IDisposable
{
    private const int WmMouseMove = 0x0200;
    private const int WmMouseWheel = 0x020A;
    private const int WmMouseHWheel = 0x020E;
    private const int WmXButtonDown = 0x020B;
    private const int WmXButtonUp = 0x020C;
    private const int VkRightWin = 0x5C;
    private const int VkI = 0x49;
    private readonly InputEnhancementPreferences _preferences;
    private readonly GlobalInputHook _hook = new();
    private readonly MouseSettingsOverride _mouseSettings = new();
    private readonly ScrollDeltaTransformer _scroll = new();
    private readonly TextExpansionEngine _expansions;
    private readonly object _expansionSync = new();
    private ThreadingTimer? _contextTimer;
    private string? _pointerProcess;
    private string? _foregroundProcess;
    private bool _focusedIsPassword = true;
    private int _mouseX;
    private int _mouseY;
    private long _lastMouseMove;
    private long _lastX1;
    private long _lastX2;
    private bool _suppressX1;
    private bool _suppressX2;
    private bool _superDown;
    private bool _settingsChordDown;
    private int _contextBusy;

    public InputEnhancementFeatureService(InputEnhancementPreferences preferences)
    {
        _preferences = preferences ?? throw new ArgumentNullException(nameof(preferences));
        _preferences.Normalize();
        _expansions = new TextExpansionEngine(() => _preferences.TextExpansions);
        _hook.MouseInput = OnMouseInput;
        _hook.KeyboardInput = OnKeyboardInput;
    }

    public FeatureId Id => BuiltInFeatures.InputEnhancements.Id;
    public event Action? OpenRequested;
    public event Action? CommandBarRequested;
    public event Action? QuickPanelRequested;

    public ValueTask StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _mouseSettings.Apply(_preferences);
        _hook.Start();
        _contextTimer = new ThreadingTimer(UpdateContext, null, TimeSpan.Zero, TimeSpan.FromMilliseconds(100));
        return ValueTask.CompletedTask;
    }

    public ValueTask StopAsync(CancellationToken cancellationToken)
    {
        _contextTimer?.Dispose();
        _contextTimer = null;
        _hook.Stop();
        _mouseSettings.Restore();
        _scroll.Reset();
        lock (_expansionSync) _expansions.Reset();
        _settingsChordDown = false;
        _superDown = false;
        _suppressX1 = _suppressX2 = false;
        return ValueTask.CompletedTask;
    }

    public void ApplyPreferences()
    {
        _preferences.Normalize();
        _mouseSettings.Apply(_preferences);
        _scroll.Reset();
        lock (_expansionSync) _expansions.Reset();
    }

    private bool OnMouseInput(GlobalMouseEvent value)
    {
        if (value.Injected) return false;
        if (value.Message == WmMouseMove)
        {
            _mouseX = value.X;
            _mouseY = value.Y;
            Interlocked.Exchange(ref _lastMouseMove, Environment.TickCount64);
            return false;
        }
        if (_preferences.IsExcluded(_pointerProcess)) return false;
        if (value.Message is WmMouseWheel or WmMouseHWheel)
        {
            bool horizontal = value.Message == WmMouseHWheel;
            bool changed = _preferences.ScrollMultiplier != 1
                           || (horizontal ? _preferences.ReverseHorizontalScroll : _preferences.ReverseVerticalScroll);
            if (!changed) return false;
            int delta = unchecked((short)((uint)value.Data >> 16));
            int transformed = _scroll.Transform(delta, horizontal, _preferences);
            if (transformed != 0) InputInjector.SendWheel(transformed, horizontal);
            return true;
        }
        if (value.Message is WmXButtonDown or WmXButtonUp)
        {
            int button = (int)((uint)value.Data >> 16) & 0xFFFF;
            SideButtonAction action = button == 1 ? _preferences.XButton1Action : _preferences.XButton2Action;
            if (action == SideButtonAction.None) return false;
            ref bool suppress = ref (button == 1 ? ref _suppressX1 : ref _suppressX2);
            if (value.Message == WmXButtonUp)
            {
                bool result = suppress;
                suppress = false;
                return result;
            }
            ref long last = ref (button == 1 ? ref _lastX1 : ref _lastX2);
            long now = Environment.TickCount64;
            suppress = true;
            if (now - last >= _preferences.SideButtonDebounceMilliseconds)
            {
                last = now;
                ExecuteSideAction(action);
            }
            return true;
        }
        return false;
    }

    private bool OnKeyboardInput(GlobalKeyboardEvent value)
    {
        if (value.Injected) return false;
        if (value.VirtualKey == VkI
            && (_settingsChordDown
                || (InputContextService.IsKeyDown(0x11) && InputContextService.IsKeyDown(0x12))))
        {
            if (value.KeyDown && !_settingsChordDown)
            {
                _settingsChordDown = true;
                OpenRequested?.Invoke();
            }
            else if (!value.KeyDown) _settingsChordDown = false;
            return true;
        }
        if (_preferences.IsExcluded(_foregroundProcess))
        {
            lock (_expansionSync) _expansions.Reset();
            return false;
        }
        if (value.VirtualKey == VkRightWin && _preferences.RightSuperKeyAction != SuperKeyAction.None)
        {
            if (value.KeyDown)
            {
                if (!_superDown) _superDown = true;
            }
            else if (_superDown)
            {
                _superDown = false;
                ExecuteSuperAction(_preferences.RightSuperKeyAction);
            }
            return true;
        }
        if (!value.KeyDown || !_preferences.TextExpansionEnabled || _focusedIsPassword)
        {
            if (_focusedIsPassword) lock (_expansionSync) _expansions.Reset();
            return false;
        }
        if (InputContextService.IsKeyDown(0x11) || InputContextService.IsKeyDown(0x12)
            || InputContextService.IsKeyDown(0x5B) || InputContextService.IsKeyDown(0x5C))
            return false;
        char? character = value.VirtualKey == 0x08
            ? '\b'
            : InputContextService.TranslateKey(value.VirtualKey, value.ScanCode);
        if (!character.HasValue) return false;
        TextExpansionMatch? match;
        lock (_expansionSync) match = _expansions.Accept(character.Value);
        if (match == null) return false;
        InputInjector.ReplaceText(match.Trigger.Length, match.Replacement, match.Delimiter);
        return true;
    }

    private void UpdateContext(object? state)
    {
        if (Interlocked.Exchange(ref _contextBusy, 1) != 0) return;
        try
        {
            _pointerProcess = InputContextService.GetProcessNameAt(_mouseX, _mouseY);
            string? nextForeground = InputContextService.GetForegroundProcessName();
            if (!string.Equals(_foregroundProcess, nextForeground, StringComparison.OrdinalIgnoreCase))
            {
                _foregroundProcess = nextForeground;
                lock (_expansionSync) _expansions.Reset();
            }
            _focusedIsPassword = !_preferences.TextExpansionEnabled
                                 || InputContextService.IsFocusedPasswordField();
            if (!_preferences.FocusFollowsMouse || _preferences.IsExcluded(_pointerProcess)) return;
            long movedAt = Interlocked.Read(ref _lastMouseMove);
            if (movedAt > 0 && Environment.TickCount64 - movedAt >= _preferences.FocusDelayMilliseconds)
                InputContextService.FocusWindowAt(_mouseX, _mouseY);
        }
        finally { Interlocked.Exchange(ref _contextBusy, 0); }
    }

    private void ExecuteSideAction(SideButtonAction action)
    {
        switch (action)
        {
            case SideButtonAction.BrowserBack: InputInjector.SendXButton(1); break;
            case SideButtonAction.BrowserForward: InputInjector.SendXButton(2); break;
            case SideButtonAction.MiddleClick: InputInjector.SendMiddleClick(); break;
            case SideButtonAction.CommandBar: CommandBarRequested?.Invoke(); break;
            case SideButtonAction.QuickPanel: QuickPanelRequested?.Invoke(); break;
        }
    }

    private void ExecuteSuperAction(SuperKeyAction action)
    {
        switch (action)
        {
            case SuperKeyAction.CommandBar: CommandBarRequested?.Invoke(); break;
            case SuperKeyAction.QuickPanel: QuickPanelRequested?.Invoke(); break;
            case SuperKeyAction.TaskView: InputInjector.SendChord(0x5B, 0x09); break;
        }
    }

    public void Dispose()
    {
        StopAsync(CancellationToken.None).AsTask().GetAwaiter().GetResult();
        _hook.Dispose();
        _mouseSettings.Dispose();
    }

    public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
}
