using Avalonia;
using Avalonia.Media.Imaging;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace BugCapture.Helpers
{
    public static class ImageConversion
    {
        public static Avalonia.Media.Imaging.Bitmap ToAvaloniaBitmap(this System.Drawing.Bitmap bitmap)
        {
            using (var memory = new MemoryStream())
            {
                bitmap.Save(memory, ImageFormat.Png);
                memory.Position = 0;
                return new Avalonia.Media.Imaging.Bitmap(memory);
            }
        }
    }
}
