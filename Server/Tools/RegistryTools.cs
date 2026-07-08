using Microsoft.Win32;
using ModelContextProtocol.Server;
using Server.Workflows;
using System.ComponentModel;
using System.Text.Json;

namespace Server.Tools
{
	public sealed record RegistryValueInfo(
		string Hive,
		string KeyPath,
		string? ValueName,
		string? ValueKind,
		object? Value);

	public sealed record RegistryWriteInfo(
		string Hive,
		string KeyPath,
		string? ValueName,
		string ValueKind,
		object? Value);

	public sealed record RegistrySubKeyInfo(
		string Hive,
		string KeyPath,
		string Name);

	public sealed class RegistryTools
	{
		[McpServerTool]
		[Description("Reads a Windows registry value.")]
		public RegistryValueInfo GetRegistryValue(
			[Description("Registry hive: HKCU, HKLM, HKCR, HKU, or HKCC.")] string hive,
			[Description("Registry key path under the hive.")] string keyPath,
			[Description("Value name. Use null for the default value.")] string? valueName = null,
			[Description("Registry view: default, registry64, or registry32.")] string view = "default")
		{
			using var key = OpenSubKey(hive, keyPath, writable: false, view);
			if (key is null)
			{
				throw new InvalidOperationException($"Registry key '{hive}\\{keyPath}' does not exist.");
			}

			var value = key.GetValue(valueName);
			var kind = value is null ? null : key.GetValueKind(valueName).ToString();

			return new RegistryValueInfo(
				NormalizeHiveName(hive),
				NormalizeKeyPath(keyPath),
				valueName,
				kind,
				NormalizeRegistryValue(value));
		}

		[McpServerTool]
		[Description("Creates or updates a Windows registry value.")]
		public RegistryWriteInfo SetRegistryValue(
			[Description("Registry hive: HKCU, HKLM, HKCR, HKU, or HKCC.")] string hive,
			[Description("Registry key path under the hive.")] string keyPath,
			[Description("Value name. Use null for the default value.")] string? valueName,
			[Description("Value content. Binary values use base64; MultiString values use a JSON string array.")] string value,
			[Description("Registry value kind: String, ExpandString, DWord, QWord, Binary, or MultiString.")] string valueKind = "String",
			[Description("Registry view: default, registry64, or registry32.")] string view = "default")
		{
			ArgumentNullException.ThrowIfNull(value);

			using var key = CreateSubKey(hive, keyPath, view);
			var kind = ParseRegistryValueKind(valueKind);
			var convertedValue = ConvertRegistryValue(value, kind);
			key.SetValue(valueName, convertedValue, kind);

			return new RegistryWriteInfo(
				NormalizeHiveName(hive),
				NormalizeKeyPath(keyPath),
				valueName,
				kind.ToString(),
				NormalizeRegistryValue(convertedValue));
		}

		[McpServerTool]
		[Description("Lists value names and values under a Windows registry key.")]
		public IReadOnlyList<RegistryValueInfo> ListRegistryValues(
			[Description("Registry hive: HKCU, HKLM, HKCR, HKU, or HKCC.")] string hive,
			[Description("Registry key path under the hive.")] string keyPath,
			[Description("Registry view: default, registry64, or registry32.")] string view = "default")
		{
			using var key = OpenSubKey(hive, keyPath, writable: false, view);
			if (key is null)
			{
				throw new InvalidOperationException($"Registry key '{hive}\\{keyPath}' does not exist.");
			}

			return key.GetValueNames()
				.Select(name =>
				{
					var value = key.GetValue(name);
					var valueName = string.IsNullOrEmpty(name) ? null : name;
					return new RegistryValueInfo(
						NormalizeHiveName(hive),
						NormalizeKeyPath(keyPath),
						valueName,
						value is null ? null : key.GetValueKind(name).ToString(),
						NormalizeRegistryValue(value));
				})
				.ToArray();
		}

		[McpServerTool]
		[Description("Lists subkey names under a Windows registry key.")]
		public IReadOnlyList<RegistrySubKeyInfo> ListRegistrySubKeys(
			[Description("Registry hive: HKCU, HKLM, HKCR, HKU, or HKCC.")] string hive,
			[Description("Registry key path under the hive.")] string keyPath,
			[Description("Registry view: default, registry64, or registry32.")] string view = "default")
		{
			using var key = OpenSubKey(hive, keyPath, writable: false, view);
			if (key is null)
			{
				throw new InvalidOperationException($"Registry key '{hive}\\{keyPath}' does not exist.");
			}

			return key.GetSubKeyNames()
				.Select(name => new RegistrySubKeyInfo(
					NormalizeHiveName(hive),
					NormalizeKeyPath(keyPath),
					name))
				.ToArray();
		}

