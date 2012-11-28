using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Nexus.Transactions;
using System.Text.RegularExpressions;
using System.Security.Permissions;
using System.Security;

namespace ChinhDo.Transactions
{
	public partial class TxFileManager
	{
		/// <summary>
		/// Provides two-phase commits/rollbacks/etc for a single <see cref="Transaction"/>.
		/// </summary>
		private class TxEnlistment : IEnlistmentNotification, IFileOperations
		{
			/// <summary>
			/// Initializes a new instance of the <see cref="TxEnlistment"/> class.
			/// </summary>
			public TxEnlistment()
				: this(null)
			{

			}

			/// <summary>
			/// Initializes a new instance of the <see cref="TxEnlistment"/> class.
			/// </summary>
			/// <param name="tx">The Transaction.</param>
			public TxEnlistment(Transaction tx)
			{
				_tx = tx;
				_journal = new List<RollbackOperation>();
			}

			/// <summary>
			/// Gets or sets a value indicating whether to ignore exceptions during Rollback.
			/// </summary>
			public bool IgnoreExceptionsInRollback
			{
				get { return _ignoreExceptionsInRollback; }
				set { _ignoreExceptionsInRollback = value; }
			}

			#region IFileOperations

			/// <summary>
			/// Appends the specified string the file, creating the file if it doesn't already exist.
			/// </summary>
			/// <param name="path">The file to append the string to.</param>
			/// <param name="contents">The string to append to the file.</param>
			public void AppendAllText(string path, string contents)
			{
				var r = new RollbackFile(path);
				try
				{
					File.AppendAllText(path, contents);
				}
				catch (Exception e)
				{
					r.CleanUp();
					throw new Exception(e.Message, e);
				}

				if (_tx != null)
				{
					_journal.Add(r);
					Enlist();
				}
			}

			/// <summary>
			/// Copies the specified <paramref name="sourceFileName"/> to <paramref name="destFileName"/>.
			/// </summary>
			/// <param name="sourceFileName">The file to copy.</param>
			/// <param name="destFileName">The name of the destination file.</param>
			/// <param name="overwrite">true if the destination file can be overwritten, otherwise false.</param>
			public void Copy(string sourceFileName, string destFileName, bool overwrite)
			{
				var r = new RollbackFile(destFileName);
				try
				{
					File.Copy(sourceFileName, destFileName, overwrite);
				}
				catch (Exception e)
				{
					r.CleanUp();
					throw new Exception(e.Message, e);
				}
				if (_tx != null)
				{
					_journal.Add(r);
					Enlist();
				}
			}

			private static readonly Regex m_rgxCleanPath = new Regex("[" + Path.DirectorySeparatorChar + Path.AltDirectorySeparatorChar + "]{2,}");
			/// <summary>
			/// Creates all directories in the specified path.
			/// </summary>
			/// <param name="path">The directory path to create.</param>
			public void CreateDirectory(string path)
			{
				//because a call to this method can actually create multiple diretories,
				// we need to find out explicitly which are being created, and add a
				// journal entry for each.

				string strNormalizedPath = m_rgxCleanPath.Replace(Path.GetFullPath(path), Path.DirectorySeparatorChar.ToString());
				strNormalizedPath = strNormalizedPath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
				string[] strPaths = strNormalizedPath.Split(Path.DirectorySeparatorChar);

				Int32 i = 0;
				string strPath = "";
				if (strPaths[0].EndsWith(Path.VolumeSeparatorChar.ToString()))
				{
					strPath = strPaths[0] + Path.DirectorySeparatorChar;
					i++;
				}
				for (; i < strPaths.Length; i++)
				{
					//if we don't have write permission to the parent directory, then whether
					// or not the child directory exists is irrelevant, as we won't be able to create it
					try
					{
						FileIOPermission fipWritePermission = new FileIOPermission(FileIOPermissionAccess.Write, strPath);
						strPath = Path.Combine(strPath, strPaths[i]);
						fipWritePermission.Demand();

						if (!Directory.Exists(strPath))
						{
							var r = new RollbackDirectory(strPath);
							try
							{
								Directory.CreateDirectory(strPath);
							}
							catch (Exception e)
							{
								r.CleanUp();
								throw new Exception(e.Message, e);
							}
							if (_tx != null)
							{
								_journal.Add(r);
								Enlist();
							}
						}
					}
					catch (SecurityException)
					{
					}
				}
			}

