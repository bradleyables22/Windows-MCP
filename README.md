# Windows MCP Server

Windows MCP Server is a stdio-based Model Context Protocol server for Windows desktop automation.

It exposes tools for:

- Mouse movement, clicking, dragging, and scrolling
- Keyboard text entry, key presses, and hotkeys
- Screenshots, monitor metadata, and virtual-screen bounds
- Leased background screen recording to H.264 MP4 files through Windows Media Foundation
- Window discovery, focus, movement, resizing, snapping, minimize, maximize, and close
- Clipboard text read, write, and clear
- Process discovery, launch, focus, shell open, and graceful close
- Polling waits for windows, processes, clipboard text, and screen changes
- Named workflows stored as JSON and executed as synchronous runs
- Local system helpers for commands, files, registry, environment variables, and Windows services

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

## AI Client Guidance

The MCP tool descriptions are written so an AI client can choose tools directly from the schema. A few usage patterns are worth keeping explicit:

- Prefer `list_workflow_tools` before creating or repairing workflow JSON. Use the returned snake_case tool names and parameter schemas for workflow steps.
- Use `run_workflow_json` to test an inline workflow before saving it, then use `save_workflow` when the JSON is ready to persist.
- For screen recording, always keep the `recordingId` returned by `start_screen_recording`. Poll with `get_screen_recording_status`, renew active recordings with `renew_screen_recording` when needed, and call `stop_screen_recording` to finalize early. If a recording auto-stops first, `stop_screen_recording` returns the saved final status.
- Prefer `recycle_file`, `recycle_directory`, or `empty_directory` with `recycle=true` for recoverable cleanup. Use permanent delete tools only when that is the intended outcome.
- Use `run_command` for a specific executable, `run_shell_command` for `cmd.exe` syntax such as pipes or redirection, and `run_power_shell` for PowerShell cmdlets/scripts.

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
- `run_workflow`: runs a saved workflow by name and returns completed step results
- `run_workflow_json`: runs inline workflow JSON without saving it

`run_workflow` and `run_workflow_json` are synchronous. Each call runs the workflow to completion and returns a run result containing the final status and per-step outputs.

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

The runner also provides a built-in `{{runId}}` variable. `{{jobId}}` is accepted as a compatibility alias.

### Run Results

A workflow run can have one of these statuses:

- `succeeded`
- `failed`

Each completed step records its index, tool name, status, start and completion timestamps, elapsed time, result JSON preview, result JSON length, and any error message. If a step fails, the run stops and returns a `failed` result with the failed step details.

Desktop workflow runs are executed one at a time. This avoids overlapping mouse, keyboard, focus, and window operations.

### Implementation Notes

The workflow layer is built on top of the existing tool classes rather than a separate automation API. The dispatcher reflects over the existing MCP tool methods and lets workflow steps call those same tools by name.

The workflow runner does not write unsolicited completion messages to stdout, because stdout is reserved for MCP JSON-RPC traffic. Completion is returned directly from `run_workflow` or `run_workflow_json`, and high-level run status is also logged to stderr.

## Screen Recording

Screen recordings are background jobs with built-in safety limits. They write H.264 MP4 files through Windows Media Foundation under:

```text
%LOCALAPPDATA%\WindowsMCP\recordings
```

Use `get_screen_recording_storage` to ask the server for the exact folder at runtime.

Recording tools:

- `get_screen_recording_storage`: returns the recording storage folder
- `start_screen_recording`: starts a leased recording and returns a `recordingId`
- `renew_screen_recording`: extends an active recording lease, bounded by the hard maximum duration
- `stop_screen_recording`: stops a recording and finalizes the MP4 file
- `get_screen_recording_status`: reads current or final recording status
- `list_screen_recordings`: lists known recording states, newest first
- `delete_screen_recording`: deletes a stopped recording state, optionally deleting output files

`start_screen_recording` defaults to 5 FPS, a 5-minute maximum duration, a 512 MiB output limit, cursor capture enabled, a 4 Mbps H.264 video bitrate, and a self-contained `.mp4` output file. Region, FPS, duration, lease, size limit, output path, and video bitrate are configurable.

Recordings are written to `*.partial.mp4` while active and moved to the final `.mp4` path only after they are stopped cleanly. If the server starts and finds an active recording state from a previous process, it marks it as `interrupted` and does not resume recording.

The recorder also attempts to stop active recordings during server shutdown, Windows session ending, and system suspend events. The hard duration, lease expiration, and max-size limit are the primary safety net for abandoned AI calls.

## System Helpers

The server also exposes local system access tools. These tools return structured results instead of printing to stdout.

### Commands

- `run_command`: runs an executable with optional arguments, working directory, timeout, and environment overrides
- `run_shell_command`: runs a command line through `cmd.exe /d /s /c`
- `run_power_shell`: runs Windows PowerShell, or `pwsh.exe` when requested

Command results include:

- executable name
- arguments
- working directory
- exit code
- timeout state
- elapsed milliseconds
- stdout
- stderr

### Files And Directories

- `read_text_file`: reads text with a configurable encoding and maximum byte count
- `write_text_file`: writes or appends text and can create parent directories
- `read_file_base64`: reads binary data as base64
- `write_file_base64`: writes or appends base64 data
- `list_directory`: lists files and directories with optional pattern, recursion, hidden entries, and max-entry limit
- `get_file_system_info`: returns file or directory metadata
- `create_directory`: creates a directory and any missing parent directories
- `delete_file`: permanently deletes a file
- `recycle_file`: sends a file to the Windows Recycle Bin
- `delete_directory`: permanently deletes a directory, optionally recursively
- `recycle_directory`: sends a directory to the Windows Recycle Bin
- `move_file`: moves or renames a file
- `move_directory`: moves or renames a directory
- `copy_file`: copies a file
- `copy_directory`: copies a directory and all of its contents
- `empty_directory`: removes everything inside a directory while keeping the directory itself
- `paste_file_system_item`: copies or moves a file or directory into another directory, preserving the item name unless a new name is provided

### Registry

- `get_registry_value`: reads a registry value
- `set_registry_value`: creates or updates a registry value
- `list_registry_values`: lists values under a key
- `list_registry_sub_keys`: lists subkeys under a key

Registry hives accept `HKCU`, `HKLM`, `HKCR`, `HKU`, and `HKCC`, plus full names such as `HKEY_CURRENT_USER`. Registry views accept `default`, `registry64`, and `registry32`.

Supported value kinds are `String`, `ExpandString`, `DWord`, `QWord`, `Binary`, and `MultiString`. Binary values use base64. Multi-string values use a JSON string array.

### Environment Variables

- `get_environment_variable`: reads a process, user, or machine environment variable
- `set_environment_variable`: sets or clears a process, user, or machine environment variable
- `list_environment_variables`: lists variables for a process, user, or machine target

Targets are `Process`, `User`, and `Machine`.

### Windows Services

- `list_windows_services`: lists services, optionally filtered by name/display name
- `get_windows_service`: gets service status and metadata
- `start_windows_service`: starts a service and optionally waits for `Running`
- `stop_windows_service`: stops a service and optionally waits for `Stopped`
- `restart_windows_service`: stops and starts a service

Service start/stop operations may require an elevated process depending on the service.

## Notes

- The server must run in an interactive Windows desktop session for screen, mouse, keyboard, clipboard, and window automation to work.
- Window and process operations that need a single target intentionally fail when a title or name matches multiple candidates.
- Text typing uses clipboard paste for reliability, then restores the prior clipboard text when possible.
- Screenshot tools can return PNG data as base64 or save PNG files to disk.
