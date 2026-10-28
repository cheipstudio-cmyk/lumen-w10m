using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.Security.Cryptography;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;

namespace Lumen.Core
{
    /// <summary>Picks, downsizes and stores images so each one costs ~100 KB of tokens and RAM, not 5 MB.</summary>
    public static class ImageHelper
    {
        private const uint MaxSide = 1024;

        public static async Task<StorageFolder> ImagesFolderAsync()
        {
            return await ApplicationData.Current.LocalFolder.CreateFolderAsync("images", CreationCollisionOption.OpenIfExists);
        }

        public static async Task<StorageFile> PickAsync()
        {
            var picker = new FileOpenPicker
            {
                ViewMode = PickerViewMode.Thumbnail,
                SuggestedStartLocation = PickerLocationId.PicturesLibrary
            };
            picker.FileTypeFilter.Add(".jpg");
            picker.FileTypeFilter.Add(".jpeg");
            picker.FileTypeFilter.Add(".png");
            picker.FileTypeFilter.Add(".bmp");
            picker.FileTypeFilter.Add(".gif");
            return await picker.PickSingleFileAsync();
        }

        /// <summary>Re-encodes the image as JPEG (max 1024 px, EXIF orientation applied) into LocalFolder\images. Returns the file name.</summary>
        public static async Task<string> ImportAsync(IStorageFile source)
        {
            using (var input = await source.OpenAsync(FileAccessMode.Read))
            {
                var decoder = await BitmapDecoder.CreateAsync(input);
                uint w = decoder.OrientedPixelWidth, h = decoder.OrientedPixelHeight;
                double scale = Math.Min(1.0, (double)MaxSide / Math.Max(w, h));

                var transform = new BitmapTransform
                {
                    InterpolationMode = BitmapInterpolationMode.Fant
                };
                // Scaled size refers to the un-rotated frame.
                transform.ScaledWidth = (uint)Math.Max(1, Math.Round(decoder.PixelWidth * scale));
                transform.ScaledHeight = (uint)Math.Max(1, Math.Round(decoder.PixelHeight * scale));

                var bitmap = await decoder.GetSoftwareBitmapAsync(
                    BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, transform,
                    ExifOrientationMode.RespectExifOrientation, ColorManagementMode.DoNotColorManage);

                var folder = await ImagesFolderAsync();
                var name = Guid.NewGuid().ToString("N") + ".jpg";
                var file = await folder.CreateFileAsync(name, CreationCollisionOption.ReplaceExisting);

                using (var output = await file.OpenAsync(FileAccessMode.ReadWrite))
                {
                    var props = new BitmapPropertySet();
                    props.Add("ImageQuality", new BitmapTypedValue(0.82, PropertyType.Single));
                    var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, output, props);
                    encoder.SetSoftwareBitmap(bitmap);
                    await encoder.FlushAsync();
                }
                bitmap.Dispose();
                return name;
            }
        }

        public static async Task<string> ReadBase64Async(string fileName)
        {
            try
            {
                var folder = await ImagesFolderAsync();
                var file = await folder.GetFileAsync(fileName);
                IBuffer buf = await FileIO.ReadBufferAsync(file);
                return CryptographicBuffer.EncodeToBase64String(buf);
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static async Task DeleteAsync(IEnumerable<string> fileNames)
        {
            var folder = await ImagesFolderAsync();
            foreach (var n in fileNames)
            {
                if (string.IsNullOrEmpty(n)) continue;
                try
                {
                    var f = await folder.TryGetItemAsync(n);
                    if (f != null) await f.DeleteAsync(StorageDeleteOption.PermanentDelete);
                }
                catch (Exception) { }
            }
        }
    }
}
