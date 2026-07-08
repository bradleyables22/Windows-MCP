using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Server.Workflows
{
	public sealed class WorkflowStore
	{
		private const int MaxWorkflowNameLength = 128;

		private readonly string storageRoot;

		public WorkflowStore()
			: this(GetDefaultStorageRoot())
		{
		}

		public WorkflowStore(string storageRoot)
		{
			this.storageRoot = Path.GetFullPath(storageRoot);
			Directory.CreateDirectory(this.storageRoot);
		}

		public string StorageRoot => storageRoot;

		public IReadOnlyList<WorkflowSummary> ListWorkflows()
		{
			Directory.CreateDirectory(storageRoot);

			return Directory
				.EnumerateFiles(storageRoot, "*.json", SearchOption.TopDirectoryOnly)
				.Select(TryReadSummary)
				.OfType<WorkflowSummary>()
				.OrderBy(workflow => workflow.Name, StringComparer.OrdinalIgnoreCase)
				.ToArray();
		}

		public WorkflowDefinition GetWorkflow(string name)
		{
			var workflowName = ValidateWorkflowName(name);
			var path = GetWorkflowPath(workflowName);

			if (!File.Exists(path))
			{
				throw new FileNotFoundException($"Workflow '{workflowName}' does not exist.", path);
			}

			var workflow = ParseWorkflow(workflowName, File.ReadAllText(path));
			workflow.Name = workflowName;
			return workflow;
		}

		public WorkflowDefinition CreateWorkflow(string name, string workflowJson)
		{
			return SaveWorkflow(name, workflowJson, overwrite: false, requireExisting: false);
		}

		public WorkflowDefinition UpdateWorkflow(string name, string workflowJson)
		{
			return SaveWorkflow(name, workflowJson, overwrite: true, requireExisting: true);
		}

		public WorkflowDefinition SaveWorkflow(string name, string workflowJson)
		{
			return SaveWorkflow(name, workflowJson, overwrite: true, requireExisting: false);
		}

		public bool DeleteWorkflow(string name)
		{
			var workflowName = ValidateWorkflowName(name);
			var path = GetWorkflowPath(workflowName);

			if (!File.Exists(path))
			{
				return false;
			}

			File.Delete(path);
			return true;
		}

		public WorkflowDefinition ParseWorkflow(string? name, string workflowJson)
		{
			if (string.IsNullOrWhiteSpace(workflowJson))
			{
				throw new ArgumentException("Workflow JSON cannot be empty.", nameof(workflowJson));
			}

			var workflow = JsonSerializer.Deserialize<WorkflowDefinition>(workflowJson, WorkflowJson.Options)
				?? throw new ArgumentException("Workflow JSON did not contain a workflow definition.", nameof(workflowJson));

			var workflowName = !string.IsNullOrWhiteSpace(name)
				? ValidateWorkflowName(name)
				: ValidateWorkflowName(string.IsNullOrWhiteSpace(workflow.Name) ? "inline-workflow" : workflow.Name);

			if (!string.IsNullOrWhiteSpace(name) &&
				!string.IsNullOrWhiteSpace(workflow.Name) &&
				!string.Equals(workflow.Name.Trim(), workflowName, StringComparison.Ordinal))
			{
				throw new ArgumentException("Workflow JSON name must match the requested workflow name.", nameof(workflowJson));
			}

			workflow.Name = workflowName;
			ValidateWorkflow(workflow);

			return workflow;
		}

		public string GetWorkflowPathForName(string name)
		{
			return GetWorkflowPath(ValidateWorkflowName(name));
		}

		private WorkflowDefinition SaveWorkflow(
			string name,
			string workflowJson,
			bool overwrite,
			bool requireExisting)
		{
			var workflow = ParseWorkflow(name, workflowJson);
			var path = GetWorkflowPath(workflow.Name!);
			var exists = File.Exists(path);

			if (requireExisting && !exists)
			{
				throw new FileNotFoundException($"Workflow '{workflow.Name}' does not exist.", path);
			}

			if (!overwrite && exists)
			{
				throw new IOException($"Workflow '{workflow.Name}' already exists.");
			}

			Directory.CreateDirectory(storageRoot);
			File.WriteAllText(path, JsonSerializer.Serialize(workflow, WorkflowJson.Options));

			return workflow;
		}

		private WorkflowSummary? TryReadSummary(string path)
		{
			try
			{
				var workflow = ParseWorkflow(null, File.ReadAllText(path));
				var fileInfo = new FileInfo(path);

				return new WorkflowSummary(
					workflow.Name!,
					workflow.Description,
					workflow.Steps.Count,
					fileInfo.CreationTimeUtc,
					fileInfo.LastWriteTimeUtc,
					path);
			}
			catch (Exception exception) when (
				exception is IOException
					or JsonException
					or ArgumentException
					or UnauthorizedAccessException)
			{
				return null;
			}
		}

		private static void ValidateWorkflow(WorkflowDefinition workflow)
		{
			ValidateWorkflowName(workflow.Name);

			if (workflow.Steps.Count == 0)
			{
				throw new ArgumentException("Workflow must contain at least one step.", nameof(workflow));
			}

			for (var index = 0; index < workflow.Steps.Count; index++)
			{
				if (string.IsNullOrWhiteSpace(workflow.Steps[index].Tool))
				{
					throw new ArgumentException($"Workflow step {index} must specify a tool name.", nameof(workflow));
				}

				workflow.Steps[index].Tool = workflow.Steps[index].Tool.Trim();
			}
		}

		private string GetWorkflowPath(string workflowName)
		{
			var fileName = CreateWorkflowFileName(workflowName);
			var path = Path.GetFullPath(Path.Combine(storageRoot, fileName));
			var root = storageRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

			if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
			{
				throw new InvalidOperationException("Resolved workflow path escaped the workflow storage directory.");
			}

			return path;
		}

		private static string CreateWorkflowFileName(string workflowName)
		{
			var slugBuilder = new StringBuilder();

			foreach (var character in workflowName.Trim().ToLowerInvariant())
			{
				if (char.IsLetterOrDigit(character))
				{
					slugBuilder.Append(character);
				}
				else if (character is ' ' or '-' or '_' or '.')
				{
					if (slugBuilder.Length > 0 && slugBuilder[^1] != '-')
					{
						slugBuilder.Append('-');
					}
				}
			}

			var slug = slugBuilder.ToString().Trim('-');
			if (string.IsNullOrWhiteSpace(slug))
			{
				slug = "workflow";
			}

			if (slug.Length > 48)
			{
				slug = slug[..48].Trim('-');
			}

			var hash = Convert
				.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(workflowName)))
				[..12]
				.ToLowerInvariant();

			return $"{slug}-{hash}.json";
		}

		private static string ValidateWorkflowName(string? name)
		{
			if (string.IsNullOrWhiteSpace(name))
			{
				throw new ArgumentException("Workflow name cannot be empty.", nameof(name));
			}

			var trimmed = name.Trim();
			if (trimmed.Length > MaxWorkflowNameLength)
			{
				throw new ArgumentOutOfRangeException(nameof(name), trimmed.Length, $"Workflow name cannot exceed {MaxWorkflowNameLength} characters.");
			}

			if (trimmed.Any(char.IsControl))
			{
				throw new ArgumentException("Workflow name cannot contain control characters.", nameof(name));
			}

			return trimmed;
		}

		private static string GetDefaultStorageRoot()
		{
			var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
			if (string.IsNullOrWhiteSpace(localAppData))
			{
				localAppData = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
			}

			if (string.IsNullOrWhiteSpace(localAppData))
			{
				localAppData = AppContext.BaseDirectory;
			}

			return Path.Combine(localAppData, "WindowsMCP", "workflows");
		}
	}
}
