using ModelContextProtocol.Server;
using System.ComponentModel;

namespace Server.Tools
{
    [McpServerToolType]
    public sealed class HtmlTools
    {
        [McpServerTool]
        [Description("Displays a Hello World page.")]
        [McpMeta("ui", JsonValue = """{"resourceUri": "ui://hello-world"}""")]
        public static string GetHelloWorldHtml() =>
            "Hello World widget displayed."; // text fallback for non-UI hosts
    }
}