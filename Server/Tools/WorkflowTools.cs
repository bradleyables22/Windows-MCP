using ModelContextProtocol.Server;
using Server.Workflows;
using System.ComponentModel;

namespace Server.Tools
{
	public sealed record WorkflowStorageInfo(string Path);

	public sealed class WorkflowTools
	{
		private readonly WorkflowStore workflowStore;
		private readonly WorkflowJobService workflowJobService;
		private readonly WorkflowToolDispatcher workflowToolDispatcher;

		public WorkflowTools(
			WorkflowStore workflowStore,
			WorkflowJobService workflowJobService,
			WorkflowToolDispatcher workflowToolDispatcher)
		{
			this.workflowStore = workflowStore;
			this.workflowJobService = workflowJobService;
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
		[Description("Queues a saved workflow to run in the background and returns its job ID.")]
		public WorkflowJobInfo RunWorkflow(
			[Description("Saved workflow name.")] string name,
			[Description("Optional JSON object containing workflow argument values.")] string? argumentsJson = null)
		{
			var workflow = workflowStore.GetWorkflow(name);
			return workflowJobService.EnqueueWorkflow(workflow, argumentsJson);
		}

		[McpServerTool]
		[Description("Queues inline workflow JSON to run in the background and returns its job ID.")]
		public WorkflowJobInfo RunWorkflowJson(
			[Description("Workflow JSON with description, optional parameters, and steps.")] string workflowJson,
			[Description("Optional JSON object containing workflow argument values.")] string? argumentsJson = null)
		{
			var workflow = workflowStore.ParseWorkflow(null, workflowJson);
			return workflowJobService.EnqueueWorkflow(workflow, argumentsJson);
		}

		[McpServerTool]
		[Description("Lists workflow jobs from this server process.")]
		public IReadOnlyList<WorkflowJobInfo> ListWorkflowJobs()
		{
			return workflowJobService.ListJobs();
		}

		[McpServerTool]
		[Description("Gets workflow job status and completed step results by job ID.")]
		public WorkflowJobInfo GetWorkflowJob(
			[Description("Workflow job ID returned by RunWorkflow or RunWorkflowJson.")] string jobId)
		{
			return workflowJobService.GetJob(jobId);
		}

		[McpServerTool]
		[Description("Polls until a workflow job completes or the timeout elapses.")]
		public WaitInfo<WorkflowJobInfo> WaitForWorkflowJob(
			[Description("Workflow job ID returned by RunWorkflow or RunWorkflowJson.")] string jobId,
			[Description("Maximum wait time in milliseconds.")] int timeoutMilliseconds,
			[Description("Polling interval in milliseconds.")] int pollIntervalMilliseconds = 100)
		{
			var wait = workflowJobService.WaitForCompletion(
				jobId,
				ToolParsing.ToTimeout(timeoutMilliseconds, nameof(timeoutMilliseconds)),
				ToolParsing.ToPollInterval(pollIntervalMilliseconds, nameof(pollIntervalMilliseconds)));

			return new WaitInfo<WorkflowJobInfo>(
				wait.Succeeded,
				wait.Job,
				wait.Elapsed.TotalMilliseconds,
				wait.Attempts,
				wait.Message);
		}
	}
}
