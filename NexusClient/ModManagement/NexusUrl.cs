using System;
using System.IO;

namespace Nexus.Client.ModManagement
{
	/// <summary>
	/// Represents an NXM url.
	/// </summary>
	/// <remarks>
	/// This is a convenice class that parses out parts of the URL that are of interest.
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
				string[] strUriParts = TrimmedSegments;
				if ((strUriParts.Length > 2) && strUriParts[1].Equals("mods", StringComparison.OrdinalIgnoreCase))
					return strUriParts[2];
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
				string[] strUriParts = TrimmedSegments;
				if ((strUriParts.Length > 4) && strUriParts[3].Equals("files", StringComparison.OrdinalIgnoreCase))
					return strUriParts[4];
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
				string[] strUriParts = Segments;
				for (Int32 i = 0; i < strUriParts.Length; i++)
					strUriParts[i] = strUriParts[i].TrimEnd(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
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
				string[] strUriParts = TrimmedSegments;
				if ((strUriParts.Length > 2) && strUriParts[1].Equals("profiles", StringComparison.OrdinalIgnoreCase))
					return strUriParts[2];
				return null;
			}
		}

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with the given values.
		/// </summary>
		/// <param name="p_uriUrl">The NXM URL.</param>
		/// <exception cref="ArgumentException">Thrown if the given URI's scheme is not NXM.</exception>
		public NexusUrl(Uri p_uriUrl)
			: base(p_uriUrl.ToString())
		{
			if (!Scheme.Equals("nxm", StringComparison.OrdinalIgnoreCase))
				throw new ArgumentException("Scheme must be NXM.");
		}

		#endregion
	}
}