			/// <summary>
			/// Deletes the specified file or directory. An exception is not thrown if the file does not exist.
			/// </summary>
			/// <param name="path">The file to be deleted.</param>
			public void Delete(string path)
			{
				var r = new RollbackFile(path);
				try
				{
					File.Delete(path);
				}
				catch (Exception e)
				{
					r.CleanUp();
					throw new Exception(e.Message, e);
				}
				if (_tx != null)
				{
					_journal.Add(r);
					Enlist();
				}
			}

			/// <summary>
			/// Moves the specified file to a new location.
			/// </summary>
			/// <param name="srcFileName">The name of the file to move.</param>
			/// <param name="destFileName">The new path for the file.</param>
			public void Move(string srcFileName, string destFileName)
			{
				var r1 = new RollbackFile(srcFileName);
				var r2 = new RollbackFile(destFileName);
				try
				{
					File.Move(srcFileName, destFileName);
				}
				catch (Exception e)
				{
					r1.CleanUp();
					r2.CleanUp();
					throw new Exception(e.Message, e);
				}
				if (_tx != null)
				{
					_journal.Add(r1);
					_journal.Add(r2);
					Enlist();
				}
			}

			/// <summary>
			/// Take a snapshot of the specified file. The snapshot is used to rollback the file later if needed.
			/// </summary>
			/// <param name="fileName">The file to take a snapshot for.</param>
			public void Snapshot(string fileName)
			{
				if (_tx != null)
				{
					_journal.Add(new RollbackFile(fileName));
					Enlist();
				}
			}

			/// <summary>
			/// Creates a file, write the specified <paramref name="contents"/> to the file.
			/// </summary>
			/// <param name="path">The file to write to.</param>
			/// <param name="contents">The string to write to the file.</param>
			public void WriteAllText(string path, string contents)
			{
				var r = new RollbackFile(path);
				try
				{
					File.WriteAllText(path, contents);
				}
				catch (Exception e)
				{
					r.CleanUp();
					throw new Exception(e.Message, e);
				}
				if (_tx != null)
				{
					_journal.Add(r);
					Enlist();
				}
			}

			/// <summary>
			/// Creates a file, and writes the specified <paramref name="contents"/> to the file. If the file
			/// already exists, it is overwritten.
			/// </summary>
			/// <param name="path">The file to write to.</param>
			/// <param name="contents">The bytes to write to the file.</param>
			public void WriteAllBytes(string path, byte[] contents)
			{
				var r = new RollbackFile(path);
				try
				{
					File.WriteAllBytes(path, contents);
				}
				catch (Exception e)
				{
					r.CleanUp();
					throw new Exception(e.Message, e);
				}
				if (_tx != null)
				{
					_journal.Add(r);
					Enlist();
				}
			}

			#endregion

			#region IEnlistmentNotification Members

			public void Commit(Enlistment enlistment)
			{
				for (int i = 0; i < _journal.Count; i++)
				{
					_journal[i].CleanUp();
				}

				_enlisted = false;
				_journal.Clear();
				_enlistments.Remove(_tx.TransactionInformation.LocalIdentifier);
				enlistment.Done();
			}

			public void InDoubt(Enlistment enlistment)
			{
				Rollback(enlistment);
			}

			public void Prepare(PreparingEnlistment preparingEnlistment)
			{
				preparingEnlistment.Prepared();
			}

