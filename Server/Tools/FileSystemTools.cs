using ModelContextProtocol.Server;
using Microsoft.VisualBasic.FileIO;
using System.ComponentModel;
using System.Text;

namespace Server.Tools
{
	public sealed record FileTextInfo(
		string Path,
		bool Exists,
		string? Text,
		string Encoding,
		long? BytesRead,
		bool Truncated);

	public sealed record FileBase64Info(
		string Path,
		bool Exists,
		string? Base64,
		long? BytesRead,
		bool Truncated);

	public sealed record FileWriteInfo(
		string Path,
		long BytesWritten,
		bool Appended,
		bool CreatedDirectory);

	public sealed record DirectoryEntryInfo(
		string Name,
		string Path,
		string Type,
		long? Length,
		DateTimeOffset CreatedAt,
		DateTimeOffset LastWriteTime);

	public sealed record FileSystemItemInfo(
		string Path,
		bool Exists,
		string? Type,
		long? Length,
		DateTimeOffset? CreatedAt,
		DateTimeOffset? LastWriteTime);

	public sealed record FileSystemMutationInfo(
		string Operation,
		string? SourcePath,
		string? DestinationPath,
		string? Type,
		bool Succeeded,
		bool Recycled,
		bool Overwrote,
		string Message);

	public sealed class FileSystemTools
	{
		[McpServerTool]
		[Description("Reads a text file and returns its content, optionally truncating after maxBytes.")]
		public FileTextInfo ReadTextFile(
			[Description("File path to read.")] string path,
			[Description("Text encoding name: utf-8, utf-16, unicode, ascii, or a .NET encoding name.")] string encoding = "utf-8",
			[Description("Maximum bytes to read. Use 0 for no limit.")] int maxBytes = 1048576)
		{
			var fullPath = RequirePath(path);
			if (!File.Exists(fullPath))
			{
				return new FileTextInfo(fullPath, false, null, encoding, null, false);
			}

			var bytes = ReadBytes(fullPath, maxBytes, out var truncated);
			var resolvedEncoding = ResolveEncoding(encoding);

			return new FileTextInfo(
				fullPath,
				true,
				DecodeText(bytes, resolvedEncoding),
				resolvedEncoding.WebName,
				bytes.Length,
				truncated);
		}

		[McpServerTool]
		[Description("Writes text to a file, creating parent directories by default.")]
		public FileWriteInfo WriteTextFile(
			[Description("File path to write.")] string path,
			[Description("Text content to write.")] string text,
			[Description("Append instead of overwriting.")] bool append = false,
			[Description("Text encoding name: utf-8, utf-16, unicode, ascii, or a .NET encoding name.")] string encoding = "utf-8",
			[Description("Create parent directories when they do not exist.")] bool createDirectories = true)
		{
			ArgumentNullException.ThrowIfNull(text);

			var fullPath = RequirePath(path);
			var createdDirectory = EnsureParentDirectory(fullPath, createDirectories);
			var resolvedEncoding = ResolveEncoding(encoding);

			if (append)
			{
				File.AppendAllText(fullPath, text, resolvedEncoding);
			}
			else
			{
				File.WriteAllText(fullPath, text, resolvedEncoding);
			}

			return new FileWriteInfo(
				fullPath,
				resolvedEncoding.GetByteCount(text),
				append,
				createdDirectory);
		}

		[McpServerTool]
		[Description("Reads a file as base64, optionally truncating after maxBytes.")]
		public FileBase64Info ReadFileBase64(
			[Description("File path to read.")] string path,
			[Description("Maximum bytes to read. Use 0 for no limit.")] int maxBytes = 1048576)
		{
			var fullPath = RequirePath(path);
			if (!File.Exists(fullPath))
			{
				return new FileBase64Info(fullPath, false, null, null, false);
			}

			var bytes = ReadBytes(fullPath, maxBytes, out var truncated);
			return new FileBase64Info(fullPath, true, Convert.ToBase64String(bytes), bytes.Length, truncated);
		}

