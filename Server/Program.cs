using Server.Resources;
using Server.Tools;

var builder = WebApplication.CreateBuilder(args);

// Add the MCP services: the transport to use (http) and the tools to register.
builder.Services
	.AddMcpServer()
	.WithHttpTransport(options =>
	{
		// Stateless mode is recommended for servers that don't need
		// server-to-client requests like sampling or elicitation.
		// See https://csharp.sdk.modelcontextprotocol.io/concepts/transports/transports.html for details.
		options.Stateless = true;
	})
	.WithTools<MouseTools>()
	.WithTools<KeyboardTools>()
	.WithTools<ScreenTools>()
	.WithTools<WindowTools>()
	.WithTools<ClipboardTools>()
	.WithTools<ProcessTools>()
	.WithTools<WaitTools>()
	.WithTools<HtmlTools>()
	.WithResources<HtmlResources>();

var app = builder.Build();
app.MapMcp("mcp");
app.Run();
