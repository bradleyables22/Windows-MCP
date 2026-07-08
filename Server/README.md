# Windows MCP Server

Windows MCP Server is a stdio-based Model Context Protocol server for Windows desktop automation.

It exposes tools for:

- Mouse movement, clicking, dragging, and scrolling
- Keyboard text entry, key presses, and hotkeys
- Screenshots, monitor metadata, and virtual-screen bounds
- Window discovery, focus, movement, resizing, snapping, minimize, maximize, and close
- Clipboard text read, write, and clear
- Process discovery, launch, focus, shell open, and graceful close
- Polling waits for windows, processes, clipboard text, and screen changes
- Named workflows stored as JSON and executed as background jobs

## Transport

The server uses MCP stdio transport. Stdout is reserved for JSON-RPC protocol messages, so application logging is routed to stderr in `Program.cs`.

Prefer running a published executable from MCP clients. This avoids `dotnet run` build output interfering with stdio transport.

Example MCP server configuration:

```json
{
  "servers": {
    "windows-mcp": {
      "type": "stdio",
      "command": "C:\\path\\to\\publish\\Server.exe"
    }
  }
}
```

For local development, you can run the project directly:

```powershell
dotnet run --project Server/Server.csproj --launch-profile stdio
```

## Build

The project targets `net10.0-windows` and publishes self-contained single-file Windows builds for:

- `win-x64`
- `win-arm64`

Publish examples:

```powershell
dotnet publish Server/Server.csproj -c Release -r win-x64
dotnet publish Server/Server.csproj -c Release -r win-arm64
```

## Workflows

Workflows are named JSON command sequences that let a client turn repeated low-level desktop automation calls into one reusable command.

Workflow files are persisted under:

```text
%LOCALAPPDATA%\WindowsMCP\workflows
```

Use `get_workflow_storage` to ask the server for the exact folder at runtime.

### Workflow Tools

The workflow MCP surface supports the full lifecycle:

- `get_workflow_storage`: returns the workflow storage folder
- `list_workflow_tools`: lists tool names that workflow steps can call
- `list_workflows`: lists saved workflow summaries
- `get_workflow`: reads a saved workflow by name
- `create_workflow`: creates a workflow and fails if it already exists
- `update_workflow`: updates a workflow and fails if it does not exist
- `save_workflow`: creates or replaces a workflow
- `delete_workflow`: deletes a workflow by name
- `run_workflow`: queues a saved workflow by name
- `run_workflow_json`: queues inline workflow JSON without saving it
- `list_workflow_jobs`: lists jobs from the current server process
- `get_workflow_job`: gets status and step results for a job ID
- `wait_for_workflow_job`: waits until a job completes or times out

`run_workflow` and `run_workflow_json` return immediately with a `jobId`. The hosted workflow service runs queued jobs in the background and logs completion to stderr. Clients can use the returned job ID with `get_workflow_job` or `wait_for_workflow_job`.

### Workflow JSON

Example workflow JSON:

```json
{
  "name": "notepad_note",
  "description": "Open Notepad and type a note.",
  "parameters": {
    "text": "Text to type into Notepad."
  },
  "steps": [
    {
      "tool": "launch_app",
      "arguments": {
        "fileName": "notepad.exe"
      }
    },
    {
      "tool": "wait_for_window",
      "arguments": {
        "title": "Notepad",
        "timeoutMilliseconds": 5000
      }
    },
    {
      "tool": "type_text",
      "arguments": {
        "text": "{{text}}"
      }
    }
  ]
}
```

Each step has:

- `tool`: a callable tool name
- `arguments`: the JSON arguments passed to that tool

Workflow steps should use the snake_case tool names shown by `list_workflow_tools`, such as `launch_app`, `wait_for_window`, and `type_text`. PascalCase method names such as `LaunchApp` are also accepted for compatibility.

### Arguments And Placeholders

`run_workflow` and `run_workflow_json` accept an optional `argumentsJson` value. It must be a JSON object.

Example arguments:

```json
{
  "text": "Hello from a saved workflow",
  "timeoutMilliseconds": 5000
}
```

Placeholders use `{{name}}` syntax and are resolved from `argumentsJson`.

```json
{
  "tool": "type_text",
  "arguments": {
    "text": "{{text}}"
  }
}
```

Exact placeholders preserve JSON types. For example, if `timeoutMilliseconds` is passed as a number, this becomes a numeric argument instead of a string:

```json
{
  "tool": "wait_for_window",
  "arguments": {
    "title": "Notepad",
    "timeoutMilliseconds": "{{timeoutMilliseconds}}"
  }
}
```

The runner also provides a built-in `{{jobId}}` variable.

### Jobs

Workflow jobs are tracked in memory for the current server process. A job can have one of these statuses:

- `queued`
- `running`
- `succeeded`
- `failed`
- `canceled`

Each completed step records its index, tool name, status, start and completion timestamps, elapsed time, result JSON preview, result JSON length, and any error message.

Desktop automation jobs are executed one at a time. This avoids overlapping mouse, keyboard, focus, and window operations.

### Implementation Notes

The workflow layer is built on top of the existing tool classes rather than a separate automation API. The dispatcher reflects over the existing MCP tool methods and lets workflow steps call those same tools by name.

The workflow runner does not write unsolicited completion messages to stdout, because stdout is reserved for MCP JSON-RPC traffic. Completion is reported through stderr logging and through `get_workflow_job` / `wait_for_workflow_job`.

## Notes

- The server must run in an interactive Windows desktop session for screen, mouse, keyboard, clipboard, and window automation to work.
- Window and process operations that need a single target intentionally fail when a title or name matches multiple candidates.
- Text typing uses clipboard paste for reliability, then restores the prior clipboard text when possible.
- Screenshot tools can return PNG data as base64 or save PNG files to disk.
