namespace Nexus.Client.Games
{
	using System;
	using System.Drawing;
	using System.Drawing.Drawing2D;
	using System.Drawing.Imaging;
	using System.IO;
	using System.Runtime.InteropServices;
	using System.Security;

	/// <summary>
	/// Optional, read-only packaged artwork is game-cover.png next to a data-driven
	/// game's game.json. User images are kept outside installed game definitions.
	/// Never opens a game session or mutates game/storage settings.
	/// </summary>
	internal sealed class GameDiscoveryArtwork
	{
		internal const int BannerWidth = 640;
		internal const int BannerHeight = 960;
		private const long MaximumImageBytes = 16L * 1024 * 1024;
		private readonly string _overrideDirectory;
		private readonly Func<string, string> _definitionPath;

		internal GameDiscoveryArtwork(string applicationDataDirectory, Func<string, string> definitionPath)
		{
			if (String.IsNullOrWhiteSpace(applicationDataDirectory))
				throw new ArgumentException("An NMM application data directory is required.", nameof(applicationDataDirectory));
			_overrideDirectory = Path.Combine(applicationDataDirectory, "Game Artwork");
			_definitionPath = definitionPath ?? throw new ArgumentNullException(nameof(definitionPath));
		}

		internal bool HasOverride(string modeId) => File.Exists(GetOverridePath(modeId));

		internal Image Load(string modeId)
		{
			string userPath = GetOverridePath(modeId);
			Image image = TryLoad(userPath);
			if (image != null) return image;
			string definitionFile = _definitionPath(modeId);
			if (String.IsNullOrWhiteSpace(definitionFile)) return null;
			string folder = Path.GetDirectoryName(definitionFile);
			return String.IsNullOrWhiteSpace(folder) ? null : TryLoad(Path.Combine(folder, "game-cover.png"));
		}

		internal void Import(string modeId, string sourceFile)
		{
			string destination = GetOverridePath(modeId);
			if (String.IsNullOrWhiteSpace(sourceFile)) throw new ArgumentException("Choose a picture.", nameof(sourceFile));
			var info = new FileInfo(sourceFile);
			if (!info.Exists || info.Length > MaximumImageBytes)
				throw new InvalidDataException("The picture is missing or larger than 16 MB.");

			Directory.CreateDirectory(_overrideDirectory);
			string temporary = Path.Combine(_overrideDirectory, Guid.NewGuid().ToString("N") + ".tmp");
			try
			{
				using (var stream = File.OpenRead(sourceFile))
				using (var input = Image.FromStream(stream, false, true))
				{
					if (input.Width < 320 || input.Height < 180 || input.Width > 8192 || input.Height > 8192)
						throw new InvalidDataException("Use an image between 320x180 and 8192x8192 pixels.");
					using (var banner = CropToBanner(input))
						banner.Save(temporary, ImageFormat.Png);
				}
				if (File.Exists(destination)) File.Replace(temporary, destination, null);
				else File.Move(temporary, destination);
			}
			finally { if (File.Exists(temporary)) File.Delete(temporary); }
		}

		internal void ClearOverride(string modeId)
		{
			string path = GetOverridePath(modeId);
			if (File.Exists(path)) File.Delete(path);
		}

		private string GetOverridePath(string modeId)
		{
			if (String.IsNullOrWhiteSpace(modeId) || modeId.Length > 120 ||
				modeId == "." || modeId == ".." ||
				Array.Exists(modeId.ToCharArray(), ch => !(Char.IsLetterOrDigit(ch) && ch < 128) && ch != '-' && ch != '_' && ch != '.'))
				throw new ArgumentException("Invalid game mode identity.", nameof(modeId));
			return Path.Combine(_overrideDirectory, modeId + ".png");
		}

		/// <summary>Normalizes new imports and read-only previews to the portrait cover ratio; existing files are never rewritten on load.</summary>
		private static Bitmap CropToBanner(Image original)
		{
			float width = original.Width;
			float height = original.Height;
			float ratio = BannerWidth / (float)BannerHeight;
			if (width / height > ratio) width = height * ratio;
			else height = width / ratio;
			var source = new RectangleF((original.Width - width) / 2f,
				(original.Height - height) / 2f, width, height);
			var banner = new Bitmap(BannerWidth, BannerHeight, PixelFormat.Format24bppRgb);
			try
			{
				using (Graphics graphics = Graphics.FromImage(banner))
				{
					graphics.Clear(Color.FromArgb(40, 44, 54));
					graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
					graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
					graphics.DrawImage(original, new RectangleF(0, 0, BannerWidth, BannerHeight), source, GraphicsUnit.Pixel);
				}
				return banner;
			}
			catch { banner.Dispose(); throw; }
		}

		private static Image TryLoad(string path)
		{
			try
			{
				var info = new FileInfo(path);
				if (!info.Exists || info.Length > MaximumImageBytes) return null;
				using (var stream = File.OpenRead(path))
				using (var original = Image.FromStream(stream, false, true))
				{
					if (original.Width > 8192 || original.Height > 8192) return null;
					return CropToBanner(original); // File handle is released before returning.
				}
			}
			catch (Exception error) when (error is IOException || error is UnauthorizedAccessException ||
				error is SecurityException || error is ArgumentException || error is ExternalException)
			{
				return null; // Corrupt/missing artwork never prevents Game Discovery.
			}
		}
	}
}
