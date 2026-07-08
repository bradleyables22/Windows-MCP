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
		[Description("Gets the persistent local folder where workflow JSON files are stored.")]
		public WorkflowStorageInfo GetWorkflowStorage()
		{
			return new WorkflowStorageInfo(workflowStore.StorageRoot);
		}

		[McpServerTool]
		[Description("Lists tool methods that can be used as steps inside workflow JSON.")]
		public IReadOnlyList<WorkflowAvailableToolInfo> ListWorkflowTools()
		{
			return workflowToolDispatcher.ListTools();
		}

		[McpServerTool]
		[Description("Lists saved workflows from persistent local storage.")]
		public IReadOnlyList<WorkflowSummary> ListWorkflows()
		{
			return workflowStore.ListWorkflows();
		}

		[McpServerTool]
		[Description("Reads a saved workflow by name.")]
		public WorkflowDefinition GetWorkflow(
			[Description("Workflow name.")] string name)
		{
			return workflowStore.GetWorkflow(name);
		}

		[McpServerTool]
		[Description("Creates a named workflow from JSON. Fails if the workflow already exists.")]
		public WorkflowDefinition CreateWorkflow(
			[Description("Workflow name.")] string name,
			[Description("Workflow JSON with description, optional parameters, and steps.")] string workflowJson)
		{
			return workflowStore.CreateWorkflow(name, workflowJson);
		}

		[McpServerTool]
		[Description("Updates an existing named workflow from JSON. Fails if the workflow does not exist.")]
		public WorkflowDefinition UpdateWorkflow(
			[Description("Workflow name.")] string name,
			[Description("Workflow JSON with description, optional parameters, and steps.")] string workflowJson)
		{
			return workflowStore.UpdateWorkflow(name, workflowJson);
		}

		[McpServerTool]
		[Description("Creates or replaces a named workflow from JSON.")]
		public WorkflowDefinition SaveWorkflow(
			[Description("Workflow name.")] string name,
			[Description("Workflow JSON with description, optional parameters, and steps.")] string workflowJson)
		{
			return workflowStore.SaveWorkflow(name, workflowJson);
		}

		[McpServerTool]
		[Description("Deletes a saved workflow by name.")]
		public ActionInfo DeleteWorkflow(
			[Description("Workflow name.")] string name)
		{
			var deleted = workflowStore.DeleteWorkflow(name);
			return new ActionInfo(deleted, deleted ? $"Workflow '{name}' deleted." : $"Workflow '{name}' did not exist.");
		}

		[McpServerTool]
		[Description("Runs a saved workflow synchronously and returns the completed step results.")]
		public WorkflowRunInfo RunWorkflow(
			[Description("Saved workflow name.")] string name,
			[Description("Optional JSON object containing workflow argument values.")] string? argumentsJson = null)
		{
			var workflow = workflowStore.GetWorkflow(name);
			return workflowRunner.RunWorkflow(workflow, argumentsJson);
		}

		[McpServerTool]
		[Description("Runs inline workflow JSON synchronously and returns the completed step results.")]
		public WorkflowRunInfo RunWorkflowJson(
			[Description("Workflow JSON with description, optional parameters, and steps.")] string workflowJson,
			[Description("Optional JSON object containing workflow argument values.")] string? argumentsJson = null)
		{
			var workflow = workflowStore.ParseWorkflow(null, workflowJson);
			return workflowRunner.RunWorkflow(workflow, argumentsJson);
		}
	}
}
