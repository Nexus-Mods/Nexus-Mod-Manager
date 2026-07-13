namespace Nexus.Client.ModManagement
{
    using System;
    using System.Collections.Generic;
    using System.Drawing;
    using System.Drawing.Imaging;
    using System.IO;
    using System.Runtime.InteropServices;
    using System.Threading;
    using System.Threading.Tasks;

    using Pfim;

    public sealed class FilePreviewOwnerOption
    {
        public FilePreviewOwnerOption(string ownerKey, string ownerName, string filePath)
        {
            OwnerKey = ownerKey ?? String.Empty;
            OwnerName = ownerName ?? String.Empty;
            FilePath = filePath ?? String.Empty;
        }

        public string OwnerKey { get; private set; }
        public string OwnerName { get; private set; }
        public string FilePath { get; private set; }
    }

    public sealed class FilePreviewRequest
    {
        public FilePreviewRequest(string filePath)
        {
            FilePath = filePath ?? String.Empty;
        }

        public string FilePath { get; private set; }
    }

    public sealed class FilePreviewResult : IDisposable
    {
        private FilePreviewResult(FilePreviewState state, string message, Bitmap image)
        {
            State = state;
            Message = message ?? String.Empty;
            Image = image;
        }

        public FilePreviewState State { get; private set; }
        public string Message { get; private set; }
        public Bitmap Image { get; private set; }

        public static FilePreviewResult Empty(string message)
        {
            return new FilePreviewResult(FilePreviewState.Empty, message, null);
        }

        public static FilePreviewResult Unsupported(string message)
        {
            return new FilePreviewResult(FilePreviewState.Unsupported, message, null);
        }

        public static FilePreviewResult Error(string message)
        {
            return new FilePreviewResult(FilePreviewState.Error, message, null);
        }

        public static FilePreviewResult FromImage(Bitmap image)
        {
            return new FilePreviewResult(FilePreviewState.Image, String.Empty, image);
        }

        public Bitmap DetachImage()
        {
            Bitmap image = Image;
            Image = null;
            return image;
        }

        public void Dispose()
        {
            if (Image != null)
            {
                Image.Dispose();
                Image = null;
            }
        }
    }

    public enum FilePreviewState
    {
        Empty,
        Unsupported,
        Error,
        Image
    }

    public interface IFilePreviewProvider
    {
        bool CanPreview(string filePath);
        FilePreviewResult LoadPreview(FilePreviewRequest request, CancellationToken cancellationToken);
    }

    public sealed class FilePreviewManager
    {
        private readonly List<IFilePreviewProvider> _providers = new List<IFilePreviewProvider>();

        public FilePreviewManager()
        {
            _providers.Add(new ImageFilePreviewProvider());
        }

        public Task<FilePreviewResult> LoadPreviewAsync(FilePreviewRequest request, CancellationToken cancellationToken)
        {
            if (request == null || String.IsNullOrWhiteSpace(request.FilePath))
                return Task.FromResult(FilePreviewResult.Empty("Select a file to preview."));

            return Task.Run(() => LoadPreview(request, cancellationToken), cancellationToken);
        }

        private FilePreviewResult LoadPreview(FilePreviewRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!File.Exists(request.FilePath))
                return FilePreviewResult.Error("The selected file does not exist.");

            foreach (IFilePreviewProvider provider in _providers)
            {
                if (!provider.CanPreview(request.FilePath))
                    continue;

                try
                {
                    return provider.LoadPreview(request, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    return FilePreviewResult.Error(ex.Message);
                }
            }

            return FilePreviewResult.Unsupported("Preview is not available for this file type.");
        }
    }

    public sealed class ImageFilePreviewProvider : IFilePreviewProvider
    {
        private const long MaxDecodedBytes = 256L * 1024L * 1024L;
        private const int MaxDimension = 16384;
        private static readonly HashSet<string> SupportedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".bmp",
            ".gif",
            ".jpg",
            ".jpeg",
            ".png",
            ".tif",
            ".tiff",
            ".dds"
        };

        public bool CanPreview(string filePath)
        {
            return SupportedExtensions.Contains(Path.GetExtension(filePath) ?? String.Empty);
        }

        public FilePreviewResult LoadPreview(FilePreviewRequest request, CancellationToken cancellationToken)
        {
            string extension = Path.GetExtension(request.FilePath) ?? String.Empty;
            if (String.Equals(extension, ".dds", StringComparison.OrdinalIgnoreCase))
                return FilePreviewResult.FromImage(LoadDdsBitmap(request.FilePath, cancellationToken));

            return FilePreviewResult.FromImage(LoadBitmap(request.FilePath, cancellationToken));
        }

        private static Bitmap LoadBitmap(string filePath, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            byte[] data = File.ReadAllBytes(filePath);
            cancellationToken.ThrowIfCancellationRequested();

            using (MemoryStream stream = new MemoryStream(data, false))
            using (Image image = Image.FromStream(stream, false, false))
            {
                ValidateDimensions(image.Width, image.Height, 4);
                return new Bitmap(image);
            }
        }

        private static Bitmap LoadDdsBitmap(string filePath, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            using (IImage image = Pfimage.FromFile(filePath))
            {
                cancellationToken.ThrowIfCancellationRequested();
                ValidateDimensions(image.Width, image.Height, 4);

                if (image.Format == Pfim.ImageFormat.Rgb8)
                    return ConvertRgb8ToBitmap(image);

                PixelFormat format = GetPixelFormat(image.Format);
                Bitmap bitmap = new Bitmap(image.Width, image.Height, format);
                BitmapData bitmapData = null;
                try
                {
                    Rectangle rectangle = new Rectangle(0, 0, image.Width, image.Height);
                    bitmapData = bitmap.LockBits(rectangle, ImageLockMode.WriteOnly, format);
                    CopyImageData(image, bitmapData);
                }
                catch
                {
                    bitmap.Dispose();
                    throw;
                }
                finally
                {
                    if (bitmapData != null)
                        bitmap.UnlockBits(bitmapData);
                }

                return bitmap;
            }
        }

        private static Bitmap ConvertRgb8ToBitmap(IImage image)
        {
            if (image == null)
                throw new ArgumentNullException("image");

            if (image.Width <= 0 || image.Height <= 0)
                throw new InvalidDataException("The DDS image has invalid dimensions.");

            if (image.Stride < image.Width)
                throw new InvalidDataException("The DDS image has an invalid row stride.");

            if (image.Data == null)
                throw new InvalidDataException("The DDS image contains no pixel data.");

            long requiredSourceLength = (long)(image.Height - 1) * image.Stride + image.Width;
            if (requiredSourceLength > image.Data.LongLength)
                throw new InvalidDataException("The DDS pixel buffer is incomplete.");

            Bitmap bitmap = null;
            BitmapData bitmapData = null;
            try
            {
                bitmap = new Bitmap(image.Width, image.Height, PixelFormat.Format32bppArgb);
                Rectangle bounds = new Rectangle(0, 0, image.Width, image.Height);
                bitmapData = bitmap.LockBits(bounds, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                byte[] destinationRow = new byte[checked(image.Width * 4)];

                for (int y = 0; y < image.Height; y++)
                {
                    int sourceOffset = checked(y * image.Stride);
                    for (int x = 0; x < image.Width; x++)
                    {
                        byte intensity = image.Data[sourceOffset + x];
                        int destinationOffset = x * 4;
                        destinationRow[destinationOffset] = intensity;
                        destinationRow[destinationOffset + 1] = intensity;
                        destinationRow[destinationOffset + 2] = intensity;
                        destinationRow[destinationOffset + 3] = 255;
                    }

                    IntPtr destinationAddress = IntPtr.Add(bitmapData.Scan0, checked(y * bitmapData.Stride));
                    Marshal.Copy(destinationRow, 0, destinationAddress, destinationRow.Length);
                }

                return bitmap;
            }
            catch
            {
                if (bitmap != null)
                    bitmap.Dispose();
                throw;
            }
            finally
            {
                if (bitmapData != null)
                    bitmap.UnlockBits(bitmapData);
            }
        }
        private static PixelFormat GetPixelFormat(Pfim.ImageFormat format)
        {
            switch (format)
            {
                case Pfim.ImageFormat.Rgb24:
                    return PixelFormat.Format24bppRgb;
                case Pfim.ImageFormat.Rgba32:
                    return PixelFormat.Format32bppArgb;
                case Pfim.ImageFormat.R5g5b5:
                    return PixelFormat.Format16bppRgb555;
                case Pfim.ImageFormat.R5g6b5:
                    return PixelFormat.Format16bppRgb565;
                default:
                    throw new NotSupportedException("DDS image format is not supported: " + format);
            }
        }

        private static void CopyImageData(IImage image, BitmapData bitmapData)
        {
            int sourceStride = image.Stride;
            int destinationStride = Math.Abs(bitmapData.Stride);
            int bytesPerRow = Math.Min(sourceStride, destinationStride);
            byte[] data = image.Data;
            IntPtr destination = bitmapData.Scan0;

            for (int y = 0; y < image.Height; y++)
            {
                Marshal.Copy(data, y * sourceStride, destination + y * bitmapData.Stride, bytesPerRow);
            }
        }

        private static void ValidateDimensions(int width, int height, int bytesPerPixel)
        {
            if (width <= 0 || height <= 0 || width > MaxDimension || height > MaxDimension)
                throw new InvalidOperationException("The selected image dimensions are too large to preview safely.");

            long decodedBytes = (long)width * height * bytesPerPixel;
            if (decodedBytes > MaxDecodedBytes)
                throw new InvalidOperationException("The selected image is too large to preview safely.");
        }
    }
}