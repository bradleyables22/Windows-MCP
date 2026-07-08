using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace Server.Tools
{
	public sealed record CommandResult(
		string FileName,
		string? Arguments,
		string? WorkingDirectory,
		int? ExitCode,
		bool TimedOut,
		double ElapsedMilliseconds,
		string Stdout,
		string Stderr);

	public sealed class CommandTools
	{
		[McpServerTool]
		[Description("Runs an executable or command and captures stdout, stderr, exit code, elapsed time, and timeout state.")]
		public CommandResult RunCommand(
			[Description("Executable or command to run.")] string fileName,
			[Description("Optional command-line arguments.")] string? arguments = null,
			[Description("Optional working directory.")] string? workingDirectory = null,
			[Description("Maximum run time in milliseconds.")] int timeoutMilliseconds = 60000,
			[Description("Optional environment variables to add or override.")] Dictionary<string, string>? environmentVariables = null)
		{
			if (string.IsNullOrWhiteSpace(fileName))
			{
				throw new ArgumentException("File name cannot be empty.", nameof(fileName));
			}

			var startInfo = CreateStartInfo(fileName.Trim(), workingDirectory, environmentVariables);
			if (!string.IsNullOrWhiteSpace(arguments))
			{
				startInfo.Arguments = arguments;
			}

			return Execute(startInfo, timeoutMilliseconds);
		}

		[McpServerTool]
		[Description("Runs a command line through cmd.exe /d /s /c and captures stdout, stderr, exit code, elapsed time, and timeout state.")]
		public CommandResult RunShellCommand(
			[Description("Command line to pass to cmd.exe.")] string commandLine,
			[Description("Optional working directory.")] string? workingDirectory = null,
			[Description("Maximum run time in milliseconds.")] int timeoutMilliseconds = 60000,
			[Description("Optional environment variables to add or override.")] Dictionary<string, string>? environmentVariables = null)
		{
			if (string.IsNullOrWhiteSpace(commandLine))
			{
				throw new ArgumentException("Command line cannot be empty.", nameof(commandLine));
			}

			var startInfo = CreateStartInfo("cmd.exe", workingDirectory, environmentVariables);
			startInfo.ArgumentList.Add("/d");
			startInfo.ArgumentList.Add("/s");
			startInfo.ArgumentList.Add("/c");
			startInfo.ArgumentList.Add(commandLine);

			return Execute(startInfo, timeoutMilliseconds);
		}

		[McpServerTool]
		[Description("Runs a PowerShell script or command and captures stdout, stderr, exit code, elapsed time, and timeout state.")]
		public CommandResult RunPowerShell(
			[Description("PowerShell script or command text.")] string script,
			[Description("Optional working directory.")] string? workingDirectory = null,
			[Description("Maximum run time in milliseconds.")] int timeoutMilliseconds = 60000,
			[Description("Use pwsh.exe instead of Windows PowerShell when true.")] bool usePwsh = false,
			[Description("Optional environment variables to add or override.")] Dictionary<string, string>? environmentVariables = null)
		{
			if (string.IsNullOrWhiteSpace(script))
			{
				throw new ArgumentException("PowerShell script cannot be empty.", nameof(script));
			}

			var startInfo = CreateStartInfo(usePwsh ? "pwsh.exe" : "powershell.exe", workingDirectory, environmentVariables);
			startInfo.ArgumentList.Add("-NoLogo");
			startInfo.ArgumentList.Add("-NonInteractive");
			startInfo.ArgumentList.Add("-NoProfile");
			startInfo.ArgumentList.Add("-ExecutionPolicy");
			startInfo.ArgumentList.Add("Bypass");
			startInfo.ArgumentList.Add("-Command");
			startInfo.ArgumentList.Add(script);

			return Execute(startInfo, timeoutMilliseconds);
		}

		private static ProcessStartInfo CreateStartInfo(
			string fileName,
			string? workingDirectory,
			Dictionary<string, string>? environmentVariables)
		{
			var startInfo = new ProcessStartInfo
			{
				FileName = fileName,
				UseShellExecute = false,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				StandardOutputEncoding = Encoding.UTF8,
				StandardErrorEncoding = Encoding.UTF8,
				CreateNoWindow = true
			};

			if (!string.IsNullOrWhiteSpace(workingDirectory))
			{
				startInfo.WorkingDirectory = Path.GetFullPath(workingDirectory);
			}

			if (environmentVariables is not null)
			{
				foreach (var (key, value) in environmentVariables)
				{
					if (string.IsNullOrWhiteSpace(key))
					{
						throw new ArgumentException("Environment variable names cannot be empty.", nameof(environmentVariables));
					}

					startInfo.Environment[key] = value;
				}
			}

			return startInfo;
		}

		private static CommandResult Execute(
			ProcessStartInfo startInfo,
			int timeoutMilliseconds)
		{
			if (timeoutMilliseconds < 0)
			{
				throw new ArgumentOutOfRangeException(nameof(timeoutMilliseconds), timeoutMilliseconds, "Timeout cannot be negative.");
			}

			using var process = new Process
			{
				StartInfo = startInfo
			};

			var startedAt = Stopwatch.GetTimestamp();

			if (!process.Start())
			{
				throw new InvalidOperationException($"Process.Start returned false for '{startInfo.FileName}'.");
			}

			var stdoutTask = process.StandardOutput.ReadToEndAsync();
			var stderrTask = process.StandardError.ReadToEndAsync();
			var waitTimeout = timeoutMilliseconds == 0 ? Timeout.Infinite : timeoutMilliseconds;
			var exited = process.WaitForExit(waitTimeout);
			var timedOut = false;

			if (!exited)
			{
				timedOut = true;
				process.Kill(entireProcessTree: true);
			}

			process.WaitForExit();
			Task.WaitAll(stdoutTask, stderrTask);

			return new CommandResult(
				startInfo.FileName,
				startInfo.Arguments,
				string.IsNullOrWhiteSpace(startInfo.WorkingDirectory) ? null : startInfo.WorkingDirectory,
				timedOut ? null : process.ExitCode,
				timedOut,
				Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds,
				stdoutTask.Result,
				stderrTask.Result);
		}
	}
}
