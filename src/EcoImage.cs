using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace XiaoYaOpt
{
    /// <summary>
    /// ECO 圖示檔（skillicon.dat 裡的 .tga，不是標準 TGA）。
    /// 24 bytes 檔頭：u32 格式（0 = 16 位元 ARGB1555，2 = 32 位元 BGRA）、u16 寬、u16 高、…、u32 像素資料大小（位移 20）。
    /// </summary>
    internal static class EcoImage
    {
        public static Bitmap Decode(byte[] data)
        {
            if (data == null || data.Length < 24) return null;
            int format = BitConverter.ToInt32(data, 0);
            int w = BitConverter.ToUInt16(data, 4), h = BitConverter.ToUInt16(data, 6);
            int bpp = format == 0 ? 2 : format == 2 ? 4 : 0;
            if (bpp == 0 || w <= 0 || h <= 0 || w > 1024 || h > 1024 || data.Length < 24 + w * h * bpp) return null;

            var argb = new int[w * h];
            for (int i = 0; i < w * h; i++)
            {
                int o = 24 + i * bpp;
                if (bpp == 2)
                {
                    int v = data[o] | (data[o + 1] << 8);
                    int a = (v & 0x8000) != 0 ? 255 : 0;
                    int r = (v >> 10) & 0x1f, g = (v >> 5) & 0x1f, b = v & 0x1f;
                    argb[i] = (a << 24) | ((r << 3 | r >> 2) << 16) | ((g << 3 | g >> 2) << 8) | (b << 3 | b >> 2);
                }
                else
                {
                    argb[i] = (data[o + 3] << 24) | (data[o + 2] << 16) | (data[o + 1] << 8) | data[o];
                }
            }
            var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            var bd = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            for (int y = 0; y < h; y++)
                Marshal.Copy(argb, y * w, bd.Scan0 + y * bd.Stride, w);
            bmp.UnlockBits(bd);
            return bmp;
        }
    }
}
