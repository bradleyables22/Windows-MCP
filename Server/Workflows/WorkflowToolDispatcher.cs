using ModelContextProtocol.Server;
using Server.Tools;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Text.Json;

namespace Server.Workflows
{
	public sealed record WorkflowToolParameterInfo(
		string Name,
		string Type,
		bool Required,
		string? DefaultValueJson);

	public sealed record WorkflowAvailableToolInfo(
		string Name,
		string ToolClass,
		IReadOnlyList<WorkflowToolParameterInfo> Parameters);

	public sealed class WorkflowToolDispatcher
	{
		private static readonly Type[] ToolTypes =
		[
			typeof(MouseTools),
			typeof(KeyboardTools),
			typeof(ScreenTools),
			typeof(WindowTools),
			typeof(ClipboardTools),
			typeof(ProcessTools),
			typeof(WaitTools),
			typeof(CommandTools),
			typeof(FileSystemTools),
			typeof(RegistryTools),
			typeof(EnvironmentTools),
			typeof(ServiceTools)
		];

		private readonly IReadOnlyDictionary<string, ToolMethodInvoker> tools;

		public WorkflowToolDispatcher()
		{
			tools = BuildToolMap();
		}

		public IReadOnlyList<WorkflowAvailableToolInfo> ListTools()
		{
			return tools.Values
				.GroupBy(tool => tool.CanonicalName, StringComparer.OrdinalIgnoreCase)
				.Select(group => group.First())
				.OrderBy(tool => tool.CanonicalName, StringComparer.OrdinalIgnoreCase)
				.Select(tool => new WorkflowAvailableToolInfo(
					tool.CanonicalName,
					tool.DeclaringType.Name,
					tool.Parameters
						.Select(parameter => new WorkflowToolParameterInfo(
							parameter.Name ?? string.Empty,
							FormatTypeName(parameter.ParameterType),
							!parameter.HasDefaultValue,
							parameter.HasDefaultValue ? SerializeDefaultValue(parameter.DefaultValue) : null))
						.ToArray()))
				.ToArray();
		}

		public object? Invoke(
			string toolName,
			IReadOnlyDictionary<string, JsonElement> arguments)
		{
			if (!tools.TryGetValue(toolName, out var tool))
			{
				throw new InvalidOperationException($"Workflow tool '{toolName}' is not available.");
			}

			var convertedArguments = tool.Parameters
				.Select(parameter => ConvertArgument(tool.CanonicalName, parameter, arguments))
				.ToArray();

			try
			{
				return tool.Method.Invoke(tool.Instance, convertedArguments);
			}
			catch (TargetInvocationException exception) when (exception.InnerException is not null)
			{
				ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
				throw;
			}
		}

		private static IReadOnlyDictionary<string, ToolMethodInvoker> BuildToolMap()
		{
			var map = new Dictionary<string, ToolMethodInvoker>(StringComparer.OrdinalIgnoreCase);

			foreach (var type in ToolTypes)
			{
				var instance = Activator.CreateInstance(type)
					?? throw new InvalidOperationException($"Could not create tool instance for {type.FullName}.");

				var methods = type
					.GetMethods(BindingFlags.Instance | BindingFlags.Public)
					.Where(method => method.GetCustomAttribute<McpServerToolAttribute>() is not null);

				foreach (var method in methods)
				{
					var canonicalName = ToSnakeCase(method.Name);
					var invoker = new ToolMethodInvoker(
						canonicalName,
						method.Name,
						type,
						instance,
						method,
						method.GetParameters());

					AddToolAlias(map, canonicalName, invoker);
					AddToolAlias(map, method.Name, invoker);
				}
			}

			return map;
		}

		private static void AddToolAlias(
			Dictionary<string, ToolMethodInvoker> map,
			string alias,
			ToolMethodInvoker invoker)
		{
			if (map.TryGetValue(alias, out var existing))
			{
				if (!string.Equals(existing.MethodName, invoker.MethodName, StringComparison.Ordinal))
				{
					throw new InvalidOperationException($"Workflow tool name '{alias}' is duplicated.");
				}

				return;
			}

			map[alias] = invoker;
		}

		private static string ToSnakeCase(string name)
		{
			var builder = new StringBuilder();

			for (var index = 0; index < name.Length; index++)
			{
				var character = name[index];
				if (char.IsUpper(character))
				{
					if (index > 0)
					{
						builder.Append('_');
					}

					builder.Append(char.ToLowerInvariant(character));
				}
				else
				{
					builder.Append(character);
				}
			}

			return builder.ToString();
		}

		private static object? ConvertArgument(
			string toolName,
			ParameterInfo parameter,
			IReadOnlyDictionary<string, JsonElement> arguments)
		{
			var parameterName = parameter.Name
				?? throw new InvalidOperationException($"Tool '{toolName}' has an unnamed parameter.");

			if (!arguments.TryGetValue(parameterName, out var value))
			{
				if (parameter.HasDefaultValue)
				{
					return parameter.DefaultValue;
				}

				throw new ArgumentException($"Workflow tool '{toolName}' requires argument '{parameterName}'.");
			}

			var targetType = parameter.ParameterType;

			if (value.ValueKind == JsonValueKind.Null)
			{
				if (IsNullable(targetType))
				{
					return null;
				}

				throw new ArgumentException($"Workflow tool '{toolName}' argument '{parameterName}' cannot be null.");
			}

			try
			{
				return JsonSerializer.Deserialize(value.GetRawText(), targetType, WorkflowJson.Options);
			}
			catch (JsonException exception)
			{
				throw new ArgumentException(
					$"Workflow tool '{toolName}' argument '{parameterName}' could not be converted to {FormatTypeName(targetType)}.",
					exception);
			}
		}

		private static string FormatTypeName(Type type)
		{
			var nullableType = Nullable.GetUnderlyingType(type);
			if (nullableType is not null)
			{
				return FormatTypeName(nullableType) + "?";
			}

			if (type.IsArray)
			{
				return FormatTypeName(type.GetElementType()!) + "[]";
			}

			return type switch
			{
				_ when type == typeof(string) => "string",
				_ when type == typeof(int) => "int",
				_ when type == typeof(bool) => "bool",
				_ => type.Name
			};
		}

		private static bool IsNullable(Type type)
		{
			return !type.IsValueType || Nullable.GetUnderlyingType(type) is not null;
		}

		private static string SerializeDefaultValue(object? value)
		{
			return JsonSerializer.Serialize(value, WorkflowJson.Options);
		}

		private sealed record ToolMethodInvoker(
			string CanonicalName,
			string MethodName,
			Type DeclaringType,
			object Instance,
			MethodInfo Method,
			IReadOnlyList<ParameterInfo> Parameters);
	}
}