			/// <summary>
			/// Notifies an enlisted object that a transaction is being rolled back (aborted).
			/// </summary>
			/// <param name="enlistment">A <see cref="T:System.Transactions.Enlistment"></see> object used to send a response to the transaction manager.</param>
			/// <remarks>This is typically called on a different thread from the transaction thread.</remarks>
			public void Rollback(Enlistment enlistment)
			{
				try
				{
					// Roll back journal items in reverse order
					for (int i = _journal.Count - 1; i >= 0; i--)
					{
						_journal[i].Rollback();
						_journal[i].CleanUp();
					}

					_enlisted = false;
					_journal.Clear();
				}
				catch (Exception e)
				{
					if (IgnoreExceptionsInRollback)
					{
						EventLog.WriteEntry(GetType().FullName, "Failed to rollback." + Environment.NewLine + e.ToString(), EventLogEntryType.Warning);
					}
					else
					{
						throw new TransactionException("Failed to roll back.", e);
					}
				}
				finally
				{
					_enlisted = false;
					if (_journal != null)
					{
						_journal.Clear();
					}
				}
				_enlistments.Remove(_tx.TransactionInformation.LocalIdentifier);
				enlistment.Done();
			}

			#endregion

			#region RollbackOps

			/// <summary>
			/// Represents a transactional file operation.
			/// </summary>
			private abstract class RollbackOperation
			{
				public abstract void Rollback();
				public abstract void CleanUp();

				protected static string CreateTempFileName(string ext)
				{
					Guid g = GetGuid();

					string retVal = Path.Combine(_tempFolder, (_tempFilesPrefix != null ? _tempFilesPrefix + "-" : "")
						+ g.ToString().Substring(0, 16)) + ext;

					return retVal;
				}
			}

			private class RollbackFile : RollbackOperation
			{
				public RollbackFile(string fileName)
				{
					_originalFileName = fileName;

					if (File.Exists(fileName))
					{
						_backupFileName = CreateTempFileName(Path.GetExtension(fileName));
						File.Copy(_originalFileName, _backupFileName);
					}
				}

				public override void Rollback()
				{
					if (_backupFileName != null)
					{
						string strDirectory = Path.GetDirectoryName(_originalFileName);
						if (!Directory.Exists(strDirectory))
							Directory.CreateDirectory(strDirectory);
						File.Copy(_backupFileName, _originalFileName, true);
					}
					else
					{
						if (File.Exists(_originalFileName))
							File.Delete(_originalFileName);
					}
				}

				public override void CleanUp()
				{
					if (_backupFileName != null)
					{
						FileInfo fi = new FileInfo(_backupFileName);
						if (fi.IsReadOnly)
						{
							fi.Attributes = FileAttributes.Normal;
						}
						File.Delete(_backupFileName);
					}
				}

				public override string ToString()
				{
					return GetType().Name + "-" + _originalFileName;
				}

				private readonly string _originalFileName;
				private readonly string _backupFileName;
			}

			private class RollbackDirectory : RollbackOperation
			{
				public RollbackDirectory(string path)
				{
					_path = path;
					_existed = Directory.Exists(path);
				}

				public override void Rollback()
				{
					if (!_existed)
					{
						if (Directory.GetFiles(_path).Length == 0 && Directory.GetDirectories(_path).Length == 0)
						{
							// Delete the dir only if it's empty
							Directory.Delete(_path);
						}
						else
						{
							EventLog.WriteEntry(GetType().FullName, "Failed to delete directory " + _path + ". Directory was not empty.", EventLogEntryType.Warning);
						}
					}
				}

				public override void CleanUp()
				{
					// Nothing to do
				}

				public override string ToString()
				{
					return GetType().Name + "-" + _path;
				}

				private readonly bool _existed;
				private readonly string _path;
			}

			#endregion

			#region Private

			private readonly Transaction _tx;
			private readonly List<RollbackOperation> _journal;
			private bool _enlisted = false;
			private bool _ignoreExceptionsInRollback;

			private void Enlist()
			{
				if (!_enlisted)
				{
					_tx.EnlistVolatile(this, EnlistmentOptions.None);
					_enlisted = true;
				}
			}

			#endregion
		}
	}
}