using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace Server.Resources
{
    [McpServerResourceType]
    public sealed class HtmlResources
    {
        [McpServerResource(
            UriTemplate = "ui://hello-world",
            Name = "hello_world",
            MimeType = "text/html;profile=mcp-app")]
        [Description("A simple Hello World page")]
        public static ResourceContents HelloWorld() =>
            new TextResourceContents
            {
                Uri = "ui://hello-world",
                MimeType = "text/html;profile=mcp-app",
                Text = """
                <!DOCTYPE html>
                <html lang="en">
                <head>
                  <meta charset="UTF-8" />
                  <meta name="viewport" content="width=device-width, initial-scale=1.0" />
                  <title>Hello World</title>
                  <style>
                    html, body { margin: 0; background: transparent; }
                  </style>
                </head>
                <body>
                  <div style="background-color: blue; color: white; padding: 16px;">
                    Hello World
                  </div>
                </body>
                </html>
                """
            };
} 
}