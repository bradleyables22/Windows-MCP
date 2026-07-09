using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using Server.InteropServices;
using System.Text.Json;

namespace Server.ScreenRecording
{
	public sealed class ScreenRecordingManager : IHostedService, IDisposable
	{
		private const int DefaultFramesPerSecond = 5;
		private const int MinFramesPerSecond = 1;
		private const int MaxFramesPerSecond = 15;
		private const int DefaultMaxDurationSeconds = 300;
		private const int HardMaxDurationSeconds = 1800;
		private const int MinLeaseSeconds = 10;
		private const int DefaultVideoBitrate = 4_000_000;
		private const int MinVideoBitrate = 100_000;
		private const int MaxVideoBitrate = 50_000_000;
		private const long DefaultMaxBytes = 512L * 1024L * 1024L;
		private const long HardMaxBytes = 2L * 1024L * 1024L * 1024L;

		private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
		{
			WriteIndented = true
		};

		private readonly ILogger<ScreenRecordingManager> logger;
		private readonly object syncRoot = new();
		private readonly Dictionary<string, ActiveRecording> activeRecordings = new(StringComparer.OrdinalIgnoreCase);
		private bool systemEventsSubscribed;

		public ScreenRecordingManager(ILogger<ScreenRecordingManager> logger)
		{
			this.logger = logger;

			var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
			StoragePath = Path.Combine(localAppData, "WindowsMCP", "recordings");
			StatePath = Path.Combine(StoragePath, "state");
		}

		public string StoragePath { get; }

		private string StatePath { get; }

		public Task StartAsync(CancellationToken cancellationToken)
		{
			Directory.CreateDirectory(StoragePath);
			Directory.CreateDirectory(StatePath);
			RecoverInterruptedRecordings();
			SubscribeSystemEvents();

			return Task.CompletedTask;
		}

		public async Task StopAsync(CancellationToken cancellationToken)
		{
			UnsubscribeSystemEvents();
			await StopAllAsync("server_shutdown", interrupted: true, cancellationToken).ConfigureAwait(false);
		}

		public void Dispose()
		{
			UnsubscribeSystemEvents();
		}

		public ScreenRecordingState StartRecording(
			ScreenRectangle? bounds,
			int framesPerSecond = DefaultFramesPerSecond,
			bool includeCursor = true,
			int maxDurationSeconds = DefaultMaxDurationSeconds,
			int? leaseSeconds = null,
			long maxBytes = DefaultMaxBytes,
			string? outputPath = null,
			int videoBitrate = DefaultVideoBitrate)
		{
			framesPerSecond = ValidateRange(
				framesPerSecond,
				MinFramesPerSecond,
				MaxFramesPerSecond,
				nameof(framesPerSecond));
			maxDurationSeconds = ValidateRange(
				maxDurationSeconds,
				1,
				HardMaxDurationSeconds,
				nameof(maxDurationSeconds));
			videoBitrate = ValidateRange(
				videoBitrate,
				MinVideoBitrate,
				MaxVideoBitrate,
				nameof(videoBitrate));

			if (maxBytes <= 0 || maxBytes > HardMaxBytes)
			{
				throw new ArgumentOutOfRangeException(nameof(maxBytes), maxBytes, $"Maximum bytes must be between 1 and {HardMaxBytes}.");
			}

			var captureBounds = NormalizeCaptureBounds(bounds);
			var recordingId = CreateRecordingId();
			var resolvedOutputPath = ResolveOutputPath(outputPath, recordingId);
			var partialPath = CreatePartialPath(resolvedOutputPath);

			if (File.Exists(resolvedOutputPath))
			{
				throw new IOException($"Output file '{resolvedOutputPath}' already exists.");
			}

			if (File.Exists(partialPath))
			{
				throw new IOException($"Partial output file '{partialPath}' already exists.");
			}

			var now = DateTimeOffset.UtcNow;
			var maxEndsAt = now.AddSeconds(maxDurationSeconds);
			var initialLeaseSeconds = leaseSeconds.HasValue
				? ValidateRange(leaseSeconds.Value, MinLeaseSeconds, maxDurationSeconds, nameof(leaseSeconds))
				: Math.Min(DefaultMaxDurationSeconds, maxDurationSeconds);

			var state = new ScreenRecordingState(
				recordingId,
				"recording",
				resolvedOutputPath,
				partialPath,
				captureBounds,
				captureBounds.Width,
				captureBounds.Height,
				framesPerSecond,
				includeCursor,
				videoBitrate,
				now,
				null,
				now.AddSeconds(initialLeaseSeconds),
				maxEndsAt,
				maxBytes,
				0,
				0,
				null,
				null);

			var activeRecording = new ActiveRecording(state);

			lock (syncRoot)
			{
				activeRecordings.Add(recordingId, activeRecording);
			}

			SaveState(state);
			activeRecording.Task = Task.Run(() => RecordAsync(activeRecording));

			return state;
		}

