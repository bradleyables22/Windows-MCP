using System.Diagnostics;
using System.Security.Cryptography;

namespace Server.InteropServices
{
	/// <summary>
	/// Describes the result of polling for a Windows state change.
	/// </summary>
	public sealed record WaitResult<T>(
		bool Succeeded,
		T? Value,
		TimeSpan Elapsed,
		int Attempts,
		string? Message = null);

	/// <summary>
	/// Provides polling primitives for windows, processes, clipboard text, and screen changes.
	/// </summary>
	public static class WaitControl
	{
		private static readonly TimeSpan DefaultPollInterval = TimeSpan.FromMilliseconds(100);

		public static WaitResult<bool> WaitUntil(
			Func<bool> predicate,
			TimeSpan timeout,
			TimeSpan? pollInterval = null)
		{
			ArgumentNullException.ThrowIfNull(predicate);
			ValidateTimeout(timeout);

			var interval = ValidatePollInterval(pollInterval);
			var startedAt = Stopwatch.GetTimestamp();
			var attempts = 0;

			while (Stopwatch.GetElapsedTime(startedAt) <= timeout)
			{
				attempts++;

				if (predicate())
				{
					return new WaitResult<bool>(
						true,
						true,
						Stopwatch.GetElapsedTime(startedAt),
						attempts);
				}

				SleepUntilNextPoll(startedAt, timeout, interval);
			}

			return new WaitResult<bool>(
				false,
				false,
				Stopwatch.GetElapsedTime(startedAt),
				attempts,
				"Timed out waiting for predicate.");
		}

		public static WaitResult<WindowSnapshot> WaitForWindow(
			string title,
			TimeSpan timeout,
			TimeSpan? pollInterval = null,
			WindowTitleMatchMode matchMode = WindowTitleMatchMode.ContainsIgnoreCase)
		{
			ValidateTimeout(timeout);

			var interval = ValidatePollInterval(pollInterval);
			var startedAt = Stopwatch.GetTimestamp();
			var attempts = 0;

			while (Stopwatch.GetElapsedTime(startedAt) <= timeout)
			{
				attempts++;

				var matches = WindowControl.FindWindowsByTitle(title, matchMode);
				if (matches.Count == 1)
				{
					return new WaitResult<WindowSnapshot>(
						true,
						matches[0],
						Stopwatch.GetElapsedTime(startedAt),
						attempts);
				}

				if (matches.Count > 1)
				{
					var matchList = string.Join(", ", matches.Take(5).Select(window => $"'{window.Title}'"));
					throw new InvalidOperationException($"Window title '{title}' matched multiple visible windows: {matchList}.");
				}

				SleepUntilNextPoll(startedAt, timeout, interval);
			}

			return new WaitResult<WindowSnapshot>(
				false,
				null,
				Stopwatch.GetElapsedTime(startedAt),
				attempts,
				$"Timed out waiting for window '{title}'.");
		}

		public static WaitResult<bool> WaitForWindowToClose(
			string title,
			TimeSpan timeout,
			TimeSpan? pollInterval = null,
			WindowTitleMatchMode matchMode = WindowTitleMatchMode.ContainsIgnoreCase)
		{
			ValidateTimeout(timeout);

			var interval = ValidatePollInterval(pollInterval);
			var startedAt = Stopwatch.GetTimestamp();
			var attempts = 0;

			while (Stopwatch.GetElapsedTime(startedAt) <= timeout)
			{
				attempts++;

				if (WindowControl.FindWindowsByTitle(title, matchMode).Count == 0)
				{
					return new WaitResult<bool>(
						true,
						true,
						Stopwatch.GetElapsedTime(startedAt),
						attempts);
				}

				SleepUntilNextPoll(startedAt, timeout, interval);
			}

			return new WaitResult<bool>(
				false,
				false,
				Stopwatch.GetElapsedTime(startedAt),
				attempts,
				$"Timed out waiting for window '{title}' to close.");
		}

		public static WaitResult<IReadOnlyList<ProcessSnapshot>> WaitForProcess(
			string name,
			TimeSpan timeout,
			TimeSpan? pollInterval = null,
			ProcessNameMatchMode matchMode = ProcessNameMatchMode.ExactIgnoreCase)
		{
			ValidateTimeout(timeout);

			var interval = ValidatePollInterval(pollInterval);
			var startedAt = Stopwatch.GetTimestamp();
			var attempts = 0;

			while (Stopwatch.GetElapsedTime(startedAt) <= timeout)
			{
				attempts++;

				var matches = ProcessControl.FindProcessesByName(name, matchMode);
				if (matches.Count > 0)
				{
					return new WaitResult<IReadOnlyList<ProcessSnapshot>>(
						true,
						matches,
						Stopwatch.GetElapsedTime(startedAt),
						attempts);
				}

				SleepUntilNextPoll(startedAt, timeout, interval);
			}

			return new WaitResult<IReadOnlyList<ProcessSnapshot>>(
				false,
				Array.Empty<ProcessSnapshot>(),
				Stopwatch.GetElapsedTime(startedAt),
				attempts,
				$"Timed out waiting for process '{name}'.");
		}

