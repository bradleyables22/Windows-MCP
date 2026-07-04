using ModelContextProtocol.Server;
using Server.InteropServices;
using System.ComponentModel;

namespace Server.Tools
{
	public sealed class ClipboardTools
	{
		[McpServerTool]
		[Description("Checks whether the Windows clipboard currently contains Unicode text.")]
		public ClipboardTextInfo GetClipboardText()
		{
			return new ClipboardTextInfo(
				ClipboardControl.ContainsText(),
				ClipboardControl.GetText());
		}

		[McpServerTool]
		[Description("Sets the Windows clipboard to Unicode text.")]
		public ClipboardTextInfo SetClipboardText(
			[Description("Text to place on the clipboard.")] string text)
		{
			ClipboardControl.SetText(text);
			return new ClipboardTextInfo(true, ClipboardControl.GetText());
		}

		[McpServerTool]
		[Description("Clears the Windows clipboard.")]
		public ActionInfo ClearClipboard()
		{
			ClipboardControl.Clear();
			return new ActionInfo(true, "Clipboard cleared.");
		}
	}
}
