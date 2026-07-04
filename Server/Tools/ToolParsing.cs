using Server.InteropServices;

namespace Server.Tools
{
	internal static class ToolParsing
	{
		private static readonly IReadOnlyDictionary<string, VirtualKey> KeyAliases =
			new Dictionary<string, VirtualKey>(StringComparer.OrdinalIgnoreCase)
			{
				["ctrl"] = VirtualKey.Control,
				["control"] = VirtualKey.Control,
				["shift"] = VirtualKey.Shift,
				["alt"] = VirtualKey.Alt,
				["escape"] = VirtualKey.Escape,
				["esc"] = VirtualKey.Escape,
				["enter"] = VirtualKey.Enter,
				["return"] = VirtualKey.Enter,
				["space"] = VirtualKey.Space,
				["win"] = VirtualKey.LeftWindows,
				["windows"] = VirtualKey.LeftWindows,
				["delete"] = VirtualKey.Delete,
				["del"] = VirtualKey.Delete,
				["backspace"] = VirtualKey.Backspace,
				["tab"] = VirtualKey.Tab,
				["pageup"] = VirtualKey.PageUp,
				["pgup"] = VirtualKey.PageUp,
				["pagedown"] = VirtualKey.PageDown,
				["pgdn"] = VirtualKey.PageDown,
				["home"] = VirtualKey.Home,
				["end"] = VirtualKey.End,
				["left"] = VirtualKey.Left,
				["right"] = VirtualKey.Right,
				["up"] = VirtualKey.Up,
				["down"] = VirtualKey.Down
			};

		public static MouseButton ParseMouseButton(string button)
		{
			if (string.Equals(button, "left", StringComparison.OrdinalIgnoreCase))
			{
				return MouseButton.Left;
			}

			if (string.Equals(button, "right", StringComparison.OrdinalIgnoreCase))
			{
				return MouseButton.Right;
			}

			throw new ArgumentException("Mouse button must be 'left' or 'right'.", nameof(button));
		}

		public static VirtualKey ParseVirtualKey(string key)
		{
			if (string.IsNullOrWhiteSpace(key))
			{
				throw new ArgumentException("Key cannot be empty.", nameof(key));
			}

			var normalized = key.Trim();

			if (KeyAliases.TryGetValue(normalized, out var aliasedKey))
			{
				return aliasedKey;
			}

			if (normalized.Length == 1)
			{
				var ch = normalized[0];
				if (char.IsLetter(ch))
				{
					return Enum.Parse<VirtualKey>(char.ToUpperInvariant(ch).ToString());
				}

				if (char.IsDigit(ch))
				{
					return Enum.Parse<VirtualKey>("D" + ch);
				}
			}

			if (Enum.TryParse<VirtualKey>(normalized, ignoreCase: true, out var parsedKey))
			{
				return parsedKey;
			}

			throw new ArgumentException($"Unsupported key '{key}'.", nameof(key));
		}

		public static WindowTitleMatchMode ToWindowTitleMatchMode(bool exactTitle)
		{
			return exactTitle
				? WindowTitleMatchMode.ExactIgnoreCase
				: WindowTitleMatchMode.ContainsIgnoreCase;
		}

		public static ProcessNameMatchMode ToProcessNameMatchMode(bool containsName)
		{
			return containsName
				? ProcessNameMatchMode.ContainsIgnoreCase
				: ProcessNameMatchMode.ExactIgnoreCase;
		}

		public static TimeSpan ToTimeout(int milliseconds, string parameterName)
		{
			if (milliseconds < 0)
			{
				throw new ArgumentOutOfRangeException(parameterName, milliseconds, "Milliseconds cannot be negative.");
			}

			return TimeSpan.FromMilliseconds(milliseconds);
		}

		public static TimeSpan ToPollInterval(int milliseconds, string parameterName)
		{
			if (milliseconds <= 0)
			{
				throw new ArgumentOutOfRangeException(parameterName, milliseconds, "Poll interval must be positive.");
			}

			return TimeSpan.FromMilliseconds(milliseconds);
		}

		public static ScreenRectangle? ToOptionalRectangle(int? x, int? y, int? width, int? height)
		{
			if (x is null && y is null && width is null && height is null)
			{
				return null;
			}

			if (x is null || y is null || width is null || height is null)
			{
				throw new ArgumentException("x, y, width, and height must all be provided when capturing a region.");
			}

			return new ScreenRectangle(x.Value, y.Value, width.Value, height.Value);
		}
	}
}
