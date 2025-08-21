using System.Diagnostics;
using System.Runtime.InteropServices;
using TwitchPlays.Twitch;

namespace TwitchPlays
{
    [StructLayout(LayoutKind.Explicit)]
    struct INPUT
    {
        [FieldOffset(0)]
        public int type;
        [FieldOffset(4)]
        public MOUSEINPUT mi;
        [FieldOffset(4)]
        public KEYBDINPUT ki;
        [FieldOffset(4)]
        public HARDWAREINPUT hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct HARDWAREINPUT
    {
        public int uMsg;
        public short wParamL;
        public short wParamH;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct KEYBDINPUT
    {
        public short wVk;
        public short wScan;
        public int dwFlags;
        public int time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public int mouseData;
        public int dwFlags;
        public int time;
        public IntPtr dwExtraInfo;
    }

    public partial class KeyPress
    {
        // const int INPUT_MOUSE = 0;
        const int INPUT_KEYBOARD = 1;
        // const int INPUT_HARDWARE = 2;

        // const int KEYEVENTF_EXTENDEDKEY = 0x0001;
        const int KEYEVENTF_KEYUP = 0x0002;

        // const ushort KEYEVENTF_KEYDOWN = 0x0000;
        const ushort KEYEVENTF_SCANCODE = 0x0008;
        // const ushort KEYEVENTF_UNICODE = 0x0004;

        [LibraryImport("user32.dll", EntryPoint = "VkKeyScanA", SetLastError = true)]
        internal static partial byte VkKeyScan(sbyte ch);

        [LibraryImport("user32.dll", EntryPoint = "MapVirtualKeyA", SetLastError = true)]
        internal static partial uint MapVirtualKey(uint uCode, uint uMapType);

        [LibraryImport("user32.dll", EntryPoint = "SendInput", SetLastError = true)]
        internal static partial uint SendInput(uint nInputs, [In] INPUT[] pInputs, int cbSize);

        public static short CharToScan(char key)
        {
            return (short)MapVirtualKey(VkKeyScan(Convert.ToSByte(key)), 0);
        }

        public IEnumerable<short> Keys { get; set; }
        // This was set to 100 to make sure DS picks up the key, but it probably only needs to be 1-2 frames (16-32 ms), unless you tested and found otherwise.
        public int Duration { get; set; } = 100;
        public bool IsCombo { get; set; } = false;

        public KeyPress(params short[] keys)
        {
            Keys = keys;
        }

        public KeyPress(params char[] keys)
        {
            Keys = [.. keys.Select(x => CharToScan(x))];
        }

        public KeyPress(char key, int duration)
        {
            Keys = [CharToScan(key)];
            Duration = duration;
        }

        public KeyPress(bool isCombo, params short[] keys)
        {
            Keys = keys;
            IsCombo = isCombo;
        }

        private static INPUT SetupInput()
        {
            var inputData = new INPUT
            {
                type = INPUT_KEYBOARD
            };
            inputData.ki.dwFlags = KEYEVENTF_SCANCODE;
            return inputData;
        }

        private static void ConvertToKeyUp(INPUT inputData)
        {
            inputData.ki.dwFlags = KEYEVENTF_KEYUP | KEYEVENTF_SCANCODE;
            inputData.ki.time = 0;
            inputData.ki.dwExtraInfo = IntPtr.Zero;
        }

        public void Send()
        {
            if (IsCombo)
            {
                var inputData = new INPUT[1] { SetupInput() };
                foreach (var key in Keys)
                {
                    inputData[0].ki.wScan = key;
                    SendInput((uint)inputData.Length, inputData, Marshal.SizeOf(typeof(INPUT)));
                }

                Thread.Sleep(Duration);

                ConvertToKeyUp(inputData[0]);
                foreach (var key in Keys)
                {
                    inputData[0].ki.wScan = key;
                    SendInput((uint)inputData.Length, inputData, Marshal.SizeOf(typeof(INPUT)));
                }
            }
            else
            {
                foreach (var key in Keys)
                {
                    var inputData = new INPUT[1] { SetupInput() };
                    inputData[0].ki.wScan = key;
                    SendInput((uint)inputData.Length, inputData, Marshal.SizeOf(typeof(INPUT)));
                    Thread.Sleep(Duration);
                    ConvertToKeyUp(inputData[0]);
                    SendInput((uint)inputData.Length, inputData, Marshal.SizeOf(typeof(INPUT)));
                }
            }
        }
    }

    public partial class ChatController
    {
        [LibraryImport("User32.dll")]
        internal static partial int SetForegroundWindow(IntPtr point);

        private readonly Dictionary<string, KeyPress> _commands = new()
        {
            // M preceds movement. MF = Move Forward, MB = Move Backwards, etc.
            { "mf", new KeyPress('W', 1000) },
            { "mb", new KeyPress('S', 1000) },
            { "ml", new KeyPress('A', 1000) },
            { "mr", new KeyPress('D', 1000) },
            // Camera Up, Down, Left, Right
            { "cu", new KeyPress('I') },
            { "cd", new KeyPress('K') },
            { "cl", new KeyPress('J') },
            { "cr", new KeyPress('L') },
            // Lock on/off
            { "l", new KeyPress('O') },
            // Use item
            { "u", new KeyPress('E') },
            // 2h toggle
            { "y", new KeyPress(56) },
            // Attacks
            { "r1", new KeyPress('H') },
            { "r2", new KeyPress('U') },
            { "l1", new KeyPress(42) },
            { "l2", new KeyPress(15) },
            // Rolling directions
            { "rl", new KeyPress('A', ' ') },
            { "rr", new KeyPress('D', ' ') },
            { "rf", new KeyPress('W', ' ') },
            { "rb", new KeyPress('S', ' ') },
            { "x", new KeyPress(false, KeyPress.CharToScan('Q'), 28) },
            // switch LH weap
            { "dl", new KeyPress('C') },
            { "dr", new KeyPress('V') },
            { "du", new KeyPress('R') },
            { "dd", new KeyPress('F') }
        };

        private readonly ITwitchIrcClient IrcClient;
        private readonly CancellationTokenSource CancellationTokenSource;

        public ChatController(ITwitchIrcClient irc, CancellationTokenSource cancellationTokenSource)
        {
            IrcClient = irc;
            CancellationTokenSource = cancellationTokenSource;
        }

        public async Task Play()
        {
            Process[] p = Process.GetProcessesByName("DARKSOULS");
            IntPtr h = (IntPtr)0;
            if (p.Length > 0)
            {
                h = p[0].MainWindowHandle;
                _ = SetForegroundWindow(h);
            }

            while (!CancellationTokenSource.IsCancellationRequested)
            {
                // message[0] has username, message[1] has message
                IEnumerable<IrcMessage> messages = await IrcClient.Process();
                foreach (var message in messages.Where(x => !string.IsNullOrWhiteSpace(x.Message)))
                {
                    var command = message.Message.Split(' ')[0].ToLower();
                    if (_commands.TryGetValue(command, out var keyPress))
                    {
                        keyPress.Send();
                    }
                }
                await Task.Delay(10);
            }
        }
    }
}
