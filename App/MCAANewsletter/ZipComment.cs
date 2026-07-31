using System;
using System.IO;
using System.Text;

namespace MCAANewsletter
{
    /// <summary>
    /// Reads and writes the zip end-of-central-directory comment.
    ///
    /// System.IO.Compression cannot do either — ZipArchive has no Comment member —
    /// so the record is edited directly. This is worth the trouble because the 34
    /// documents already in Published/ carry a stamp written by the original
    /// Python pipeline, and the app should agree with what is already on disk.
    ///
    /// The comment sits outside the OPC package: no OOXML reader ever sees it, so
    /// stamping a document changes nothing about how Word opens it. Word drops the
    /// comment when it rewrites the zip on save, which is the correct outcome —
    /// a Word save re-inflates the images and the file genuinely is dirty again.
    /// </summary>
    public static class ZipComment
    {
        // End of central directory: signature, 18 bytes of fields, 2-byte comment
        // length, then the comment.
        const uint EocdSignature = 0x06054B50;
        const int EocdMinSize = 22;
        const int MaxCommentLength = 0xFFFF;

        public static void Write(string zipPath, string comment)
        {
            byte[] text = Encoding.ASCII.GetBytes(comment ?? string.Empty);
            if (text.Length > MaxCommentLength)
                throw new ArgumentException("comment too long for a zip record", nameof(comment));

            using (var fs = new FileStream(zipPath, FileMode.Open, FileAccess.ReadWrite))
            {
                long eocd = FindEocd(fs);
                if (eocd < 0) throw new InvalidDataException("no end-of-central-directory record in " + zipPath);

                // Truncate any existing comment, then write the new length and body.
                fs.SetLength(eocd + EocdMinSize);
                fs.Seek(eocd + EocdMinSize - 2, SeekOrigin.Begin);
                fs.WriteByte((byte)(text.Length & 0xFF));
                fs.WriteByte((byte)((text.Length >> 8) & 0xFF));
                fs.Write(text, 0, text.Length);
            }
        }

        public static string Read(string zipPath)
        {
            using (var fs = new FileStream(zipPath, FileMode.Open, FileAccess.Read))
            {
                long eocd = FindEocd(fs);
                if (eocd < 0) return null;

                fs.Seek(eocd + EocdMinSize - 2, SeekOrigin.Begin);
                int lo = fs.ReadByte(), hi = fs.ReadByte();
                if (lo < 0 || hi < 0) return null;

                int length = lo | (hi << 8);
                if (length == 0) return string.Empty;

                var buffer = new byte[length];
                int read = fs.Read(buffer, 0, length);
                return Encoding.ASCII.GetString(buffer, 0, read);
            }
        }

        /// <summary>
        /// Scans backwards for the EOCD signature. The record is last in the file
        /// apart from its own comment, so the search window is the 22-byte record
        /// plus the largest comment a zip can hold.
        /// </summary>
        static long FindEocd(FileStream fs)
        {
            long length = fs.Length;
            if (length < EocdMinSize) return -1;

            int window = (int)Math.Min(length, EocdMinSize + MaxCommentLength);
            var buffer = new byte[window];
            fs.Seek(length - window, SeekOrigin.Begin);
            ReadExactly(fs, buffer, window);

            long origin = length - window;
            for (int i = window - EocdMinSize; i >= 0; i--)
            {
                uint sig = (uint)(buffer[i] | (buffer[i + 1] << 8) | (buffer[i + 2] << 16) | (buffer[i + 3] << 24));
                if (sig != EocdSignature) continue;

                // Guard against the signature appearing inside compressed data:
                // the declared comment length must reach exactly the end of file.
                int commentLength = buffer[i + EocdMinSize - 2] | (buffer[i + EocdMinSize - 1] << 8);
                if (origin + i + EocdMinSize + commentLength == length) return origin + i;
            }
            return -1;
        }

        static void ReadExactly(Stream s, byte[] buffer, int count)
        {
            int offset = 0;
            while (offset < count)
            {
                int n = s.Read(buffer, offset, count - offset);
                if (n <= 0) throw new EndOfStreamException();
                offset += n;
            }
        }
    }
}
