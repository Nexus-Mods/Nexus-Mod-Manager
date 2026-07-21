using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace Nexus.Client.Util
{
	/// <summary>
	/// Extension methods for <see cref="Image"/>.
	/// </summary>
	/// <remarks>
	/// This allows instantiation of an image from a <see cref="byte"/> array,
	/// and returns the file extension commonly associated with the image's
	/// format.
	/// </remarks>
	public static class ImageExtensions
	{
		/// <summary>
		/// Returns the file extension commonly associated with the image's
		/// format.
		/// </summary>
		/// <param name="p_imgImage">The image whose format is to be examined.</param>
		/// <returns>The file extension commonly associated with the image's
		/// format.</returns>
		/// <exception cref="ImageFormatException">Thrown if the <see cref="Image"/>'s
		/// <see cref="ImageFormat"/> is not recognized.</exception>
		public static string GetExtension(this Image p_imgImage)
		{
			ImageFormat ifmFormat = p_imgImage.RawFormat;
			if (ifmFormat.Equals(ImageFormat.Bmp))
				return ".bmp";
			if (ifmFormat.Equals(ImageFormat.Emf))
				return ".emf";
			if (ifmFormat.Equals(ImageFormat.Exif))
				return ".jpg";
			if (ifmFormat.Equals(ImageFormat.Gif))
				return ".gif";
			if (ifmFormat.Equals(ImageFormat.Icon))
				return ".ico";
			if (ifmFormat.Equals(ImageFormat.Jpeg))
				return ".jpg";
			if (ifmFormat.Equals(ImageFormat.MemoryBmp))
				return ".bmp";
			if (ifmFormat.Equals(ImageFormat.Png))
				return ".png";
			if (ifmFormat.Equals(ImageFormat.Tiff))
				return ".tif";
			if (ifmFormat.Equals(ImageFormat.Wmf))
				return ".wmf";
			throw new ImageFormatException("Unrecognized image format: " + ifmFormat.ToString(), ifmFormat);
		}
	}
}