		public ScreenRecordingState StopRecording(string recordingId, int waitMilliseconds = 10000)
		{
			if (waitMilliseconds < 0)
			{
				throw new ArgumentOutOfRangeException(nameof(waitMilliseconds), waitMilliseconds, "Wait milliseconds cannot be negative.");
			}

			var recording = GetActiveRecording(recordingId);
			if (recording is null)
			{
				var existing = GetRecording(recordingId);
				if (!IsActiveStatus(existing.Status))
				{
					return existing;
				}

				throw new InvalidOperationException($"Recording '{recordingId}' is not active.");
			}

			recording.RequestStop("stopped_by_request", interrupted: false);

			if (waitMilliseconds == 0)
			{
				return recording.Snapshot;
			}

			if (!recording.Task.Wait(waitMilliseconds))
			{
				return recording.Snapshot with { Status = "stopping", StopReason = "stopped_by_request" };
			}

			return GetRecording(recordingId);
		}

		public ScreenRecordingState RenewRecording(string recordingId, int extendSeconds)
		{
			if (extendSeconds < MinLeaseSeconds || extendSeconds > HardMaxDurationSeconds)
			{
				throw new ArgumentOutOfRangeException(
					nameof(extendSeconds),
					extendSeconds,
					$"Extend seconds must be between {MinLeaseSeconds} and {HardMaxDurationSeconds}.");
			}

			var recording = GetActiveRecording(recordingId)
				?? throw new InvalidOperationException($"Recording '{recordingId}' is not active.");

			var updated = recording.Update(state =>
			{
				var requestedExpiration = DateTimeOffset.UtcNow.AddSeconds(extendSeconds);
				var expiresAt = requestedExpiration <= state.MaxEndsAt
					? requestedExpiration
					: state.MaxEndsAt;

				return state with { ExpiresAt = expiresAt };
			});

			SaveState(updated);
			return updated;
		}

		public ScreenRecordingState GetRecording(string recordingId)
		{
			var active = GetActiveRecording(recordingId);
			if (active is not null)
			{
				return active.Snapshot;
			}

			var stateFile = GetStateFile(recordingId);
			if (!File.Exists(stateFile))
			{
				throw new FileNotFoundException($"Recording '{recordingId}' was not found.", stateFile);
			}

			return LoadStateFile(stateFile);
		}

		public IReadOnlyList<ScreenRecordingState> ListRecordings()
		{
			Directory.CreateDirectory(StatePath);

			var states = new Dictionary<string, ScreenRecordingState>(StringComparer.OrdinalIgnoreCase);

			foreach (var file in Directory.EnumerateFiles(StatePath, "*.json"))
			{
				var state = LoadStateFile(file);
				states[state.RecordingId] = state;
			}

			lock (syncRoot)
			{
				foreach (var recording in activeRecordings.Values)
				{
					states[recording.Snapshot.RecordingId] = recording.Snapshot;
				}
			}

			return states.Values
				.OrderByDescending(state => state.StartedAt)
				.ToArray();
		}

