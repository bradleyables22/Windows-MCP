using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Server.Tools;
using Server.Workflows;

var builder = Host.CreateApplicationBuilder(args);

// stdout is reserved for the JSON-RPC protocol on the stdio transport,
// so all logging must be routed to stderr to avoid corrupting messages.
builder.Logging.AddConsole(options =>
{
	options.LogToStandardErrorThreshold = LogLevel.Trace;
});

builder.Services.AddSingleton<WorkflowStore>();
builder.Services.AddSingleton<WorkflowToolDispatcher>();
builder.Services.AddSingleton<WorkflowRunner>();

// Add the MCP services: the transport to use (stdio) and the tools to register.
builder.Services
	.AddMcpServer()
	.WithStdioServerTransport()
	.WithTools<MouseTools>()
	.WithTools<KeyboardTools>()
	.WithTools<ScreenTools>()
	.WithTools<WindowTools>()
	.WithTools<ClipboardTools>()
	.WithTools<ProcessTools>()
	.WithTools<WaitTools>()
	.WithTools<WorkflowTools>()
	;

await builder.Build().RunAsync();
