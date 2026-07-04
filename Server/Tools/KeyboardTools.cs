using ModelContextProtocol.Server;
using Server.InteropServices;
using System.ComponentModel;

namespace Server.Tools
{
	public sealed class KeyboardTools
	{
		[McpServerTool]
		[Description("Types Unicode text into the currently focused control.")]
		public ActionInfo TypeText(
			[Description("Text to type exactly as provided.")] string text)
		{
			KeyboardControl.TypeText(text);
			return new ActionInfo(true, "Text typed.");
		}

		[McpServerTool]
		[Description("Presses and releases a single virtual key.")]
		public ActionInfo PressKey(
			[Description("Key name, such as Enter, Escape, A, F5, Ctrl, Alt, Delete, or LeftWindows.")] string key)
		{
			KeyboardControl.Press(ToolParsing.ParseVirtualKey(key));
			return new ActionInfo(true, $"Pressed {key}.");
		}

		[McpServerTool]
		[Description("Presses a hotkey combination, holding keys in order and releasing them in reverse order.")]
		public ActionInfo PressHotkey(
			[Description("Keys to press together, such as ['Ctrl','S'] or ['Alt','F4'].")] string[] keys)
		{
			if (keys.Length == 0)
			{
				throw new ArgumentException("At least one key is required.", nameof(keys));
			}

			KeyboardControl.Hotkey(keys.Select(ToolParsing.ParseVirtualKey).ToArray());
			return new ActionInfo(true, $"Pressed hotkey {string.Join("+", keys)}.");
		}
	}
}
