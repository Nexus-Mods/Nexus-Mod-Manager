using System;
using System.ComponentModel;
using System.IO;
using SevenZip;

namespace Nexus.Client.Mods.Formats.FOMod
{
	/// <summary>
	/// This class is subclassed to compress a source folder into a FOMod.
	/// </summary>
	public class FOModModCompressor : ModCompressorBase
	{
		#region Constructors

		/// <summary>
		/// A simple constructor that initializes with its dependencies.
		/// </summary>
		/// <param name="p_eifEnvironmentInfo">The application's envrionment info.</param>
		public FOModModCompressor(IEnvironmentInfo p_eifEnvironmentInfo)
			: base(p_eifEnvironmentInfo)
		{
		}

		#endregion

		/// <summary>
		/// Compresses the specified source folder into a mod file at the specified destination.
		/// </summary>
		/// <remarks>
		/// If the desitnation file exists, it will be overwritten.
		/// </remarks>
		/// <param name="p_strSourcePath">The folder to compress into a mod file.</param>
		/// <param name="p_strDestinationPath">The path of the mod file to create.</param>
		public override void Compress(string p_strSourcePath, string p_strDestinationPath)
		{
			Int32 intFileCount = Directory.GetFiles(p_strSourcePath).Length;
			SevenZipCompressor szcCompressor = new SevenZipCompressor();
			szcCompressor.CompressionLevel = EnvironmentInfo.Settings.ModCompressionLevel;
			szcCompressor.ArchiveFormat = EnvironmentInfo.Settings.ModCompressionFormat;
			szcCompressor.CompressionMethod = CompressionMethod.Default;
			switch (szcCompressor.ArchiveFormat)
			{
				case OutArchiveFormat.Zip:
				case OutArchiveFormat.GZip:
				case OutArchiveFormat.BZip2:
					szcCompressor.CustomParameters.Add("mt", "on");
					break;
				case OutArchiveFormat.SevenZip:
				case OutArchiveFormat.XZ:
					szcCompressor.CustomParameters.Add("mt", "on");
					szcCompressor.CustomParameters.Add("s", "off");
					break;
			}
			szcCompressor.CompressionMode = CompressionMode.Create;
			szcCompressor.FileCompressionStarted += new EventHandler<FileNameEventArgs>(Compressor_FileCompressionStarted);
			szcCompressor.CompressDirectory(p_strSourcePath, p_strDestinationPath);
		}

		/// <summary>
		/// Handles the <see cref="SevenZipCompressor.FileCompressionStarted"/> event of the file compressor
		/// being used to compress the mod.
		/// </summary>
		/// <remarks>
		/// This checks to see if the compression has been cancelled.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">A <see cref="FileNameEventArgs"/> describing the event arguments.</param>
		private void Compressor_FileCompressionStarted(object sender, FileNameEventArgs e)
		{
			CancelEventArgs ceaArgs = new CancelEventArgs();
			OnFileCompressionFinished(ceaArgs);
			e.Cancel = ceaArgs.Cancel;
		}
	}
}
