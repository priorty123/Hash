using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using HashDash.Helpers;
using HashDash.Models;

namespace HashDash.Services
{
    /// <summary>
    /// Produces small preview images for the Thumbnail view. Never writes to a source:
    /// - Local/network files: Windows Shell thumbnails.
    /// - iOS files: the small JPEG the Photos app already keeps under PhotoData/Thumbnails,
    ///   falling back to decoding the original for images.
    /// - MTP files: decodes the original for images.
    /// </summary>
    public sealed class ThumbnailService
    {
        public const int Size = 160;
        private const long MaxDecodeBytes = 60L * 1024 * 1024;

        private readonly SemaphoreSlim _gate = new(2);
        private readonly Func<FileHashEntry, Stream> _openDevice;
        private readonly Func<string, Stream> _openIosPath;

        public ThumbnailService(Func<FileHashEntry, Stream> openDevice, Func<string, Stream> openIosPath)
        {
            _openDevice  = openDevice;
            _openIosPath = openIosPath;
        }

        public async Task<ImageSource?> LoadAsync(FileHashEntry entry, CancellationToken token)
        {
            await _gate.WaitAsync(token);
            try
            {
                return await Task.Run(() => Load(entry), token);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                return null;
            }
            finally
            {
                _gate.Release();
            }
        }

        private ImageSource? Load(FileHashEntry entry)
        {
            if (!entry.IsDeviceSource)
                return ShellThumbnail.Get(entry.FullPath, Size);

            string ext = Path.GetExtension(entry.FileName);

            if (entry.IsIosSource && entry.IosDevicePath != null &&
                (entry.LibraryRole == "Original" || ThumbnailTile.IsImage(ext) || ThumbnailTile.IsVideo(ext)))
            {
                try
                {
                    using var thumb = _openIosPath(IosPhotoLibraryService.ThumbnailPathFor(entry.IosDevicePath));
                    var image = Decode(thumb);
                    if (image != null) return image;
                }
                catch
                {
                    // No Photos thumbnail for this file — fall through to the original
                }
            }

            if (!ThumbnailTile.IsImage(ext) || entry.FileSizeBytes > MaxDecodeBytes)
                return null;

            using var original = _openDevice(entry);
            return Decode(original);
        }

        private static BitmapSource? Decode(Stream source)
        {
            // Device streams are forward-only; WIC needs a seekable stream.
            using var buffer = new MemoryStream();
            source.CopyTo(buffer);
            buffer.Position = 0;

            var decoder = BitmapDecoder.Create(buffer, BitmapCreateOptions.IgnoreColorProfile | BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
            var frame   = decoder.Frames[0];
            int rotation = OrientationToRotation(frame.Metadata as BitmapMetadata);

            buffer.Position = 0;
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption      = BitmapCacheOption.OnLoad;
            image.CreateOptions    = BitmapCreateOptions.IgnoreColorProfile;
            image.DecodePixelWidth = Size;
            image.StreamSource     = buffer;
            image.EndInit();

            BitmapSource result = rotation == 0
                ? image
                : new TransformedBitmap(image, new RotateTransform(rotation));
            result.Freeze();
            return result;
        }

        /// <summary>EXIF orientation → clockwise rotation (mirrored variants are rare from phone cameras and shown unmirrored).</summary>
        private static int OrientationToRotation(BitmapMetadata? metadata)
        {
            try
            {
                object? value = metadata?.GetQuery("/app1/ifd/{ushort=274}");
                return value is ushort o
                    ? o switch { 3 or 4 => 180, 5 or 6 => 90, 7 or 8 => 270, _ => 0 }
                    : 0;
            }
            catch
            {
                return 0;
            }
        }
    }
}