		[McpServerTool]
		[Description("Writes base64 content to a file, creating parent directories by default.")]
		public FileWriteInfo WriteFileBase64(
			[Description("File path to write.")] string path,
			[Description("Base64 content to write.")] string base64,
			[Description("Append instead of overwriting.")] bool append = false,
			[Description("Create parent directories when they do not exist.")] bool createDirectories = true)
		{
			if (string.IsNullOrWhiteSpace(base64))
			{
				throw new ArgumentException("Base64 content cannot be empty.", nameof(base64));
			}

			var fullPath = RequirePath(path);
			var createdDirectory = EnsureParentDirectory(fullPath, createDirectories);
			var bytes = Convert.FromBase64String(base64);

			if (append)
			{
				using var stream = new FileStream(fullPath, FileMode.Append, FileAccess.Write, FileShare.Read);
				stream.Write(bytes);
			}
			else
			{
				File.WriteAllBytes(fullPath, bytes);
			}

			return new FileWriteInfo(fullPath, bytes.Length, append, createdDirectory);
		}

		[McpServerTool]
		[Description("Lists files and directories in a directory.")]
		public IReadOnlyList<DirectoryEntryInfo> ListDirectory(
			[Description("Directory path to list.")] string path,
			[Description("Search pattern such as * or *.txt.")] string searchPattern = "*",
			[Description("Recurse into subdirectories.")] bool recursive = false,
			[Description("Include hidden entries.")] bool includeHidden = true,
			[Description("Maximum number of entries to return.")] int maxEntries = 500)
		{
			var fullPath = RequirePath(path);
			if (!Directory.Exists(fullPath))
			{
				throw new DirectoryNotFoundException($"Directory '{fullPath}' does not exist.");
			}

			if (string.IsNullOrWhiteSpace(searchPattern))
			{
				throw new ArgumentException("Search pattern cannot be empty.", nameof(searchPattern));
			}

			if (maxEntries <= 0)
			{
				throw new ArgumentOutOfRangeException(nameof(maxEntries), maxEntries, "Maximum entries must be positive.");
			}

			var options = new EnumerationOptions
			{
				RecurseSubdirectories = recursive,
				IgnoreInaccessible = true,
				AttributesToSkip = includeHidden ? 0 : FileAttributes.Hidden
			};

			return Directory
				.EnumerateFileSystemEntries(fullPath, searchPattern, options)
				.Take(maxEntries)
				.Select(CreateDirectoryEntryInfo)
				.ToArray();
		}

		[McpServerTool]
		[Description("Gets metadata for a file or directory path.")]
		public FileSystemItemInfo GetFileSystemInfo(
			[Description("File or directory path to inspect.")] string path)
		{
			var fullPath = RequirePath(path);

			if (File.Exists(fullPath))
			{
				var file = new FileInfo(fullPath);
				return new FileSystemItemInfo(
					fullPath,
					true,
					"file",
					file.Length,
					file.CreationTimeUtc,
					file.LastWriteTimeUtc);
			}

			if (Directory.Exists(fullPath))
			{
				var directory = new DirectoryInfo(fullPath);
				return new FileSystemItemInfo(
					fullPath,
					true,
					"directory",
					null,
					directory.CreationTimeUtc,
					directory.LastWriteTimeUtc);
			}

			return new FileSystemItemInfo(fullPath, false, null, null, null, null);
		}

		[McpServerTool]
		[Description("Creates a directory and any missing parent directories.")]
		public FileSystemMutationInfo CreateDirectory(
			[Description("Directory path to create.")] string path)
		{
			var fullPath = RequirePath(path);
			var alreadyExisted = Directory.Exists(fullPath);
			Directory.CreateDirectory(fullPath);

			return new FileSystemMutationInfo(
				"create_directory",
				null,
				fullPath,
				"directory",
				true,
				false,
				false,
				alreadyExisted ? "Directory already existed." : "Directory created.");
		}

		[McpServerTool]
		[Description("Permanently deletes a file. Prefer recycle_file when recoverability is desired.")]
		public FileSystemMutationInfo DeleteFile(
			[Description("File path to delete.")] string path,
			[Description("When true, return success if the file is already missing; when false, missing files throw.")] bool missingOk = false)
		{
			var fullPath = RequirePath(path);
			if (!File.Exists(fullPath))
			{
				return MissingPathResult("delete_file", fullPath, "file", missingOk);
			}

			File.Delete(fullPath);
			return new FileSystemMutationInfo(
				"delete_file",
				fullPath,
				null,
				"file",
				true,
				false,
				false,
				"File deleted.");
		}

