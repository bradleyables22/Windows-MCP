using ModelContextProtocol.Extensions.Apps;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;

namespace Server.Tools
{
    [McpServerToolType]
    public sealed class HtmlTools
    {
        [McpServerTool]
        [Description("Displays a Hello World page.")]
        [McpAppUi(ResourceUri = "ui://hello-world.html", Visibility = [McpUiToolVisibility.Model, McpUiToolVisibility.App])]
        public static string GetHelloWorldHtml() =>
            "Hello World widget displayed."; // text fallback for non-UI hosts
    }
}