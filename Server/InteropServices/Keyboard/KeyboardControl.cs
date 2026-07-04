using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Server.InteropServices
{
	/// <summary>
	/// Provides native Windows keyboard input, hotkey, and Unicode text typing operations.
	/// </summary>
	[SupportedOSPlatform("windows")]
	public static class KeyboardControl
	{
		internal static class Imports
		{
			[StructLayout(LayoutKind.Sequential)]
			internal struct INPUT
			{
				public int type;
				public INPUTUNION u;
			}

			[StructLayout(LayoutKind.Explicit)]
			internal struct INPUTUNION
			{
				[FieldOffset(0)]
				public MOUSEINPUT mi;

				[FieldOffset(0)]
				public KEYBDINPUT ki;
			}

			[StructLayout(LayoutKind.Sequential)]
			internal struct MOUSEINPUT
			{
				public int dx;
				public int dy;
				public uint mouseData;
				public uint dwFlags;
				public uint time;
				public IntPtr dwExtraInfo;
			}

			[StructLayout(LayoutKind.Sequential)]
			internal struct KEYBDINPUT
			{
				public ushort wVk;
				public ushort wScan;
				public uint dwFlags;
				public uint time;
				public IntPtr dwExtraInfo;
			}

			internal const int INPUT_KEYBOARD = 1;

			internal const uint KEYEVENTF_KEYUP = 0x0002;
			internal const uint KEYEVENTF_UNICODE = 0x0004;

			[DllImport("user32.dll", SetLastError = true)]
			internal static extern uint SendInput(
				uint nInputs,
				INPUT[] pInputs,
				int cbSize);
		}

		/// <summary>
		/// Simulates a key press (key down) for the specified virtual key code.
		/// </summary>
		public static void KeyDown(ushort virtualKeyCode)
		{
			SendKeyboardInput(new Imports.KEYBDINPUT
			{
				wVk = virtualKeyCode,
				dwFlags = 0
			});
		}

		/// <summary>
		/// Simulates a key press (key down) for the specified virtual key.
		/// </summary>
		public static void KeyDown(VirtualKey key)
		{
			KeyDown((ushort)key);
		}

		/// <summary>
		/// Simulates a key release (key up) for the specified virtual key code.
		/// </summary>
		public static void KeyUp(ushort virtualKeyCode)
		{
			SendKeyboardInput(new Imports.KEYBDINPUT
			{
				wVk = virtualKeyCode,
				dwFlags = Imports.KEYEVENTF_KEYUP
			});
		}

		/// <summary>
		/// Simulates a key release (key up) for the specified virtual key.
		/// </summary>
		public static void KeyUp(VirtualKey key)
		{
			KeyUp((ushort)key);
		}

		/// <summary>
		/// Simulates a key press (key down followed by key up) for the specified virtual key code.
		/// </summary>
		public static void Press(ushort virtualKeyCode)
		{
			KeyDown(virtualKeyCode);
			Thread.Sleep(25);
			KeyUp(virtualKeyCode);
		}

		/// <summary>
		/// Simulates a key press (key down followed by key up) for the specified virtual key.
		/// </summary>
		public static void Press(VirtualKey key)
		{
			Press((ushort)key);
		}

		/// <summary>
		/// Simulates a hotkey combination by pressing and releasing the specified virtual key codes in order.
		/// </summary>
		public static void Hotkey(params ushort[] virtualKeyCodes)
		{
			foreach (var key in virtualKeyCodes)
			{
				KeyDown(key);
				Thread.Sleep(10);
			}

			for (var i = virtualKeyCodes.Length - 1; i >= 0; i--)
			{
				KeyUp(virtualKeyCodes[i]);
				Thread.Sleep(10);
			}
		}

		/// <summary>
		/// Simulates a hotkey combination by pressing and releasing the specified virtual keys in order.
		/// </summary>
		public static void Hotkey(params VirtualKey[] keys)
		{
			Hotkey(keys.Select(key => (ushort)key).ToArray());
		}

		/// <summary>
		/// Simulates typing a string of text by sending individual Unicode input events.
		/// </summary>
		public static void TypeText(string text)
		{
			ArgumentNullException.ThrowIfNull(text);

			var hadClipboardText = ClipboardControl.ContainsText();
			var previousClipboardText = hadClipboardText ? ClipboardControl.GetText() : null;

			try
			{
				// Clipboard paste is more reliable than KEYEVENTF_UNICODE across modern text controls.
				ClipboardControl.SetText(text);
				Thread.Sleep(25);
				Hotkey(VirtualKey.Control, VirtualKey.V);
				Thread.Sleep(150);
			}
			finally
			{
				if (hadClipboardText)
				{
					ClipboardControl.SetText(previousClipboardText ?? string.Empty);
				}
				else
				{
					ClipboardControl.Clear();
				}
			}
		}

		/// <summary>
		/// Simulates typing a single character by sending a Unicode input event.
		/// </summary>
		public static void TypeChar(char character)
		{
			var inputs = new[]
			{
				new Imports.INPUT
				{
					type = Imports.INPUT_KEYBOARD,
					u = new Imports.INPUTUNION
					{
						ki = new Imports.KEYBDINPUT
						{
							wVk = 0,
							wScan = character,
							dwFlags = Imports.KEYEVENTF_UNICODE
						}
					}
				},
				new Imports.INPUT
				{
					type = Imports.INPUT_KEYBOARD,
					u = new Imports.INPUTUNION
					{
						ki = new Imports.KEYBDINPUT
						{
							wVk = 0,
							wScan = character,
							dwFlags = Imports.KEYEVENTF_UNICODE | Imports.KEYEVENTF_KEYUP
						}
					}
				}
			};

			SendInputs(inputs);
		}

		private static void SendKeyboardInput(Imports.KEYBDINPUT keyboardInput)
		{
			var inputs = new[]
			{
				new Imports.INPUT
				{
					type = Imports.INPUT_KEYBOARD,
					u = new Imports.INPUTUNION
					{
						ki = keyboardInput
					}
				}
			};

			SendInputs(inputs);
		}

		private static void SendInputs(Imports.INPUT[] inputs)
		{
			var sent = Imports.SendInput(
				(uint)inputs.Length,
				inputs,
				Marshal.SizeOf<Imports.INPUT>());

			if (sent != inputs.Length)
			{
				ThrowLastWin32Error("SendInput failed");
			}
		}

		private static void ThrowLastWin32Error(string message)
		{
			throw new InvalidOperationException($"{message}. Win32 error: {Marshal.GetLastWin32Error()}");
		}
	}
}
