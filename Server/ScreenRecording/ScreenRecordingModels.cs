using Server.InteropServices;

namespace Server.ScreenRecording
{
	public sealed record ScreenRecordingStorageInfo(string Path);

	public sealed record ScreenRecordingState(
		string RecordingId,
		string Status,
		string OutputPath,
		string PartialPath,
		ScreenRectangle Bounds,
		int Width,
		int Height,
		int FramesPerSecond,
		bool IncludeCursor,
		int VideoBitrate,
		DateTimeOffset StartedAt,
		DateTimeOffset? CompletedAt,
		DateTimeOffset ExpiresAt,
		DateTimeOffset MaxEndsAt,
		long MaxBytes,
		long BytesWritten,
		int FramesWritten,
		string? StopReason,
		string? ErrorMessage);
}
