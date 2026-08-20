using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace CodexQuotaOverlay
{
    internal sealed class TaskLightWindowSnapshot
    {
        public IntPtr LightHandle { get; set; }
        public Rectangle LightBounds { get; set; }
        public IntPtr DetailsHandle { get; set; }
        public Rectangle DetailsBounds { get; set; }
        public bool LightTopMost { get; set; }
        public bool DetailsTopMost { get; set; }
    }

    internal static class TaskLightWindowProbe
    {
        private const int WsExTopMost = 0x00000008;

        public static TaskLightWindowSnapshot Read()
        {
            TaskLightWindowSnapshot snapshot = new TaskLightWindowSnapshot();
            NativeMethods.EnumWindows(delegate(IntPtr handle, IntPtr parameter)
            {
                if (!NativeMethods.IsWindowVisible(handle))
                {
                    return true;
                }

                string title = GetWindowTitle(handle);
                if (string.Equals(title, "Codex 任务灯", StringComparison.Ordinal))
                {
                    snapshot.LightHandle = handle;
                    snapshot.LightBounds = GetBounds(handle);
                    snapshot.LightTopMost = IsTopMost(handle);
                }
                else if (string.Equals(title, "Codex 任务详情", StringComparison.Ordinal))
                {
                    snapshot.DetailsHandle = handle;
                    snapshot.DetailsBounds = GetBounds(handle);
                    snapshot.DetailsTopMost = IsTopMost(handle);
                }

                return true;
            }, IntPtr.Zero);
            return snapshot;
        }

        public static bool Capture(string outputPath, out TaskLightWindowSnapshot snapshot, out string status)
        {
            snapshot = Read();
            if (snapshot.LightHandle == IntPtr.Zero || snapshot.LightBounds.Width <= 0 || snapshot.LightBounds.Height <= 0)
            {
                status = "未找到正在显示的任务灯窗口";
                return false;
            }

            Rectangle captureBounds = snapshot.LightBounds;
            if (snapshot.DetailsHandle != IntPtr.Zero && snapshot.DetailsBounds.Width > 0)
            {
                captureBounds = Rectangle.Union(captureBounds, snapshot.DetailsBounds);
            }

            captureBounds.Inflate(28, 28);
            captureBounds = Rectangle.Intersect(captureBounds, SystemInformation.VirtualScreen);
            try
            {
                string directory = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                using (Bitmap bitmap = new Bitmap(captureBounds.Width, captureBounds.Height, PixelFormat.Format32bppArgb))
                using (Graphics graphics = Graphics.FromImage(bitmap))
                {
                    graphics.CopyFromScreen(captureBounds.Location, Point.Empty, captureBounds.Size, CopyPixelOperation.SourceCopy);
                    bitmap.Save(outputPath, ImageFormat.Png);
                }

                status = "截图已保存";
                return true;
            }
            catch (Exception exception)
            {
                status = exception.Message;
                return false;
            }
        }

        private static string GetWindowTitle(IntPtr handle)
        {
            int length = NativeMethods.GetWindowTextLength(handle);
            if (length <= 0)
            {
                return string.Empty;
            }

            StringBuilder title = new StringBuilder(length + 1);
            NativeMethods.GetWindowText(handle, title, title.Capacity);
            return title.ToString();
        }

        private static Rectangle GetBounds(IntPtr handle)
        {
            NativeMethods.RECT rect;
            if (!NativeMethods.GetWindowRect(handle, out rect))
            {
                return Rectangle.Empty;
            }

            return Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
        }

        private static bool IsTopMost(IntPtr handle)
        {
            return (NativeMethods.GetWindowLong(handle, NativeMethods.GwlExStyle) & WsExTopMost) != 0;
        }

    }
}
