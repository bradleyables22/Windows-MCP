using ModelContextProtocol.Server;
using Server.ScreenRecording;
using System.ComponentModel;

namespace Server.Tools
{
	public sealed record ScreenRecordingInfo(
		string RecordingId,
		string Status,
		string OutputPath,
		string PartialPath,
		RectangleInfo Bounds,
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

	public sealed class ScreenRecordingTools
	{
		private readonly ScreenRecordingManager recordings;

		public ScreenRecordingTools(ScreenRecordingManager recordings)
		{
			this.recordings = recordings;
		}

		[McpServerTool]
		[Description("Returns the local folder where screen recording MP4 files and state JSON are stored. Call this when you need to find or show saved recordings.")]
		public ScreenRecordingStorageInfo GetScreenRecordingStorage()
		{
			return new ScreenRecordingStorageInfo(recordings.StoragePath);
		}

		[McpServerTool]
		[Description("Starts a background screen recording to an H.264 MP4 file using Windows Media Foundation. Always save the returned recordingId and later call get_screen_recording_status or stop_screen_recording. The recording auto-stops when maxDurationSeconds, leaseSeconds, or maxBytes is reached.")]
		public ScreenRecordingInfo StartScreenRecording(
			[Description("Optional capture region X coordinate in virtual-screen pixels. Provide x, y, width, and height together, or omit all four to record the full virtual screen.")] int? x = null,
			[Description("Optional capture region Y coordinate in virtual-screen pixels. Provide x, y, width, and height together, or omit all four to record the full virtual screen.")] int? y = null,
			[Description("Optional capture region width in pixels. Provide x, y, width, and height together, or omit all four to record the full virtual screen.")] int? width = null,
			[Description("Optional capture region height in pixels. Provide x, y, width, and height together, or omit all four to record the full virtual screen.")] int? height = null,
			[Description("Frames per second. Allowed range is 1 to 15. Use lower values for lightweight AI observation and higher values for smoother demos.")] int framesPerSecond = 5,
			[Description("Whether to include the current mouse cursor in the recording.")] bool includeCursor = true,
			[Description("Hard maximum recording duration in seconds. Allowed range is 1 to 1800. This is the absolute cap even when the lease is renewed.")] int maxDurationSeconds = 300,
			[Description("Optional initial lease in seconds. If omitted, the lease defaults up to 300 seconds. If not renewed before ExpiresAt, recording stops automatically.")] int? leaseSeconds = null,
			[Description("Maximum output bytes before recording auto-stops. Default is 512 MiB.")] long maxBytes = 536870912,
			[Description("Optional output .mp4 file path. If omitted, a unique MP4 is written under the screen recording storage folder.")] string? outputPath = null,
			[Description("Target H.264 video bitrate in bits per second. Allowed range is 100000 to 50000000. Default 4000000 is a good general-purpose screen recording setting.")] int videoBitrate = 4000000)
		{
			var region = ToolParsing.ToOptionalRectangle(x, y, width, height);
			return ToInfo(recordings.StartRecording(
				region,
				framesPerSecond,
				includeCursor,
				maxDurationSeconds,
				leaseSeconds,
				maxBytes,
				outputPath,
				videoBitrate));
		}

		[McpServerTool]
		[Description("Stops a screen recording and finalizes its MP4 file. If the recording already auto-stopped, this returns the saved final status instead of failing.")]
		public ScreenRecordingInfo StopScreenRecording(
			[Description("Recording ID returned by start_screen_recording.")] string recordingId,
			[Description("Milliseconds to wait for finalization. Use 0 to request stop and return immediately; otherwise 10000 is usually enough.")] int waitMilliseconds = 10000)
		{
			return ToInfo(recordings.StopRecording(recordingId, waitMilliseconds));
		}

		[McpServerTool]
		[Description("Extends an active screen recording lease so it does not stop from lease expiration. This cannot extend past the recording's MaxEndsAt hard duration cap.")]
		public ScreenRecordingInfo RenewScreenRecording(
			[Description("Recording ID returned by start_screen_recording.")] string recordingId,
			[Description("Seconds from now before the renewed lease expires. Allowed range is 10 to 1800, but the effective expiration is capped at MaxEndsAt.")] int extendSeconds = 300)
		{
			return ToInfo(recordings.RenewRecording(recordingId, extendSeconds));
		}

		[McpServerTool]
		[Description("Gets current or final status for a screen recording, including outputPath, partialPath, ExpiresAt, MaxEndsAt, framesWritten, bytesWritten, stopReason, and any errorMessage.")]
		public ScreenRecordingInfo GetScreenRecordingStatus(
			[Description("Recording ID returned by start_screen_recording.")] string recordingId)
		{
			return ToInfo(recordings.GetRecording(recordingId));
		}

		[McpServerTool]
		[Description("Lists known screen recording states newest first. Use this to recover recording IDs or find recently completed MP4 outputs.")]
		public IReadOnlyList<ScreenRecordingInfo> ListScreenRecordings()
		{
			return recordings.ListRecordings()
				.Select(ToInfo)
				.ToArray();
		}

		[McpServerTool]
		[Description("Deletes a stopped screen recording's state JSON. Set deleteOutput true only when you also want to remove the final or partial MP4 files.")]
		public ActionInfo DeleteScreenRecording(
			[Description("Recording ID to delete.")] string recordingId,
			[Description("When true, delete outputPath and partialPath files too. When false, keep the MP4 files and delete only state metadata.")] bool deleteOutput = false)
		{
			recordings.DeleteRecording(recordingId, deleteOutput);
			return new ActionInfo(true, $"Screen recording '{recordingId}' deleted.");
		}

		private static ScreenRecordingInfo ToInfo(ScreenRecordingState state)
		{
			return new ScreenRecordingInfo(
				state.RecordingId,
				state.Status,
				state.OutputPath,
				state.PartialPath,
				ToolModels.ToInfo(state.Bounds),
				state.Width,
				state.Height,
				state.FramesPerSecond,
				state.IncludeCursor,
				state.VideoBitrate,
				state.StartedAt,
				state.CompletedAt,
				state.ExpiresAt,
				state.MaxEndsAt,
				state.MaxBytes,
				state.BytesWritten,
				state.FramesWritten,
				state.StopReason,
				state.ErrorMessage);
		}
	}
}
