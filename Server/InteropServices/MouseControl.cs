using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Server.InteropServices
{
	/// <summary>
	/// Provides native Windows mouse movement, click, scroll, and drag operations.
	/// </summary>
	[SupportedOSPlatform("windows")]
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

			[DllImport("user32.dll", SetLastError = true)]
			internal static extern bool SetCursorPos(int x, int y);

			[DllImport("user32.dll", SetLastError = true)]
			internal static extern bool GetCursorPos(out POINT point);

			[DllImport("user32.dll", SetLastError = true)]
			internal static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);
		}

		/// <summary>
		/// Gets the current position of the mouse cursor.
		/// </summary>
		public static (int X, int Y) Get()
		{
			var point = GetPosition();
			return (point.X, point.Y);
		}

		/// <summary>
		/// Gets the current position of the mouse cursor.
		/// </summary>
		public static ScreenPoint GetPosition()
		{
			if (!Imports.GetCursorPos(out var point))
			{
				ThrowLastWin32Error("GetCursorPos failed");
			}

			return new ScreenPoint(point.X, point.Y);
		}

		/// <summary>
		/// Moves the mouse cursor to the specified screen coordinates.
		/// </summary>
		public static bool MoveTo(int x, int y)
		{
			return Imports.SetCursorPos(x, y);
		}

		/// <summary>
		/// Moves the mouse cursor to the specified screen coordinates.
		/// </summary>
		public static bool MoveTo(ScreenPoint point)
		{
			return MoveTo(point.X, point.Y);
		}

		/// <summary>
		/// Moves the mouse cursor only when the target point is inside the virtual screen.
		/// </summary>
		public static void MoveToChecked(int x, int y)
		{
			if (!ScreenControl.IsPointOnScreen(x, y))
			{
				throw new ArgumentOutOfRangeException(
					nameof(x),
					new ScreenPoint(x, y),
					"Mouse coordinates must be inside the virtual screen bounds.");
			}

			if (!MoveTo(x, y))
			{
				ThrowLastWin32Error("SetCursorPos failed");
			}
		}

		/// <summary>
		/// Moves the mouse cursor only when the target point is inside the virtual screen.
		/// </summary>
		public static void MoveToChecked(ScreenPoint point)
		{
			MoveToChecked(point.X, point.Y);
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
		/// Simulates a mouse button click at the current cursor position.
		/// </summary>
		public static void Click(MouseButton button)
		{
			MouseDown(button);
			MouseUp(button);
		}

		/// <summary>
		/// Simulates a double-click at the current cursor position.
		/// </summary>
		public static void DoubleClick(MouseButton button = MouseButton.Left, int delayMilliseconds = 75)
		{
			if (delayMilliseconds < 0)
			{
				throw new ArgumentOutOfRangeException(
					nameof(delayMilliseconds),
					delayMilliseconds,
					"Delay cannot be negative.");
			}

			Click(button);
			Thread.Sleep(delayMilliseconds);
			Click(button);
		}

		/// <summary>
		/// Simulates a mouse button press at the current cursor position.
		/// </summary>
		public static void MouseDown(MouseButton button)
		{
			MouseDown(GetMouseDownFlag(button));
		}

		/// <summary>
		/// Simulates a mouse button release at the current cursor position.
		/// </summary>
		public static void MouseUp(MouseButton button)
		{
			MouseUp(GetMouseUpFlag(button));
		}

		/// <summary>
		/// Moves the mouse cursor to the specified screen coordinates and simulates a left mouse button click.
		/// </summary>
		public static void LeftClickAt(int x, int y)
		{
			ClickAt(MouseButton.Left, x, y);
		}

		/// <summary>
		/// Moves the mouse cursor to the specified screen coordinates and simulates a right mouse button click.
		/// </summary>
		public static void RightClickAt(int x, int y)
		{
			ClickAt(MouseButton.Right, x, y);
		}

		/// <summary>
		/// Moves the mouse cursor to the specified screen coordinates and simulates a mouse button click.
		/// </summary>
		public static void ClickAt(MouseButton button, int x, int y)
		{
			MoveToChecked(x, y);
			Thread.Sleep(50);
			Click(button);
		}

		/// <summary>
		/// Drags from the current cursor position to the specified screen coordinates.
		/// </summary>
		public static void DragTo(
			int x,
			int y,
			MouseButton button = MouseButton.Left,
			int steps = 20,
			int stepDelayMilliseconds = 10)
		{
			var start = GetPosition();
			DragFromTo(start.X, start.Y, x, y, button, steps, stepDelayMilliseconds);
		}

		/// <summary>
		/// Drags from one screen coordinate to another.
		/// </summary>
		public static void DragFromTo(
			int startX,
			int startY,
			int endX,
			int endY,
			MouseButton button = MouseButton.Left,
			int steps = 20,
			int stepDelayMilliseconds = 10)
		{
			if (steps <= 0)
			{
				throw new ArgumentOutOfRangeException(nameof(steps), steps, "Drag steps must be positive.");
			}

			if (stepDelayMilliseconds < 0)
			{
				throw new ArgumentOutOfRangeException(
					nameof(stepDelayMilliseconds),
					stepDelayMilliseconds,
					"Step delay cannot be negative.");
			}

			MoveToChecked(startX, startY);
			Thread.Sleep(50);
			MouseDown(button);

			try
			{
				for (var step = 1; step <= steps; step++)
				{
					var progress = (double)step / steps;
					var nextX = startX + (int)Math.Round((endX - startX) * progress);
					var nextY = startY + (int)Math.Round((endY - startY) * progress);

					MoveToChecked(nextX, nextY);

					if (stepDelayMilliseconds > 0)
					{
						Thread.Sleep(stepDelayMilliseconds);
					}
				}
			}
			finally
			{
				MouseUp(button);
			}
		}

		/// <summary>
		/// Simulates mouse wheel scrolling. Positive values scroll up, negative values scroll down.
		/// </summary>
		public static void Scroll(int amount)
		{
			SendMouseInput(new Imports.MOUSEINPUT
			{
				mouseData = unchecked((uint)amount),
				dwFlags = Imports.MOUSEEVENTF_WHEEL
			});
		}

		private static void MouseDown(uint flag)
		{
			SendMouseInput(new Imports.MOUSEINPUT
			{
				dwFlags = flag
			});
		}

		private static void MouseUp(uint flag)
		{
			SendMouseInput(new Imports.MOUSEINPUT
			{
				dwFlags = flag
			});
		}

		private static uint GetMouseDownFlag(MouseButton button)
		{
			return button switch
			{
				MouseButton.Left => Imports.MOUSEEVENTF_LEFTDOWN,
				MouseButton.Right => Imports.MOUSEEVENTF_RIGHTDOWN,
				_ => throw new ArgumentOutOfRangeException(nameof(button), button, "Unsupported mouse button.")
			};
		}

		private static uint GetMouseUpFlag(MouseButton button)
		{
			return button switch
			{
				MouseButton.Left => Imports.MOUSEEVENTF_LEFTUP,
				MouseButton.Right => Imports.MOUSEEVENTF_RIGHTUP,
				_ => throw new ArgumentOutOfRangeException(nameof(button), button, "Unsupported mouse button.")
			};
		}

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
				ThrowLastWin32Error("SendInput failed");
			}
		}

		private static void ThrowLastWin32Error(string message)
		{
			throw new InvalidOperationException($"{message}. Win32 error: {Marshal.GetLastWin32Error()}");
		}
	}
}
