using Server.InteropServices;

namespace Server.Tools
{
	public sealed record PointInfo(int X, int Y);

	public sealed record RectangleInfo(
		int X,
		int Y,
		int Width,
		int Height,
		int Left,
		int Top,
		int Right,
		int Bottom);

	public sealed record MonitorInfo(
		string DeviceName,
		RectangleInfo Bounds,
		RectangleInfo WorkingArea,
		bool IsPrimary);

	public sealed record ScreenshotInfo(
		string MimeType,
		string PngBase64,
		int Width,
		int Height,
		RectangleInfo Bounds,
		bool CursorIncluded,
		DateTimeOffset CapturedAt);

	public sealed record WindowInfo(
		long Handle,
		string Title,
		string ClassName,
		int ProcessId,
		RectangleInfo Bounds,
		bool IsVisible,
		bool IsForeground,
		bool IsMinimized,
		bool IsMaximized);

	public sealed record ProcessInfo(
		int Id,
		string Name,
		string MainWindowTitle,
		long MainWindowHandle,
		bool HasMainWindow,
		bool Responding,
		DateTimeOffset? StartedAt,
		string? FilePath);

	public sealed record ProcessCloseInfo(
		int ProcessId,
		string ProcessName,
		bool CloseMessageSent,
		bool Exited,
		double WaitedMilliseconds);

	public sealed record ClipboardTextInfo(bool ContainsText, string? Text);

	public sealed record ActionInfo(bool Succeeded, string Message);

	public sealed record WaitInfo<T>(
		bool Succeeded,
		T? Value,
		double ElapsedMilliseconds,
		int Attempts,
		string? Message);

	internal static class ToolModels
	{
		public static PointInfo ToInfo(ScreenPoint point)
		{
			return new PointInfo(point.X, point.Y);
		}

		public static RectangleInfo ToInfo(ScreenRectangle rectangle)
		{
			return new RectangleInfo(
				rectangle.X,
				rectangle.Y,
				rectangle.Width,
				rectangle.Height,
				rectangle.Left,
				rectangle.Top,
				rectangle.Right,
				rectangle.Bottom);
		}

		public static MonitorInfo ToInfo(DisplayMonitor monitor)
		{
			return new MonitorInfo(
				monitor.DeviceName,
				ToInfo(monitor.Bounds),
				ToInfo(monitor.WorkingArea),
				monitor.IsPrimary);
		}

		public static ScreenshotInfo ToInfo(ScreenCaptureResult capture)
		{
			return new ScreenshotInfo(
				capture.MimeType,
				Convert.ToBase64String(capture.Bytes),
				capture.Width,
				capture.Height,
				ToInfo(capture.Bounds),
				capture.CursorIncluded,
				capture.CapturedAt);
		}

		public static WindowInfo ToInfo(WindowSnapshot window)
		{
			return new WindowInfo(
				window.Handle.ToInt64(),
				window.Title,
				window.ClassName,
				window.ProcessId,
				ToInfo(window.Bounds),
				window.IsVisible,
				window.IsForeground,
				window.IsMinimized,
				window.IsMaximized);
		}

		public static ProcessInfo ToInfo(ProcessSnapshot process)
		{
			return new ProcessInfo(
				process.Id,
				process.Name,
				process.MainWindowTitle,
				process.MainWindowHandle.ToInt64(),
				process.HasMainWindow,
				process.Responding,
				process.StartedAt,
				process.FilePath);
		}

		public static ProcessCloseInfo ToInfo(ProcessCloseResult result)
		{
			return new ProcessCloseInfo(
				result.ProcessId,
				result.ProcessName,
				result.CloseMessageSent,
				result.Exited,
				result.Waited.TotalMilliseconds);
		}

		public static WaitInfo<TInfo> ToInfo<TValue, TInfo>(
			WaitResult<TValue> result,
			Func<TValue, TInfo> mapValue)
		{
			return new WaitInfo<TInfo>(
				result.Succeeded,
				result.Value is null ? default : mapValue(result.Value),
				result.Elapsed.TotalMilliseconds,
				result.Attempts,
				result.Message);
		}
	}
}
