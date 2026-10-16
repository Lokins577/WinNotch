using System.Runtime.InteropServices;

namespace WinNotch.Platform.Windows.InputEnhancements;

public static class InputInjector
{
    private const uint InputMouse = 0;
    private const uint InputKeyboard = 1;
    private const uint MouseWheel = 0x0800;
    private const uint MouseHWheel = 0x01000;
    private const uint MouseMiddleDown = 0x0020;
    private const uint MouseMiddleUp = 0x0040;
    private const uint MouseXDown = 0x0080;
    private const uint MouseXUp = 0x0100;
    private const uint KeyUp = 0x0002;
    private const uint KeyUnicode = 0x0004;

    public static void SendWheel(int delta, bool horizontal)
        => Send([Mouse(horizontal ? MouseHWheel : MouseWheel, unchecked((uint)delta))]);

    public static void SendMiddleClick()
        => Send([Mouse(MouseMiddleDown, 0), Mouse(MouseMiddleUp, 0)]);

    public static void SendXButton(int button)
        => Send([Mouse(MouseXDown, (uint)button), Mouse(MouseXUp, (uint)button)]);

    public static void SendChord(params ushort[] virtualKeys)
    {
        var inputs = new List<Input>();
        inputs.AddRange(virtualKeys.Select(key => Keyboard(key, 0)));
        inputs.AddRange(virtualKeys.Reverse().Select(key => Keyboard(key, KeyUp)));
        Send(inputs.ToArray());
    }

    public static void ReplaceText(int backspaces, string replacement, char delimiter)
    {
        var inputs = new List<Input>();
        for (int index = 0; index < backspaces; index++)
        {
            inputs.Add(Keyboard(0x08, 0));
            inputs.Add(Keyboard(0x08, KeyUp));
        }
        foreach (char value in replacement + delimiter)
        {
            inputs.Add(Unicode(value, false));
            inputs.Add(Unicode(value, true));
        }
        Send(inputs.ToArray());
    }

    private static Input Mouse(uint flags, uint data) => new()
    {
        Type = InputMouse,
        Union = new InputUnion { Mouse = new MouseInput { MouseData = data, Flags = flags } }
    };

    private static Input Keyboard(ushort key, uint flags) => new()
    {
        Type = InputKeyboard,
        Union = new InputUnion { Keyboard = new KeyboardInput { VirtualKey = key, Flags = flags } }
    };

    private static Input Unicode(char value, bool up) => new()
    {
        Type = InputKeyboard,
        Union = new InputUnion
        {
            Keyboard = new KeyboardInput
            {
                ScanCode = value,
                Flags = KeyUnicode | (up ? KeyUp : 0)
            }
        }
    };

    private static void Send(Input[] inputs)
    {
        if (inputs.Length == 0) return;
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Input { public uint Type; public InputUnion Union; }
    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MouseInput Mouse;
        [FieldOffset(0)] public KeyboardInput Keyboard;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int X;
        public int Y;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }
    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, Input[] inputs, int size);
}
