using System;
using System.Diagnostics;
using System.IO;
using Nexus.Transactions;

namespace Nexus.Client.ModManagement.Scripting
{
	/// <summary>Restores installer replay artifacts alongside the native file and registration rollback.</summary>
	internal sealed class ScriptedReplayTransactionSnapshot : IEnlistmentNotification
	{
		private readonly string _cachePath;
		private readonly string _backupDirectory;
		private readonly string _backupCache;

		/// <summary>Captures the current replay and payloads before the installer replaces them.</summary>
		internal ScriptedReplayTransactionSnapshot(string cachePath)
		{
			if (Transaction.Current == null) throw new InvalidOperationException("Replay snapshots require a native transaction.");
			_cachePath = cachePath;
			_backupDirectory = Path.Combine(Path.GetTempPath(), "NMM-Replay-" + Guid.NewGuid().ToString("N"));
			_backupCache = Path.Combine(_backupDirectory, "replay.xml");
			try
			{
				CopyArtifacts(_cachePath, _backupCache);
				Transaction.Current.EnlistVolatile(this, EnlistmentOptions.None);
			}
			catch { CleanUp(); throw; }
		}

		/// <inheritdoc />
		public void Prepare(PreparingEnlistment enlistment) => enlistment.Prepared();

		/// <inheritdoc />
		public void Commit(Enlistment enlistment)
		{
			CleanUp();
			enlistment.Done();
		}

		/// <inheritdoc />
		public void Rollback(Enlistment enlistment)
		{
			ScriptedFileSelectionCache.DeleteArtifacts(_cachePath);
			CopyArtifacts(_backupCache, _cachePath);
			CleanUp();
			enlistment.Done();
		}

		/// <inheritdoc />
		public void InDoubt(Enlistment enlistment) { }

		/// <summary>Copies replay artifacts, including a payload-only or empty sidecar directory.</summary>
		private static void CopyArtifacts(string source, string destination)
		{
			if (File.Exists(source))
			{
				Directory.CreateDirectory(Path.GetDirectoryName(destination));
				File.Copy(source, destination, true);
			}
			string payloads = ScriptedFileSelectionCache.GetPayloadDirectoryPath(source);
			if (!Directory.Exists(payloads)) return;
			string target = ScriptedFileSelectionCache.GetPayloadDirectoryPath(destination);
			Directory.CreateDirectory(target);
			foreach (string directory in Directory.GetDirectories(payloads, "*", SearchOption.AllDirectories))
				Directory.CreateDirectory(Path.Combine(target, directory.Substring(payloads.Length + 1)));
			foreach (string file in Directory.GetFiles(payloads, "*", SearchOption.AllDirectories))
				File.Copy(file, Path.Combine(target, file.Substring(payloads.Length + 1)), true);
		}

		/// <summary>Removes temporary snapshots without turning successful finalization into a failed installation.</summary>
		private void CleanUp()
		{
			try { if (Directory.Exists(_backupDirectory)) Directory.Delete(_backupDirectory, true); }
			catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
			{ Trace.TraceWarning("Could not remove installer replay snapshot '{0}': {1}", _backupDirectory, exception.Message); }
		}
	}
}
