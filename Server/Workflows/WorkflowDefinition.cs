using System.Text.Json;
using System.Text.Json.Serialization;

namespace Server.Workflows
{
	public sealed class WorkflowDefinition
	{
		public string? Name { get; set; }

		public string? Description { get; set; }

		public Dictionary<string, string>? Parameters { get; set; }

		public List<WorkflowStepDefinition> Steps { get; set; } = [];
	}

	public sealed class WorkflowStepDefinition
	{
		public string Tool { get; set; } = string.Empty;

		public Dictionary<string, JsonElement>? Arguments { get; set; }
	}

	public sealed record WorkflowSummary(
		string Name,
		string? Description,
		int StepCount,
		DateTimeOffset CreatedAt,
		DateTimeOffset UpdatedAt,
		string Path);

	internal static class WorkflowJson
	{
		public static readonly JsonSerializerOptions Options = CreateOptions();

		private static JsonSerializerOptions CreateOptions()
		{
			var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
			{
				WriteIndented = true,
				DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
			};

			return options;
		}
	}
}
