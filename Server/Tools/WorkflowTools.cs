using ModelContextProtocol.Server;
using Server.Workflows;
using System.ComponentModel;

namespace Server.Tools
{
	public sealed record WorkflowStorageInfo(string Path);

	public sealed class WorkflowTools
	{
		private readonly WorkflowStore workflowStore;
		private readonly WorkflowRunner workflowRunner;
		private readonly WorkflowToolDispatcher workflowToolDispatcher;

		public WorkflowTools(
			WorkflowStore workflowStore,
			WorkflowRunner workflowRunner,
			WorkflowToolDispatcher workflowToolDispatcher)
		{
			this.workflowStore = workflowStore;
			this.workflowRunner = workflowRunner;
			this.workflowToolDispatcher = workflowToolDispatcher;
		}

		[McpServerTool]
		[Description("Returns the persistent local folder where named workflow JSON files are stored.")]
		public WorkflowStorageInfo GetWorkflowStorage()
		{
			return new WorkflowStorageInfo(workflowStore.StorageRoot);
		}

		[McpServerTool]
		[Description("Lists tools and parameter schemas available inside workflow JSON steps. Call this before creating or repairing workflow JSON so step tool names and arguments match the MCP surface.")]
		public IReadOnlyList<WorkflowAvailableToolInfo> ListWorkflowTools()
		{
			return workflowToolDispatcher.ListTools();
		}

		[McpServerTool]
		[Description("Lists saved named workflows from persistent local storage.")]
		public IReadOnlyList<WorkflowSummary> ListWorkflows()
		{
			return workflowStore.ListWorkflows();
		}

		[McpServerTool]
		[Description("Reads a saved workflow definition by name, including its parameters and ordered steps.")]
		public WorkflowDefinition GetWorkflow(
			[Description("Saved workflow name.")] string name)
		{
			return workflowStore.GetWorkflow(name);
		}

		[McpServerTool]
		[Description("Creates a named workflow from JSON and fails if that name already exists. Use save_workflow when replacement is intended.")]
		public WorkflowDefinition CreateWorkflow(
			[Description("Workflow name to create.")] string name,
			[Description("Workflow JSON object with description, optional parameters, and ordered steps. Each step uses a tool name from list_workflow_tools and an arguments object.")] string workflowJson)
		{
			return workflowStore.CreateWorkflow(name, workflowJson);
		}

		[McpServerTool]
		[Description("Updates an existing named workflow from JSON and fails if that workflow does not exist. Use create_workflow for new workflows or save_workflow for upsert behavior.")]
		public WorkflowDefinition UpdateWorkflow(
			[Description("Existing workflow name to update.")] string name,
			[Description("Replacement workflow JSON object with description, optional parameters, and ordered steps.")] string workflowJson)
		{
			return workflowStore.UpdateWorkflow(name, workflowJson);
		}

		[McpServerTool]
		[Description("Creates or replaces a named workflow from JSON. Use this for upsert behavior when you intentionally want the provided JSON to become the saved definition.")]
		public WorkflowDefinition SaveWorkflow(
			[Description("Workflow name to create or replace.")] string name,
			[Description("Workflow JSON object with description, optional parameters, and ordered steps. Arguments may use placeholders such as {{name}} resolved from run arguments.")] string workflowJson)
		{
			return workflowStore.SaveWorkflow(name, workflowJson);
		}

		[McpServerTool]
		[Description("Deletes a saved workflow by name. Returns succeeded=false when the workflow did not exist.")]
		public ActionInfo DeleteWorkflow(
			[Description("Saved workflow name to delete.")] string name)
		{
			var deleted = workflowStore.DeleteWorkflow(name);
			return new ActionInfo(deleted, deleted ? $"Workflow '{name}' deleted." : $"Workflow '{name}' did not exist.");
		}

		[McpServerTool]
		[Description("Runs a saved workflow synchronously to completion and returns final status plus per-step results. Desktop automation workflow runs are serialized to avoid overlapping input/focus actions.")]
		public WorkflowRunInfo RunWorkflow(
			[Description("Saved workflow name.")] string name,
			[Description("Optional JSON object containing workflow argument values used to resolve {{placeholder}} values in step arguments.")] string? argumentsJson = null)
		{
			var workflow = workflowStore.GetWorkflow(name);
			return workflowRunner.RunWorkflow(workflow, argumentsJson);
		}

		[McpServerTool]
		[Description("Runs inline workflow JSON synchronously without saving it and returns final status plus per-step results. Use this to test a workflow before saving it.")]
		public WorkflowRunInfo RunWorkflowJson(
			[Description("Workflow JSON object with description, optional parameters, and ordered steps. Each step uses a tool name from list_workflow_tools and an arguments object.")] string workflowJson,
			[Description("Optional JSON object containing workflow argument values used to resolve {{placeholder}} values in step arguments.")] string? argumentsJson = null)
		{
			var workflow = workflowStore.ParseWorkflow(null, workflowJson);
			return workflowRunner.RunWorkflow(workflow, argumentsJson);
		}
	}
}
