using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading.Tasks;
using ShareX.ScreenCaptureLib;
using ShareX.HelpersLib;
using BugCapture.Models;
using BugCapture.Helpers;
using System.Collections.Generic;
using System.Linq;

namespace BugCapture.Services
{
    public class ShareXCaptureService
    {
        public async Task<CapturedImage?> CaptureRegion()
        {
            return await Task.Run(() =>
            {
                var bitmap = RegionCaptureTasks.GetRegionImage();
                if (bitmap != null)
                {
                    return SaveCapturedImage(bitmap, "Region");
                }
                return null;
            });
        }

        public async Task<CapturedImage?> CaptureScrolling()
        {
            var options = new ScrollingCaptureOptions();
            options.ScrollMethod = ScrollMethod.MouseWheel;
            options.AutoIgnoreBottomEdge = false;
            options.AutoScrollTop = false;

            using (var manager = new ScrollingCaptureManager(options))
            {
                if (manager.SelectWindow())
                {
                    var status = await manager.StartCapture();
                    if (manager.Result != null)
                    {
                        return SaveCapturedImage(manager.Result, "Scrolling");
                    }
                }
            }
            return null;
        }

        private CapturedImage SaveCapturedImage(Bitmap bitmap, string type)
        {
            var dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Captures");
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            var fileName = $"Evidence_{DateTime.Now:yyyyMMddHHmmssfff}.png";
            var filePath = Path.Combine(dir, fileName);
            bitmap.Save(filePath, ImageFormat.Png);

            var captured = new CapturedImage
            {
                FilePath = filePath,
                Thumbnail = bitmap.ToAvaloniaBitmap(),
                CaptureType = type,
                CapturedAt = DateTime.Now
            };

            return captured;
        }
    }
}