		public void DeleteRecording(string recordingId, bool deleteOutput = false)
		{
			if (GetActiveRecording(recordingId) is not null)
			{
				throw new InvalidOperationException($"Recording '{recordingId}' is active. Stop it before deleting it.");
			}

			var stateFile = GetStateFile(recordingId);
			if (!File.Exists(stateFile))
			{
				throw new FileNotFoundException($"Recording '{recordingId}' was not found.", stateFile);
			}

			var state = LoadStateFile(stateFile);
			File.Delete(stateFile);

			if (!deleteOutput)
			{
				return;
			}

			DeleteIfExists(state.OutputPath);
			DeleteIfExists(state.PartialPath);
		}

		private async Task RecordAsync(ActiveRecording recording)
		{
			var stopReason = "completed";
			var interrupted = false;
			Exception? failure = null;

			try
			{
				var state = recording.Snapshot;
				using var writer = new MediaFoundationMp4Writer(
					state.PartialPath,
					state.Width,
					state.Height,
					state.FramesPerSecond,
					state.VideoBitrate);

				var frameInterval = TimeSpan.FromSeconds(1d / state.FramesPerSecond);
				var nextFrameAt = DateTimeOffset.UtcNow;
				var nextStateSaveAt = DateTimeOffset.UtcNow.AddSeconds(2);

				while (true)
				{
					state = recording.Snapshot;

					if (recording.Cancellation.IsCancellationRequested)
					{
						stopReason = recording.StopReason ?? "stopped";
						interrupted = recording.Interrupted;
						break;
					}

					var now = DateTimeOffset.UtcNow;
					if (now >= state.ExpiresAt)
					{
						stopReason = "lease_expired";
						break;
					}

					if (now >= state.MaxEndsAt)
					{
						stopReason = "max_duration_reached";
						break;
					}

					if (writer.BytesWritten >= state.MaxBytes)
					{
						stopReason = "max_bytes_reached";
						break;
					}

					var capture = ScreenControl.CaptureRectangleBgra32(
						state.Bounds,
						state.IncludeCursor);

					writer.WriteFrame(capture);

					state = recording.Update(current => current with
					{
						BytesWritten = writer.BytesWritten,
						FramesWritten = writer.FrameCount
					});

					if (DateTimeOffset.UtcNow >= nextStateSaveAt)
					{
						SaveState(state);
						nextStateSaveAt = DateTimeOffset.UtcNow.AddSeconds(2);
					}

					nextFrameAt = nextFrameAt.Add(frameInterval);
					var delay = nextFrameAt - DateTimeOffset.UtcNow;
					if (delay > TimeSpan.Zero)
					{
						await Task.Delay(delay, recording.Cancellation.Token).ConfigureAwait(false);
					}
					else
					{
						nextFrameAt = DateTimeOffset.UtcNow;
					}
				}

				writer.Complete();
			}
			catch (OperationCanceledException) when (recording.Cancellation.IsCancellationRequested)
			{
				stopReason = recording.StopReason ?? "stopped";
				interrupted = recording.Interrupted;
			}
			catch (Exception exception)
			{
				failure = exception;
				stopReason = "failed";
				logger.LogError(exception, "Screen recording {RecordingId} failed.", recording.Snapshot.RecordingId);
			}
			finally
			{
				var completedAt = DateTimeOffset.UtcNow;
				var finalState = recording.Update(state =>
				{
					var status = failure is not null
						? "failed"
						: interrupted ? "interrupted" : "completed";

					return state with
					{
						Status = status,
						CompletedAt = completedAt,
						BytesWritten = File.Exists(state.PartialPath) ? new FileInfo(state.PartialPath).Length : state.BytesWritten,
						StopReason = stopReason,
						ErrorMessage = failure?.Message
					};
				});

				if (failure is null)
				{
					MovePartialToOutput(recording.Snapshot.PartialPath, recording.Snapshot.OutputPath);
					finalState = recording.Update(state => state with
					{
						BytesWritten = File.Exists(state.OutputPath) ? new FileInfo(state.OutputPath).Length : state.BytesWritten
					});
				}

				SaveState(finalState);

				lock (syncRoot)
				{
					activeRecordings.Remove(finalState.RecordingId);
				}

				recording.Dispose();
			}
		}

