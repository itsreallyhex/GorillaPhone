using System;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace GorillaPhone.Photo
{
    /// <summary>What can be read from a PNG without decoding the picture.</summary>
    public sealed class PngInfo
    {
        public int Width;
        public int Height;
        /// <summary>The value of the tEXt chunk stored under PngWriter.CommentKeyword, or null.</summary>
        public string Comment;
    }

    /// <summary>
    /// A small PNG decoder for the gallery: 8-bit RGB and RGBA, not interlaced, all five row filters
    /// (so it also reads PNGs written by other programs in that format). Needs nothing from Unity, so it
    /// runs on a background thread and can be tested outside the game. Anything else gives an error
    /// message instead of an exception.
    /// </summary>
    public static class PngReader
    {
        static readonly byte[] Signature = { 137, 80, 78, 71, 13, 10, 26, 10 };

        /// <summary>Reads only the header and text chunks (everything before the image data). Cheap enough for every file in a folder.</summary>
        public static bool TryReadInfo(string path, out PngInfo info)
        {
            info = null;
            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    var sig = new byte[8];
                    if (fs.Read(sig, 0, 8) != 8 || !SignatureOk(sig, 0)) return false;

                    var r = new PngInfo();
                    var head = new byte[8];
                    while (fs.Read(head, 0, 8) == 8)
                    {
                        int len = ReadBE(head, 0);
                        string type = Encoding.ASCII.GetString(head, 4, 4);
                        if (type == "IDAT" || type == "IEND") break;
                        if (len < 0 || len > 1 << 20) return false;   // text and header chunks are small
                        var data = new byte[len];
                        int got = 0;
                        while (got < len)
                        {
                            int n = fs.Read(data, got, len - got);
                            if (n <= 0) return false;
                            got += n;
                        }
                        fs.Seek(4, SeekOrigin.Current);   // the chunk's CRC
                        if (type == "IHDR" && len >= 8) { r.Width = ReadBE(data, 0); r.Height = ReadBE(data, 4); }
                        else if (type == "tEXt") ReadText(data, 0, len, r);
                    }
                    info = r;
                    return r.Width > 0 && r.Height > 0;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>Decodes the whole picture to RGBA (width*height*4, first row = top).</summary>
        public static bool TryDecode(string path, out int width, out int height, out byte[] rgba, out PngInfo info, out string error)
        {
            width = 0; height = 0; rgba = null; info = null; error = null;
            try
            {
                byte[] file = File.ReadAllBytes(path);
                if (file.Length < 8 || !SignatureOk(file, 0)) { error = "not a PNG file"; return false; }

                var r = new PngInfo();
                int bitDepth = 0, colorType = -1, interlace = 0;
                var idat = new MemoryStream(file.Length);
                int pos = 8;
                while (pos + 12 <= file.Length)
                {
                    int len = ReadBE(file, pos);
                    string type = Encoding.ASCII.GetString(file, pos + 4, 4);
                    int dataPos = pos + 8;
                    if (len < 0 || (long)dataPos + len + 4 > file.Length) { error = "the PNG file is cut off"; return false; }
                    if (type == "IHDR" && len >= 13)
                    {
                        r.Width = ReadBE(file, dataPos);
                        r.Height = ReadBE(file, dataPos + 4);
                        bitDepth = file[dataPos + 8];
                        colorType = file[dataPos + 9];
                        interlace = file[dataPos + 12];
                    }
                    else if (type == "tEXt") ReadText(file, dataPos, len, r);
                    else if (type == "IDAT") idat.Write(file, dataPos, len);
                    else if (type == "IEND") break;
                    pos = dataPos + len + 4;
                }

                if (r.Width <= 0 || r.Height <= 0) { error = "no image header"; return false; }
                if (bitDepth != 8 || (colorType != 2 && colorType != 6) || interlace != 0)
                {
                    error = "unsupported PNG (depth " + bitDepth + ", colour type " + colorType + ", interlace " + interlace + ")";
                    return false;
                }

                int w = r.Width, h = r.Height;
                int bpp = colorType == 2 ? 3 : 4;
                long strideL = (long)w * bpp;
                long rawLenL = (strideL + 1) * h;
                if (rawLenL > 400L * 1024 * 1024) { error = "picture too large"; return false; }
                int stride = (int)strideL;
                var raw = new byte[(int)rawLenL];

                if (idat.Length < 3) { error = "no image data"; return false; }
                idat.Position = 2;   // skip the two-byte zlib header
                using (var ds = new DeflateStream(idat, CompressionMode.Decompress, true))
                {
                    int got = 0;
                    while (got < raw.Length)
                    {
                        int n = ds.Read(raw, got, raw.Length - got);
                        if (n <= 0) break;
                        got += n;
                    }
                    if (got != raw.Length) { error = "the image data is too short"; return false; }
                }

                // Undo the row filters in place.
                for (int y = 0; y < h; y++)
                {
                    int o = y * (stride + 1);
                    int filter = raw[o];
                    int c = o + 1;
                    int p = y > 0 ? (y - 1) * (stride + 1) + 1 : -1;
                    switch (filter)
                    {
                        case 0:
                            break;
                        case 1:
                            for (int i = bpp; i < stride; i++) raw[c + i] = (byte)(raw[c + i] + raw[c + i - bpp]);
                            break;
                        case 2:
                            if (p >= 0) for (int i = 0; i < stride; i++) raw[c + i] = (byte)(raw[c + i] + raw[p + i]);
                            break;
                        case 3:
                            for (int i = 0; i < stride; i++)
                            {
                                int left = i >= bpp ? raw[c + i - bpp] : 0;
                                int up = p >= 0 ? raw[p + i] : 0;
                                raw[c + i] = (byte)(raw[c + i] + ((left + up) >> 1));
                            }
                            break;
                        case 4:
                            for (int i = 0; i < stride; i++)
                            {
                                int a = i >= bpp ? raw[c + i - bpp] : 0;
                                int b = p >= 0 ? raw[p + i] : 0;
                                int cc = (i >= bpp && p >= 0) ? raw[p + i - bpp] : 0;
                                raw[c + i] = (byte)(raw[c + i] + Paeth(a, b, cc));
                            }
                            break;
                        default:
                            error = "bad row filter " + filter;
                            return false;
                    }
                }

                var outPx = new byte[w * h * 4];
                for (int y = 0; y < h; y++)
                {
                    int src = y * (stride + 1) + 1;
                    int dst = y * w * 4;
                    if (bpp == 4)
                    {
                        Buffer.BlockCopy(raw, src, outPx, dst, w * 4);
                    }
                    else
                    {
                        for (int x = 0; x < w; x++)
                        {
                            outPx[dst++] = raw[src++];
                            outPx[dst++] = raw[src++];
                            outPx[dst++] = raw[src++];
                            outPx[dst++] = 255;
                        }
                    }
                }

                width = w; height = h; rgba = outPx; info = r;
                return true;
            }
            catch (Exception e)
            {
                error = e.GetType().Name + ": " + e.Message;
                return false;
            }
        }

        static int Paeth(int a, int b, int c)
        {
            int p = a + b - c;
            int pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
            if (pa <= pb && pa <= pc) return a;
            return pb <= pc ? b : c;
        }

        static void ReadText(byte[] d, int offset, int len, PngInfo info)
        {
            int zero = -1;
            for (int i = 0; i < len; i++) if (d[offset + i] == 0) { zero = i; break; }
            if (zero <= 0) return;
            string keyword = Encoding.ASCII.GetString(d, offset, zero);
            if (keyword != PngWriter.CommentKeyword) return;
            var sb = new StringBuilder(len - zero);
            for (int i = zero + 1; i < len; i++) sb.Append((char)d[offset + i]);   // Latin-1
            info.Comment = sb.ToString();
        }

        static bool SignatureOk(byte[] b, int offset)
        {
            for (int i = 0; i < 8; i++) if (b[offset + i] != Signature[i]) return false;
            return true;
        }

        static int ReadBE(byte[] b, int o)
        {
            return (b[o] << 24) | (b[o + 1] << 16) | (b[o + 2] << 8) | b[o + 3];
        }
    }
}
