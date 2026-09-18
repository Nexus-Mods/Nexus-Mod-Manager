using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Nexus.Client.CollectionManagement;
using Nexus.Client.ModRepositories;

namespace Nexus.Client.OnlineServices.NexusMods.Collections
{
	/// <summary>
	/// Verifies immutable Collection archive bytes through Nexus Mods' exact MD5-to-mod/file lookup.
	/// </summary>
	/// <remarks>
	/// MD5 is used only as Nexus' provider lookup key. The retained object itself is independently sealed by C4.10 with
	/// SHA-256 and later consumers use that stronger local immutable content identity.
	/// </remarks>
	public sealed class NexusCollectionArchiveIdentityVerifier : ICollectionArchiveIdentityVerifier
	{
		private const int BufferSize = 81920;
		private readonly NexusModsApiRepository _repository;

		/// <summary>
		/// Creates a verifier over the existing owned Nexus repository/provider stack.
		/// </summary>
		public NexusCollectionArchiveIdentityVerifier(NexusModsApiRepository repository)
		{
			_repository = repository ?? throw new ArgumentNullException(nameof(repository));
		}

		/// <inheritdoc />
		public bool IsExactMatch(CollectionArtifactReference requestedArtifact, Stream immutableArchive,
			CancellationToken cancellationToken)
		{
			if (requestedArtifact == null)
				throw new ArgumentNullException(nameof(requestedArtifact));
			if (immutableArchive == null)
				throw new ArgumentNullException(nameof(immutableArchive));
			if (!immutableArchive.CanRead)
				throw new ArgumentException("The immutable archive stream must be readable.", nameof(immutableArchive));

			string expectedDomain;
			long expectedModId;
			long expectedFileId;
			if (!NexusCollectionModFileArtifactIdentity.TryParse(requestedArtifact,
				out expectedDomain, out expectedModId, out expectedFileId))
				return false;

			if (expectedModId > Int32.MaxValue || expectedFileId > Int32.MaxValue)
				return false;

			string md5 = ComputeMd5(immutableArchive, cancellationToken);
			return _repository.IsExactArchiveIdentityByMd5(md5, expectedDomain, (int)expectedModId,
				(int)expectedFileId, cancellationToken);
		}

		private static string ComputeMd5(Stream stream, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			using (MD5 md5 = MD5.Create())
			{
				byte[] buffer = new byte[BufferSize];
				int read;
				while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
				{
					cancellationToken.ThrowIfCancellationRequested();
					md5.TransformBlock(buffer, 0, read, buffer, 0);
				}
				cancellationToken.ThrowIfCancellationRequested();
				md5.TransformFinalBlock(new byte[0], 0, 0);

				var builder = new StringBuilder(md5.Hash.Length * 2);
				for (int index = 0; index < md5.Hash.Length; index++)
					builder.Append(md5.Hash[index].ToString("x2"));
				return builder.ToString();
			}
		}
	}
}
