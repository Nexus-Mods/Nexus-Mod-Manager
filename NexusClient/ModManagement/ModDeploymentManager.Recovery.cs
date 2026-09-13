namespace Nexus.Client.ModManagement
{
	using System;
	using System.Collections.Generic;
	using System.Diagnostics;
	using System.IO;
	using System.Linq;
	using System.Text;
	using System.Xml.Linq;

	using Nexus.Transactions;

	/// <summary>
	/// Crash-recovery support for filesystem mutations performed on Direct/promoted deployment targets.
	/// </summary>
	public sealed partial class ModDeploymentManager
	{
		private const string DeploymentRecoveryFolderName = "_recovery";
		private const string DeploymentRecoveryHeaderName = "transaction.xml";
		private const string DeploymentRecoveryRecordsName = "targets.bin";
		private const string DeploymentRecoverySnapshotsFolderName = "snapshots";
		private const int MaximumRecoveryRecordLength = 16 * 1024 * 1024;

		private readonly object m_objDeploymentRecoveryLock = new object();
		private Dictionary<string, DeploymentRecoveryTransactionEnlistment> m_dicDeploymentRecoveryEnlistments;

		/// <summary>
		/// Durably captures one target before the first Direct/promoted mutation in the ambient transaction.
		/// </summary>
		private void TouchDeploymentRecoveryTarget(ModDeploymentTarget p_mdtTarget)
		{
			if (p_mdtTarget == null)
				throw new ArgumentNullException(nameof(p_mdtTarget));

			GetDeploymentRecoveryEnlistment().Touch(p_mdtTarget);
		}

		private DeploymentRecoveryTransactionEnlistment GetDeploymentRecoveryEnlistment()
		{
			Transaction transaction = Transaction.Current;
			if (transaction == null)
				throw new InvalidOperationException("Deployment crash recovery requires an ambient transaction.");

			string transactionId = transaction.TransactionInformation.LocalIdentifier;
			lock (m_objDeploymentRecoveryLock)
			{
				if (m_dicDeploymentRecoveryEnlistments == null)
					m_dicDeploymentRecoveryEnlistments = new Dictionary<string, DeploymentRecoveryTransactionEnlistment>();

				DeploymentRecoveryTransactionEnlistment enlistment;
				if (!m_dicDeploymentRecoveryEnlistments.TryGetValue(transactionId, out enlistment))
				{
					enlistment = new DeploymentRecoveryTransactionEnlistment(this, transaction, transactionId);
					m_dicDeploymentRecoveryEnlistments.Add(transactionId, enlistment);
				}
				return enlistment;
			}
		}

		private void ReleaseDeploymentRecoveryEnlistment(string p_strTransactionId)
		{
			lock (m_objDeploymentRecoveryLock)
			{
				if (m_dicDeploymentRecoveryEnlistments != null)
					m_dicDeploymentRecoveryEnlistments.Remove(p_strTransactionId);
			}
		}

		/// <summary>
		/// Reconciles journals left by a process termination before accepting new deployment work.
		/// </summary>
		private void RecoverPendingDeploymentTransactions()
		{
			string recoveryRoot = GetDeploymentRecoveryRoot();
			if (!Directory.Exists(recoveryRoot))
				return;

			foreach (string transactionDirectory in Directory.GetDirectories(recoveryRoot).OrderBy(x => Path.GetFileName(x), StringComparer.OrdinalIgnoreCase))
			{
				string headerPath = Path.Combine(transactionDirectory, DeploymentRecoveryHeaderName);
				if (!File.Exists(headerPath))
				{
					DeleteRecoveryDirectory(transactionDirectory);
					continue;
				}

				RecoverDeploymentTransactionDirectory(transactionDirectory);
			}
		}

		private void RecoverDeploymentTransactionDirectory(string p_strTransactionDirectory)
		{
			XDocument header = XDocument.Load(Path.Combine(p_strTransactionDirectory, DeploymentRecoveryHeaderName));
			XElement root = header.Root;
			if (root == null || !String.Equals(root.Name.LocalName, "deploymentRecovery", StringComparison.Ordinal))
				throw new InvalidDataException("Invalid deployment recovery journal header.");

			long preCommitSequence;
			if (!Int64.TryParse((string)root.Attribute("preCommitSequence"), out preCommitSequence) || preCommitSequence < 0)
				throw new InvalidDataException("Invalid deployment recovery commit sequence.");

			long currentSequence = m_ilgInstallLog.DeploymentCommitSequence;
			if (currentSequence < preCommitSequence)
				throw new InvalidDataException("InstallLog deployment commit sequence predates a pending deployment recovery journal.");

			if (currentSequence > preCommitSequence)
			{
				DeleteRecoveryDirectory(p_strTransactionDirectory);
				return;
			}

			foreach (XElement record in ReadRecoveryRecords(p_strTransactionDirectory))
				RestoreRecoveryTarget(p_strTransactionDirectory, record);

			DeleteRecoveryDirectory(p_strTransactionDirectory);
		}

		private void RestoreRecoveryTarget(string p_strTransactionDirectory, XElement p_xelRecord)
		{
			ModDeploymentRoot root;
			if (!Enum.TryParse((string)p_xelRecord.Attribute("root"), true, out root) || !Enum.IsDefined(typeof(ModDeploymentRoot), root))
				throw new InvalidDataException("Invalid deployment root in recovery target.");

			ModDeploymentTarget target = ModDeploymentTargetResolver.FromCanonical(root, (string)p_xelRecord.Attribute("path"));
			XElement backupStates = p_xelRecord.Element("backups");
			if (backupStates != null)
			{
				foreach (XElement backupState in backupStates.Elements("backup"))
				{
					string ownerKey = (string)backupState.Attribute("ownerKey");
					RestoreCapturedFileState(
						p_strTransactionDirectory,
						GetOwnerBackupPath(target, ownerKey),
						backupState);
				}
			}

			XElement legacyState = p_xelRecord.Element("legacyOverwrite");
			if (legacyState != null)
			{
				string legacyPath = (string)legacyState.Attribute("path");
				ValidateRecoveryPath(legacyPath, Path.Combine(m_vmaVirtualModActivator.VirtualPath, "_overwrites"), "legacy Virtual overwrite");
				RestoreCapturedFileState(p_strTransactionDirectory, legacyPath, legacyState);
			}

			string deploymentPath = GetDeploymentPath(target);
			string deploymentState = (string)p_xelRecord.Attribute("deploymentState") ?? "Absent";
			switch (deploymentState)
			{
				case "Absent":
					DeleteFileIfPresent(deploymentPath);
					break;
				case "Snapshot":
					RestoreSnapshotFile(p_strTransactionDirectory, (string)p_xelRecord.Attribute("deploymentSnapshot"), deploymentPath);
					break;
				case "Virtual":
					// Normal transaction rollback may already have restored the correct Virtual link.
					// The Virtual backend verifies that state before replacing anything.
					m_vmaVirtualModActivator.RecoverVirtualDeploymentWinner(target, (string)p_xelRecord.Attribute("virtualOwnerKey"));
					break;
				default:
					throw new InvalidDataException("Unknown deployment recovery state.");
			}
		}

		private IEnumerable<XElement> ReadRecoveryRecords(string p_strTransactionDirectory)
		{
			string recordsPath = Path.Combine(p_strTransactionDirectory, DeploymentRecoveryRecordsName);
			if (!File.Exists(recordsPath))
				yield break;

			using (var stream = new FileStream(recordsPath, FileMode.Open, FileAccess.Read, FileShare.Read))
			using (var reader = new BinaryReader(stream, Encoding.UTF8))
			{
				while (stream.Position < stream.Length)
				{
					if (stream.Length - stream.Position < sizeof(int))
						yield break;

					int length = reader.ReadInt32();
					if (length <= 0 || length > MaximumRecoveryRecordLength)
						throw new InvalidDataException("Invalid deployment recovery record length.");
					if (stream.Length - stream.Position < length)
						yield break;

					byte[] payload = reader.ReadBytes(length);
					if (payload.Length != length)
						yield break;
					yield return XElement.Parse(Encoding.UTF8.GetString(payload));
				}
			}
		}

		private XElement CaptureRecoveryTarget(string p_strTransactionDirectory, ModDeploymentTarget p_mdtTarget)
		{
			string[] owners = m_ilgInstallLog.GetDeploymentOwnerKeys(p_mdtTarget).ToArray();
			string[] virtualOwners = m_vmaVirtualModActivator.GetVirtualOwnerKeys(p_mdtTarget).ToArray();
			string deploymentPath = GetDeploymentPath(p_mdtTarget);

			var record = new XElement("target",
				new XAttribute("root", p_mdtTarget.Root),
				new XAttribute("path", p_mdtTarget.RelativePath),
				new XElement("owners", owners.Select(x => new XElement("owner", new XAttribute("key", x)))));

			string currentOwnerKey = owners.Length == 0 ? null : owners[owners.Length - 1];
			if (currentOwnerKey != null && !currentOwnerKey.Equals(m_ilgInstallLog.OriginalValuesKey, StringComparison.OrdinalIgnoreCase) &&
				m_ilgInstallLog.GetModInstallMethod(currentOwnerKey) == ModInstallMethod.Virtual)
			{
				record.Add(new XAttribute("deploymentState", "Virtual"), new XAttribute("virtualOwnerKey", currentOwnerKey));
			}
			else if (currentOwnerKey == null && virtualOwners.Length > 0)
			{
				record.Add(new XAttribute("deploymentState", "Virtual"), new XAttribute("virtualOwnerKey", virtualOwners[virtualOwners.Length - 1]));
			}
			else if (File.Exists(deploymentPath))
			{
				record.Add(new XAttribute("deploymentState", "Snapshot"),
					new XAttribute("deploymentSnapshot", CaptureSnapshotFile(p_strTransactionDirectory, deploymentPath)));
			}
			else
			{
				record.Add(new XAttribute("deploymentState", "Absent"));
			}

			var backupOwners = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { m_ilgInstallLog.OriginalValuesKey };
			foreach (string ownerKey in owners)
			{
				if (ownerKey.Equals(m_ilgInstallLog.OriginalValuesKey, StringComparison.OrdinalIgnoreCase) ||
					m_ilgInstallLog.GetModInstallMethod(ownerKey) == ModInstallMethod.Direct)
				{
					backupOwners.Add(ownerKey);
				}
			}

			var backups = new XElement("backups");
			foreach (string ownerKey in backupOwners)
				backups.Add(CaptureFileState(p_strTransactionDirectory, "backup", GetOwnerBackupPath(p_mdtTarget, ownerKey), new XAttribute("ownerKey", ownerKey)));
			record.Add(backups);

			string legacyOverwritePath = FindExistingVirtualOverwritePath(p_mdtTarget, virtualOwners);
			if (!String.IsNullOrWhiteSpace(legacyOverwritePath))
				record.Add(CaptureFileState(p_strTransactionDirectory, "legacyOverwrite", legacyOverwritePath, new XAttribute("path", legacyOverwritePath)));

			return record;
		}

		private XElement CaptureFileState(string p_strTransactionDirectory, string p_strElementName, string p_strPath, params XAttribute[] p_xatAttributes)
		{
			var element = new XElement(p_strElementName, p_xatAttributes);
			bool exists = File.Exists(p_strPath);
			element.Add(new XAttribute("existed", exists));
			if (exists)
				element.Add(new XAttribute("snapshot", CaptureSnapshotFile(p_strTransactionDirectory, p_strPath)));
			return element;
		}

		private string CaptureSnapshotFile(string p_strTransactionDirectory, string p_strSourcePath)
		{
			string snapshotsDirectory = Path.Combine(p_strTransactionDirectory, DeploymentRecoverySnapshotsFolderName);
			Directory.CreateDirectory(snapshotsDirectory);
			string snapshotName = Guid.NewGuid().ToString("N") + ".bin";
			string snapshotPath = Path.Combine(snapshotsDirectory, snapshotName);

			using (var source = new FileStream(p_strSourcePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
			using (var destination = new FileStream(snapshotPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
			{
				source.CopyTo(destination);
				destination.Flush(true);
			}
			return snapshotName;
		}

		private void RestoreCapturedFileState(string p_strTransactionDirectory, string p_strDestinationPath, XElement p_xelState)
		{
			bool existed = (bool?)p_xelState.Attribute("existed") ?? false;
			if (!existed)
			{
				DeleteFileIfPresent(p_strDestinationPath);
				return;
			}

			RestoreSnapshotFile(p_strTransactionDirectory, (string)p_xelState.Attribute("snapshot"), p_strDestinationPath);
		}

		private void RestoreSnapshotFile(string p_strTransactionDirectory, string p_strSnapshotName, string p_strDestinationPath)
		{
			if (String.IsNullOrWhiteSpace(p_strSnapshotName) || Path.GetFileName(p_strSnapshotName) != p_strSnapshotName)
				throw new InvalidDataException("Invalid deployment recovery snapshot name.");

			string snapshotPath = Path.Combine(p_strTransactionDirectory, DeploymentRecoverySnapshotsFolderName, p_strSnapshotName);
			if (!File.Exists(snapshotPath))
				throw new FileNotFoundException("A deployment recovery snapshot is missing.", snapshotPath);

			string destinationDirectory = Path.GetDirectoryName(p_strDestinationPath);
			if (!String.IsNullOrWhiteSpace(destinationDirectory) && !Directory.Exists(destinationDirectory))
				Directory.CreateDirectory(destinationDirectory);

			string temporaryPath = Path.Combine(
				destinationDirectory,
				".nmm-recover-" + Guid.NewGuid().ToString("N").Substring(0, 16) + ".tmp");
			try
			{
				using (var source = new FileStream(snapshotPath, FileMode.Open, FileAccess.Read, FileShare.Read))
				using (var destination = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
				{
					source.CopyTo(destination);
					destination.Flush(true);
				}

				DeleteFileIfPresent(p_strDestinationPath);
				File.Move(temporaryPath, p_strDestinationPath);
			}
			finally
			{
				if (File.Exists(temporaryPath))
					File.Delete(temporaryPath);
			}
		}

		private static void DeleteFileIfPresent(string p_strPath)
		{
			if (String.IsNullOrWhiteSpace(p_strPath))
				return;

			try
			{
				File.Delete(p_strPath);
			}
			catch (DirectoryNotFoundException)
			{
				// Restoring an absent file is already complete when its parent directory is absent.
			}
		}

		private static void ValidateRecoveryPath(string p_strPath, string p_strRoot, string p_strDescription)
		{
			if (String.IsNullOrWhiteSpace(p_strPath) || String.IsNullOrWhiteSpace(p_strRoot))
				throw new InvalidDataException("Invalid " + p_strDescription + " path in deployment recovery journal.");

			string root = Path.GetFullPath(p_strRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
			string path = Path.GetFullPath(p_strPath);
			if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
				throw new InvalidDataException("The " + p_strDescription + " path escapes its configured root.");
		}

		private string GetDeploymentRecoveryRoot()
		{
			string overwriteDirectory = m_gmdGameMode.GameModeEnvironmentInfo.OverwriteDirectory;
			if (String.IsNullOrWhiteSpace(overwriteDirectory))
				throw new InvalidOperationException("The overwrite backup directory is not configured for deployment recovery.");
			return Path.Combine(overwriteDirectory, "deployment", DeploymentRecoveryFolderName);
		}

		private static string GetSafeRecoveryTransactionName(string p_strTransactionId)
		{
			string value = String.IsNullOrWhiteSpace(p_strTransactionId) ? Guid.NewGuid().ToString("N") : p_strTransactionId;
			foreach (char invalid in Path.GetInvalidFileNameChars())
				value = value.Replace(invalid, '_');
			return DateTime.UtcNow.Ticks.ToString("D19") + "-" + value;
		}

		private static void WriteRecoveryHeaderDurably(string p_strPath, string p_strTransactionId, long p_lngPreCommitSequence)
		{
			string temporaryPath = p_strPath + ".tmp";
			var document = new XDocument(new XElement("deploymentRecovery",
				new XAttribute("transactionId", p_strTransactionId ?? String.Empty),
				new XAttribute("preCommitSequence", p_lngPreCommitSequence)));
			byte[] payload = Encoding.UTF8.GetBytes(document.ToString(SaveOptions.DisableFormatting));
			try
			{
				using (var stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
				{
					stream.Write(payload, 0, payload.Length);
					stream.Flush(true);
				}
				if (File.Exists(p_strPath))
					File.Delete(p_strPath);
				File.Move(temporaryPath, p_strPath);
			}
			finally
			{
				if (File.Exists(temporaryPath))
					File.Delete(temporaryPath);
			}
		}

		private static void AppendRecoveryRecordDurably(string p_strRecordsPath, XElement p_xelRecord)
		{
			byte[] payload = Encoding.UTF8.GetBytes(p_xelRecord.ToString(SaveOptions.DisableFormatting));
			if (payload.Length <= 0 || payload.Length > MaximumRecoveryRecordLength)
				throw new InvalidDataException("Deployment recovery record is too large.");

			using (var stream = new FileStream(p_strRecordsPath, FileMode.Append, FileAccess.Write, FileShare.Read))
			using (var writer = new BinaryWriter(stream, Encoding.UTF8))
			{
				writer.Write(payload.Length);
				writer.Write(payload);
				writer.Flush();
				stream.Flush(true);
			}
		}

		private static void DeleteRecoveryDirectory(string p_strDirectory)
		{
			if (Directory.Exists(p_strDirectory))
				Directory.Delete(p_strDirectory, true);
		}

		private sealed class DeploymentRecoveryTransactionEnlistment : IEnlistmentNotification
		{
			private readonly ModDeploymentManager m_mdmOwner;
			private readonly Transaction m_trnTransaction;
			private readonly string m_strTransactionId;
			private readonly string m_strTransactionDirectory;
			private readonly HashSet<ModDeploymentTarget> m_hstTouchedTargets = new HashSet<ModDeploymentTarget>();
			private readonly object m_objTouchLock = new object();

			public DeploymentRecoveryTransactionEnlistment(ModDeploymentManager p_mdmOwner, Transaction p_trnTransaction, string p_strTransactionId)
			{
				m_mdmOwner = p_mdmOwner;
				m_trnTransaction = p_trnTransaction;
				m_strTransactionId = p_strTransactionId;

				long preCommitSequence = m_mdmOwner.m_ilgInstallLog.DeploymentCommitSequence;
				string recoveryRoot = m_mdmOwner.GetDeploymentRecoveryRoot();
				Directory.CreateDirectory(recoveryRoot);
				m_strTransactionDirectory = Path.Combine(recoveryRoot, GetSafeRecoveryTransactionName(p_strTransactionId));
				Directory.CreateDirectory(m_strTransactionDirectory);
				WriteRecoveryHeaderDurably(Path.Combine(m_strTransactionDirectory, DeploymentRecoveryHeaderName), p_strTransactionId, preCommitSequence);

				long enlistedSequence = m_mdmOwner.m_ilgInstallLog.EnlistDeploymentRecoveryTransaction();
				if (enlistedSequence != preCommitSequence)
					throw new InvalidOperationException("InstallLog deployment commit sequence changed while creating the recovery journal.");

				m_trnTransaction.TransactionCompleted += TransactionCompleted;
				m_trnTransaction.EnlistVolatile(this, EnlistmentOptions.None);
			}

			public void Touch(ModDeploymentTarget p_mdtTarget)
			{
				lock (m_objTouchLock)
				{
					if (m_hstTouchedTargets.Contains(p_mdtTarget))
						return;

					XElement record = m_mdmOwner.CaptureRecoveryTarget(m_strTransactionDirectory, p_mdtTarget);
					AppendRecoveryRecordDurably(Path.Combine(m_strTransactionDirectory, DeploymentRecoveryRecordsName), record);
					m_hstTouchedTargets.Add(p_mdtTarget);
				}
			}

			public void Prepare(PreparingEnlistment p_prePreparingEnlistment)
			{
				p_prePreparingEnlistment.Prepared();
			}

			public void Commit(Enlistment p_enlEnlistment)
			{
				p_enlEnlistment.Done();
			}

			public void Rollback(Enlistment p_enlEnlistment)
			{
				// Normal resource-manager rollback must finish before durable deployment recovery runs.
				// The terminal TransactionCompleted callback executes after every participant has rolled back.
				p_enlEnlistment.Done();
			}

			public void InDoubt(Enlistment p_enlEnlistment)
			{
				p_enlEnlistment.Done();
			}

			private void TransactionCompleted(object p_objSender, EventArgs p_eaEventArgs)
			{
				m_trnTransaction.TransactionCompleted -= TransactionCompleted;
				try
				{
					if (m_trnTransaction.TransactionInformation.Status == TransactionStatus.Committed)
						DeleteRecoveryDirectory(m_strTransactionDirectory);
					else if (m_trnTransaction.TransactionInformation.Status == TransactionStatus.Aborted)
						m_mdmOwner.RecoverDeploymentTransactionDirectory(m_strTransactionDirectory);
				}
				catch (Exception ex)
				{
					Trace.TraceError("Unable to finalize deployment recovery journal '{0}': {1}", m_strTransactionDirectory, ex);
					if (m_trnTransaction.TransactionInformation.Status == TransactionStatus.Aborted)
					{
						throw new TransactionException(
							"Deployment rollback recovery failed. The recovery journal was retained at '" +
							m_strTransactionDirectory + "'.", ex);
					}
				}
				finally
				{
					m_mdmOwner.ReleaseDeploymentRecoveryEnlistment(m_strTransactionId);
				}
			}
		}
	}
}