		private static RegistryKey? OpenSubKey(
			string hive,
			string keyPath,
			bool writable,
			string view)
		{
			using var baseKey = OpenBaseKey(hive, view);
			return baseKey.OpenSubKey(NormalizeKeyPath(keyPath), writable);
		}

		private static RegistryKey CreateSubKey(
			string hive,
			string keyPath,
			string view)
		{
			using var baseKey = OpenBaseKey(hive, view);
			return baseKey.CreateSubKey(NormalizeKeyPath(keyPath), writable: true)
				?? throw new InvalidOperationException($"Could not create registry key '{hive}\\{keyPath}'.");
		}

		private static RegistryKey OpenBaseKey(
			string hive,
			string view)
		{
			return RegistryKey.OpenBaseKey(ParseHive(hive), ParseRegistryView(view));
		}

		private static RegistryHive ParseHive(string hive)
		{
			return NormalizeHiveName(hive) switch
			{
				"HKCR" => RegistryHive.ClassesRoot,
				"HKCU" => RegistryHive.CurrentUser,
				"HKLM" => RegistryHive.LocalMachine,
				"HKU" => RegistryHive.Users,
				"HKCC" => RegistryHive.CurrentConfig,
				_ => throw new ArgumentException("Hive must be HKCR, HKCU, HKLM, HKU, or HKCC.", nameof(hive))
			};
		}

		private static string NormalizeHiveName(string hive)
		{
			if (string.IsNullOrWhiteSpace(hive))
			{
				throw new ArgumentException("Hive cannot be empty.", nameof(hive));
			}

			return hive.Trim().ToUpperInvariant() switch
			{
				"HKEY_CLASSES_ROOT" or "HKCR" => "HKCR",
				"HKEY_CURRENT_USER" or "HKCU" => "HKCU",
				"HKEY_LOCAL_MACHINE" or "HKLM" => "HKLM",
				"HKEY_USERS" or "HKU" => "HKU",
				"HKEY_CURRENT_CONFIG" or "HKCC" => "HKCC",
				var value => value
			};
		}

		private static RegistryView ParseRegistryView(string view)
		{
			return string.IsNullOrWhiteSpace(view)
				? RegistryView.Default
				: view.Trim().ToLowerInvariant() switch
				{
					"default" => RegistryView.Default,
					"registry64" or "64" or "x64" => RegistryView.Registry64,
					"registry32" or "32" or "x86" => RegistryView.Registry32,
					_ => throw new ArgumentException("Registry view must be default, registry64, or registry32.", nameof(view))
				};
		}

		private static string NormalizeKeyPath(string keyPath)
		{
			if (string.IsNullOrWhiteSpace(keyPath))
			{
				throw new ArgumentException("Registry key path cannot be empty.", nameof(keyPath));
			}

			return keyPath.Trim().Trim('\\');
		}

		private static RegistryValueKind ParseRegistryValueKind(string valueKind)
		{
			if (Enum.TryParse<RegistryValueKind>(valueKind, ignoreCase: true, out var kind) &&
				kind is RegistryValueKind.String
					or RegistryValueKind.ExpandString
					or RegistryValueKind.DWord
					or RegistryValueKind.QWord
					or RegistryValueKind.Binary
					or RegistryValueKind.MultiString)
			{
				return kind;
			}

			throw new ArgumentException(
				"Registry value kind must be String, ExpandString, DWord, QWord, Binary, or MultiString.",
				nameof(valueKind));
		}

		private static object ConvertRegistryValue(
			string value,
			RegistryValueKind kind)
		{
			return kind switch
			{
				RegistryValueKind.String or RegistryValueKind.ExpandString => value,
				RegistryValueKind.DWord => int.Parse(value),
				RegistryValueKind.QWord => long.Parse(value),
				RegistryValueKind.Binary => Convert.FromBase64String(value),
				RegistryValueKind.MultiString =>
					JsonSerializer.Deserialize<string[]>(value, WorkflowJson.Options)
						?? throw new ArgumentException("MultiString value must be a JSON string array.", nameof(value)),
				_ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unsupported registry value kind.")
			};
		}

		private static object? NormalizeRegistryValue(object? value)
		{
			return value switch
			{
				byte[] bytes => Convert.ToBase64String(bytes),
				string[] strings => strings,
				_ => value
			};
		}
	}
}
