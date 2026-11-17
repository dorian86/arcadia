using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace Arcadia.Services;

public enum PadButton { Up, Down, Left, Right, A, B, X, Y, LB, RB, Start, Back }

/// <summary>
/// XInput polling (Xbox and compatible controllers). Inputs are only raised while
/// <see cref="CanHandle"/> returns true, so a game running in the foreground never drives Arcadia.
/// </summary>
public sealed class GamepadService
{
    private readonly DispatcherTimer _timer;
    private ushort _previous;
    private readonly Dictionary<PadButton, DateTime> _repeatAt = new();
    private bool _available = true;
    private int _idleSlowdown;

    public event Action<PadButton>? Pressed;
    public event Action<bool>? ConnectionChanged;

    public Func<bool> CanHandle { get; set; } = () => true;
    public bool IsConnected { get; private set; }

    /// <summary>Fast polling while the TV mode is open, slow polling otherwise.</summary>
    public bool FastMode
    {
        set => _timer.Interval = TimeSpan.FromMilliseconds(value ? 16 : 120);
    }

    public GamepadService()
    {
        _timer = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(120) };
        _timer.Tick += (_, _) => Poll();
    }

    public void Start() => _timer.Start();

    private void Poll()
    {
        if (!_available) return;
        if (!IsConnected && _idleSlowdown++ % 10 != 0) return; // probe disconnected pads ~1x/s
        XINPUT_STATE state;
        uint result;
        try { result = XInputGetState(0, out state); }
        catch (DllNotFoundException) { _available = false; return; }
        catch (EntryPointNotFoundException) { _available = false; return; }

        var connected = result == 0;
        if (connected != IsConnected)
        {
            IsConnected = connected;
            ConnectionChanged?.Invoke(connected);
        }
        if (!connected) { _previous = 0; return; }

        var buttons = state.Gamepad.wButtons;
        // Left stick as d-pad
        const short dead = 16000;
        if (state.Gamepad.sThumbLX < -dead) buttons |= 0x0004;
        if (state.Gamepad.sThumbLX > dead) buttons |= 0x0008;
        if (state.Gamepad.sThumbLY > dead) buttons |= 0x0001;
        if (state.Gamepad.sThumbLY < -dead) buttons |= 0x0002;

        if (!CanHandle())
        {
            _previous = buttons;
            return;
        }

        var now = DateTime.Now;
        foreach (var (mask, button, repeats) in Map)
        {
            var down = (buttons & mask) != 0;
            var was = (_previous & mask) != 0;
            if (down && !was)
            {
                Pressed?.Invoke(button);
                if (repeats) _repeatAt[button] = now.AddMilliseconds(380);
            }
            else if (down && repeats && _repeatAt.TryGetValue(button, out var at) && now >= at)
            {
                Pressed?.Invoke(button);
                _repeatAt[button] = now.AddMilliseconds(110);
            }
        }
        _previous = buttons;
    }

    private static readonly (ushort Mask, PadButton Button, bool Repeats)[] Map =
    {
        (0x0001, PadButton.Up, true), (0x0002, PadButton.Down, true),
        (0x0004, PadButton.Left, true), (0x0008, PadButton.Right, true),
        (0x1000, PadButton.A, false), (0x2000, PadButton.B, false),
        (0x4000, PadButton.X, false), (0x8000, PadButton.Y, false),
        (0x0100, PadButton.LB, false), (0x0200, PadButton.RB, false),
        (0x0010, PadButton.Start, false), (0x0020, PadButton.Back, false),
    };

    [StructLayout(LayoutKind.Sequential)]
    private struct XINPUT_GAMEPAD
    {
        public ushort wButtons;
        public byte bLeftTrigger, bRightTrigger;
        public short sThumbLX, sThumbLY, sThumbRX, sThumbRY;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XINPUT_STATE
    {
        public uint dwPacketNumber;
        public XINPUT_GAMEPAD Gamepad;
    }

    [DllImport("xinput1_4.dll")]
    private static extern uint XInputGetState(uint index, out XINPUT_STATE state);
}
