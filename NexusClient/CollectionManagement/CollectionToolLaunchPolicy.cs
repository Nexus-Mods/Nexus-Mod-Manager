using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>
	/// Resolves the characterized Collection-tool subset against one active game root without executing anything.
	/// </summary>
	internal static class CollectionToolLaunchPolicy
	{
		internal static ProcessStartInfo CreateStartInfo(CollectionLaunchTool tool, string gameRoot)
		{
			if (tool == null)
				throw new ArgumentNullException(nameof(tool));

			string executable = ResolvePath(gameRoot, tool.RelativeExecutablePath);
			string workingDirectory = String.IsNullOrEmpty(tool.RelativeWorkingDirectory)
				? Path.GetDirectoryName(executable)
				: ResolvePath(gameRoot, tool.RelativeWorkingDirectory);
			var startInfo = new ProcessStartInfo
			{
				FileName = executable,
				Arguments = BuildArgumentString(tool.Arguments),
				WorkingDirectory = workingDirectory,
				UseShellExecute = false
			};
			foreach (KeyValuePair<string, string> variable in tool.Environment)
				startInfo.EnvironmentVariables[variable.Key] = variable.Value;
			return startInfo;
		}

		internal static string ResolvePath(string gameRoot, string relativePath)
		{
			if (String.IsNullOrWhiteSpace(gameRoot) || !Path.IsPathRooted(gameRoot))
				throw new ArgumentException("A rooted active game path is required.", nameof(gameRoot));
			if (String.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
				throw new ArgumentException("A relative Collection tool path is required.", nameof(relativePath));

			string root = Path.GetFullPath(gameRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
			string candidate = Path.GetFullPath(Path.Combine(root, relativePath));
			if (!candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase))
				throw new InvalidDataException("A Collection tool path escaped the active game root.");
			return candidate;
		}

		internal static string BuildArgumentString(IEnumerable<string> arguments)
		{
			return String.Join(" ", (arguments ?? Enumerable.Empty<string>()).Select(QuoteArgument).ToArray());
		}

		private static string QuoteArgument(string argument)
		{
			if (String.IsNullOrEmpty(argument))
				return "\"\"";
			if (argument.IndexOfAny(new[] { ' ', '\t', '\n', '\r', '"' }) < 0)
				return argument;

			var builder = new StringBuilder();
			builder.Append('"');
			int backslashes = 0;
			foreach (char current in argument)
			{
				if (current == '\\')
				{
					backslashes++;
					continue;
				}
				if (current == '"')
				{
					builder.Append('\\', backslashes * 2 + 1);
					builder.Append(current);
				}
				else
				{
					builder.Append('\\', backslashes);
					builder.Append(current);
				}
				backslashes = 0;
			}
			builder.Append('\\', backslashes * 2);
			builder.Append('"');
			return builder.ToString();
		}
	}
}
