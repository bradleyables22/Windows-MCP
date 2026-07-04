using System.Runtime.InteropServices;

namespace Server.InteropServices
{
	public static class MouseControl
	{
		internal static class Imports
		{
			[StructLayout(LayoutKind.Sequential)]
			internal struct POINT
			{
				public int X;
				public int Y;
			}

			[StructLayout(LayoutKind.Sequential)]
			internal struct INPUT
			{
				public int type;
				public MOUSEINPUT mi;
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

			internal const int INPUT_MOUSE = 0;

			internal const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
			internal const uint MOUSEEVENTF_LEFTUP = 0x0004;
			internal const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
			internal const uint MOUSEEVENTF_RIGHTUP = 0x0010;
			internal const uint MOUSEEVENTF_WHEEL = 0x0800;

			[DllImport("user32.dll")]
			internal static extern bool SetCursorPos(int x, int y);

			[DllImport("user32.dll")]
			internal static extern bool GetCursorPos(out POINT point);

			[DllImport("user32.dll", SetLastError = true)]
			internal static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);
		}

		/// <summary>
		/// Gets the current position of the mouse cursor.
		/// </summary>
		/// <returns></returns>
		public static (int X, int Y) Get()
		{
			Imports.GetCursorPos(out var point);
			return (point.X, point.Y);
		}

		/// <summary>
		/// Moves the mouse cursor to the specified screen coordinates.
		/// </summary>
		/// <param name="x"></param>
		/// <param name="y"></param>
		/// <returns></returns>
		public static bool MoveTo(int x, int y)
		{
			return Imports.SetCursorPos(x, y);
		}

		/// <summary>
		/// Simulates a left mouse button click at the current cursor position.
		/// </summary>
		public static void LeftClick()
		{
			MouseDown(Imports.MOUSEEVENTF_LEFTDOWN);
			MouseUp(Imports.MOUSEEVENTF_LEFTUP);
		}

		/// <summary>
		/// Simulates a right mouse button click at the current cursor position.
		/// </summary>
		public static void RightClick()
		{
			MouseDown(Imports.MOUSEEVENTF_RIGHTDOWN);
			MouseUp(Imports.MOUSEEVENTF_RIGHTUP);
		}

		/// <summary>
		/// Moves the mouse cursor to the specified screen coordinates and simulates a left mouse button click.
		/// </summary>
		/// <param name="x"></param>
		/// <param name="y"></param>
		public static void LeftClickAt(int x, int y)
		{
			MoveTo(x, y);
			Thread.Sleep(50);
			LeftClick();
		}

		/// <summary>
		/// Moves the mouse cursor to the specified screen coordinates and simulates a right mouse button click.
		/// </summary>
		/// <param name="x"></param>
		/// <param name="y"></param>
		public static void RightClickAt(int x, int y)
		{
			MoveTo(x, y);
			Thread.Sleep(50);
			RightClick();
		}

		/// <summary>
		/// Simulates mouse wheel scrolling. Positive values scroll up, negative values scroll down.
		/// </summary>
		/// <param name="amount"></param>
		public static void Scroll(int amount)
		{
			SendMouseInput(new Imports.MOUSEINPUT
			{
				mouseData = unchecked((uint)amount),
				dwFlags = Imports.MOUSEEVENTF_WHEEL
			});
		}

		/// <summary>
		/// Simulates a mouse button press.
		/// </summary>
		/// <param name="flag"></param>
		private static void MouseDown(uint flag)
		{
			SendMouseInput(new Imports.MOUSEINPUT
			{
				dwFlags = flag
			});
		}

		/// <summary>
		/// Simulates a mouse button release.
		/// </summary>
		/// <param name="flag"></param>
		private static void MouseUp(uint flag)
		{
			SendMouseInput(new Imports.MOUSEINPUT
			{
				dwFlags = flag
			});
		}

		/// <summary>
		/// Sends mouse input to the system using the SendInput function.
		/// </summary>
		/// <param name="mouseInput"></param>
		/// <exception cref="InvalidOperationException"></exception>
		private static void SendMouseInput(Imports.MOUSEINPUT mouseInput)
		{
			var inputs = new[]
			{
				new Imports.INPUT
				{
					type = Imports.INPUT_MOUSE,
					mi = mouseInput
				}
			};

			var sent = Imports.SendInput(
				(uint)inputs.Length,
				inputs,
				Marshal.SizeOf<Imports.INPUT>());

			if (sent != inputs.Length)
			{
				var error = Marshal.GetLastWin32Error();
				throw new InvalidOperationException($"SendInput failed. Win32 error: {error}");
			}
		}
	}
}