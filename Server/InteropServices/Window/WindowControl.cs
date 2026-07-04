using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace Server.InteropServices
{
	/// <summary>
	/// Describes a visible top-level window and its current state.
	/// </summary>
	public sealed record WindowSnapshot(
		IntPtr Handle,
		string Title,
		string ClassName,
		int ProcessId,
		ScreenRectangle Bounds,
		bool IsVisible,
		bool IsForeground,
		bool IsMinimized,
		bool IsMaximized);

	/// <summary>
	/// Provides native Windows top-level window discovery, focus, movement, resizing, and state operations.
	/// </summary>
	[SupportedOSPlatform("windows")]
	public static class WindowControl
	{
		internal static class Imports
		{
			internal delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

			[StructLayout(LayoutKind.Sequential)]
			internal struct RECT
			{
				public int Left;
				public int Top;
				public int Right;
				public int Bottom;
			}

			internal const int SW_MAXIMIZE = 3;
			internal const int SW_MINIMIZE = 6;
			internal const int SW_RESTORE = 9;

			internal const uint SWP_NOZORDER = 0x0004;
			internal const uint SWP_NOACTIVATE = 0x0010;

			internal const uint WM_CLOSE = 0x0010;

			[DllImport("user32.dll")]
			internal static extern IntPtr GetForegroundWindow();

			[DllImport("user32.dll", SetLastError = true)]
			internal static extern bool SetForegroundWindow(IntPtr hWnd);

			[DllImport("user32.dll")]
			internal static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

			[DllImport("user32.dll")]
			internal static extern bool IsIconic(IntPtr hWnd);

			[DllImport("user32.dll")]
			internal static extern bool IsZoomed(IntPtr hWnd);

			[DllImport("user32.dll")]
			internal static extern bool IsWindowVisible(IntPtr hWnd);

			[DllImport("user32.dll", SetLastError = true)]
			internal static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

			[DllImport("user32.dll", SetLastError = true)]
			internal static extern bool SetWindowPos(
				IntPtr hWnd,
				IntPtr hWndInsertAfter,
				int x,
				int y,
				int cx,
				int cy,
				uint uFlags);

			[DllImport("user32.dll", SetLastError = true)]
			internal static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

			[DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
			internal static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

			[DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
			internal static extern int GetWindowTextLength(IntPtr hWnd);

			[DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
			internal static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

			[DllImport("user32.dll", SetLastError = true)]
			internal static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

			[DllImport("user32.dll", SetLastError = true)]
			internal static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
		}

		public static WindowSnapshot? GetForegroundWindowSnapshot()
		{
			var handle = Imports.GetForegroundWindow();
			return handle == IntPtr.Zero ? null : GetWindowSnapshot(handle, handle);
		}

		public static IReadOnlyList<WindowSnapshot> GetVisibleWindows()
		{
			var foregroundHandle = Imports.GetForegroundWindow();
			var windows = new List<WindowSnapshot>();

			var success = Imports.EnumWindows((handle, _) =>
			{
				if (Imports.IsWindowVisible(handle))
				{
					var snapshot = GetWindowSnapshot(handle, foregroundHandle);
					if (!snapshot.Bounds.IsEmpty)
					{
						windows.Add(snapshot);
					}
				}

				return true;
			}, IntPtr.Zero);

			if (!success)
			{
				ThrowLastWin32Error("EnumWindows failed");
			}

			return windows;
		}

		public static IReadOnlyList<WindowSnapshot> FindWindowsByTitle(
			string title,
			WindowTitleMatchMode matchMode = WindowTitleMatchMode.ContainsIgnoreCase)
		{
			if (string.IsNullOrWhiteSpace(title))
			{
				throw new ArgumentException("Window title cannot be empty.", nameof(title));
			}

			var searchTitle = title.Trim();

			return GetVisibleWindows()
				.Where(window => !string.IsNullOrWhiteSpace(window.Title))
				.Where(window => TitleMatches(window.Title, searchTitle, matchMode))
				.ToArray();
		}

		public static WindowSnapshot GetWindowByTitle(
			string title,
			WindowTitleMatchMode matchMode = WindowTitleMatchMode.ContainsIgnoreCase)
		{
			var matches = FindWindowsByTitle(title, matchMode);

			if (matches.Count == 0)
			{
				throw new InvalidOperationException($"No visible window matched title '{title}'.");
			}

			if (matches.Count > 1)
			{
				var matchList = string.Join(", ", matches.Take(5).Select(window => $"'{window.Title}'"));
				throw new InvalidOperationException($"Window title '{title}' matched multiple visible windows: {matchList}.");
			}

			return matches[0];
		}

		public static WindowSnapshot FocusWindow(
			string title,
			WindowTitleMatchMode matchMode = WindowTitleMatchMode.ContainsIgnoreCase)
		{
			var window = GetWindowByTitle(title, matchMode);
			RestoreIfMinimized(window.Handle);

			if (!Imports.SetForegroundWindow(window.Handle))
			{
				throw new InvalidOperationException($"SetForegroundWindow failed for window '{window.Title}'.");
			}

			return GetWindowSnapshot(window.Handle);
		}

		public static WindowSnapshot MoveWindow(
			string title,
			int x,
			int y,
			WindowTitleMatchMode matchMode = WindowTitleMatchMode.ContainsIgnoreCase)
		{
			var window = GetWindowByTitle(title, matchMode);
			RestoreIfMinimizedOrMaximized(window.Handle);

			var current = GetWindowSnapshot(window.Handle);
			SetWindowBounds(window.Handle, new ScreenRectangle(x, y, current.Bounds.Width, current.Bounds.Height));

			return GetWindowSnapshot(window.Handle);
		}

		public static WindowSnapshot ResizeWindow(
			string title,
			int width,
			int height,
			WindowTitleMatchMode matchMode = WindowTitleMatchMode.ContainsIgnoreCase)
		{
			if (width <= 0)
			{
				throw new ArgumentOutOfRangeException(nameof(width), width, "Window width must be positive.");
			}

			if (height <= 0)
			{
				throw new ArgumentOutOfRangeException(nameof(height), height, "Window height must be positive.");
			}

			var window = GetWindowByTitle(title, matchMode);
			RestoreIfMinimizedOrMaximized(window.Handle);

			var current = GetWindowSnapshot(window.Handle);
			SetWindowBounds(window.Handle, new ScreenRectangle(current.Bounds.X, current.Bounds.Y, width, height));

			return GetWindowSnapshot(window.Handle);
		}

		public static WindowSnapshot MinimizeWindow(
			string title,
			WindowTitleMatchMode matchMode = WindowTitleMatchMode.ContainsIgnoreCase)
		{
			var window = GetWindowByTitle(title, matchMode);
			Imports.ShowWindow(window.Handle, Imports.SW_MINIMIZE);

			return GetWindowSnapshot(window.Handle);
		}

		public static WindowSnapshot MaximizeWindow(
			string title,
			WindowTitleMatchMode matchMode = WindowTitleMatchMode.ContainsIgnoreCase)
		{
			var window = GetWindowByTitle(title, matchMode);
			Imports.ShowWindow(window.Handle, Imports.SW_MAXIMIZE);

			return GetWindowSnapshot(window.Handle);
		}

		public static void CloseWindow(
			string title,
			WindowTitleMatchMode matchMode = WindowTitleMatchMode.ContainsIgnoreCase)
		{
			var window = GetWindowByTitle(title, matchMode);

			if (!Imports.PostMessage(window.Handle, Imports.WM_CLOSE, IntPtr.Zero, IntPtr.Zero))
			{
				ThrowLastWin32Error("PostMessage(WM_CLOSE) failed");
			}
		}

		public static WindowSnapshot SnapWindowLeft(
			string title,
			WindowTitleMatchMode matchMode = WindowTitleMatchMode.ContainsIgnoreCase)
		{
			return SnapWindow(title, leftSide: true, matchMode);
		}

		public static WindowSnapshot SnapWindowRight(
			string title,
			WindowTitleMatchMode matchMode = WindowTitleMatchMode.ContainsIgnoreCase)
		{
			return SnapWindow(title, leftSide: false, matchMode);
		}

		public static bool TrySetForegroundWindow(IntPtr handle)
		{
			return handle != IntPtr.Zero && Imports.SetForegroundWindow(handle);
		}

		public static WindowSnapshot FocusWindow(IntPtr handle)
		{
			if (handle == IntPtr.Zero)
			{
				throw new ArgumentException("Window handle cannot be zero.", nameof(handle));
			}

			RestoreIfMinimized(handle);

			if (!Imports.SetForegroundWindow(handle))
			{
				throw new InvalidOperationException("SetForegroundWindow failed.");
			}

			return GetWindowSnapshot(handle);
		}

		public static WindowSnapshot GetWindowSnapshot(IntPtr handle)
		{
			return GetWindowSnapshot(handle, Imports.GetForegroundWindow());
		}

		private static WindowSnapshot SnapWindow(
			string title,
			bool leftSide,
			WindowTitleMatchMode matchMode)
		{
			var window = GetWindowByTitle(title, matchMode);
			RestoreIfMinimizedOrMaximized(window.Handle);

			var current = GetWindowSnapshot(window.Handle);
			var monitor = GetBestMonitorForWindow(current.Bounds);
			var workingArea = monitor.WorkingArea;
			var leftWidth = workingArea.Width / 2;
			var rightWidth = workingArea.Width - leftWidth;

			var target = leftSide
				? new ScreenRectangle(workingArea.Left, workingArea.Top, leftWidth, workingArea.Height)
				: new ScreenRectangle(workingArea.Left + leftWidth, workingArea.Top, rightWidth, workingArea.Height);

			SetWindowBounds(window.Handle, target);
			return GetWindowSnapshot(window.Handle);
		}

		private static DisplayMonitor GetBestMonitorForWindow(ScreenRectangle bounds)
		{
			var monitors = ScreenControl.GetMonitors();
			if (monitors.Count == 0)
			{
				throw new InvalidOperationException("No monitors were found.");
			}

			var center = new ScreenPoint(bounds.CenterX, bounds.CenterY);
			var centerMonitor = monitors.FirstOrDefault(monitor => monitor.Bounds.Contains(center));
			if (centerMonitor is not null)
			{
				return centerMonitor;
			}

			var intersectingMonitor = monitors
				.Select(monitor => new
				{
					Monitor = monitor,
					IntersectionArea = bounds.Intersect(monitor.Bounds).Area
				})
				.OrderByDescending(result => result.IntersectionArea)
				.FirstOrDefault(result => result.IntersectionArea > 0);

			if (intersectingMonitor is not null)
			{
				return intersectingMonitor.Monitor;
			}

			return ScreenControl.GetPrimaryMonitor() ?? monitors[0];
		}

		private static void SetWindowBounds(IntPtr handle, ScreenRectangle bounds)
		{
			if (bounds.IsEmpty)
			{
				throw new ArgumentOutOfRangeException(nameof(bounds), "Window bounds must have positive width and height.");
			}

			if (!Imports.SetWindowPos(
				handle,
				IntPtr.Zero,
				bounds.X,
				bounds.Y,
				bounds.Width,
				bounds.Height,
				Imports.SWP_NOZORDER | Imports.SWP_NOACTIVATE))
			{
				ThrowLastWin32Error("SetWindowPos failed");
			}
		}

		private static void RestoreIfMinimized(IntPtr handle)
		{
			if (Imports.IsIconic(handle))
			{
				Imports.ShowWindow(handle, Imports.SW_RESTORE);
			}
		}

		private static void RestoreIfMinimizedOrMaximized(IntPtr handle)
		{
			if (Imports.IsIconic(handle) || Imports.IsZoomed(handle))
			{
				Imports.ShowWindow(handle, Imports.SW_RESTORE);
			}
		}

		private static bool TitleMatches(string windowTitle, string searchTitle, WindowTitleMatchMode matchMode)
		{
			return matchMode switch
			{
				WindowTitleMatchMode.ContainsIgnoreCase =>
					windowTitle.Contains(searchTitle, StringComparison.OrdinalIgnoreCase),
				WindowTitleMatchMode.ExactIgnoreCase =>
					string.Equals(windowTitle, searchTitle, StringComparison.OrdinalIgnoreCase),
				_ => throw new ArgumentOutOfRangeException(nameof(matchMode), matchMode, "Unsupported title match mode.")
			};
		}

		private static WindowSnapshot GetWindowSnapshot(IntPtr handle, IntPtr foregroundHandle)
		{
			if (handle == IntPtr.Zero)
			{
				throw new ArgumentException("Window handle cannot be zero.", nameof(handle));
			}

			if (!Imports.GetWindowRect(handle, out var rectangle))
			{
				ThrowLastWin32Error("GetWindowRect failed");
			}

			Imports.GetWindowThreadProcessId(handle, out var processId);

			return new WindowSnapshot(
				handle,
				GetWindowTitle(handle),
				GetWindowClassName(handle),
				unchecked((int)processId),
				ScreenRectangle.FromLTRB(rectangle.Left, rectangle.Top, rectangle.Right, rectangle.Bottom),
				Imports.IsWindowVisible(handle),
				handle == foregroundHandle,
				Imports.IsIconic(handle),
				Imports.IsZoomed(handle));
		}

		private static string GetWindowTitle(IntPtr handle)
		{
			var length = Imports.GetWindowTextLength(handle);
			if (length <= 0)
			{
				return string.Empty;
			}

			var builder = new StringBuilder(length + 1);
			Imports.GetWindowText(handle, builder, builder.Capacity);
			return builder.ToString();
		}

		private static string GetWindowClassName(IntPtr handle)
		{
			const int maxClassNameLength = 256;
			var builder = new StringBuilder(maxClassNameLength);

			var length = Imports.GetClassName(handle, builder, builder.Capacity);
			return length <= 0 ? string.Empty : builder.ToString();
		}

		private static void ThrowLastWin32Error(string message)
		{
			throw new InvalidOperationException($"{message}. Win32 error: {Marshal.GetLastWin32Error()}");
		}
	}
}
