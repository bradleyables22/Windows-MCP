using System.ComponentModel;
using System.Diagnostics;

namespace Server.InteropServices
{
	/// <summary>
	/// Describes a running Windows process and its primary window, when one exists.
	/// </summary>
	public sealed record ProcessSnapshot(
		int Id,
		string Name,
		string MainWindowTitle,
		IntPtr MainWindowHandle,
		bool HasMainWindow,
		bool Responding,
		DateTimeOffset? StartedAt,
		string? FilePath);

	/// <summary>
	/// Describes the result of asking an application process to close through its main window.
	/// </summary>
	public sealed record ProcessCloseResult(
		int ProcessId,
		string ProcessName,
		bool CloseMessageSent,
		bool Exited,
		TimeSpan Waited);

	/// <summary>
	/// Provides process discovery, application launch, focus, and graceful close operations.
	/// </summary>
	public static class ProcessControl
	{
		public static IReadOnlyList<ProcessSnapshot> GetProcesses()
		{
			return Process.GetProcesses()
				.Select(TryCreateSnapshot)
				.OfType<ProcessSnapshot>()
				.OrderBy(process => process.Name, StringComparer.OrdinalIgnoreCase)
				.ThenBy(process => process.Id)
				.ToArray();
		}

		public static IReadOnlyList<ProcessSnapshot> FindProcessesByName(
			string name,
			ProcessNameMatchMode matchMode = ProcessNameMatchMode.ExactIgnoreCase)
		{
			if (string.IsNullOrWhiteSpace(name))
			{
				throw new ArgumentException("Process name cannot be empty.", nameof(name));
			}

			var searchName = NormalizeProcessName(name.Trim());

			return GetProcesses()
				.Where(process => ProcessNameMatches(process.Name, searchName, matchMode))
				.ToArray();
		}

		public static ProcessSnapshot GetProcessById(int processId)
		{
			using var process = Process.GetProcessById(processId);
			return CreateSnapshot(process);
		}

		public static ProcessSnapshot GetSingleProcessByName(
			string name,
			ProcessNameMatchMode matchMode = ProcessNameMatchMode.ExactIgnoreCase)
		{
			var matches = FindProcessesByName(name, matchMode);

			if (matches.Count == 0)
			{
				throw new InvalidOperationException($"No process matched name '{name}'.");
			}

			if (matches.Count > 1)
			{
				var matchList = string.Join(", ", matches.Take(8).Select(process => $"{process.Name}({process.Id})"));
				throw new InvalidOperationException($"Process name '{name}' matched multiple processes: {matchList}.");
			}

			return matches[0];
		}

		public static ProcessSnapshot LaunchApp(
			string fileName,
			string? arguments = null,
			string? workingDirectory = null,
			bool useShellExecute = false)
		{
			if (string.IsNullOrWhiteSpace(fileName))
			{
				throw new ArgumentException("File name cannot be empty.", nameof(fileName));
			}

			var startInfo = new ProcessStartInfo
			{
				FileName = fileName,
				UseShellExecute = useShellExecute
			};

			if (!string.IsNullOrWhiteSpace(arguments))
			{
				startInfo.Arguments = arguments;
			}

			if (!string.IsNullOrWhiteSpace(workingDirectory))
			{
				startInfo.WorkingDirectory = workingDirectory;
			}

			var process = Process.Start(startInfo)
				?? throw new InvalidOperationException($"Process.Start returned null for '{fileName}'.");

			using (process)
			{
				return CreateSnapshot(process);
			}
		}

		public static ProcessSnapshot? OpenAppFileOrUrl(string target)
		{
			if (string.IsNullOrWhiteSpace(target))
			{
				throw new ArgumentException("Target cannot be empty.", nameof(target));
			}

			var startInfo = new ProcessStartInfo
			{
				FileName = target,
				UseShellExecute = true
			};

			var process = Process.Start(startInfo);
			if (process is null)
			{
				return null;
			}

			using (process)
			{
				return CreateSnapshot(process);
			}
		}

		public static WindowSnapshot FocusProcess(int processId)
		{
			var window = WindowControl.GetVisibleWindows()
				.FirstOrDefault(candidate => candidate.ProcessId == processId && candidate.Handle != IntPtr.Zero);

			if (window is null)
			{
				throw new InvalidOperationException($"Process {processId} has no visible main window.");
			}

			return WindowControl.FocusWindow(window.Handle);
		}

