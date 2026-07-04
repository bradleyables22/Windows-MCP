using ModelContextProtocol.Server;
using Server.InteropServices;
using System.ComponentModel;

namespace Server.Tools
{
	public sealed class WaitTools
	{
		[McpServerTool]
		[Description("Polls until a visible top-level window matching a title appears.")]
		public WaitInfo<WindowInfo> WaitForWindow(
			[Description("Window title text to match.")] string title,
			[Description("Maximum wait time in milliseconds.")] int timeoutMilliseconds,
			[Description("Polling interval in milliseconds.")] int pollIntervalMilliseconds = 100,
			[Description("When true, the title must match exactly; otherwise it can contain the provided text.")] bool exactTitle = false)
		{
			return ToolModels.ToInfo(
				WaitControl.WaitForWindow(
					title,
					ToolParsing.ToTimeout(timeoutMilliseconds, nameof(timeoutMilliseconds)),
					ToolParsing.ToPollInterval(pollIntervalMilliseconds, nameof(pollIntervalMilliseconds)),
					ToolParsing.ToWindowTitleMatchMode(exactTitle)),
				ToolModels.ToInfo);
		}

		[McpServerTool]
		[Description("Polls until no visible top-level window matches a title.")]
		public WaitInfo<bool> WaitForWindowToClose(
			[Description("Window title text to match.")] string title,
			[Description("Maximum wait time in milliseconds.")] int timeoutMilliseconds,
			[Description("Polling interval in milliseconds.")] int pollIntervalMilliseconds = 100,
			[Description("When true, the title must match exactly; otherwise it can contain the provided text.")] bool exactTitle = false)
		{
			return ToolModels.ToInfo(
				WaitControl.WaitForWindowToClose(
					title,
					ToolParsing.ToTimeout(timeoutMilliseconds, nameof(timeoutMilliseconds)),
					ToolParsing.ToPollInterval(pollIntervalMilliseconds, nameof(pollIntervalMilliseconds)),
					ToolParsing.ToWindowTitleMatchMode(exactTitle)),
				value => value);
		}

		[McpServerTool]
		[Description("Polls until at least one process matching a name is running.")]
		public WaitInfo<ProcessInfo[]> WaitForProcess(
			[Description("Process name to match, with or without .exe.")] string name,
			[Description("Maximum wait time in milliseconds.")] int timeoutMilliseconds,
			[Description("Polling interval in milliseconds.")] int pollIntervalMilliseconds = 100,
			[Description("When true, the process name can contain the provided text; otherwise it must match exactly.")] bool containsName = false)
		{
			return ToolModels.ToInfo(
				WaitControl.WaitForProcess(
					name,
					ToolParsing.ToTimeout(timeoutMilliseconds, nameof(timeoutMilliseconds)),
					ToolParsing.ToPollInterval(pollIntervalMilliseconds, nameof(pollIntervalMilliseconds)),
					ToolParsing.ToProcessNameMatchMode(containsName)),
				processes => processes.Select(ToolModels.ToInfo).ToArray());
		}

		[McpServerTool]
		[Description("Polls until a process ID exits.")]
		public WaitInfo<bool> WaitForProcessExit(
			[Description("Process ID to wait for.")] int processId,
			[Description("Maximum wait time in milliseconds.")] int timeoutMilliseconds,
			[Description("Polling interval in milliseconds.")] int pollIntervalMilliseconds = 100)
		{
			return ToolModels.ToInfo(
				WaitControl.WaitForProcessExit(
					processId,
					ToolParsing.ToTimeout(timeoutMilliseconds, nameof(timeoutMilliseconds)),
					ToolParsing.ToPollInterval(pollIntervalMilliseconds, nameof(pollIntervalMilliseconds))),
				value => value);
		}

		[McpServerTool]
		[Description("Polls until the clipboard contains text, optionally matching expected text.")]
		public WaitInfo<string> WaitForClipboardText(
			[Description("Maximum wait time in milliseconds.")] int timeoutMilliseconds,
			[Description("Optional text to wait for in the clipboard.")] string? expectedText = null,
			[Description("When true, clipboard text can contain expectedText; otherwise it must equal expectedText.")] bool containsExpectedText = true,
			[Description("Polling interval in milliseconds.")] int pollIntervalMilliseconds = 100)
		{
			return ToolModels.ToInfo(
				WaitControl.WaitForClipboardText(
					ToolParsing.ToTimeout(timeoutMilliseconds, nameof(timeoutMilliseconds)),
					expectedText,
					ToolParsing.ToPollInterval(pollIntervalMilliseconds, nameof(pollIntervalMilliseconds)),
					containsExpectedText),
				value => value);
		}

		[McpServerTool]
		[Description("Polls until screen pixels change in the full virtual screen or an optional region.")]
		public WaitInfo<ScreenshotInfo?> WaitForScreenChange(
			[Description("Maximum wait time in milliseconds.")] int timeoutMilliseconds,
			[Description("Polling interval in milliseconds.")] int pollIntervalMilliseconds = 100,
			[Description("Whether to include the changed screenshot as base64 PNG in the result.")] bool returnScreenshot = false,
			[Description("Whether to include the current mouse cursor in pixel comparison and returned screenshot.")] bool includeCursor = false,
			[Description("Optional region X coordinate. Provide all region values or none.")] int? x = null,
			[Description("Optional region Y coordinate. Provide all region values or none.")] int? y = null,
			[Description("Optional region width. Provide all region values or none.")] int? width = null,
			[Description("Optional region height. Provide all region values or none.")] int? height = null)
		{
			var result = WaitControl.WaitForScreenChange(
				ToolParsing.ToTimeout(timeoutMilliseconds, nameof(timeoutMilliseconds)),
				ToolParsing.ToOptionalRectangle(x, y, width, height),
				ToolParsing.ToPollInterval(pollIntervalMilliseconds, nameof(pollIntervalMilliseconds)),
				includeCursor);

			return new WaitInfo<ScreenshotInfo?>(
				result.Succeeded,
				result.Value is null || !returnScreenshot ? null : ToolModels.ToInfo(result.Value),
				result.Elapsed.TotalMilliseconds,
				result.Attempts,
				result.Message);
		}
	}
}