		private async Task StopAllAsync(
			string reason,
			bool interrupted,
			CancellationToken cancellationToken)
		{
			ActiveRecording[] recordings;
			lock (syncRoot)
			{
				recordings = activeRecordings.Values.ToArray();
			}

			foreach (var recording in recordings)
			{
				recording.RequestStop(reason, interrupted);
			}

			foreach (var recording in recordings)
			{
				try
				{
					await recording.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
				}
				catch (OperationCanceledException)
				{
					return;
				}
			}
		}

		private void RecoverInterruptedRecordings()
		{
			foreach (var file in Directory.EnumerateFiles(StatePath, "*.json"))
			{
				try
				{
					var state = LoadStateFile(file);
					if (!IsActiveStatus(state.Status))
					{
						continue;
					}

					var recovered = state with
					{
						Status = "interrupted",
						CompletedAt = DateTimeOffset.UtcNow,
						StopReason = "server_startup_recovery",
						BytesWritten = File.Exists(state.PartialPath)
							? new FileInfo(state.PartialPath).Length
							: state.BytesWritten
					};

					SaveState(recovered);
				}
				catch (Exception exception)
				{
					logger.LogWarning(exception, "Could not recover screen recording state file {StateFile}.", file);
				}
			}
		}

		private void SubscribeSystemEvents()
		{
			if (systemEventsSubscribed)
			{
				return;
			}

			try
			{
				SystemEvents.SessionEnding += OnSessionEnding;
				SystemEvents.PowerModeChanged += OnPowerModeChanged;
				systemEventsSubscribed = true;
			}
			catch (Exception exception)
			{
				logger.LogWarning(exception, "Could not subscribe to Windows session or power events.");
			}
		}

		private void UnsubscribeSystemEvents()
		{
			if (!systemEventsSubscribed)
			{
				return;
			}

			try
			{
				SystemEvents.SessionEnding -= OnSessionEnding;
				SystemEvents.PowerModeChanged -= OnPowerModeChanged;
			}
			catch (Exception exception)
			{
				logger.LogWarning(exception, "Could not unsubscribe from Windows session or power events.");
			}
			finally
			{
				systemEventsSubscribed = false;
			}
		}

		private void OnSessionEnding(object sender, SessionEndingEventArgs args)
		{
			_ = Task.Run(() => StopAllAsync("session_ending", interrupted: true, CancellationToken.None));
		}

		private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs args)
		{
			if (args.Mode == PowerModes.Suspend)
			{
				_ = Task.Run(() => StopAllAsync("power_suspend", interrupted: true, CancellationToken.None));
			}
		}

		private ActiveRecording? GetActiveRecording(string recordingId)
		{
			if (string.IsNullOrWhiteSpace(recordingId))
			{
				throw new ArgumentException("Recording ID cannot be empty.", nameof(recordingId));
			}

			lock (syncRoot)
			{
				return activeRecordings.GetValueOrDefault(recordingId);
			}
		}

		private ScreenRecordingState LoadStateFile(string stateFile)
		{
			var json = File.ReadAllText(stateFile);
			return JsonSerializer.Deserialize<ScreenRecordingState>(json, JsonOptions)
				?? throw new InvalidOperationException($"State file '{stateFile}' did not contain a recording state.");
		}

		private void SaveState(ScreenRecordingState state)
		{
			Directory.CreateDirectory(StatePath);
			var stateFile = GetStateFile(state.RecordingId);
			var json = JsonSerializer.Serialize(state, JsonOptions);
			File.WriteAllText(stateFile, json);
		}

