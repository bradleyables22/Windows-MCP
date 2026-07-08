using Microsoft.AspNetCore.Hosting.Server;
using ModelContextProtocol.Extensions.Apps;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace Server.Resources
{
    [McpServerResourceType]
    public sealed class HtmlResources
    {
        [McpServerResource(
            UriTemplate = "ui://hello-world.html",
            Name = "hello_world",
            MimeType = McpApps.HtmlMimeType)]
        [Description("A simple Hello World page")]
        public static string HelloWorld(McpServer server) 
        {
            var uiCapability = McpApps.GetUiCapability(server.ClientCapabilities);
            //if (uiCapability is null)
            //{
            //    // Client doesn't support MCP Apps — return plain text
            //    return $"Hello world";
            //}

            return """
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
                """;
        }
            
    } 
}