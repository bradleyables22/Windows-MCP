using ModelContextProtocol.Server;
using Server.InteropServices;
using System.ComponentModel;

namespace Server.Tools
{
	public sealed class ScreenTools
	{
		[McpServerTool]
		[Description("Gets the full virtual-screen bounds across all monitors.")]
		public RectangleInfo GetScreenBounds()
		{
			return ToolModels.ToInfo(ScreenControl.GetVirtualScreenBounds());
		}

		[McpServerTool]
		[Description("Lists connected monitors, their full bounds, and their working areas.")]
		public IReadOnlyList<MonitorInfo> GetMonitors()
		{
			return ScreenControl.GetMonitors()
				.Select(ToolModels.ToInfo)
				.ToArray();
		}

		[McpServerTool]
		[Description("Captures a screenshot as an in-memory PNG encoded as base64.")]
		public ScreenshotInfo TakeScreenshot(
			[Description("Whether to include the current mouse cursor in the screenshot.")] bool includeCursor = true,
			[Description("Optional region X coordinate. Provide all region values or none.")] int? x = null,
			[Description("Optional region Y coordinate. Provide all region values or none.")] int? y = null,
			[Description("Optional region width. Provide all region values or none.")] int? width = null,
			[Description("Optional region height. Provide all region values or none.")] int? height = null)
		{
			var region = ToolParsing.ToOptionalRectangle(x, y, width, height);
			var capture = region.HasValue
				? ScreenControl.CaptureRectangle(region.Value, includeCursor)
				: ScreenControl.CaptureVirtualScreen(includeCursor);

			return ToolModels.ToInfo(capture);
		}

		[McpServerTool]
		[Description("Captures the foreground window as an in-memory PNG encoded as base64.")]
		public ScreenshotInfo TakeForegroundWindowScreenshot(
			[Description("Whether to include the current mouse cursor in the screenshot.")] bool includeCursor = true)
		{
			return ToolModels.ToInfo(ScreenControl.CaptureForegroundWindow(includeCursor));
		}

		[McpServerTool]
		[Description("Captures a screenshot and saves it as a PNG file on disk.")]
		public ActionInfo SaveScreenshot(
			[Description("Absolute or relative path where the PNG file should be written.")] string path,
			[Description("Whether to include the current mouse cursor in the screenshot.")] bool includeCursor = true,
			[Description("Optional region X coordinate. Provide all region values or none.")] int? x = null,
			[Description("Optional region Y coordinate. Provide all region values or none.")] int? y = null,
			[Description("Optional region width. Provide all region values or none.")] int? width = null,
			[Description("Optional region height. Provide all region values or none.")] int? height = null)
		{
			ScreenControl.SavePng(path, ToolParsing.ToOptionalRectangle(x, y, width, height), includeCursor);
			return new ActionInfo(true, $"Screenshot saved to {Path.GetFullPath(path)}.");
		}
	}
}
