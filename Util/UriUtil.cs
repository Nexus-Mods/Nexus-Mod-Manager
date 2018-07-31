namespace Nexus.Client.Util
{
    using System;

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
		/// <param name="address">The string from which to construct a <see cref="Uri"/>.</param>
		/// <returns>The <see cref="Uri"/> represented by the given string, or
		/// <c>null</c> if we couldn't build one.</returns>
		private static Uri BuildUri(string address)
		{
		    if (Uri.TryCreate(address, UriKind.Absolute, out var result))
            {
                return result;
            }

		    if (Uri.TryCreate($"http://{address}", UriKind.Absolute, out result))
            {
                return result;
            }

            return null;
		}

		/// <summary>
		/// Tries to build a <see cref="Uri"/> from the given string.
		/// </summary>
		/// <remarks>
		/// If the given string is not a valid Uri, we try turning it into
		/// an HTTP Uri.
		/// </remarks>
		/// <param name="input">The string from which to construct a <see cref="Uri"/>.</param>
		/// <param name="result">The constructed <see cref="Uri"/>.</param>
		/// <returns><c>true</c> if we were able to build the <see cref="Uri"/>;
		/// <c>false</c> otherwise.</returns>
		public static bool TryBuildUri(string input, out Uri result)
		{
			result = BuildUri(input);
			return (result != null);
		}
	}
}
