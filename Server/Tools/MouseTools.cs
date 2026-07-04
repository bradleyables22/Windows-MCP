using ModelContextProtocol.Server;
using System.ComponentModel;

namespace Server.Tools
{
	public class MouseTools
	{

		[McpServerTool]
		[Description("Moves the mouse to the specified coordinates.")]
		public void MoveMouse() { }
		[McpServerTool]
		[Description("Performs a right-click action.")] 
		public void RightClick() { }
		public void LeftClick() { }


	}
}
