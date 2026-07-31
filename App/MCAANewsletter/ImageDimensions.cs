using System;

namespace MCAANewsletter
{
    /// <summary>
    /// Reads pixel dimensions straight out of PNG and JPEG headers.
    ///
    /// Deliberately not System.Drawing: this runs over every media part in the
    /// document, and decoding a 4000px photo to learn two integers is wasteful.
    /// Anything that is not a PNG or JPEG (the .emf/.wmf/.wdp parts Word leaves
    /// behind) returns null and is skipped by the caller, which is the same
    /// behaviour the Python original had.
    /// </summary>
    public static class ImageDimensions
    {
        static readonly byte[] PngSignature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

        public static bool TryRead(byte[] b, out int width, out int height)
        {
            width = height = 0;
            if (b == null || b.Length < 24) return false;

            if (IsPng(b))
            {
                // IHDR is always the first chunk: width and height are big-endian
                // 32-bit values at offsets 16 and 20.
                width = ReadInt32Be(b, 16);
                height = ReadInt32Be(b, 20);
                return width > 0 && height > 0;
            }

            if (b[0] == 0xFF && b[1] == 0xD8) return TryReadJpeg(b, out width, out height);

            return false;
        }

        static bool IsPng(byte[] b)
        {
            for (int i = 0; i < PngSignature.Length; i++)
                if (b[i] != PngSignature[i]) return false;
            return true;
        }

        static bool TryReadJpeg(byte[] b, out int width, out int height)
        {
            width = height = 0;
            int i = 2;
            while (i < b.Length - 9)
            {
                if (b[i] != 0xFF) { i++; continue; }
                byte marker = b[i + 1];

                // Start-of-frame markers carry the dimensions; SOF4/SOF8/SOF12
                // (0xC4/0xC8/0xCC) are tables, not frames, and are excluded.
                if (marker == 0xC0 || marker == 0xC1 || marker == 0xC2 || marker == 0xC3 ||
                    marker == 0xC5 || marker == 0xC6 || marker == 0xC7 ||
                    marker == 0xC9 || marker == 0xCA || marker == 0xCB ||
                    marker == 0xCD || marker == 0xCE || marker == 0xCF)
                {
                    height = ReadInt16Be(b, i + 5);
                    width = ReadInt16Be(b, i + 7);
                    return width > 0 && height > 0;
                }

                // Standalone markers carry no length field.
                if (marker == 0xD8 || marker == 0xD9 || (marker >= 0xD0 && marker <= 0xD7))
                {
                    i += 2;
                    continue;
                }

                int segment = ReadInt16Be(b, i + 2);
                if (segment < 2) return false;      // malformed; give up rather than loop
                i += 2 + segment;
            }
            return false;
        }

        static int ReadInt32Be(byte[] b, int o) =>
            (b[o] << 24) | (b[o + 1] << 16) | (b[o + 2] << 8) | b[o + 3];

        static int ReadInt16Be(byte[] b, int o) => (b[o] << 8) | b[o + 1];
    }
}
