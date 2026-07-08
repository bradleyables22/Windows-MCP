using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text.Json;

namespace Server.Workflows
{
	public sealed record WorkflowStepResultInfo(
		int Index,
		string Tool,
		string Status,
		DateTimeOffset StartedAt,
		DateTimeOffset? CompletedAt,
		double? ElapsedMilliseconds,
		string? ResultJsonPreview,
		int? ResultJsonLength,
		string? Error);

	public sealed record WorkflowRunInfo(
		string RunId,
		string WorkflowName,
		string Status,
		DateTimeOffset StartedAt,
		DateTimeOffset CompletedAt,
		double ElapsedMilliseconds,
		string? Message,
		IReadOnlyList<WorkflowStepResultInfo> Steps);

	public sealed class WorkflowRunner
	{
		private const int MaxResultJsonPreviewLength = 4096;

		private readonly object runGate = new();
		private readonly WorkflowToolDispatcher dispatcher;
		private readonly ILogger<WorkflowRunner> logger;

		public WorkflowRunner(
			WorkflowToolDispatcher dispatcher,
			ILogger<WorkflowRunner> logger)
		{
			this.dispatcher = dispatcher;
			this.logger = logger;
		}

		public WorkflowRunInfo RunWorkflow(
			WorkflowDefinition workflow,
			string? argumentsJson)
		{
			ArgumentNullException.ThrowIfNull(workflow);

			lock (runGate)
			{
				return RunWorkflowCore(workflow, argumentsJson);
			}
		}

		private WorkflowRunInfo RunWorkflowCore(
			WorkflowDefinition workflow,
			string? argumentsJson)
		{
			var runId = Guid.NewGuid().ToString("N");
			var workflowName = workflow.Name ?? "inline-workflow";
			var startedAt = DateTimeOffset.UtcNow;
			var startedAtTimestamp = Stopwatch.GetTimestamp();
			var steps = new List<WorkflowStepResultInfo>();

			logger.LogInformation(
				"Workflow run {RunId} started for workflow {WorkflowName}.",
				runId,
				workflowName);

			try
			{
				var arguments = WorkflowTemplate.ParseArgumentsJson(argumentsJson);
				var variables = WorkflowTemplate.CreateVariables(arguments, runId);

				for (var index = 0; index < workflow.Steps.Count; index++)
				{
					var step = workflow.Steps[index];
					var stepStartedAt = DateTimeOffset.UtcNow;
					var stepStartedAtTimestamp = Stopwatch.GetTimestamp();

					try
					{
						var renderedArguments = WorkflowTemplate.RenderArguments(step.Arguments, variables);
						var result = dispatcher.Invoke(step.Tool, renderedArguments);
						var serializedResult = SerializeResult(result);

						steps.Add(new WorkflowStepResultInfo(
							index,
							step.Tool,
							"succeeded",
							stepStartedAt,
							DateTimeOffset.UtcNow,
							Stopwatch.GetElapsedTime(stepStartedAtTimestamp).TotalMilliseconds,
							serializedResult.Preview,
							serializedResult.Length,
							null));
					}
					catch (Exception exception)
					{
						steps.Add(new WorkflowStepResultInfo(
							index,
							step.Tool,
							"failed",
							stepStartedAt,
							DateTimeOffset.UtcNow,
							Stopwatch.GetElapsedTime(stepStartedAtTimestamp).TotalMilliseconds,
							null,
							null,
							exception.Message));

						return CompleteRun(
							runId,
							workflowName,
							"failed",
							startedAt,
							startedAtTimestamp,
							exception.Message,
							steps);
					}
				}

				return CompleteRun(
					runId,
					workflowName,
					"succeeded",
					startedAt,
					startedAtTimestamp,
					"Workflow completed.",
					steps);
			}
			catch (Exception exception)
			{
				return CompleteRun(
					runId,
					workflowName,
					"failed",
					startedAt,
					startedAtTimestamp,
					exception.Message,
					steps);
			}
		}

		private WorkflowRunInfo CompleteRun(
			string runId,
			string workflowName,
			string status,
			DateTimeOffset startedAt,
			long startedAtTimestamp,
			string? message,
			IReadOnlyList<WorkflowStepResultInfo> steps)
		{
			var run = new WorkflowRunInfo(
				runId,
				workflowName,
				status,
				startedAt,
				DateTimeOffset.UtcNow,
				Stopwatch.GetElapsedTime(startedAtTimestamp).TotalMilliseconds,
				message,
				steps.ToArray());

			logger.LogInformation(
				"Workflow run {RunId} completed with status {Status}.",
				run.RunId,
				run.Status);

			return run;
		}

		private static SerializedWorkflowResult SerializeResult(object? result)
		{
			var json = result is null
				? "null"
				: JsonSerializer.Serialize(result, result.GetType(), WorkflowJson.Options);

			if (json.Length <= MaxResultJsonPreviewLength)
			{
				return new SerializedWorkflowResult(json, json.Length);
			}

			return new SerializedWorkflowResult(
				json[..MaxResultJsonPreviewLength] + "...",
				json.Length);
		}

		private sealed record SerializedWorkflowResult(
			string Preview,
			int Length);
	}
}