		public static WindowSnapshot FocusProcessByName(
			string name,
			ProcessNameMatchMode matchMode = ProcessNameMatchMode.ExactIgnoreCase)
		{
			var process = GetSingleProcessByName(name, matchMode);
			return FocusProcess(process.Id);
		}

		public static ProcessCloseResult CloseProcessGracefully(
			int processId,
			TimeSpan? timeout = null)
		{
			var waitTimeout = timeout ?? TimeSpan.FromSeconds(5);
			ValidateTimeout(waitTimeout);
			var startedAt = Stopwatch.GetTimestamp();

			using var process = Process.GetProcessById(processId);
			var snapshot = CreateSnapshot(process);
			var closeMessageSent = process.CloseMainWindow();
			var exited = closeMessageSent && process.WaitForExit(ToWaitMilliseconds(waitTimeout));

			return new ProcessCloseResult(
				processId,
				snapshot.Name,
				closeMessageSent,
				exited || HasExited(process),
				Stopwatch.GetElapsedTime(startedAt));
		}

		public static ProcessCloseResult CloseProcessByNameGracefully(
			string name,
			ProcessNameMatchMode matchMode = ProcessNameMatchMode.ExactIgnoreCase,
			TimeSpan? timeout = null)
		{
			var process = GetSingleProcessByName(name, matchMode);
			return CloseProcessGracefully(process.Id, timeout);
		}

		private static ProcessSnapshot? TryCreateSnapshot(Process process)
		{
			using (process)
			{
				try
				{
					return CreateSnapshot(process);
				}
				catch (InvalidOperationException)
				{
					return null;
				}
				catch (Win32Exception)
				{
					return null;
				}
			}
		}

		private static ProcessSnapshot CreateSnapshot(Process process)
		{
			process.Refresh();
			var mainWindowHandle = SafeGet(() => process.MainWindowHandle, IntPtr.Zero);

			return new ProcessSnapshot(
				process.Id,
				process.ProcessName,
				SafeGet(() => process.MainWindowTitle, string.Empty),
				mainWindowHandle,
				mainWindowHandle != IntPtr.Zero,
				SafeGet(() => process.Responding, false),
				SafeGetStartedAt(process),
				SafeGetFilePath(process));
		}

		private static string NormalizeProcessName(string name)
		{
			return name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
				? Path.GetFileNameWithoutExtension(name)
				: name;
		}

		private static bool ProcessNameMatches(string processName, string searchName, ProcessNameMatchMode matchMode)
		{
			return matchMode switch
			{
				ProcessNameMatchMode.ExactIgnoreCase =>
					string.Equals(processName, searchName, StringComparison.OrdinalIgnoreCase),
				ProcessNameMatchMode.ContainsIgnoreCase =>
					processName.Contains(searchName, StringComparison.OrdinalIgnoreCase),
				_ => throw new ArgumentOutOfRangeException(nameof(matchMode), matchMode, "Unsupported process match mode.")
			};
		}

		private static DateTimeOffset? SafeGetStartedAt(Process process)
		{
			try
			{
				return process.StartTime;
			}
			catch (Exception exception) when (IsExpectedProcessAccessException(exception))
			{
				return null;
			}
		}

		private static string? SafeGetFilePath(Process process)
		{
			try
			{
				return process.MainModule?.FileName;
			}
			catch (Exception exception) when (IsExpectedProcessAccessException(exception))
			{
				return null;
			}
		}

		private static T SafeGet<T>(Func<T> getter, T fallback)
		{
			try
			{
				return getter();
			}
			catch (Exception exception) when (IsExpectedProcessAccessException(exception))
			{
				return fallback;
			}
		}

		private static bool HasExited(Process process)
		{
			try
			{
				return process.HasExited;
			}
			catch (Exception exception) when (IsExpectedProcessAccessException(exception))
			{
				return true;
			}
		}

		private static bool IsExpectedProcessAccessException(Exception exception)
		{
			return exception is InvalidOperationException
				or NotSupportedException
				or Win32Exception
				or UnauthorizedAccessException;
		}

		private static void ValidateTimeout(TimeSpan timeout)
		{
			if (timeout < TimeSpan.Zero)
			{
				throw new ArgumentOutOfRangeException(nameof(timeout), timeout, "Timeout cannot be negative.");
			}
		}

		private static int ToWaitMilliseconds(TimeSpan timeout)
		{
			if (timeout.TotalMilliseconds > int.MaxValue)
			{
				return int.MaxValue;
			}

			return (int)Math.Ceiling(timeout.TotalMilliseconds);
		}
	}
}