		[McpServerTool]
		[Description("Sends a file to the Windows Recycle Bin instead of permanently deleting it. Use this for safer cleanup when the user may want recovery.")]
		public FileSystemMutationInfo RecycleFile(
			[Description("File path to recycle.")] string path,
			[Description("When true, return success if the file is already missing; when false, missing files throw.")] bool missingOk = false)
		{
			var fullPath = RequirePath(path);
			if (!File.Exists(fullPath))
			{
				return MissingPathResult("recycle_file", fullPath, "file", missingOk);
			}

			FileSystem.DeleteFile(fullPath, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
			return new FileSystemMutationInfo(
				"recycle_file",
				fullPath,
				null,
				"file",
				true,
				true,
				false,
				"File sent to Recycle Bin.");
		}

		[McpServerTool]
		[Description("Permanently deletes a directory. Set recursive=true to delete non-empty directories. Prefer recycle_directory when recoverability is desired.")]
		public FileSystemMutationInfo DeleteDirectory(
			[Description("Directory path to delete.")] string path,
			[Description("Delete contents recursively.")] bool recursive = false,
			[Description("When true, return success if the directory is already missing; when false, missing directories throw.")] bool missingOk = false)
		{
			var fullPath = RequirePath(path);
			if (!Directory.Exists(fullPath))
			{
				return MissingPathResult("delete_directory", fullPath, "directory", missingOk);
			}

			Directory.Delete(fullPath, recursive);
			return new FileSystemMutationInfo(
				"delete_directory",
				fullPath,
				null,
				"directory",
				true,
				false,
				false,
				"Directory deleted.");
		}

		[McpServerTool]
		[Description("Sends a directory and its contents to the Windows Recycle Bin instead of permanently deleting them. Use this for safer cleanup when the user may want recovery.")]
		public FileSystemMutationInfo RecycleDirectory(
			[Description("Directory path to recycle.")] string path,
			[Description("When true, return success if the directory is already missing; when false, missing directories throw.")] bool missingOk = false)
		{
			var fullPath = RequirePath(path);
			if (!Directory.Exists(fullPath))
			{
				return MissingPathResult("recycle_directory", fullPath, "directory", missingOk);
			}

			FileSystem.DeleteDirectory(fullPath, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
			return new FileSystemMutationInfo(
				"recycle_directory",
				fullPath,
				null,
				"directory",
				true,
				true,
				false,
				"Directory sent to Recycle Bin.");
		}

		[McpServerTool]
		[Description("Moves or renames a file to an exact destination path. This removes the source after the move succeeds.")]
		public FileSystemMutationInfo MoveFile(
			[Description("Source file path.")] string sourcePath,
			[Description("Destination file path, including the final filename.")] string destinationPath,
			[Description("Overwrite destination when it already exists.")] bool overwrite = false,
			[Description("Create destination parent directories when they do not exist.")] bool createDirectories = true)
		{
			var source = RequirePath(sourcePath);
			var destination = RequirePath(destinationPath);
			RequireExistingFile(source);
			var overwritten = PrepareDestinationFile(destination, overwrite, createDirectories);

			File.Move(source, destination, overwrite);
			return new FileSystemMutationInfo(
				"move_file",
				source,
				destination,
				"file",
				true,
				false,
				overwritten,
				"File moved.");
		}

		[McpServerTool]
		[Description("Moves or renames a directory to an exact destination path. This removes the source after the move succeeds and refuses destinations inside the source tree.")]
		public FileSystemMutationInfo MoveDirectory(
			[Description("Source directory path.")] string sourcePath,
			[Description("Destination directory path, including the final directory name.")] string destinationPath,
			[Description("Overwrite destination directory when it already exists.")] bool overwrite = false,
			[Description("Create destination parent directories when they do not exist.")] bool createDirectories = true)
		{
			var source = RequirePath(sourcePath);
			var destination = RequirePath(destinationPath);
			RequireExistingDirectory(source);
			EnsureDestinationIsNotInsideSource(source, destination);
			var overwritten = PrepareDestinationDirectory(destination, overwrite, createDirectories);

			Directory.Move(source, destination);
			return new FileSystemMutationInfo(
				"move_directory",
				source,
				destination,
				"directory",
				true,
				false,
				overwritten,
				"Directory moved.");
		}

		[McpServerTool]
		[Description("Copies a file to an exact destination path while leaving the source in place.")]
		public FileSystemMutationInfo CopyFile(
			[Description("Source file path.")] string sourcePath,
			[Description("Destination file path, including the final filename.")] string destinationPath,
			[Description("Overwrite destination when it already exists.")] bool overwrite = false,
			[Description("Create destination parent directories when they do not exist.")] bool createDirectories = true)
		{
			var source = RequirePath(sourcePath);
			var destination = RequirePath(destinationPath);
			RequireExistingFile(source);
			var overwritten = PrepareDestinationFile(destination, overwrite, createDirectories);

			File.Copy(source, destination, overwrite);
			return new FileSystemMutationInfo(
				"copy_file",
				source,
				destination,
				"file",
				true,
				false,
				overwritten,
				"File copied.");
		}

		[McpServerTool]
		[Description("Copies a directory and all of its contents to an exact destination path while leaving the source in place. Refuses destinations inside the source tree.")]
		public FileSystemMutationInfo CopyDirectory(
			[Description("Source directory path.")] string sourcePath,
			[Description("Destination directory path, including the final directory name.")] string destinationPath,
			[Description("Overwrite destination files when they already exist. Does not delete extra files already present in the destination directory.")] bool overwrite = false,
			[Description("Create destination parent directories when they do not exist.")] bool createDirectories = true)
		{
			var source = RequirePath(sourcePath);
			var destination = RequirePath(destinationPath);
			RequireExistingDirectory(source);
			var overwritten = Directory.Exists(destination);
			if (!overwrite && overwritten)
			{
				throw new IOException($"Destination directory '{destination}' already exists.");
			}

			EnsureParentDirectory(destination, createDirectories);
			CopyDirectoryRecursive(source, destination, overwrite);

			return new FileSystemMutationInfo(
				"copy_directory",
				source,
				destination,
				"directory",
				true,
				false,
				overwritten,
				"Directory copied.");
		}

		[McpServerTool]
		[Description("Removes all contents inside a directory while leaving the directory itself in place. Set recycle=true for safer cleanup.")]
		public FileSystemMutationInfo EmptyDirectory(
			[Description("Directory path to empty.")] string path,
			[Description("Send contents to Recycle Bin instead of permanently deleting them.")] bool recycle = false,
			[Description("When true, return success if the directory is already missing; when false, missing directories throw.")] bool missingOk = false)
		{
			var fullPath = RequirePath(path);
			if (!Directory.Exists(fullPath))
			{
				return MissingPathResult("empty_directory", fullPath, "directory", missingOk);
			}

			foreach (var file in Directory.EnumerateFiles(fullPath))
			{
				if (recycle)
				{
					FileSystem.DeleteFile(file, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
				}
				else
				{
					File.Delete(file);
				}
			}

			foreach (var directory in Directory.EnumerateDirectories(fullPath))
			{
				if (recycle)
				{
					FileSystem.DeleteDirectory(directory, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
				}
				else
				{
					Directory.Delete(directory, recursive: true);
				}
			}

			return new FileSystemMutationInfo(
				"empty_directory",
				fullPath,
				fullPath,
				"directory",
				true,
				recycle,
				false,
				recycle ? "Directory contents sent to Recycle Bin." : "Directory contents deleted.");
		}

		[McpServerTool]
		[Description("Copies or moves a file system item into a destination directory, like paste. By default it copies and preserves the source name; set move=true to cut/paste, and set newName to rename at the destination.")]
		public FileSystemMutationInfo PasteFileSystemItem(
			[Description("Source file or directory path.")] string sourcePath,
			[Description("Destination directory path.")] string destinationDirectory,
			[Description("When false, copy/paste and leave the source in place. When true, move/cut-paste and remove the source after success.")] bool move = false,
			[Description("Overwrite the destination item when it already exists.")] bool overwrite = false,
			[Description("Optional new file or directory name at the destination. Must be a name only, not a path.")] string? newName = null,
			[Description("Create destination directory when it does not exist.")] bool createDirectories = true)
		{
			var source = RequirePath(sourcePath);
			var destinationRoot = RequirePath(destinationDirectory);
			EnsureDirectory(destinationRoot, createDirectories);

			var itemName = GetSafeItemName(source, newName);
			var destination = Path.GetFullPath(Path.Combine(destinationRoot, itemName));

			if (File.Exists(source))
			{
				return move
					? MoveFile(source, destination, overwrite, createDirectories: true) with { Operation = "paste_move_file" }
					: CopyFile(source, destination, overwrite, createDirectories: true) with { Operation = "paste_copy_file" };
			}

			if (Directory.Exists(source))
			{
				return move
					? MoveDirectory(source, destination, overwrite, createDirectories: true) with { Operation = "paste_move_directory" }
					: CopyDirectory(source, destination, overwrite, createDirectories: true) with { Operation = "paste_copy_directory" };
			}

			throw new FileNotFoundException($"Source path '{source}' does not exist.", source);
		}

		private static DirectoryEntryInfo CreateDirectoryEntryInfo(string path)
		{
			var attributes = File.GetAttributes(path);
			if ((attributes & FileAttributes.Directory) == FileAttributes.Directory)
			{
				var directory = new DirectoryInfo(path);
				return new DirectoryEntryInfo(
					directory.Name,
					directory.FullName,
					"directory",
					null,
					directory.CreationTimeUtc,
					directory.LastWriteTimeUtc);
			}

			var file = new FileInfo(path);
			return new DirectoryEntryInfo(
				file.Name,
				file.FullName,
				"file",
				file.Length,
				file.CreationTimeUtc,
				file.LastWriteTimeUtc);
		}

		private static FileSystemMutationInfo MissingPathResult(
			string operation,
			string fullPath,
			string type,
			bool missingOk)
		{
			if (!missingOk)
			{
				if (type == "directory")
				{
					throw new DirectoryNotFoundException($"Directory '{fullPath}' does not exist.");
				}

				throw new FileNotFoundException($"File '{fullPath}' does not exist.", fullPath);
			}

			return new FileSystemMutationInfo(
				operation,
				fullPath,
				null,
				type,
				true,
				false,
				false,
				$"Missing {type} ignored.");
		}

		private static void RequireExistingFile(string fullPath)
		{
			if (!File.Exists(fullPath))
			{
				throw new FileNotFoundException($"File '{fullPath}' does not exist.", fullPath);
			}
		}

		private static void RequireExistingDirectory(string fullPath)
		{
			if (!Directory.Exists(fullPath))
			{
				throw new DirectoryNotFoundException($"Directory '{fullPath}' does not exist.");
			}
		}

		private static bool PrepareDestinationFile(
			string destination,
			bool overwrite,
			bool createDirectories)
		{
			EnsureParentDirectory(destination, createDirectories);

			if (Directory.Exists(destination))
			{
				throw new IOException($"Destination '{destination}' is a directory.");
			}

			if (!File.Exists(destination))
			{
				return false;
			}

			if (!overwrite)
			{
				throw new IOException($"Destination file '{destination}' already exists.");
			}

			return true;
		}

		private static bool PrepareDestinationDirectory(
			string destination,
			bool overwrite,
			bool createDirectories)
		{
			EnsureParentDirectory(destination, createDirectories);

			if (File.Exists(destination))
			{
				throw new IOException($"Destination '{destination}' is a file.");
			}

			if (!Directory.Exists(destination))
			{
				return false;
			}

			if (!overwrite)
			{
				throw new IOException($"Destination directory '{destination}' already exists.");
			}

			Directory.Delete(destination, recursive: true);
			return true;
		}

		private static void EnsureDirectory(
			string directory,
			bool createDirectories)
		{
			if (Directory.Exists(directory))
			{
				return;
			}

			if (File.Exists(directory))
			{
				throw new IOException($"Path '{directory}' is a file.");
			}

			if (!createDirectories)
			{
				throw new DirectoryNotFoundException($"Directory '{directory}' does not exist.");
			}

			Directory.CreateDirectory(directory);
		}

		private static void CopyDirectoryRecursive(
			string source,
			string destination,
			bool overwrite)
		{
			EnsureDestinationIsNotInsideSource(source, destination);

			Directory.CreateDirectory(destination);

			foreach (var file in Directory.EnumerateFiles(source))
			{
				var targetFile = Path.Combine(destination, Path.GetFileName(file));
				File.Copy(file, targetFile, overwrite);
			}

			foreach (var directory in Directory.EnumerateDirectories(source))
			{
				var targetDirectory = Path.Combine(destination, Path.GetFileName(directory));
				CopyDirectoryRecursive(directory, targetDirectory, overwrite);
			}
		}

		private static string EnsureTrailingSeparator(string path)
		{
			return path.EndsWith(Path.DirectorySeparatorChar) || path.EndsWith(Path.AltDirectorySeparatorChar)
				? path
				: path + Path.DirectorySeparatorChar;
		}

		private static void EnsureDestinationIsNotInsideSource(
			string source,
			string destination)
		{
			var normalizedSource = EnsureTrailingSeparator(Path.GetFullPath(source));
			var normalizedDestination = EnsureTrailingSeparator(Path.GetFullPath(destination));
			if (normalizedDestination.StartsWith(normalizedSource, StringComparison.OrdinalIgnoreCase))
			{
				throw new IOException("Destination directory cannot be inside the source directory.");
			}
		}

		private static string GetSafeItemName(string source, string? newName)
		{
			var itemName = string.IsNullOrWhiteSpace(newName)
				? Path.GetFileName(source.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
				: newName.Trim();

			if (string.IsNullOrWhiteSpace(itemName))
			{
				throw new ArgumentException("Could not determine destination item name.", nameof(source));
			}

			if (itemName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
				itemName.Contains(Path.DirectorySeparatorChar) ||
				itemName.Contains(Path.AltDirectorySeparatorChar))
			{
				throw new ArgumentException("New name must be a file or directory name, not a path.", nameof(newName));
			}

			return itemName;
		}

		private static byte[] ReadBytes(
			string fullPath,
			int maxBytes,
			out bool truncated)
		{
			if (maxBytes < 0)
			{
				throw new ArgumentOutOfRangeException(nameof(maxBytes), maxBytes, "Maximum bytes cannot be negative.");
			}

			var fileInfo = new FileInfo(fullPath);
			if (maxBytes == 0 || fileInfo.Length <= maxBytes)
			{
				truncated = false;
				return File.ReadAllBytes(fullPath);
			}

			var bytes = new byte[maxBytes];
			using var stream = File.OpenRead(fullPath);
			var read = stream.Read(bytes, 0, maxBytes);
			truncated = true;

			return read == bytes.Length ? bytes : bytes[..read];
		}

		private static bool EnsureParentDirectory(
			string fullPath,
			bool createDirectories)
		{
			var directory = Path.GetDirectoryName(fullPath);
			if (string.IsNullOrWhiteSpace(directory) || Directory.Exists(directory))
			{
				return false;
			}

			if (!createDirectories)
			{
				throw new DirectoryNotFoundException($"Directory '{directory}' does not exist.");
			}

			Directory.CreateDirectory(directory);
			return true;
		}

		private static string RequirePath(string path)
		{
			if (string.IsNullOrWhiteSpace(path))
			{
				throw new ArgumentException("Path cannot be empty.", nameof(path));
			}

			return Path.GetFullPath(path);
		}

		private static Encoding ResolveEncoding(string encoding)
		{
			if (string.IsNullOrWhiteSpace(encoding))
			{
				return Encoding.UTF8;
			}

			return encoding.Trim().ToLowerInvariant() switch
			{
				"utf8" or "utf-8" => new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
				"utf16" or "utf-16" or "unicode" => Encoding.Unicode,
				"ascii" => Encoding.ASCII,
				_ => Encoding.GetEncoding(encoding)
			};
		}

		private static string DecodeText(byte[] bytes, Encoding encoding)
		{
			using var stream = new MemoryStream(bytes);
			using var reader = new StreamReader(stream, encoding, detectEncodingFromByteOrderMarks: true);
			return reader.ReadToEnd();
		}
	}
}
