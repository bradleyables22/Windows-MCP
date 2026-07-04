using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace Server.InteropServices
{
	/// <summary>
	/// Provides native Windows clipboard operations for Unicode text.
	/// </summary>
	[SupportedOSPlatform("windows")]
	public static class ClipboardControl
	{
		private const uint CF_UNICODETEXT = 13;
		private const uint GMEM_MOVEABLE = 0x0002;
		private const int DefaultRetryCount = 10;
		private const int DefaultRetryDelayMilliseconds = 25;

		internal static class Imports
		{
			[DllImport("user32.dll", SetLastError = true)]
			internal static extern bool OpenClipboard(IntPtr hWndNewOwner);

			[DllImport("user32.dll", SetLastError = true)]
			internal static extern bool CloseClipboard();

			[DllImport("user32.dll", SetLastError = true)]
			internal static extern bool EmptyClipboard();

			[DllImport("user32.dll", SetLastError = true)]
			internal static extern bool IsClipboardFormatAvailable(uint format);

			[DllImport("user32.dll", SetLastError = true)]
			internal static extern IntPtr GetClipboardData(uint uFormat);

			[DllImport("user32.dll", SetLastError = true)]
			internal static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

			[DllImport("kernel32.dll", SetLastError = true)]
			internal static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);

			[DllImport("kernel32.dll", SetLastError = true)]
			internal static extern IntPtr GlobalLock(IntPtr hMem);

			[DllImport("kernel32.dll", SetLastError = true)]
			internal static extern bool GlobalUnlock(IntPtr hMem);

			[DllImport("kernel32.dll", SetLastError = true)]
			internal static extern IntPtr GlobalFree(IntPtr hMem);
		}

		public static bool ContainsText()
		{
			using var clipboard = OpenClipboardWithRetry();
			return Imports.IsClipboardFormatAvailable(CF_UNICODETEXT);
		}

		public static string? GetText()
		{
			using var clipboard = OpenClipboardWithRetry();

			if (!Imports.IsClipboardFormatAvailable(CF_UNICODETEXT))
			{
				return null;
			}

			var handle = Imports.GetClipboardData(CF_UNICODETEXT);
			if (handle == IntPtr.Zero)
			{
				ThrowLastWin32Error("GetClipboardData failed");
			}

			var pointer = Imports.GlobalLock(handle);
			if (pointer == IntPtr.Zero)
			{
				ThrowLastWin32Error("GlobalLock failed");
			}

			try
			{
				return Marshal.PtrToStringUni(pointer);
			}
			finally
			{
				Imports.GlobalUnlock(handle);
			}
		}

		public static string GetRequiredText()
		{
			return GetText() ?? throw new InvalidOperationException("Clipboard does not contain Unicode text.");
		}

		public static void SetText(string text)
		{
			ArgumentNullException.ThrowIfNull(text);

			using var clipboard = OpenClipboardWithRetry();

			if (!Imports.EmptyClipboard())
			{
				ThrowLastWin32Error("EmptyClipboard failed");
			}

			var bytes = Encoding.Unicode.GetBytes(text + '\0');
			var handle = Imports.GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)bytes.Length);
			if (handle == IntPtr.Zero)
			{
				ThrowLastWin32Error("GlobalAlloc failed");
			}

			var transferOwnership = false;

			try
			{
				var pointer = Imports.GlobalLock(handle);
				if (pointer == IntPtr.Zero)
				{
					ThrowLastWin32Error("GlobalLock failed");
				}

				try
				{
					Marshal.Copy(bytes, 0, pointer, bytes.Length);
				}
				finally
				{
					Imports.GlobalUnlock(handle);
				}

				if (Imports.SetClipboardData(CF_UNICODETEXT, handle) == IntPtr.Zero)
				{
					ThrowLastWin32Error("SetClipboardData failed");
				}

				transferOwnership = true;
			}
			finally
			{
				if (!transferOwnership)
				{
					Imports.GlobalFree(handle);
				}
			}
		}

		public static void Clear()
		{
			using var clipboard = OpenClipboardWithRetry();

			if (!Imports.EmptyClipboard())
			{
				ThrowLastWin32Error("EmptyClipboard failed");
			}
		}

		private static ClipboardScope OpenClipboardWithRetry(
			int retryCount = DefaultRetryCount,
			int retryDelayMilliseconds = DefaultRetryDelayMilliseconds)
		{
			for (var attempt = 0; attempt <= retryCount; attempt++)
			{
				if (Imports.OpenClipboard(IntPtr.Zero))
				{
					return new ClipboardScope();
				}

				if (attempt < retryCount)
				{
					Thread.Sleep(retryDelayMilliseconds);
				}
			}

			ThrowLastWin32Error("OpenClipboard failed");
			throw new InvalidOperationException("OpenClipboard failed.");
		}

		private static void ThrowLastWin32Error(string message)
		{
			throw new InvalidOperationException($"{message}. Win32 error: {Marshal.GetLastWin32Error()}");
		}

		private sealed class ClipboardScope : IDisposable
		{
			private bool disposed;

			public void Dispose()
			{
				if (disposed)
				{
					return;
				}

				Imports.CloseClipboard();
				disposed = true;
			}
		}
	}
}
