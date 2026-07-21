using System;
using System.Drawing;

namespace Nexus.UI
{
	/// <summary>
	/// Describes the properties of a font set.
	/// </summary>
	public class FontSetInformation
	{
		/// <summary>
		/// Gets the empty <see cref="FontSetInformation"/>.
		/// </summary>
		/// <value>The empty <see cref="FontSetInformation"/>.</value>
		public static FontSetInformation Empty { get; private set; }

		/// <summary>Creates the static instance of the <see cref="FontSetInformation"/> class.</summary>
		static FontSetInformation()
		{
			FontSetInformation.Empty = new FontSetInformation();
		}

		#region Properties

		/// <summary>Gets or sets the font set.</summary>
		public string Set { get; set; }

		/// <summary>Gets or sets the size.</summary>
		public float Size { get; set; }

		/// <summary>Gets or sets the style.</summary>
		public FontStyle Style { get; set; }

		#endregion

		#region Constructors

		/// <summary>Creates a new instances of the <see cref="FontSetInformation"/> class.</summary>
		public FontSetInformation()
		{
			Size = 8.25f;
			Style = FontStyle.Regular;
		}

		/// <summary>
		/// The copy constructor.
		/// </summary>
		/// <param name="p_fsiCopy">The object to be copied.</param>
		public FontSetInformation(FontSetInformation p_fsiCopy)
		{
			Set = p_fsiCopy.Set;
			Size = p_fsiCopy.Size;
			Style = p_fsiCopy.Style;
		}

		#endregion

		#region Public Methods

		/// <summary>Indicates whether this instance and a specified font provider information are equal.</summary>
		/// <param name="information">The information to check the equality of.</param>
		/// <returns>True if logically equal, otherwise false.</returns>
		public bool Equals(FontSetInformation information)
		{
			return ((Set == information.Set) || (String.IsNullOrEmpty(Set) && String.IsNullOrEmpty(information.Set))) && Size == information.Size && Style == information.Style;
		}

		/// <summary>Indicates whether this instance and a specified font provider information are equal.</summary>
		/// <param name="obj">Another object to compare to.</param>
		/// <returns>True if obj and this instance are the same type and represent the same value; otherwise, false.</returns>
		public override bool Equals(object obj)
		{
			return obj is FontSetInformation ? Equals((FontSetInformation)obj) : base.Equals(obj);
		}

		/// <summary>Returns the hash code for this instance.</summary>
		/// <returns>A 32-bit signed integer that is the hash code for this instance.</returns>
		public override int GetHashCode()
		{
			int hashCode = base.GetHashCode();
			if (Set != null)
				hashCode ^= Set.GetHashCode();

			hashCode ^= Size.GetHashCode();
			hashCode ^= Style.GetHashCode();

			return hashCode;
		}

		#endregion
	}
}
