using ModelContextProtocol.Server;
using System.Collections;
using System.ComponentModel;

namespace Server.Tools
{
	public sealed record EnvironmentVariableInfo(
		string Name,
		string? Value,
		string Target);

	public sealed record EnvironmentVariableWriteInfo(
		string Name,
		string? Value,
		string Target);

	public sealed class EnvironmentTools
	{
		[McpServerTool]
		[Description("Gets an environment variable from the process, current user, or machine target.")]
		public EnvironmentVariableInfo GetEnvironmentVariable(
			[Description("Environment variable name.")] string name,
			[Description("Environment target: Process, User, or Machine.")] string target = "Process")
		{
			var variableName = RequireVariableName(name);
			var resolvedTarget = ParseTarget(target);

			return new EnvironmentVariableInfo(
				variableName,
				Environment.GetEnvironmentVariable(variableName, resolvedTarget),
				resolvedTarget.ToString());
		}

		[McpServerTool]
		[Description("Sets or clears an environment variable for the process, current user, or machine target.")]
		public EnvironmentVariableWriteInfo SetEnvironmentVariable(
			[Description("Environment variable name.")] string name,
			[Description("Environment variable value. Use null to clear the variable.")] string? value,
			[Description("Environment target: Process, User, or Machine.")] string target = "Process")
		{
			var variableName = RequireVariableName(name);
			var resolvedTarget = ParseTarget(target);
			Environment.SetEnvironmentVariable(variableName, value, resolvedTarget);

			return new EnvironmentVariableWriteInfo(
				variableName,
				Environment.GetEnvironmentVariable(variableName, resolvedTarget),
				resolvedTarget.ToString());
		}

		[McpServerTool]
		[Description("Lists environment variables from the process, current user, or machine target.")]
		public IReadOnlyList<EnvironmentVariableInfo> ListEnvironmentVariables(
			[Description("Environment target: Process, User, or Machine.")] string target = "Process")
		{
			var resolvedTarget = ParseTarget(target);
			var variables = Environment.GetEnvironmentVariables(resolvedTarget);
			var results = new List<EnvironmentVariableInfo>();

			foreach (DictionaryEntry entry in variables)
			{
				results.Add(new EnvironmentVariableInfo(
					Convert.ToString(entry.Key) ?? string.Empty,
					Convert.ToString(entry.Value),
					resolvedTarget.ToString()));
			}

			return results
				.OrderBy(variable => variable.Name, StringComparer.OrdinalIgnoreCase)
				.ToArray();
		}

		private static string RequireVariableName(string name)
		{
			if (string.IsNullOrWhiteSpace(name))
			{
				throw new ArgumentException("Environment variable name cannot be empty.", nameof(name));
			}

			if (name.Contains('='))
			{
				throw new ArgumentException("Environment variable name cannot contain '='.", nameof(name));
			}

			return name.Trim();
		}

		private static EnvironmentVariableTarget ParseTarget(string target)
		{
			if (Enum.TryParse<EnvironmentVariableTarget>(target, ignoreCase: true, out var parsed))
			{
				return parsed;
			}

			throw new ArgumentException("Environment target must be Process, User, or Machine.", nameof(target));
		}
	}
}
