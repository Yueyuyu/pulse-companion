using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace CodexQuotaOverlay
{
    internal enum UiMotionCurve
    {
        EaseOut,
        EaseInOut
    }

    internal sealed class UiMotionValue
    {
        private float current;
        private float start;
        private float target;
        private long startedAt;
        private int durationMilliseconds;
        private UiMotionCurve curve;
        private bool running;

        public UiMotionValue(float initialValue)
        {
            current = initialValue;
            start = initialValue;
            target = initialValue;
        }

        public float Current
        {
            get { return current; }
        }

        public bool IsRunning
        {
            get { return running; }
        }

        public void JumpTo(float value)
        {
            current = value;
            start = value;
            target = value;
            running = false;
        }

        public void AnimateTo(float value, int milliseconds, UiMotionCurve animationCurve)
        {
            Update();
            start = current;
            target = value;
            durationMilliseconds = Math.Max(1, milliseconds);
            curve = animationCurve;
            startedAt = Stopwatch.GetTimestamp();
            running = Math.Abs(target - start) > 0.0001F;
            if (!running)
            {
                current = target;
            }
        }

        public bool Update()
        {
            if (!running)
            {
                return false;
            }

            double elapsedMilliseconds = (Stopwatch.GetTimestamp() - startedAt) * 1000D / Stopwatch.Frequency;
            float progress = (float)Math.Max(0D, Math.Min(1D, elapsedMilliseconds / durationMilliseconds));
            float eased = curve == UiMotionCurve.EaseInOut
                ? UiDrawing.EaseInOut(progress)
                : UiDrawing.EaseOut(progress);
            current = start + (target - start) * eased;
            if (progress >= 1F)
            {
                current = target;
                running = false;
            }

            return running;
        }
    }

    internal static class UiDrawing
    {
        public static void Configure(Graphics graphics)
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.CompositingQuality = CompositingQuality.GammaCorrected;
            graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        }

        public static Font CreateFont(float pixelSize, FontStyle style)
        {
            string[] families = { "Segoe UI Variable Text", "Segoe UI", "Microsoft YaHei UI" };
            foreach (string family in families)
            {
                Font font = new Font(family, pixelSize, style, GraphicsUnit.Pixel);
                if (string.Equals(font.FontFamily.Name, family, StringComparison.OrdinalIgnoreCase))
                {
                    return font;
                }

                font.Dispose();
            }

            return new Font(SystemFonts.MessageBoxFont.FontFamily, pixelSize, style, GraphicsUnit.Pixel);
        }

        public static GraphicsPath CreateRoundedPath(RectangleF bounds, float radius)
        {
            float diameter = Math.Min(Math.Min(radius * 2F, bounds.Width), bounds.Height);
            GraphicsPath path = new GraphicsPath();
            path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static GraphicsPath CreateRoundedPath(Rectangle bounds, float radius)
        {
            return CreateRoundedPath(new RectangleF(bounds.X, bounds.Y, bounds.Width, bounds.Height), radius);
        }

        public static void DrawCompactShadow(Graphics graphics, RectangleF bounds, float radius, float scale)
        {
            DrawShadowLayers(graphics, bounds, radius, scale, 10, 0.72F, 1.5F, 2);
            DrawShadowLayers(graphics, bounds, radius, scale, 4, 0.55F, 3.0F, 3);
        }

        public static void DrawPanelShadow(Graphics graphics, RectangleF bounds, float radius, float scale)
        {
            DrawShadowLayers(graphics, bounds, radius, scale, 14, 0.82F, 2.0F, 2);
            DrawShadowLayers(graphics, bounds, radius, scale, 6, 0.62F, 4.5F, 3);
        }

        public static Color Blend(Color from, Color to, float progress)
        {
            progress = Math.Max(0F, Math.Min(1F, progress));
            return Color.FromArgb(
                Lerp(from.A, to.A, progress),
                Lerp(from.R, to.R, progress),
                Lerp(from.G, to.G, progress),
                Lerp(from.B, to.B, progress));
        }

        public static float EaseOut(float progress)
        {
            return EvaluateBezier(progress, 0.23D, 1D, 0.32D, 1D);
        }

        public static float EaseInOut(float progress)
        {
            return EvaluateBezier(progress, 0.77D, 0D, 0.175D, 1D);
        }

        private static void DrawShadowLayers(
            Graphics graphics,
            RectangleF bounds,
            float radius,
            float scale,
            int steps,
            float expansionPerStep,
            float offsetY,
            int alphaPerLayer)
        {
            for (int step = steps; step >= 1; step--)
            {
                float expansion = step * expansionPerStep * scale;
                RectangleF layer = bounds;
                layer.Inflate(expansion, expansion);
                layer.Offset(0F, offsetY * scale);
                using (GraphicsPath path = CreateRoundedPath(layer, radius + expansion))
                using (SolidBrush brush = new SolidBrush(Color.FromArgb(alphaPerLayer, 28, 32, 29)))
                {
                    graphics.FillPath(brush, path);
                }
            }
        }

        private static int Lerp(int from, int to, float progress)
        {
            return (int)Math.Round(from + (to - from) * progress);
        }

        private static float EvaluateBezier(float progress, double x1, double y1, double x2, double y2)
        {
            double x = Math.Max(0D, Math.Min(1D, progress));
            double parameter = x;
            for (int iteration = 0; iteration < 8; iteration++)
            {
                double estimate = SampleCurve(parameter, x1, x2) - x;
                double slope = SampleSlope(parameter, x1, x2);
                if (Math.Abs(slope) < 0.000001D)
                {
                    break;
                }

                parameter -= estimate / slope;
                parameter = Math.Max(0D, Math.Min(1D, parameter));
            }

            double lower = 0D;
            double upper = 1D;
            for (int iteration = 0; iteration < 10; iteration++)
            {
                double estimate = SampleCurve(parameter, x1, x2);
                if (Math.Abs(estimate - x) < 0.000001D)
                {
                    break;
                }

                if (estimate < x)
                {
                    lower = parameter;
                }
                else
                {
                    upper = parameter;
                }

                parameter = (lower + upper) / 2D;
            }

            return (float)SampleCurve(parameter, y1, y2);
        }

        private static double SampleCurve(double parameter, double firstControl, double secondControl)
        {
            double inverse = 1D - parameter;
            return 3D * inverse * inverse * parameter * firstControl +
                   3D * inverse * parameter * parameter * secondControl +
                   parameter * parameter * parameter;
        }

        private static double SampleSlope(double parameter, double firstControl, double secondControl)
        {
            double inverse = 1D - parameter;
            return 3D * inverse * inverse * firstControl +
                   6D * inverse * parameter * (secondControl - firstControl) +
                   3D * parameter * parameter * (1D - secondControl);
        }
    }

    internal static class LayeredWindowRenderer
    {
        private const int AcSrcOver = 0x00;
        private const int AcSrcAlpha = 0x01;
        private const int UlwAlpha = 0x00000002;

        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint
        {
            public int X;
            public int Y;

            public NativePoint(int x, int y)
            {
                X = x;
                Y = y;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeSize
        {
            public int Width;
            public int Height;

            public NativeSize(int width, int height)
            {
                Width = width;
                Height = height;
            }
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct BlendFunction
        {
            public byte BlendOperation;
            public byte BlendFlags;
            public byte SourceConstantAlpha;
            public byte AlphaFormat;
        }

        public static void Present(IntPtr windowHandle, Point location, Bitmap bitmap, byte opacity)
        {
            IntPtr screenDc = GetDC(IntPtr.Zero);
            IntPtr memoryDc = CreateCompatibleDC(screenDc);
            IntPtr bitmapHandle = IntPtr.Zero;
            IntPtr previousBitmap = IntPtr.Zero;
            try
            {
                bitmapHandle = bitmap.GetHbitmap(Color.FromArgb(0));
                previousBitmap = SelectObject(memoryDc, bitmapHandle);
                NativePoint destination = new NativePoint(location.X, location.Y);
                NativePoint source = new NativePoint(0, 0);
                NativeSize size = new NativeSize(bitmap.Width, bitmap.Height);
                BlendFunction blend = new BlendFunction
                {
                    BlendOperation = AcSrcOver,
                    BlendFlags = 0,
                    SourceConstantAlpha = opacity,
                    AlphaFormat = AcSrcAlpha
                };

                if (!UpdateLayeredWindow(
                    windowHandle,
                    screenDc,
                    ref destination,
                    ref size,
                    memoryDc,
                    ref source,
                    0,
                    ref blend,
                    UlwAlpha))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }
            }
            finally
            {
                if (previousBitmap != IntPtr.Zero)
                {
                    SelectObject(memoryDc, previousBitmap);
                }

                if (bitmapHandle != IntPtr.Zero)
                {
                    DeleteObject(bitmapHandle);
                }

                if (memoryDc != IntPtr.Zero)
                {
                    DeleteDC(memoryDc);
                }

                if (screenDc != IntPtr.Zero)
                {
                    ReleaseDC(IntPtr.Zero, screenDc);
                }
            }
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UpdateLayeredWindow(
            IntPtr windowHandle,
            IntPtr destinationDc,
            ref NativePoint destination,
            ref NativeSize size,
            IntPtr sourceDc,
            ref NativePoint source,
            int colorKey,
            ref BlendFunction blend,
            int flags);

        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr windowHandle);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr windowHandle, IntPtr deviceContext);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleDC(IntPtr deviceContext);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteDC(IntPtr deviceContext);

        [DllImport("gdi32.dll")]
        private static extern IntPtr SelectObject(IntPtr deviceContext, IntPtr graphicsObject);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr graphicsObject);
    }
}
