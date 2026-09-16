namespace Nexus.Client.ModRepositories
{
	using System;

	/// <summary>
	/// Describes a downloadable file source returned by a mod repository.
	/// </summary>
	public sealed class RepositoryDownloadLink
	{
		/// <summary>
		/// Creates a repository download link.
		/// </summary>
		/// <param name="uri">The download URI.</param>
		/// <param name="sourceName">The display name of the download source.</param>
		public RepositoryDownloadLink(Uri uri, string sourceName)
		{
			Uri = uri;
			SourceName = sourceName;
		}

		/// <summary>
		/// Gets the download URI.
		/// </summary>
		public Uri Uri { get; }

		/// <summary>
		/// Gets the display name of the download source.
		/// </summary>
		public string SourceName { get; }
	}
}
