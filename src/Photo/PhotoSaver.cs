using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace GorillaPhone.Photo
{
    /// <summary>One photo to process. Rgba is width*height*4 bytes as read back from the GPU.</summary>
    public sealed class PhotoJob
    {
        public byte[] Rgba;
        public int Width;
        public int Height;
        /// <summary>The first row of Rgba is the bottom of the picture (true on OpenGL-style layouts).</summary>
        public bool FlipVertical;
        /// <summary>Mirror left to right (selfie).</summary>
        public bool MirrorHorizontal;
        public string Folder;
        public string FileName;
        public int ThumbHeight = 160;
    }

    public sealed class PhotoResult
    {
        public string Path;
        public string Error;
        /// <summary>Small copy of the photo, RGBA, first row = top of the picture.</summary>
        public byte[] ThumbRgba;
        public int ThumbWidth;
        public int ThumbHeight;
        public int Width;
        public int Height;
        public long Milliseconds;
        public long Bytes;
    }

    /// <summary>
    /// Everything after the GPU readback: orientation, PNG encoding, the file write and the
    /// thumbnail. No Unity calls, so it runs on a background thread and never touches the main thread.
    /// </summary>
    public static class PhotoSaver
    {
        public static void SaveAsync(PhotoJob job, Action<PhotoResult> done)
        {
            var t = new Thread(() => done(Save(job)))
            {
                IsBackground = true,
                Name = "GorillaPhone photo",
                Priority = ThreadPriority.BelowNormal
            };
            t.Start();
        }

        public static PhotoResult Save(PhotoJob job)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                int w = job.Width, h = job.Height;
                byte[] px = job.Rgba;
                if (px == null || px.Length != w * h * 4)
                    throw new InvalidOperationException("pixel buffer is " + (px == null ? 0 : px.Length) + " bytes, expected " + (long)w * h * 4);

                Arrange(px, w, h, job.FlipVertical, job.MirrorHorizontal);

                Directory.CreateDirectory(job.Folder);
                string finalPath = Path.Combine(job.Folder, job.FileName);
                string tmpPath = finalPath + ".tmp";
                long bytes;
                using (var fs = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    PngWriter.WriteRgb(fs, px, w, h);
                    bytes = fs.Length;
                }
                if (File.Exists(finalPath)) File.Delete(finalPath);
                File.Move(tmpPath, finalPath);

                var r = new PhotoResult { Path = finalPath, Width = w, Height = h, Bytes = bytes };
                r.ThumbHeight = Math.Max(1, job.ThumbHeight);
                r.ThumbWidth = Math.Max(1, (int)((long)w * r.ThumbHeight / h));
                r.ThumbRgba = Downscale(px, w, h, r.ThumbWidth, r.ThumbHeight);
                r.Milliseconds = sw.ElapsedMilliseconds;
                return r;
            }
            catch (Exception e)
            {
                return new PhotoResult { Error = e.GetType().Name + ": " + e.Message, Milliseconds = sw.ElapsedMilliseconds };
            }
        }

        /// <summary>Puts the picture the right way up and round, and makes the alpha channel opaque (the camera's alpha is not meaningful).</summary>
        public static void Arrange(byte[] p, int w, int h, bool flipVertical, bool mirrorHorizontal)
        {
            int stride = w * 4;
            if (flipVertical)
            {
                var tmp = new byte[stride];
                for (int y = 0; y < h / 2; y++)
                {
                    int a = y * stride, b = (h - 1 - y) * stride;
                    Buffer.BlockCopy(p, a, tmp, 0, stride);
                    Buffer.BlockCopy(p, b, p, a, stride);
                    Buffer.BlockCopy(tmp, 0, p, b, stride);
                }
            }
            for (int y = 0; y < h; y++)
            {
                int row = y * stride;
                if (mirrorHorizontal)
                {
                    for (int x = 0; x < w / 2; x++)
                    {
                        int i = row + x * 4, j = row + (w - 1 - x) * 4;
                        for (int c = 0; c < 4; c++)
                        {
                            byte t = p[i + c];
                            p[i + c] = p[j + c];
                            p[j + c] = t;
                        }
                    }
                }
                for (int x = 0; x < w; x++) p[row + x * 4 + 3] = 255;
            }
        }

        /// <summary>Box-filter downscale of an RGBA picture (first row = top).</summary>
        public static byte[] Downscale(byte[] src, int sw, int sh, int dw, int dh)
        {
            var dst = new byte[dw * dh * 4];
            for (int y = 0; y < dh; y++)
            {
                int y0 = (int)((long)y * sh / dh);
                int y1 = Math.Max(y0 + 1, (int)((long)(y + 1) * sh / dh));
                for (int x = 0; x < dw; x++)
                {
                    int x0 = (int)((long)x * sw / dw);
                    int x1 = Math.Max(x0 + 1, (int)((long)(x + 1) * sw / dw));
                    int r = 0, g = 0, b = 0, n = 0;
                    for (int yy = y0; yy < y1; yy++)
                    {
                        int i = (yy * sw + x0) * 4;
                        for (int xx = x0; xx < x1; xx++, i += 4)
                        {
                            r += src[i];
                            g += src[i + 1];
                            b += src[i + 2];
                            n++;
                        }
                    }
                    int o = (y * dw + x) * 4;
                    dst[o] = (byte)(r / n);
                    dst[o + 1] = (byte)(g / n);
                    dst[o + 2] = (byte)(b / n);
                    dst[o + 3] = 255;
                }
            }
            return dst;
        }
    }
}
