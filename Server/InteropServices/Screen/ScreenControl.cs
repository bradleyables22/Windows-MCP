using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Server.InteropServices
{
	/// <summary>
	/// Describes a display monitor and its usable working area.
	/// </summary>
	public sealed record DisplayMonitor(
		string DeviceName,
		ScreenRectangle Bounds,
		ScreenRectangle WorkingArea,
		bool IsPrimary);

	/// <summary>
	/// Contains an in-memory PNG screen capture and its capture metadata.
	/// </summary>
	public sealed record ScreenCaptureResult(
		byte[] Bytes,
		string MimeType,
		int Width,
		int Height,
		ScreenRectangle Bounds,
		bool CursorIncluded,
		DateTimeOffset CapturedAt);

	public sealed record ScreenFrameResult(
		byte[] Bgra32Bytes,
		int Width,
		int Height,
		int Stride,
		ScreenRectangle Bounds,
		bool CursorIncluded,
		DateTimeOffset CapturedAt);

	/// <summary>
	/// Provides native Windows monitor discovery and screen capture operations.
	/// </summary>
	[SupportedOSPlatform("windows")]
	public static class ScreenControl
	{
		private const string PngMimeType = "image/png";
		private const string JpegMimeType = "image/jpeg";

		internal static class Imports
		{
			internal delegate bool MonitorEnumProc(
				IntPtr hMonitor,
				IntPtr hdcMonitor,
				ref RECT lprcMonitor,
				IntPtr dwData);

			[StructLayout(LayoutKind.Sequential)]
			internal struct POINT
			{
				public int X;
				public int Y;
			}

			[StructLayout(LayoutKind.Sequential)]
			internal struct RECT
			{
				public int Left;
				public int Top;
				public int Right;
				public int Bottom;
			}

			[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
			internal struct MONITORINFOEX
			{
				public uint cbSize;
				public RECT rcMonitor;
				public RECT rcWork;
				public uint dwFlags;

				[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
				public string szDevice;
			}

			[StructLayout(LayoutKind.Sequential)]
			internal struct CURSORINFO
			{
				public int cbSize;
				public int flags;
				public IntPtr hCursor;
				public POINT ptScreenPos;
			}

			internal const int SM_XVIRTUALSCREEN = 76;
			internal const int SM_YVIRTUALSCREEN = 77;
			internal const int SM_CXVIRTUALSCREEN = 78;
			internal const int SM_CYVIRTUALSCREEN = 79;

			internal const uint MONITORINFOF_PRIMARY = 0x00000001;
			internal const int CURSOR_SHOWING = 0x00000001;
			internal const uint DI_NORMAL = 0x0003;

			[DllImport("user32.dll")]
			internal static extern int GetSystemMetrics(int nIndex);

			[DllImport("user32.dll", SetLastError = true)]
			internal static extern bool EnumDisplayMonitors(
				IntPtr hdc,
				IntPtr lprcClip,
				MonitorEnumProc lpfnEnum,
				IntPtr dwData);

			[DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
			internal static extern bool GetMonitorInfo(
				IntPtr hMonitor,
				ref MONITORINFOEX lpmi);

			[DllImport("user32.dll", SetLastError = true)]
			internal static extern bool GetCursorInfo(ref CURSORINFO pci);

			[DllImport("user32.dll", SetLastError = true)]
			internal static extern bool DrawIconEx(
				IntPtr hdc,
				int xLeft,
				int yTop,
				IntPtr hIcon,
				int cxWidth,
				int cyHeight,
				uint istepIfAniCur,
				IntPtr hbrFlickerFreeDraw,
				uint diFlags);
		}

		public static ScreenRectangle GetVirtualScreenBounds()
		{
			return new ScreenRectangle(
				Imports.GetSystemMetrics(Imports.SM_XVIRTUALSCREEN),
				Imports.GetSystemMetrics(Imports.SM_YVIRTUALSCREEN),
				Imports.GetSystemMetrics(Imports.SM_CXVIRTUALSCREEN),
				Imports.GetSystemMetrics(Imports.SM_CYVIRTUALSCREEN));
		}

		public static IReadOnlyList<DisplayMonitor> GetMonitors()
		{
			var monitors = new List<DisplayMonitor>();

			var success = Imports.EnumDisplayMonitors(
				IntPtr.Zero,
				IntPtr.Zero,
				(IntPtr hMonitor, IntPtr hdcMonitor, ref Imports.RECT monitorRectangle, IntPtr state) =>
				{
					var info = new Imports.MONITORINFOEX
					{
						cbSize = (uint)Marshal.SizeOf<Imports.MONITORINFOEX>(),
						szDevice = string.Empty
					};

					if (!Imports.GetMonitorInfo(hMonitor, ref info))
					{
						ThrowLastWin32Error("GetMonitorInfo failed");
					}

					monitors.Add(new DisplayMonitor(
						info.szDevice,
						ToScreenRectangle(info.rcMonitor),
						ToScreenRectangle(info.rcWork),
						(info.dwFlags & Imports.MONITORINFOF_PRIMARY) != 0));

					return true;
				},
				IntPtr.Zero);

			if (!success)
			{
				ThrowLastWin32Error("EnumDisplayMonitors failed");
			}

			return monitors;
		}

		public static DisplayMonitor? GetPrimaryMonitor()
		{
			return GetMonitors().FirstOrDefault(monitor => monitor.IsPrimary);
		}

		public static bool IsPointOnScreen(ScreenPoint point)
		{
			return IsPointOnScreen(point.X, point.Y);
		}

		public static bool IsPointOnScreen(int x, int y)
		{
			return GetVirtualScreenBounds().Contains(x, y);
		}

		public static ScreenCaptureResult CaptureVirtualScreen(bool includeCursor = true)
		{
			return CaptureRectangle(GetVirtualScreenBounds(), includeCursor);
		}

		public static ScreenCaptureResult CapturePrimaryMonitor(bool includeCursor = true)
		{
			var primary = GetPrimaryMonitor()
				?? throw new InvalidOperationException("No primary monitor was found.");

			return CaptureRectangle(primary.Bounds, includeCursor);
		}

		public static ScreenCaptureResult CaptureRectangle(ScreenRectangle bounds, bool includeCursor = true)
		{
			return CaptureRectangle(
				bounds,
				includeCursor,
				PngMimeType,
				(bitmap, stream) => bitmap.Save(stream, ImageFormat.Png));
		}

		public static ScreenCaptureResult CaptureRectangleJpeg(
			ScreenRectangle bounds,
			bool includeCursor = true,
			int quality = 75)
		{
			if (quality is < 1 or > 100)
			{
				throw new ArgumentOutOfRangeException(nameof(quality), quality, "JPEG quality must be between 1 and 100.");
			}

			return CaptureRectangle(
				bounds,
				includeCursor,
				JpegMimeType,
				(bitmap, stream) => SaveJpeg(bitmap, stream, quality));
		}

		public static ScreenFrameResult CaptureRectangleBgra32(ScreenRectangle bounds, bool includeCursor = true)
		{
			using var bitmap = CaptureBitmap(bounds, includeCursor, out var captureBounds, out var cursorIncluded);
			var stride = captureBounds.Width * 4;
			var bytes = new byte[stride * captureBounds.Height];
			var bitmapData = bitmap.LockBits(
				new Rectangle(0, 0, bitmap.Width, bitmap.Height),
				ImageLockMode.ReadOnly,
				PixelFormat.Format32bppRgb);

			try
			{
				for (var row = 0; row < captureBounds.Height; row++)
				{
					var sourceRow = captureBounds.Height - 1 - row;
					var source = bitmapData.Scan0 + (sourceRow * bitmapData.Stride);
					Marshal.Copy(source, bytes, row * stride, stride);
				}
			}
			finally
			{
				bitmap.UnlockBits(bitmapData);
			}

			return new ScreenFrameResult(
				bytes,
				captureBounds.Width,
				captureBounds.Height,
				stride,
				captureBounds,
				cursorIncluded,
				DateTimeOffset.UtcNow);
		}

		private static ScreenCaptureResult CaptureRectangle(
			ScreenRectangle bounds,
			bool includeCursor,
			string mimeType,
			Action<Bitmap, Stream> save)
		{
			using var bitmap = CaptureBitmap(bounds, includeCursor, out var captureBounds, out var cursorIncluded);
			using var stream = new MemoryStream();
			save(bitmap, stream);

			return new ScreenCaptureResult(
				stream.ToArray(),
				mimeType,
				captureBounds.Width,
				captureBounds.Height,
				captureBounds,
				cursorIncluded,
				DateTimeOffset.UtcNow);
		}

		private static Bitmap CaptureBitmap(
			ScreenRectangle bounds,
			bool includeCursor,
			out ScreenRectangle captureBounds,
			out bool cursorIncluded)
		{
			if (bounds.IsEmpty)
			{
				throw new ArgumentOutOfRangeException(nameof(bounds), "Capture bounds must have positive width and height.");
			}

			var virtualScreen = GetVirtualScreenBounds();
			captureBounds = bounds.Intersect(virtualScreen);

			if (captureBounds.IsEmpty)
			{
				throw new ArgumentOutOfRangeException(nameof(bounds), "Capture bounds must overlap the virtual screen.");
			}

			var bitmap = new Bitmap(captureBounds.Width, captureBounds.Height, PixelFormat.Format32bppRgb);
			using var graphics = Graphics.FromImage(bitmap);

			graphics.CopyFromScreen(
				captureBounds.X,
				captureBounds.Y,
				0,
				0,
				new Size(captureBounds.Width, captureBounds.Height),
				CopyPixelOperation.SourceCopy);

			cursorIncluded = includeCursor && DrawCursor(graphics, captureBounds);
			return bitmap;
		}

		private static void SaveJpeg(Bitmap bitmap, Stream stream, int quality)
		{
			var encoder = ImageCodecInfo
				.GetImageEncoders()
				.FirstOrDefault(codec => string.Equals(codec.MimeType, JpegMimeType, StringComparison.OrdinalIgnoreCase))
				?? throw new InvalidOperationException("JPEG encoder was not found.");

			using var parameters = new EncoderParameters(1);
			parameters.Param[0] = new EncoderParameter(Encoder.Quality, quality);
			bitmap.Save(stream, encoder, parameters);
		}

		public static ScreenCaptureResult CaptureForegroundWindow(bool includeCursor = true)
		{
			var foregroundWindow = WindowControl.GetForegroundWindowSnapshot()
				?? throw new InvalidOperationException("No foreground window was found.");

			return CaptureRectangle(foregroundWindow.Bounds, includeCursor);
		}

		public static void SavePng(string path, ScreenRectangle? bounds = null, bool includeCursor = true)
		{
			var capture = bounds.HasValue
				? CaptureRectangle(bounds.Value, includeCursor)
				: CaptureVirtualScreen(includeCursor);

			var directory = Path.GetDirectoryName(Path.GetFullPath(path));
			if (!string.IsNullOrWhiteSpace(directory))
			{
				Directory.CreateDirectory(directory);
			}

			File.WriteAllBytes(path, capture.Bytes);
		}

		private static bool DrawCursor(Graphics graphics, ScreenRectangle captureBounds)
		{
			var cursorInfo = new Imports.CURSORINFO
			{
				cbSize = Marshal.SizeOf<Imports.CURSORINFO>()
			};

			if (!Imports.GetCursorInfo(ref cursorInfo) ||
				cursorInfo.flags != Imports.CURSOR_SHOWING ||
				cursorInfo.hCursor == IntPtr.Zero)
			{
				return false;
			}

			var cursorPoint = new ScreenPoint(cursorInfo.ptScreenPos.X, cursorInfo.ptScreenPos.Y);
			if (!captureBounds.Contains(cursorPoint))
			{
				return false;
			}

			var hdc = graphics.GetHdc();
			try
			{
				Imports.DrawIconEx(
					hdc,
					cursorPoint.X - captureBounds.X,
					cursorPoint.Y - captureBounds.Y,
					cursorInfo.hCursor,
					0,
					0,
					0,
					IntPtr.Zero,
					Imports.DI_NORMAL);

				return true;
			}
			finally
			{
				graphics.ReleaseHdc(hdc);
			}
		}

		private static ScreenRectangle ToScreenRectangle(Imports.RECT rectangle)
		{
			return ScreenRectangle.FromLTRB(
				rectangle.Left,
				rectangle.Top,
				rectangle.Right,
				rectangle.Bottom);
		}

		private static void ThrowLastWin32Error(string message)
		{
			throw new InvalidOperationException($"{message}. Win32 error: {Marshal.GetLastWin32Error()}");
		}
	}
}
