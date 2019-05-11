namespace Nexus.Client.ModManagement
{
    using System;
    using System.Diagnostics;
    using System.IO;
    using System.Text.RegularExpressions;

    /// <inheritdoc />
    /// <summary>
    /// Represents an NXM url.
    /// </summary>
    /// <remarks>
    /// This is a convenience class that parses out parts of the URL that are of interest.
    /// </remarks>
    public class NexusUrl : Uri
	{
		#region Properties

		/// <summary>
		/// Gets the id of the mod encoded in the url.
		/// </summary>
		/// <remarks>
		/// This returns <c>null</c> if there is no mod id in the url.
		/// </remarks>
		/// <value>The id of the mod encoded in the url.</value>
		public string ModId
		{
			get
			{
				var strUriParts = TrimmedSegments;

                if (strUriParts.Length > 2 && strUriParts[1].Equals("mods", StringComparison.OrdinalIgnoreCase))
                {
                    return strUriParts[2];
                }

                return null;
			}
		}

		/// <summary>
		/// Gets the id of the file encoded in the url.
		/// </summary>
		/// <remarks>
		/// This returns <c>null</c> if there is no file id in the url.
		/// </remarks>
		/// <value>The id of the file encoded in the url.</value>
		public string FileId
		{
			get
			{
				var strUriParts = TrimmedSegments;

                if (strUriParts.Length > 4 && strUriParts[3].Equals("files", StringComparison.OrdinalIgnoreCase))
                {
                    return strUriParts[4];
                }

                return null;
			}
		}

		/// <summary>
		/// Gets the segments from the URI, without trailing slashes.
		/// </summary>
		/// <value>The segments from the URI, without trailing slashes.</value>
		protected string[] TrimmedSegments
		{
			get
			{
				var strUriParts = Segments;

                for (var i = 0; i < strUriParts.Length; i++)
                {
                    strUriParts[i] = strUriParts[i].TrimEnd(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
                }

                return strUriParts;
			}
		}

		/// <summary>
		/// Gets the id of the file encoded in the url.
		/// </summary>
		/// <remarks>
		/// This returns <c>null</c> if there is no file id in the url.
		/// </remarks>
		/// <value>The id of the file encoded in the url.</value>
		public string ProfileId
		{
			get
			{
				var strUriParts = TrimmedSegments;

                if (strUriParts.Length > 2 && strUriParts[1].Equals("profiles", StringComparison.OrdinalIgnoreCase))
                {
                    return strUriParts[2];
                }

                return null;
			}
		}

        /// <summary>
        /// Key from Nexus for this download.
        /// </summary>
        public string Key { get; private set; }

        /// <summary>
        /// Expiry of the key for this download.
        /// </summary>
        public int Expiry { get; private set; }

        /// <summary>
        /// User associated with this download.
        /// </summary>
        public int UserId { get; private set; }

        #endregion

        #region Constructors

        /// <inheritdoc />
        /// <summary>
        /// A simple constructor that initializes the object with the given values.
        /// </summary>
        /// <param name="url">The NXM URL.</param>
        /// <exception cref="T:System.ArgumentException">Thrown if the given URI's scheme is not NXM.</exception>
        public NexusUrl(Uri url) : base(url.ToString())
		{
            if (!Scheme.Equals("nxm", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("Scheme must be NXM.");
            }

            SetQueryValues(Query);
        }

        #endregion

        private void SetQueryValues(string query)
        {
            var match = Regex.Match(query, @"\?key=([^&]*)\&expires=(\d*)\&user_id=(\d*)");

            if (match.Groups.Count != 4)
            {
                Trace.TraceWarning("Download URL did not contain expected values.");
                Key = string.Empty;
                Expiry = -1;
                return;
            }

            Key = match.Groups[1].Value;
            Expiry = Convert.ToInt32(match.Groups[2].Value);
            UserId = Convert.ToInt32(match.Groups[3].Value);
        }
	}
}
