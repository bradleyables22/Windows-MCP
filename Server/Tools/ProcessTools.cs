using ModelContextProtocol.Server;
using Server.InteropServices;
using System.ComponentModel;

namespace Server.Tools
{
	public sealed class ProcessTools
	{
		[McpServerTool]
		[Description("Lists running processes with their main-window metadata when available.")]
		public IReadOnlyList<ProcessInfo> ListProcesses()
		{
			return ProcessControl.GetProcesses()
				.Select(ToolModels.ToInfo)
				.ToArray();
		}

		[McpServerTool]
		[Description("Finds running processes by process name.")]
		public IReadOnlyList<ProcessInfo> FindProcesses(
			[Description("Process name to match, with or without .exe.")] string name,
			[Description("When true, the process name can contain the provided text; otherwise it must match exactly.")] bool containsName = false)
		{
			return ProcessControl.FindProcessesByName(name, ToolParsing.ToProcessNameMatchMode(containsName))
				.Select(ToolModels.ToInfo)
				.ToArray();
		}

		[McpServerTool]
		[Description("Launches an executable or command as a process.")]
		public ProcessInfo LaunchApp(
			[Description("Executable or command to launch.")] string fileName,
			[Description("Optional command-line arguments.")] string? arguments = null,
			[Description("Optional working directory.")] string? workingDirectory = null,
			[Description("Use Windows shell execution. Useful for shell verbs but less direct for process control.")] bool useShellExecute = false)
		{
			return ToolModels.ToInfo(ProcessControl.LaunchApp(fileName, arguments, workingDirectory, useShellExecute));
		}

		[McpServerTool]
		[Description("Opens an app, file, folder, or URL using the Windows shell.")]
		public ProcessInfo? OpenAppFileOrUrl(
			[Description("App URI, file path, folder path, or URL to open.")] string target)
		{
			var process = ProcessControl.OpenAppFileOrUrl(target);
			return process is null ? null : ToolModels.ToInfo(process);
		}

		[McpServerTool]
		[Description("Focuses the first visible window that belongs to a process ID.")]
		public WindowInfo FocusProcess(
			[Description("Process ID to focus.")] int processId)
		{
			return ToolModels.ToInfo(ProcessControl.FocusProcess(processId));
		}

		[McpServerTool]
		[Description("Focuses the first visible window that belongs to a uniquely matched process name.")]
		public WindowInfo FocusProcessByName(
			[Description("Process name to match, with or without .exe.")] string name,
			[Description("When true, the process name can contain the provided text; otherwise it must match exactly.")] bool containsName = false)
		{
			return ToolModels.ToInfo(ProcessControl.FocusProcessByName(name, ToolParsing.ToProcessNameMatchMode(containsName)));
		}

		[McpServerTool]
		[Description("Asks a process to close through its main window and waits for it to exit.")]
		public ProcessCloseInfo CloseProcessGracefully(
			[Description("Process ID to close.")] int processId,
			[Description("How long to wait for exit after sending the close message.")] int timeoutMilliseconds = 5000)
		{
			return ToolModels.ToInfo(ProcessControl.CloseProcessGracefully(
				processId,
				ToolParsing.ToTimeout(timeoutMilliseconds, nameof(timeoutMilliseconds))));
		}

		[McpServerTool]
		[Description("Asks a uniquely matched process to close through its main window and waits for it to exit.")]
		public ProcessCloseInfo CloseProcessByNameGracefully(
			[Description("Process name to match, with or without .exe.")] string name,
			[Description("When true, the process name can contain the provided text; otherwise it must match exactly.")] bool containsName = false,
			[Description("How long to wait for exit after sending the close message.")] int timeoutMilliseconds = 5000)
		{
			return ToolModels.ToInfo(ProcessControl.CloseProcessByNameGracefully(
				name,
				ToolParsing.ToProcessNameMatchMode(containsName),
				ToolParsing.ToTimeout(timeoutMilliseconds, nameof(timeoutMilliseconds))));
		}
	}
}
