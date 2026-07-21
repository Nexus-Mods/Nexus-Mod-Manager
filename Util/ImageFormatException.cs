using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Drawing.Imaging;

namespace Nexus.Client.Util
{
	/// <summary>
	/// An exception that is thrown when there is a problem with an <see cref="ImageFormat"/>
	/// </summary>
	public class ImageFormatException : Exception
	{
		#region Properties

		/// <summary>
		/// Gets the problematic <see cref="ImageFormat"/>.
		/// </summary>
		/// <value>The problematic <see cref="ImageFormat"/>.</value>
		public ImageFormat Format { get; private set; }

		#endregion

		#region Constructor

		/// <summary>
		/// A simple constructor that initializes the object's properties.
		/// </summary>
		/// <param name="p_strMessage">The exception message.</param>
		/// <param name="p_imfFormat">The problematic <see cref="ImageFormat"/>.</param>
		public ImageFormatException(string p_strMessage, ImageFormat p_imfFormat)
			: base(p_strMessage)
		{
			Format = p_imfFormat;
		}

		#endregion
	}
}
