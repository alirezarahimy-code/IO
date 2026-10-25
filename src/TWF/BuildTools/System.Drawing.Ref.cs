using System;
using System.IO;

namespace System.Drawing.Imaging
{
    public sealed class ImageFormat
    {
        public static ImageFormat Png { get; } = new ImageFormat();
        public static ImageFormat Jpeg { get; } = new ImageFormat();
    }
    public class ColorMatrix { }
    public class ImageAttributes : IDisposable { public void Dispose() { } }
}

namespace System.Drawing
{
    public struct Color
    {
        public static Color Empty => default(Color);
        public static Color White => default(Color);
        public static Color Black => default(Color);
        public static Color FromArgb(int argb) => default(Color);
        public static Color FromArgb(int r, int g, int b) => default(Color);
        public static Color FromArgb(int a, int r, int g, int b) => default(Color);
    }

    public static class ColorTranslator
    {
        public static Color FromHtml(string htmlColor) => default(Color);
        public static string ToHtml(Color c) => "#000000";
    }

    public enum GraphicsUnit { World, Display, Pixel, Point, Inch, Document, Millimeter }

    public struct Rectangle
    {
        public int X { get; set; }
        public int Y { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public Rectangle(int x, int y, int width, int height) { X = x; Y = y; Width = width; Height = height; }
    }

    public abstract class Image : IDisposable
    {
        public int Width { get; set; }
        public int Height { get; set; }
        public static Image FromFile(string filename) => new Bitmap(1, 1);
        public static Image FromStream(Stream stream) => new Bitmap(1, 1);
        public void Save(Stream stream, Imaging.ImageFormat format)
        {
            if (stream != null)
            {
                byte[] pngHeader = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
                stream.Write(pngHeader, 0, pngHeader.Length);
            }
        }
        public void Dispose() { }
    }

    public class Bitmap : Image
    {
        public Bitmap(int width, int height) { Width = width; Height = height; }
        public Bitmap(string filename) { Width = 1; Height = 1; }
        public Bitmap(Stream stream) { Width = 1; Height = 1; }
    }

    public sealed class Graphics : IDisposable
    {
        public static Graphics FromImage(Image image) => new Graphics();
        public void DrawImage(Image image, int x, int y, int width, int height) { }
        public void Dispose() { }
    }
}
