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
		[Description("Gets the folder where screen recordings and recording state are stored.")]
		public ScreenRecordingStorageInfo GetScreenRecordingStorage()
		{
			return new ScreenRecordingStorageInfo(recordings.StoragePath);
		}

		[McpServerTool]
		[Description("Starts a leased background screen recording as an H.264 MP4 file using Windows Media Foundation and returns its recording ID.")]
		public ScreenRecordingInfo StartScreenRecording(
			[Description("Optional region X coordinate. Provide all region values or none.")] int? x = null,
			[Description("Optional region Y coordinate. Provide all region values or none.")] int? y = null,
			[Description("Optional region width. Provide all region values or none.")] int? width = null,
			[Description("Optional region height. Provide all region values or none.")] int? height = null,
			[Description("Frames per second. Allowed range is 1 to 15.")] int framesPerSecond = 5,
			[Description("Whether to include the current mouse cursor in the recording.")] bool includeCursor = true,
			[Description("Hard maximum recording duration in seconds. Allowed range is 1 to 1800.")] int maxDurationSeconds = 300,
			[Description("Optional initial lease in seconds. If not renewed before this expires, recording stops.")] int? leaseSeconds = null,
			[Description("Maximum output bytes before recording auto-stops. Default is 512 MiB.")] long maxBytes = 536870912,
			[Description("Optional output MP4 file path. Defaults under the screen recording storage folder.")] string? outputPath = null,
			[Description("Target H.264 video bitrate in bits per second. Allowed range is 100000 to 50000000.")] int videoBitrate = 4000000)
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
		[Description("Stops an active screen recording and returns its final status.")]
		public ScreenRecordingInfo StopScreenRecording(
			[Description("Recording ID returned by start_screen_recording.")] string recordingId,
			[Description("Milliseconds to wait for the recording to finalize before returning a stopping status.")] int waitMilliseconds = 10000)
		{
			return ToInfo(recordings.StopRecording(recordingId, waitMilliseconds));
		}

		[McpServerTool]
		[Description("Renews an active screen recording lease, bounded by its hard maximum duration.")]
		public ScreenRecordingInfo RenewScreenRecording(
			[Description("Recording ID returned by start_screen_recording.")] string recordingId,
			[Description("Seconds from now before the renewed lease expires.")] int extendSeconds = 300)
		{
			return ToInfo(recordings.RenewRecording(recordingId, extendSeconds));
		}

		[McpServerTool]
		[Description("Gets the current or final status for a screen recording.")]
		public ScreenRecordingInfo GetScreenRecordingStatus(
			[Description("Recording ID returned by start_screen_recording.")] string recordingId)
		{
			return ToInfo(recordings.GetRecording(recordingId));
		}

		[McpServerTool]
		[Description("Lists screen recording states, newest first.")]
		public IReadOnlyList<ScreenRecordingInfo> ListScreenRecordings()
		{
			return recordings.ListRecordings()
				.Select(ToInfo)
				.ToArray();
		}

		[McpServerTool]
		[Description("Deletes a stopped screen recording state file, optionally deleting its output files too.")]
		public ActionInfo DeleteScreenRecording(
			[Description("Recording ID to delete.")] string recordingId,
			[Description("Delete the output or partial recording files as well as the state file.")] bool deleteOutput = false)
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
