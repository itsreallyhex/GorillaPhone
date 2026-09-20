using System;
using System.IO;
using System.IO.Compression;

namespace GorillaPhone.Photo
{
    /// <summary>
    /// A small PNG encoder (8-bit RGB) that needs nothing from Unity, so it can run on a background
    /// thread and be tested outside the game. Unity's own EncodeToPNG has to run on the main thread.
    /// </summary>
    public static class PngWriter
    {
        static readonly byte[] Signature = { 137, 80, 78, 71, 13, 10, 26, 10 };
        static uint[] crcTable;

        /// <summary>The PNG text keyword the phone stores its photo details under (camera and map).</summary>
        public const string CommentKeyword = "GorillaPhone";

        public static void WriteRgb(Stream output, byte[] rgba, int width, int height)
        {
            WriteRgb(output, rgba, width, height, null);
        }

        /// <summary>
        /// Writes rgba (width*height*4 bytes, first row = top of the image) as an RGB PNG. Alpha is ignored.
        /// A non-empty comment is stored in a standard tEXt chunk before the image data (other viewers ignore it).
        /// </summary>
        public static void WriteRgb(Stream output, byte[] rgba, int width, int height, string comment)
        {
            if (width <= 0 || height <= 0) throw new ArgumentException("bad size");
            if (rgba.Length < width * height * 4) throw new ArgumentException("pixel buffer too small");

            // Each scanline is a filter byte (0 = none) followed by its RGB bytes.
            var raw = new byte[(1 + width * 3) * height];
            int ri = 0, si = 0;
            for (int y = 0; y < height; y++)
            {
                raw[ri++] = 0;
                for (int x = 0; x < width; x++)
                {
                    raw[ri++] = rgba[si];
                    raw[ri++] = rgba[si + 1];
                    raw[ri++] = rgba[si + 2];
                    si += 4;
                }
            }

            byte[] idat = Zlib(raw);

            output.Write(Signature, 0, Signature.Length);

            var ihdr = new byte[13];
            WriteBE(ihdr, 0, (uint)width);
            WriteBE(ihdr, 4, (uint)height);
            ihdr[8] = 8;    // bit depth
            ihdr[9] = 2;    // colour type: RGB
            ihdr[10] = 0;   // deflate
            ihdr[11] = 0;   // adaptive filtering
            ihdr[12] = 0;   // no interlace
            Chunk(output, "IHDR", ihdr);
            if (!string.IsNullOrEmpty(comment)) Chunk(output, "tEXt", TextChunk(CommentKeyword, comment));
            Chunk(output, "IDAT", idat);
            Chunk(output, "IEND", new byte[0]);
        }

        /// <summary>A tEXt chunk body: keyword, a zero byte, then the text (Latin-1; anything outside it becomes '?').</summary>
        static byte[] TextChunk(string keyword, string text)
        {
            var b = new byte[keyword.Length + 1 + text.Length];
            int i = 0;
            foreach (char c in keyword) b[i++] = (byte)(c < 256 ? c : '?');
            b[i++] = 0;
            foreach (char c in text) b[i++] = (byte)(c < 256 ? c : '?');
            return b;
        }

        static byte[] Zlib(byte[] data)
        {
            using (var ms = new MemoryStream(data.Length / 2 + 64))
            {
                ms.WriteByte(0x78);   // zlib header: deflate, 32K window; 0x7801 is a valid check value
                ms.WriteByte(0x01);
                using (var ds = new DeflateStream(ms, CompressionLevel.Fastest, true))
                    ds.Write(data, 0, data.Length);

                var adler = new byte[4];
                WriteBE(adler, 0, Adler32(data));
                ms.Write(adler, 0, 4);
                return ms.ToArray();
            }
        }

        static uint Adler32(byte[] data)
        {
            uint a = 1, b = 0;
            int i = 0;
            while (i < data.Length)
            {
                int n = Math.Min(3800, data.Length - i);   // small enough that the sums cannot overflow before the modulo
                for (int k = 0; k < n; k++)
                {
                    a += data[i + k];
                    b += a;
                }
                a %= 65521;
                b %= 65521;
                i += n;
            }
            return (b << 16) | a;
        }

        static void Chunk(Stream o, string type, byte[] data)
        {
            var len = new byte[4];
            WriteBE(len, 0, (uint)data.Length);
            o.Write(len, 0, 4);

            var t = new[] { (byte)type[0], (byte)type[1], (byte)type[2], (byte)type[3] };
            o.Write(t, 0, 4);
            if (data.Length > 0) o.Write(data, 0, data.Length);

            uint crc = Crc(0xFFFFFFFFu, t, t.Length);
            crc = Crc(crc, data, data.Length) ^ 0xFFFFFFFFu;
            var c = new byte[4];
            WriteBE(c, 0, crc);
            o.Write(c, 0, 4);
        }

        static uint Crc(uint crc, byte[] data, int length)
        {
            if (crcTable == null)
            {
                var table = new uint[256];
                for (uint n = 0; n < 256; n++)
                {
                    uint c = n;
                    for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                    table[n] = c;
                }
                crcTable = table;
            }
            for (int i = 0; i < length; i++) crc = crcTable[(crc ^ data[i]) & 0xFF] ^ (crc >> 8);
            return crc;
        }

        static void WriteBE(byte[] b, int offset, uint v)
        {
            b[offset] = (byte)(v >> 24);
            b[offset + 1] = (byte)(v >> 16);
            b[offset + 2] = (byte)(v >> 8);
            b[offset + 3] = (byte)v;
        }
    }
}
