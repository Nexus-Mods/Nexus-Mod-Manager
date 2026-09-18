using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>
	/// Persists the one native ownership authority accepted for a physical Collection target.
	/// </summary>
	/// <remarks>
	/// The record is stored outside any individual Game Storage so two independent NMM copies cannot each validate a
	/// different InstallLog simply because they use different InstallInfo roots. A target is never rebound implicitly.
	/// </remarks>
	public sealed class CollectionTargetOwnershipAuthorityStore
	{
		private const int CurrentSchemaVersion = 1;
		private readonly string _rootDirectory;

		/// <summary>
		/// Creates the production authority store in the machine-wide application-data area.
		/// </summary>
		public CollectionTargetOwnershipAuthorityStore()
			: this(GetDefaultRootDirectory())
		{
		}

		/// <summary>
		/// Creates an authority store at an explicit root, primarily for deterministic tests and diagnostics.
		/// </summary>
		public CollectionTargetOwnershipAuthorityStore(string rootDirectory)
		{
			if (string.IsNullOrWhiteSpace(rootDirectory))
				throw new ArgumentException("An ownership-authority root directory is required.", nameof(rootDirectory));

			_rootDirectory = Path.GetFullPath(rootDirectory);
		}

		/// <summary>
		/// Gets or creates the authority binding for a physical target and rejects any conflicting native owner.
		/// </summary>
		public CollectionTargetOwnershipAuthorityBinding BindOrValidate(CollectionTargetAuthority authority, string installInfoPhysicalKey)
		{
			if (authority == null)
				throw new ArgumentNullException(nameof(authority));
			if (authority.Target == null || !authority.Target.IsCanonical)
				throw new ArgumentException("Canonical Collection target authority is required.", nameof(authority));

			string installInfoKey = CollectionIdentityValidation.RequireOpaqueToken(installInfoPhysicalKey, nameof(installInfoPhysicalKey));
			string bindingPath = GetBindingPath(authority.PhysicalGameKey);
			try
			{
				Directory.CreateDirectory(_rootDirectory);
				if (!File.Exists(bindingPath))
					CreateBinding(bindingPath, authority, installInfoKey);

				BindingRecord record = ReadBinding(bindingPath);
				ValidateRecord(record, authority, installInfoKey);
				return new CollectionTargetOwnershipAuthorityBinding(record.TargetFingerprint, record.StorageId, record.InstallInfoPhysicalKey);
			}
			catch (CollectionTargetOwnershipAuthorityException)
			{
				throw;
			}
			catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException ||
				exception is System.Security.SecurityException || exception is NotSupportedException)
			{
				throw new CollectionTargetOwnershipAuthorityException(CollectionTargetOwnershipAuthorityFailureKind.AuthorityStoreUnavailable,
					"The machine-wide Collection ownership-authority store is unavailable. Mutation is blocked rather than assuming ownership is safe.", exception);
			}
		}

		/// <summary>
		/// Builds a path from a one-way hash of physical target identity so filesystem names do not reveal game paths.
		/// </summary>
		private string GetBindingPath(string physicalGameKey)
		{
			using (SHA256 sha256 = SHA256.Create())
			{
				byte[] hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(
					CollectionIdentityValidation.RequireOpaqueToken(physicalGameKey, nameof(physicalGameKey))));
				var builder = new StringBuilder(hash.Length * 2);
				foreach (byte value in hash)
					builder.Append(value.ToString("x2"));
				return Path.Combine(_rootDirectory, builder.ToString() + ".json");
			}
		}

		/// <summary>
		/// Atomically creates the first authority record without replacing an existing owner.
		/// </summary>
		private static void CreateBinding(string bindingPath, CollectionTargetAuthority authority, string installInfoPhysicalKey)
		{
			var record = new BindingRecord
			{
				SchemaVersion = CurrentSchemaVersion,
				TargetFingerprint = authority.Target.Fingerprint,
				StorageId = authority.StorageId,
				InstallInfoPhysicalKey = installInfoPhysicalKey
			};
			string payload = JsonConvert.SerializeObject(record, Formatting.Indented);
			string temporaryPath = bindingPath + ".tmp-" + Guid.NewGuid().ToString("N");
			try
			{
				byte[] bytes = new UTF8Encoding(false).GetBytes(payload);
				using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
				{
					stream.Write(bytes, 0, bytes.Length);
					stream.Flush(true);
				}

				try
				{
					File.Move(temporaryPath, bindingPath);
				}
				catch (IOException)
				{
					if (!File.Exists(bindingPath))
						throw;
				}
			}
			finally
			{
				if (File.Exists(temporaryPath))
					File.Delete(temporaryPath);
			}
		}

		/// <summary>
		/// Reads and structurally validates one persisted authority binding.
		/// </summary>
		private static BindingRecord ReadBinding(string bindingPath)
		{
			try
			{
				BindingRecord record = JsonConvert.DeserializeObject<BindingRecord>(File.ReadAllText(bindingPath));
				if (record == null || record.SchemaVersion != CurrentSchemaVersion ||
					string.IsNullOrWhiteSpace(record.TargetFingerprint) || string.IsNullOrWhiteSpace(record.StorageId) ||
					string.IsNullOrWhiteSpace(record.InstallInfoPhysicalKey))
				{
					throw new CollectionTargetOwnershipAuthorityException(CollectionTargetOwnershipAuthorityFailureKind.AuthorityBindingInvalid,
						"The persisted Collection ownership-authority binding is missing or structurally invalid.");
				}
				return record;
			}
			catch (CollectionTargetOwnershipAuthorityException)
			{
				throw;
			}
			catch (JsonException exception)
			{
				throw new CollectionTargetOwnershipAuthorityException(CollectionTargetOwnershipAuthorityFailureKind.AuthorityBindingInvalid,
					"The persisted Collection ownership-authority binding is corrupt.", exception);
			}
		}

		/// <summary>
		/// Rejects a physical target that was previously bound to another native storage or InstallInfo authority.
		/// </summary>
		private static void ValidateRecord(BindingRecord record, CollectionTargetAuthority authority, string installInfoPhysicalKey)
		{
			if (!StringComparer.Ordinal.Equals(record.TargetFingerprint, authority.Target.Fingerprint) ||
				!StringComparer.OrdinalIgnoreCase.Equals(record.StorageId, authority.StorageId) ||
				!StringComparer.OrdinalIgnoreCase.Equals(record.InstallInfoPhysicalKey, installInfoPhysicalKey))
			{
				throw new CollectionTargetOwnershipAuthorityException(CollectionTargetOwnershipAuthorityFailureKind.AuthorityConflict,
					"This physical game target is already bound to a different native InstallLog/Game Storage authority. Collection mutation is blocked until the ownership overlap is explicitly resolved.");
			}
		}

		/// <summary>
		/// Returns the machine-wide default binding root shared by independent NMM installations.
		/// </summary>
		private static string GetDefaultRootDirectory()
		{
			string commonData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
			if (string.IsNullOrWhiteSpace(commonData))
				throw new CollectionTargetOwnershipAuthorityException(CollectionTargetOwnershipAuthorityFailureKind.AuthorityStoreUnavailable,
					"The machine-wide application-data directory is unavailable, so Collection ownership authority cannot be coordinated safely.");

			return Path.Combine(commonData, "NMMCE", "CollectionTargetAuthorities");
		}

		private sealed class BindingRecord
		{
			public int SchemaVersion { get; set; }
			public string TargetFingerprint { get; set; }
			public string StorageId { get; set; }
			public string InstallInfoPhysicalKey { get; set; }
		}
	}
}
