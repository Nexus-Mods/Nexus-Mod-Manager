using System.Drawing;

namespace Nexus.Client.Util
{
	/// <summary>
	/// This class extends the information and functionality if an <see cref="Image"/>.
	/// </summary>
	/// <remarks>
	/// Ideally this class would extend <see cref="Image"/>, but all of <see cref="Image"/>'s
	/// constructors are internal, making extension pointless.
	/// </remarks>
	public class ExtendedImage
	{
		private byte[] m_bteImage = null;

		#region Properties

		/// <summary>
		/// Gets the byte array containing the image data.
		/// </summary>
		/// <value>The byte array containing the image data.</value>
		public byte[] Data
		{
			get
			{
				return m_bteImage;
			}
			private set
			{
				m_bteImage = value;
				if (value == null)
				{
					Image = null;
					return;
				}
				ImageConverter cnvConverter = new ImageConverter();
				Image = (Image)cnvConverter.ConvertFrom(m_bteImage);
			}
		}

		/// <summary>
		/// Gets the underlying <see cref="Image"/> of the object.
		/// </summary>
		/// <value>The underlying <see cref="Image"/> of the object.</value>
		public Image Image { get; private set; }

		#endregion

		#region Constructors

		/// <summary>
		/// Creates an image from the given byte array.
		/// </summary>
		/// <param name="p_bteImage">A byte array representing an image.</param>
		public ExtendedImage(byte[] p_bteImage)
		{
			Data = p_bteImage;
		}

		#endregion

		/// <summary>
		/// An implicit operator that converts this <see cref="ExtendedImage"/>
		/// to an <see cref="Image"/>.
		/// </summary>
		/// <param name="p_eimImage">The <see cref="ExtendedImage"/> to convert.</param>
		/// <returns>The underlying <see cref="Image"/> of the object.</returns>
		public static implicit operator Image(ExtendedImage p_eimImage)
		{
			return (p_eimImage == null) ? null : p_eimImage.Image;
		}

		/// <summary>
		/// Returns the file extension commonly associated with the image's
		/// format.
		/// </summary>
		/// <param name="p_imgImage">The image whose format is to be examined.</param>
		/// <returns>The file extension commonly associated with the image's
		/// format.</returns>
		/// <exception cref="ImageFormatException">Thrown if the <see cref="Image"/>'s
		/// <see cref="ImageFormat"/> is not recognized.</exception>
		public string GetExtension()
		{
			return Image.GetExtension();
		}
	}

}
