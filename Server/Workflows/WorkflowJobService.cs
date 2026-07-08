using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using System.Threading.Channels;

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

	public sealed record WorkflowJobInfo(
		string JobId,
		string WorkflowName,
		string Status,
		DateTimeOffset QueuedAt,
		DateTimeOffset? StartedAt,
		DateTimeOffset? CompletedAt,
		string? Message,
		IReadOnlyList<WorkflowStepResultInfo> Steps);

	public sealed record WorkflowJobWaitResult(
		bool Succeeded,
		WorkflowJobInfo Job,
		TimeSpan Elapsed,
		int Attempts,
		string? Message);

	public sealed class WorkflowJobService : BackgroundService
	{
		private const int MaxResultJsonPreviewLength = 4096;

		private readonly Channel<WorkflowExecutionRequest> queue = Channel.CreateUnbounded<WorkflowExecutionRequest>();
		private readonly ConcurrentDictionary<string, WorkflowJobState> jobs = new(StringComparer.OrdinalIgnoreCase);
		private readonly WorkflowToolDispatcher dispatcher;
		private readonly ILogger<WorkflowJobService> logger;

		public WorkflowJobService(
			WorkflowToolDispatcher dispatcher,
			ILogger<WorkflowJobService> logger)
		{
			this.dispatcher = dispatcher;
			this.logger = logger;
		}

		public WorkflowJobInfo EnqueueWorkflow(
			WorkflowDefinition workflow,
			string? argumentsJson)
		{
			ArgumentNullException.ThrowIfNull(workflow);

			var jobId = Guid.NewGuid().ToString("N");
			var arguments = WorkflowTemplate.ParseArgumentsJson(argumentsJson);
			var job = new WorkflowJobState(jobId, workflow.Name ?? "inline-workflow");

			if (!jobs.TryAdd(jobId, job))
			{
				throw new InvalidOperationException($"Workflow job '{jobId}' already exists.");
			}

			if (!queue.Writer.TryWrite(new WorkflowExecutionRequest(jobId, workflow, arguments)))
			{
				throw new InvalidOperationException("Workflow job queue is closed.");
			}

			logger.LogInformation(
				"Workflow job {JobId} queued for workflow {WorkflowName}.",
				jobId,
				job.WorkflowName);

			return job.Snapshot();
		}

		public IReadOnlyList<WorkflowJobInfo> ListJobs()
		{
			return jobs.Values
				.Select(job => job.Snapshot())
				.OrderByDescending(job => job.QueuedAt)
				.ToArray();
		}

		public WorkflowJobInfo GetJob(string jobId)
		{
			return GetJobState(jobId).Snapshot();
		}

		public WorkflowJobWaitResult WaitForCompletion(
			string jobId,
			TimeSpan timeout,
			TimeSpan pollInterval)
		{
			if (timeout < TimeSpan.Zero)
			{
				throw new ArgumentOutOfRangeException(nameof(timeout), timeout, "Timeout cannot be negative.");
			}

			if (pollInterval <= TimeSpan.Zero)
			{
				throw new ArgumentOutOfRangeException(nameof(pollInterval), pollInterval, "Poll interval must be positive.");
			}

			var startedAt = Stopwatch.GetTimestamp();
			var attempts = 0;

			while (Stopwatch.GetElapsedTime(startedAt) <= timeout)
			{
				attempts++;

				var job = GetJob(jobId);
				if (IsTerminalStatus(job.Status))
				{
					return new WorkflowJobWaitResult(
						true,
						job,
						Stopwatch.GetElapsedTime(startedAt),
						attempts,
						null);
				}

				var remaining = timeout - Stopwatch.GetElapsedTime(startedAt);
				if (remaining <= TimeSpan.Zero)
				{
					break;
				}

				Thread.Sleep(remaining < pollInterval ? remaining : pollInterval);
			}

			return new WorkflowJobWaitResult(
				false,
				GetJob(jobId),
				Stopwatch.GetElapsedTime(startedAt),
				attempts,
				$"Timed out waiting for workflow job '{jobId}'.");
		}

		protected override async Task ExecuteAsync(CancellationToken stoppingToken)
		{
			await foreach (var request in queue.Reader.ReadAllAsync(stoppingToken))
			{
				RunWorkflow(request, stoppingToken);
			}
		}

		private void RunWorkflow(
			WorkflowExecutionRequest request,
			CancellationToken stoppingToken)
		{
			var job = GetJobState(request.JobId);
			job.MarkRunning();

			logger.LogInformation(
				"Workflow job {JobId} started for workflow {WorkflowName}.",
				request.JobId,
				job.WorkflowName);

			try
			{
				var variables = WorkflowTemplate.CreateVariables(request.Arguments, request.JobId);

				for (var index = 0; index < request.Workflow.Steps.Count; index++)
				{
					stoppingToken.ThrowIfCancellationRequested();

					var step = request.Workflow.Steps[index];
					var stepStartedAt = DateTimeOffset.UtcNow;
					var stepStopwatch = Stopwatch.GetTimestamp();

					try
					{
						var renderedArguments = WorkflowTemplate.RenderArguments(step.Arguments, variables);
						var result = dispatcher.Invoke(step.Tool, renderedArguments);
						var serializedResult = SerializeResult(result);

						job.AddStep(new WorkflowStepResultInfo(
							index,
							step.Tool,
							"succeeded",
							stepStartedAt,
							DateTimeOffset.UtcNow,
							Stopwatch.GetElapsedTime(stepStopwatch).TotalMilliseconds,
							serializedResult.Preview,
							serializedResult.Length,
							null));
					}
					catch (Exception exception)
					{
						job.AddStep(new WorkflowStepResultInfo(
							index,
							step.Tool,
							"failed",
							stepStartedAt,
							DateTimeOffset.UtcNow,
							Stopwatch.GetElapsedTime(stepStopwatch).TotalMilliseconds,
							null,
							null,
							exception.Message));

						throw;
					}
				}

				job.MarkCompleted("succeeded", "Workflow completed.");
			}
			catch (OperationCanceledException)
			{
				job.MarkCompleted("canceled", "Workflow job was canceled.");
			}
			catch (Exception exception)
			{
				job.MarkCompleted("failed", exception.Message);
			}

			var snapshot = job.Snapshot();
			logger.LogInformation(
				"Workflow job {JobId} completed with status {Status}.",
				snapshot.JobId,
				snapshot.Status);
		}

		private WorkflowJobState GetJobState(string jobId)
		{
			if (string.IsNullOrWhiteSpace(jobId))
			{
				throw new ArgumentException("Job ID cannot be empty.", nameof(jobId));
			}

			if (jobs.TryGetValue(jobId.Trim(), out var job))
			{
				return job;
			}

			throw new InvalidOperationException($"Workflow job '{jobId}' does not exist.");
		}

		private static bool IsTerminalStatus(string status)
		{
			return status is "succeeded" or "failed" or "canceled";
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

		private sealed record WorkflowExecutionRequest(
			string JobId,
			WorkflowDefinition Workflow,
			IReadOnlyDictionary<string, JsonElement> Arguments);

		private sealed record SerializedWorkflowResult(
			string Preview,
			int Length);

		private sealed class WorkflowJobState
		{
			private readonly object gate = new();
			private readonly List<WorkflowStepResultInfo> steps = [];
			private string status = "queued";
			private DateTimeOffset? startedAt;
			private DateTimeOffset? completedAt;
			private string? message;

			public WorkflowJobState(
				string jobId,
				string workflowName)
			{
				JobId = jobId;
				WorkflowName = workflowName;
				QueuedAt = DateTimeOffset.UtcNow;
			}

			public string JobId { get; }

			public string WorkflowName { get; }

			public DateTimeOffset QueuedAt { get; }

			public void MarkRunning()
			{
				lock (gate)
				{
					status = "running";
					startedAt = DateTimeOffset.UtcNow;
					message = "Workflow is running.";
				}
			}

			public void AddStep(WorkflowStepResultInfo step)
			{
				lock (gate)
				{
					steps.Add(step);
				}
			}

			public void MarkCompleted(
				string finalStatus,
				string? finalMessage)
			{
				lock (gate)
				{
					status = finalStatus;
					completedAt = DateTimeOffset.UtcNow;
					message = finalMessage;
				}
			}

			public WorkflowJobInfo Snapshot()
			{
				lock (gate)
				{
					return new WorkflowJobInfo(
						JobId,
						WorkflowName,
						status,
						QueuedAt,
						startedAt,
						completedAt,
						message,
						steps.ToArray());
				}
			}
		}
	}
}
