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
using System.Threading;

namespace BugCapture.Services
{
    public class ShareXCaptureService
    {
        public sealed class GifCapturePendingResult
        {
            public CapturedImage PendingImage { get; }
            public Task RenderTask { get; }

            public GifCapturePendingResult(CapturedImage pendingImage, Task renderTask)
            {
                PendingImage = pendingImage;
                RenderTask = renderTask;
            }
        }

        private const int GifCaptureFramesPerSecond = 15;
        private const float GifCaptureDurationSeconds = 0f;
        private const bool GifCaptureDrawCursor = true;
        private const GIFQuality GifCaptureQuality = GIFQuality.Bit8;

        public async Task<CapturedImage?> CaptureRegion()
        {
            return await Task.Run(() =>
            {
                if (!RegionCaptureTasks.GetRectangleRegion(out var captureRectangle) || captureRectangle.IsEmpty)
                {
                    return null;
                }

                var screenshot = new Screenshot
                {
                    CaptureCursor = true
                };

                var bitmap = screenshot.CaptureRectangle(captureRectangle);
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

        public async Task<GifCapturePendingResult?> CaptureGif()
        {
            return await Task.Run(() =>
            {
                if (!RegionCaptureTasks.GetRectangleRegion(out var captureRectangle) || captureRectangle.IsEmpty)
                {
                    return null;
                }

                var capturesDirectory = GetCaptureDirectory();
                var cacheFilePath = Path.Combine(capturesDirectory, $"GifCache_{DateTime.Now:yyyyMMddHHmmssfff}.cache");
                var gifFilePath = Path.Combine(capturesDirectory, $"Evidence_{DateTime.Now:yyyyMMddHHmmssfff}.gif");

                var options = new ScreenRecordingOptions
                {
                    FPS = GifCaptureFramesPerSecond,
                    Duration = GifCaptureDurationSeconds,
                    DrawCursor = GifCaptureDrawCursor,
                    OutputPath = cacheFilePath,
                    CaptureArea = captureRectangle,
                };

                var renderTask = RunGifRecordingWithShareXForm(captureRectangle, options, gifFilePath, cacheFilePath);

                var pendingImage = new CapturedImage
                {
                    FilePath = gifFilePath,
                    CaptureType = "GIF",
                    CapturedAt = DateTime.Now,
                    IsImage = true,
                    IsRendering = true
                };

                return new GifCapturePendingResult(pendingImage, renderTask);
            });
        }

        private static Task RunGifRecordingWithShareXForm(Rectangle captureRectangle, ScreenRecordingOptions options, string gifFilePath, string cacheFilePath)
        {
            Exception? setupException = null;
            using var recordingStoppedEvent = new ManualResetEventSlim(false);
            var renderTaskCompletionSource = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            var recordingThread = new Thread(() =>
            {
                try
                {
                    var screenshot = new Screenshot
                    {
                        CaptureCursor = options.DrawCursor
                    };
                    using var recordForm = new ScreenRecordForm(captureRectangle)
                    {
                        ActivateWindow = true
                    };

                    var recorder = new ScreenRecorder(ScreenRecordOutput.GIF, options, screenshot, captureRectangle);
                    recordForm.StopRequested += recorder.StopRecording;

                    var recordingTask = Task.Run(() =>
                    {
                        try
                        {
                            ExecuteOnFormThread(recordForm, () => recordForm.ChangeState(ScreenRecordState.BeforeStart));
                            ExecuteOnFormThread(recordForm, () => recordForm.ChangeState(ScreenRecordState.AfterRecordingStart));
                            recorder.StartRecording();

                            recordingStoppedEvent.Set();
                            ExecuteOnFormThread(recordForm, () =>
                            {
                                recordForm.ChangeState(ScreenRecordState.RecordingEnd);
                                recordForm.Close();
                            });

                            recorder.SaveAsGIF(gifFilePath, GifCaptureQuality);
                            renderTaskCompletionSource.TrySetResult(true);
                        }
                        catch (Exception ex)
                        {
                            if (!recordingStoppedEvent.IsSet)
                            {
                                recordingStoppedEvent.Set();
                            }

                            renderTaskCompletionSource.TrySetException(ex);
                        }
                        finally
                        {
                            recorder.Dispose();
                        }
                    });

                    System.Windows.Forms.Application.Run(recordForm);
                    recordingTask.GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    setupException = ex;
                    if (!recordingStoppedEvent.IsSet)
                    {
                        recordingStoppedEvent.Set();
                    }

                    renderTaskCompletionSource.TrySetException(ex);
                }
            });

            recordingThread.IsBackground = true;
            recordingThread.SetApartmentState(ApartmentState.STA);
            recordingThread.Start();

            recordingStoppedEvent.Wait();

            if (setupException != null)
            {
                throw setupException;
            }

            return renderTaskCompletionSource.Task.ContinueWith(task =>
            {
                if (task.IsFaulted)
                {
                    throw task.Exception?.InnerException ?? new InvalidOperationException("GIF rendering failed.");
                }

                if (File.Exists(cacheFilePath))
                {
                    File.Delete(cacheFilePath);
                }
            }, TaskScheduler.Default);
        }

        private static void ExecuteOnFormThread(System.Windows.Forms.Form form, Action action)
        {
            try
            {
                if (form.InvokeRequired)
                {
                    form.Invoke(new System.Windows.Forms.MethodInvoker(() =>
                    {
                        action();
                    }));
                    return;
                }

                action();
            }
            catch (ObjectDisposedException)
            {
                // フォーム終了後は処理不要
            }
            catch (InvalidOperationException)
            {
                // ハンドル未作成/破棄済み時は処理不要
            }
        }

        private CapturedImage SaveCapturedImage(Bitmap bitmap, string type)
        {
            var dir = GetCaptureDirectory();

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

        private static string GetCaptureDirectory()
        {
            var dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Captures");
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            return dir;
        }
    }
}
