using ModelContextProtocol.Server;
using Server.InteropServices;
using System.ComponentModel;

namespace Server.Tools
{
	public sealed class MouseTools
	{
		[McpServerTool]
		[Description("Gets the current mouse cursor position in virtual-screen coordinates.")]
		public PointInfo GetCursorPosition()
		{
			return ToolModels.ToInfo(MouseControl.GetPosition());
		}

		[McpServerTool]
		[Description("Moves the mouse cursor to an absolute virtual-screen coordinate.")]
		public PointInfo MoveMouse(
			[Description("Target X coordinate in virtual-screen pixels.")] int x,
			[Description("Target Y coordinate in virtual-screen pixels.")] int y)
		{
			MouseControl.MoveToChecked(x, y);
			return ToolModels.ToInfo(MouseControl.GetPosition());
		}

		[McpServerTool]
		[Description("Clicks a mouse button at the current cursor position.")]
		public PointInfo ClickMouse(
			[Description("Mouse button to click: left or right.")] string button = "left")
		{
			MouseControl.Click(ToolParsing.ParseMouseButton(button));
			return ToolModels.ToInfo(MouseControl.GetPosition());
		}

		[McpServerTool]
		[Description("Double-clicks a mouse button at the current cursor position.")]
		public PointInfo DoubleClickMouse(
			[Description("Mouse button to double-click: left or right.")] string button = "left",
			[Description("Delay between clicks, in milliseconds.")] int delayMilliseconds = 75)
		{
			MouseControl.DoubleClick(ToolParsing.ParseMouseButton(button), delayMilliseconds);
			return ToolModels.ToInfo(MouseControl.GetPosition());
		}

		[McpServerTool]
		[Description("Presses and holds a mouse button at the current cursor position.")]
		public PointInfo MouseDown(
			[Description("Mouse button to press: left or right.")] string button = "left")
		{
			MouseControl.MouseDown(ToolParsing.ParseMouseButton(button));
			return ToolModels.ToInfo(MouseControl.GetPosition());
		}

		[McpServerTool]
		[Description("Releases a mouse button at the current cursor position.")]
		public PointInfo MouseUp(
			[Description("Mouse button to release: left or right.")] string button = "left")
		{
			MouseControl.MouseUp(ToolParsing.ParseMouseButton(button));
			return ToolModels.ToInfo(MouseControl.GetPosition());
		}

		[McpServerTool]
		[Description("Moves the mouse to an absolute coordinate and clicks a mouse button.")]
		public PointInfo ClickMouseAt(
			[Description("Target X coordinate in virtual-screen pixels.")] int x,
			[Description("Target Y coordinate in virtual-screen pixels.")] int y,
			[Description("Mouse button to click: left or right.")] string button = "left")
		{
			MouseControl.ClickAt(ToolParsing.ParseMouseButton(button), x, y);
			return ToolModels.ToInfo(MouseControl.GetPosition());
		}

		[McpServerTool]
		[Description("Drags from the current cursor position to an absolute virtual-screen coordinate.")]
		public PointInfo DragMouseTo(
			[Description("Target X coordinate in virtual-screen pixels.")] int x,
			[Description("Target Y coordinate in virtual-screen pixels.")] int y,
			[Description("Mouse button to hold while dragging: left or right.")] string button = "left",
			[Description("Number of movement steps to interpolate during the drag.")] int steps = 20,
			[Description("Delay between drag movement steps, in milliseconds.")] int stepDelayMilliseconds = 10)
		{
			MouseControl.DragTo(x, y, ToolParsing.ParseMouseButton(button), steps, stepDelayMilliseconds);
			return ToolModels.ToInfo(MouseControl.GetPosition());
		}

		[McpServerTool]
		[Description("Drags from one absolute virtual-screen coordinate to another.")]
		public PointInfo DragMouse(
			[Description("Start X coordinate in virtual-screen pixels.")] int startX,
			[Description("Start Y coordinate in virtual-screen pixels.")] int startY,
			[Description("End X coordinate in virtual-screen pixels.")] int endX,
			[Description("End Y coordinate in virtual-screen pixels.")] int endY,
			[Description("Mouse button to hold while dragging: left or right.")] string button = "left",
			[Description("Number of movement steps to interpolate during the drag.")] int steps = 20,
			[Description("Delay between drag movement steps, in milliseconds.")] int stepDelayMilliseconds = 10)
		{
			MouseControl.DragFromTo(
				startX,
				startY,
				endX,
				endY,
				ToolParsing.ParseMouseButton(button),
				steps,
				stepDelayMilliseconds);

			return ToolModels.ToInfo(MouseControl.GetPosition());
		}

		[McpServerTool]
		[Description("Scrolls the mouse wheel. Positive values scroll up; negative values scroll down.")]
		public PointInfo ScrollMouse(
			[Description("Wheel delta amount. Use 120 for one notch up or -120 for one notch down.")] int amount)
		{
			MouseControl.Scroll(amount);
			return ToolModels.ToInfo(MouseControl.GetPosition());
		}
	}
}
