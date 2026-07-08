using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Server.Workflows
{
	internal static partial class WorkflowTemplate
	{
		public static IReadOnlyDictionary<string, JsonElement> ParseArgumentsJson(string? argumentsJson)
		{
			if (string.IsNullOrWhiteSpace(argumentsJson))
			{
				return new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
			}

			var parsed = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(argumentsJson, WorkflowJson.Options)
				?? new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);

			return CloneDictionary(parsed);
		}

		public static IReadOnlyDictionary<string, JsonElement> CreateVariables(
			IReadOnlyDictionary<string, JsonElement> arguments,
			string runId)
		{
			var variables = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);

			foreach (var (key, value) in arguments)
			{
				variables[key] = value.Clone();
			}

			var runIdElement = JsonSerializer.SerializeToElement(runId, WorkflowJson.Options);
			variables["runId"] = runIdElement;
			variables["jobId"] = runIdElement;
			return variables;
		}

		public static IReadOnlyDictionary<string, JsonElement> RenderArguments(
			IReadOnlyDictionary<string, JsonElement>? arguments,
			IReadOnlyDictionary<string, JsonElement> variables)
		{
			if (arguments is null || arguments.Count == 0)
			{
				return new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
			}

			var rendered = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);

			foreach (var (key, value) in arguments)
			{
				var node = JsonNode.Parse(value.GetRawText());
				var renderedNode = RenderNode(node, variables);
				var renderedJson = renderedNode?.ToJsonString(WorkflowJson.Options) ?? "null";
				rendered[key] = JsonSerializer.Deserialize<JsonElement>(renderedJson, WorkflowJson.Options).Clone();
			}

			return rendered;
		}

		private static JsonNode? RenderNode(
			JsonNode? node,
			IReadOnlyDictionary<string, JsonElement> variables)
		{
			if (node is null)
			{
				return null;
			}

			if (node is JsonValue value && value.TryGetValue<string>(out var text))
			{
				return RenderString(text, variables);
			}

			if (node is JsonArray array)
			{
				var renderedArray = new JsonArray();
				foreach (var item in array)
				{
					renderedArray.Add(RenderNode(item?.DeepClone(), variables));
				}

				return renderedArray;
			}

			if (node is JsonObject obj)
			{
				var renderedObject = new JsonObject();
				foreach (var property in obj)
				{
					renderedObject[property.Key] = RenderNode(property.Value?.DeepClone(), variables);
				}

				return renderedObject;
			}

			return node.DeepClone();
		}

		private static JsonNode? RenderString(
			string text,
			IReadOnlyDictionary<string, JsonElement> variables)
		{
			var exactMatch = ExactPlaceholderRegex().Match(text);
			if (exactMatch.Success)
			{
				var variable = GetVariable(exactMatch.Groups["name"].Value, variables);
				return variable.ValueKind == JsonValueKind.Null
					? null
					: JsonNode.Parse(variable.GetRawText());
			}

			var renderedText = PlaceholderRegex().Replace(
				text,
				match => GetVariableAsString(match.Groups["name"].Value, variables));

			return JsonValue.Create(renderedText);
		}

		private static JsonElement GetVariable(
			string name,
			IReadOnlyDictionary<string, JsonElement> variables)
		{
			if (variables.TryGetValue(name, out var value))
			{
				return value;
			}

			throw new InvalidOperationException($"Workflow variable '{name}' was not provided.");
		}

		private static string GetVariableAsString(
			string name,
			IReadOnlyDictionary<string, JsonElement> variables)
		{
			var value = GetVariable(name, variables);

			return value.ValueKind switch
			{
				JsonValueKind.String => value.GetString() ?? string.Empty,
				JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => value.GetRawText(),
				JsonValueKind.Null => string.Empty,
				_ => value.GetRawText()
			};
		}

		private static Dictionary<string, JsonElement> CloneDictionary(
			Dictionary<string, JsonElement> source)
		{
			var clone = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);

			foreach (var (key, value) in source)
			{
				clone[key] = value.Clone();
			}

			return clone;
		}

		[GeneratedRegex(@"^\{\{\s*(?<name>[A-Za-z0-9_.-]+)\s*\}\}$")]
		private static partial Regex ExactPlaceholderRegex();

		[GeneratedRegex(@"\{\{\s*(?<name>[A-Za-z0-9_.-]+)\s*\}\}")]
		private static partial Regex PlaceholderRegex();
	}
}
