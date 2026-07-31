using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace MCAANewsletter
{
    /// <summary>
    /// Downsampling and re-encoding, applied to the copy that goes into Published/.
    ///
    /// This is the one operation with a genuine tradeoff. It cannot move anything
    /// on the page — display geometry lives in word/document.xml and is never
    /// touched — but it does reduce the stored resolution of a photo.
    ///
    /// Her working draft is never re-encoded, so the full-resolution original is
    /// always still in Drafts/ if a photo ever needs to be recovered.
    /// </summary>
    public static class ImageReducer
    {
        static readonly string[] Convertible = { ".png", ".jpg", ".jpeg", ".bmp", ".tif", ".tiff" };

        /// <summary>
        /// Returns the reduced bytes, or null to keep the original untouched.
        /// </summary>
        public static byte[] TryReduce(byte[] raw, string extension, int maxEdge, int quality,
                                       int minPhoto, out string newExtension)
        {
            newExtension = extension;
            if (raw == null || string.IsNullOrEmpty(extension)) return null;
            if (!Convertible.Contains(extension.ToLowerInvariant())) return null;

            try
            {
                // GDI+ reads lazily, so the source stream has to outlive the Image.
                using (var source = new MemoryStream(raw, false))
                using (var image = Image.FromStream(source, false, false))
                {
                    int width = image.Width, height = image.Height;

                    // Small enough already: an icon, a rule, or a decorative pill.
                    if (Math.Min(width, height) < minPhoto && Math.Max(width, height) <= maxEdge)
                        return null;

                    bool keepPng = HasMeaningfulAlpha(image);
                    bool needsResize = Math.Max(width, height) > maxEdge;
                    string e = extension.ToLowerInvariant();
                    bool alreadyJpeg = e == ".jpg" || e == ".jpeg";

                    // Already at or under printing size, and already in the format it
                    // would end up in. Re-encoding here would buy nothing and cost a
                    // little quality every time.
                    //
                    // This matters because the tool runs monthly, not once. If the
                    // master is carried forward from a published issue, its photos
                    // arrive already reduced — and Word drops the processed-stamp
                    // whenever it saves, so the stamp alone cannot be relied on to
                    // stop a second pass. Judging by the image itself always can.
                    if (!needsResize && (alreadyJpeg || keepPng)) return null;

                    int targetWidth = width, targetHeight = height;
                    if (needsResize)
                    {
                        double scale = (double)maxEdge / Math.Max(width, height);
                        targetWidth = Math.Max(1, (int)Math.Round(width * scale));
                        targetHeight = Math.Max(1, (int)Math.Round(height * scale));
                    }

                    byte[] encoded = Encode(image, targetWidth, targetHeight, keepPng, quality);
                    if (encoded == null) return null;

                    string produced = keepPng ? ".png" : ".jpeg";

                    // No point rewriting a part to make it bigger.
                    if (encoded.Length >= raw.Length &&
                        string.Equals(produced, extension, StringComparison.OrdinalIgnoreCase))
                        return null;

                    newExtension = produced;
                    return encoded;
                }
            }
            catch
            {
                // An image GDI+ will not decode is left exactly as it is.
                return null;
            }
        }

        static byte[] Encode(Image image, int width, int height, bool keepPng, int quality)
        {
            var format = keepPng ? PixelFormat.Format32bppArgb : PixelFormat.Format24bppRgb;
            using (var canvas = new Bitmap(width, height, format))
            {
                using (var g = Graphics.FromImage(canvas))
                {
                    g.CompositingMode = CompositingMode.SourceCopy;
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.SmoothingMode = SmoothingMode.HighQuality;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;

                    // JPEG has no alpha, so anything transparent must land on white
                    // rather than on the uninitialised black a new bitmap starts as.
                    if (!keepPng) g.Clear(Color.White);
                    g.DrawImage(image, new Rectangle(0, 0, width, height));
                }

                using (var output = new MemoryStream())
                {
                    if (keepPng)
                    {
                        canvas.Save(output, ImageFormat.Png);
                    }
                    else
                    {
                        ImageCodecInfo jpeg = ImageCodecInfo.GetImageEncoders()
                            .FirstOrDefault(c => c.FormatID == ImageFormat.Jpeg.Guid);
                        if (jpeg == null) return null;

                        using (var parameters = new EncoderParameters(1))
                        {
                            parameters.Param[0] = new EncoderParameter(Encoder.Quality, (long)quality);
                            canvas.Save(output, jpeg, parameters);
                        }
                    }
                    return output.ToArray();
                }
            }
        }

        /// <summary>
        /// True only if transparency is actually used. A PNG flagged as having an
        /// alpha channel but with every pixel opaque is just a large JPEG waiting
        /// to happen — which is most of what was in this archive.
        /// </summary>
        static bool HasMeaningfulAlpha(Image image)
        {
            if (!Image.IsAlphaPixelFormat(image.PixelFormat)) return false;

            using (var bitmap = new Bitmap(image.Width, image.Height, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(bitmap))
                {
                    g.CompositingMode = CompositingMode.SourceCopy;
                    g.DrawImage(image, new Rectangle(0, 0, image.Width, image.Height));
                }

                BitmapData data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height),
                                                  ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                try
                {
                    var row = new byte[data.Stride];
                    for (int y = 0; y < data.Height; y++)
                    {
                        Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, data.Stride);
                        for (int x = 0; x < data.Width; x++)
                            if (row[x * 4 + 3] < 250) return true;   // BGRA: alpha last
                    }
                }
                finally { bitmap.UnlockBits(data); }
            }
            return false;
        }
    }
}
