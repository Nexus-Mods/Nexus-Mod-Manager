using System;

namespace Nexus.Client.Util
{
	/// <summary>
	/// Utility class for working with <see cref="Uri"/>s.
	/// </summary>
	public static class UriUtil
	{
		/// <summary>
		/// Builds a <see cref="Uri"/> from the given string.
		/// </summary>
		/// <remarks>
		/// If the given string is not a valid Uri, we try turning it into
		/// an HTTP Uri.
		/// </remarks>
		/// <param name="p_strUri">The string from which to construct a <see cref="Uri"/>.</param>
		/// <returns>The <see cref="Uri"/> represented by the given string, or
		/// <c>null</c> if we couldn't build one.</returns>
		public static Uri BuildUri(string p_strUri)
		{
			Uri uriParsed = null;
			if (Uri.TryCreate(p_strUri, UriKind.Absolute, out uriParsed))
				return uriParsed;
			string strUri = "http://" + p_strUri;
			if (Uri.TryCreate(strUri, UriKind.Absolute, out uriParsed))
				return uriParsed;
			return null;
		}

		/// <summary>
		/// Tries to build a <see cref="Uri"/> from the given string.
		/// </summary>
		/// <remarks>
		/// If the given string is not a valid Uri, we try turning it into
		/// an HTTP Uri.
		/// </remarks>
		/// <param name="p_strUri">The string from which to construct a <see cref="Uri"/>.</param>
		/// <param name="p_uriUri">The constructed <see cref="Uri"/>.</param>
		/// <returns><c>true</c> if we were able to build the <see cref="Uri"/>;
		/// <c>false</c> otherwise.</returns>
		public static bool TryBuildUri(string p_strUri, out Uri p_uriUri)
		{
			p_uriUri = BuildUri(p_strUri);
			return (p_uriUri != null);
		}
	}
}