		private string GetStateFile(string recordingId)
		{
			if (string.IsNullOrWhiteSpace(recordingId))
			{
				throw new ArgumentException("Recording ID cannot be empty.", nameof(recordingId));
			}

			if (recordingId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
			{
				throw new ArgumentException("Recording ID contains invalid filename characters.", nameof(recordingId));
			}

			return Path.Combine(StatePath, recordingId + ".json");
		}

		private static ScreenRectangle NormalizeCaptureBounds(ScreenRectangle? requestedBounds)
		{
			var virtualScreen = ScreenControl.GetVirtualScreenBounds();
			var captureBounds = (requestedBounds ?? virtualScreen).Intersect(virtualScreen);

			if (captureBounds.IsEmpty)
			{
				throw new ArgumentOutOfRangeException(nameof(requestedBounds), "Recording bounds must overlap the virtual screen.");
			}

			return captureBounds;
		}

		private string ResolveOutputPath(string? outputPath, string recordingId)
		{
			if (string.IsNullOrWhiteSpace(outputPath))
			{
				return Path.Combine(StoragePath, recordingId + ".mp4");
			}

			var fullPath = Path.GetFullPath(outputPath);
			if (string.IsNullOrWhiteSpace(Path.GetExtension(fullPath)))
			{
				fullPath = Path.ChangeExtension(fullPath, ".mp4");
			}

			if (!string.Equals(Path.GetExtension(fullPath), ".mp4", StringComparison.OrdinalIgnoreCase))
			{
				throw new ArgumentException("Screen recording output path must use the .mp4 extension.", nameof(outputPath));
			}

			var directory = Path.GetDirectoryName(fullPath);
			if (!string.IsNullOrWhiteSpace(directory))
			{
				Directory.CreateDirectory(directory);
			}

			return fullPath;
		}

		private static string CreateRecordingId()
		{
			return DateTimeOffset.UtcNow.ToString("yyyyMMdd_HHmmss_fff") + "_" + Guid.NewGuid().ToString("N")[..8];
		}

		private static string CreatePartialPath(string outputPath)
		{
			var directory = Path.GetDirectoryName(outputPath);
			var filename = Path.GetFileNameWithoutExtension(outputPath);
			var extension = Path.GetExtension(outputPath);
			return Path.Combine(directory ?? string.Empty, filename + ".partial" + extension);
		}

		private static int ValidateRange(int value, int minimum, int maximum, string parameterName)
		{
			if (value < minimum || value > maximum)
			{
				throw new ArgumentOutOfRangeException(parameterName, value, $"{parameterName} must be between {minimum} and {maximum}.");
			}

			return value;
		}

		private static bool IsActiveStatus(string status)
		{
			return string.Equals(status, "recording", StringComparison.OrdinalIgnoreCase) ||
				string.Equals(status, "stopping", StringComparison.OrdinalIgnoreCase);
		}

		private static void MovePartialToOutput(string partialPath, string outputPath)
		{
			if (!File.Exists(partialPath))
			{
				return;
			}

			if (File.Exists(outputPath))
			{
				File.Delete(outputPath);
			}

			File.Move(partialPath, outputPath);
		}

		private static void DeleteIfExists(string path)
		{
			if (File.Exists(path))
			{
				File.Delete(path);
			}
		}

		private sealed class ActiveRecording : IDisposable
		{
			private readonly object syncRoot = new();
			private ScreenRecordingState state;

			public ActiveRecording(ScreenRecordingState state)
			{
				this.state = state;
			}

			public CancellationTokenSource Cancellation { get; } = new();

			public Task Task { get; set; } = Task.CompletedTask;

			public string? StopReason { get; private set; }

			public bool Interrupted { get; private set; }

			public ScreenRecordingState Snapshot
			{
				get
				{
					lock (syncRoot)
					{
						return state;
					}
				}
			}

			public ScreenRecordingState Update(Func<ScreenRecordingState, ScreenRecordingState> update)
			{
				lock (syncRoot)
				{
					state = update(state);
					return state;
				}
			}

			public void RequestStop(string reason, bool interrupted)
			{
				lock (syncRoot)
				{
					StopReason ??= reason;
					Interrupted |= interrupted;
					state = state with { Status = "stopping", StopReason = StopReason };
				}

				Cancellation.Cancel();
			}

			public void Dispose()
			{
				Cancellation.Dispose();
			}
		}
	}
}