		public static WaitResult<bool> WaitForProcessExit(
			int processId,
			TimeSpan timeout,
			TimeSpan? pollInterval = null)
		{
			ValidateTimeout(timeout);

			var interval = ValidatePollInterval(pollInterval);
			var startedAt = Stopwatch.GetTimestamp();
			var attempts = 0;

			while (Stopwatch.GetElapsedTime(startedAt) <= timeout)
			{
				attempts++;

				if (!ProcessExists(processId))
				{
					return new WaitResult<bool>(
						true,
						true,
						Stopwatch.GetElapsedTime(startedAt),
						attempts);
				}

				SleepUntilNextPoll(startedAt, timeout, interval);
			}

			return new WaitResult<bool>(
				false,
				false,
				Stopwatch.GetElapsedTime(startedAt),
				attempts,
				$"Timed out waiting for process {processId} to exit.");
		}

		public static WaitResult<string> WaitForClipboardText(
			TimeSpan timeout,
			string? expectedText = null,
			TimeSpan? pollInterval = null,
			bool containsExpectedText = true)
		{
			ValidateTimeout(timeout);

			var interval = ValidatePollInterval(pollInterval);
			var startedAt = Stopwatch.GetTimestamp();
			var attempts = 0;

			while (Stopwatch.GetElapsedTime(startedAt) <= timeout)
			{
				attempts++;

				var text = ClipboardControl.GetText();
				if (text is not null && ClipboardTextMatches(text, expectedText, containsExpectedText))
				{
					return new WaitResult<string>(
						true,
						text,
						Stopwatch.GetElapsedTime(startedAt),
						attempts);
				}

				SleepUntilNextPoll(startedAt, timeout, interval);
			}

			return new WaitResult<string>(
				false,
				null,
				Stopwatch.GetElapsedTime(startedAt),
				attempts,
				"Timed out waiting for clipboard text.");
		}

		public static WaitResult<ScreenCaptureResult> WaitForScreenChange(
			TimeSpan timeout,
			ScreenRectangle? bounds = null,
			TimeSpan? pollInterval = null,
			bool includeCursor = false)
		{
			ValidateTimeout(timeout);

			var interval = ValidatePollInterval(pollInterval);
			var startedAt = Stopwatch.GetTimestamp();
			var attempts = 0;
			var baseline = CaptureForWait(bounds, includeCursor);
			var baselineHash = SHA256.HashData(baseline.Bytes);

			while (Stopwatch.GetElapsedTime(startedAt) <= timeout)
			{
				attempts++;
				SleepUntilNextPoll(startedAt, timeout, interval);

				var current = CaptureForWait(bounds, includeCursor);
				var currentHash = SHA256.HashData(current.Bytes);

				if (!currentHash.SequenceEqual(baselineHash))
				{
					return new WaitResult<ScreenCaptureResult>(
						true,
						current,
						Stopwatch.GetElapsedTime(startedAt),
						attempts);
				}
			}

			return new WaitResult<ScreenCaptureResult>(
				false,
				null,
				Stopwatch.GetElapsedTime(startedAt),
				attempts,
				"Timed out waiting for screen pixels to change.");
		}

		private static ScreenCaptureResult CaptureForWait(ScreenRectangle? bounds, bool includeCursor)
		{
			return bounds.HasValue
				? ScreenControl.CaptureRectangle(bounds.Value, includeCursor)
				: ScreenControl.CaptureVirtualScreen(includeCursor);
		}

		private static bool ClipboardTextMatches(
			string text,
			string? expectedText,
			bool containsExpectedText)
		{
			if (expectedText is null)
			{
				return true;
			}

			return containsExpectedText
				? text.Contains(expectedText, StringComparison.Ordinal)
				: string.Equals(text, expectedText, StringComparison.Ordinal);
		}

		private static bool ProcessExists(int processId)
		{
			try
			{
				using var process = Process.GetProcessById(processId);
				return !process.HasExited;
			}
			catch (ArgumentException)
			{
				return false;
			}
			catch (InvalidOperationException)
			{
				return false;
			}
		}

		private static void ValidateTimeout(TimeSpan timeout)
		{
			if (timeout < TimeSpan.Zero)
			{
				throw new ArgumentOutOfRangeException(nameof(timeout), timeout, "Timeout cannot be negative.");
			}
		}

		private static TimeSpan ValidatePollInterval(TimeSpan? pollInterval)
		{
			var interval = pollInterval ?? DefaultPollInterval;

			if (interval <= TimeSpan.Zero)
			{
				throw new ArgumentOutOfRangeException(nameof(pollInterval), interval, "Poll interval must be positive.");
			}

			return interval;
		}

		private static void SleepUntilNextPoll(long startedAt, TimeSpan timeout, TimeSpan pollInterval)
		{
			var remaining = timeout - Stopwatch.GetElapsedTime(startedAt);
			if (remaining <= TimeSpan.Zero)
			{
				return;
			}

			Thread.Sleep(remaining < pollInterval ? remaining : pollInterval);
		}
	}
}
