using ModelContextProtocol.Server;
using Server.InteropServices;
using System.ComponentModel;

namespace Server.Tools
{
	public sealed class WindowTools
	{
		[McpServerTool]
		[Description("Lists all visible top-level windows.")]
		public IReadOnlyList<WindowInfo> ListWindows()
		{
			return WindowControl.GetVisibleWindows()
				.Select(ToolModels.ToInfo)
				.ToArray();
		}

		[McpServerTool]
		[Description("Finds visible top-level windows by title.")]
		public IReadOnlyList<WindowInfo> FindWindows(
			[Description("Window title text to match.")] string title,
			[Description("When true, the title must match exactly; otherwise it can contain the provided text.")] bool exactTitle = false)
		{
			return WindowControl.FindWindowsByTitle(title, ToolParsing.ToWindowTitleMatchMode(exactTitle))
				.Select(ToolModels.ToInfo)
				.ToArray();
		}

		[McpServerTool]
		[Description("Focuses a visible top-level window by title.")]
		public WindowInfo FocusWindow(
			[Description("Window title text to match.")] string title,
			[Description("When true, the title must match exactly; otherwise it can contain the provided text.")] bool exactTitle = false)
		{
			return ToolModels.ToInfo(WindowControl.FocusWindow(title, ToolParsing.ToWindowTitleMatchMode(exactTitle)));
		}

		[McpServerTool]
		[Description("Moves a visible top-level window to an absolute virtual-screen coordinate.")]
		public WindowInfo MoveWindow(
			[Description("Window title text to match.")] string title,
			[Description("Target X coordinate in virtual-screen pixels.")] int x,
			[Description("Target Y coordinate in virtual-screen pixels.")] int y,
			[Description("When true, the title must match exactly; otherwise it can contain the provided text.")] bool exactTitle = false)
		{
			return ToolModels.ToInfo(WindowControl.MoveWindow(title, x, y, ToolParsing.ToWindowTitleMatchMode(exactTitle)));
		}

		[McpServerTool]
		[Description("Resizes a visible top-level window while keeping its current top-left coordinate.")]
		public WindowInfo ResizeWindow(
			[Description("Window title text to match.")] string title,
			[Description("Target window width in pixels.")] int width,
			[Description("Target window height in pixels.")] int height,
			[Description("When true, the title must match exactly; otherwise it can contain the provided text.")] bool exactTitle = false)
		{
			return ToolModels.ToInfo(WindowControl.ResizeWindow(title, width, height, ToolParsing.ToWindowTitleMatchMode(exactTitle)));
		}

		[McpServerTool]
		[Description("Minimizes a visible top-level window by title.")]
		public WindowInfo MinimizeWindow(
			[Description("Window title text to match.")] string title,
			[Description("When true, the title must match exactly; otherwise it can contain the provided text.")] bool exactTitle = false)
		{
			return ToolModels.ToInfo(WindowControl.MinimizeWindow(title, ToolParsing.ToWindowTitleMatchMode(exactTitle)));
		}

		[McpServerTool]
		[Description("Maximizes a visible top-level window by title.")]
		public WindowInfo MaximizeWindow(
			[Description("Window title text to match.")] string title,
			[Description("When true, the title must match exactly; otherwise it can contain the provided text.")] bool exactTitle = false)
		{
			return ToolModels.ToInfo(WindowControl.MaximizeWindow(title, ToolParsing.ToWindowTitleMatchMode(exactTitle)));
		}

		[McpServerTool]
		[Description("Sends a graceful close request to a visible top-level window by title.")]
		public ActionInfo CloseWindow(
			[Description("Window title text to match.")] string title,
			[Description("When true, the title must match exactly; otherwise it can contain the provided text.")] bool exactTitle = false)
		{
			WindowControl.CloseWindow(title, ToolParsing.ToWindowTitleMatchMode(exactTitle));
			return new ActionInfo(true, $"Close requested for window '{title}'.");
		}

		[McpServerTool]
		[Description("Moves and resizes a visible top-level window to the left half of its current monitor working area.")]
		public WindowInfo SnapWindowLeft(
			[Description("Window title text to match.")] string title,
			[Description("When true, the title must match exactly; otherwise it can contain the provided text.")] bool exactTitle = false)
		{
			return ToolModels.ToInfo(WindowControl.SnapWindowLeft(title, ToolParsing.ToWindowTitleMatchMode(exactTitle)));
		}

		[McpServerTool]
		[Description("Moves and resizes a visible top-level window to the right half of its current monitor working area.")]
		public WindowInfo SnapWindowRight(
			[Description("Window title text to match.")] string title,
			[Description("When true, the title must match exactly; otherwise it can contain the provided text.")] bool exactTitle = false)
		{
			return ToolModels.ToInfo(WindowControl.SnapWindowRight(title, ToolParsing.ToWindowTitleMatchMode(exactTitle)));
		}
	}
}
