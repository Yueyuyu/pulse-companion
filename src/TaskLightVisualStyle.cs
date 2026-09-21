using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace CodexQuotaOverlay
{
    /// <summary>
    /// Quiet Workspace 的任务灯视觉令牌。所有任务灯、详情和通知共享这一处定义，
    /// 避免桌面实现与 UI Design Lab 再次出现颜色、字体和圆角漂移。
    /// </summary>
    internal static class TaskLightVisualStyle
    {
        public static readonly Color Paper = Color.FromArgb(252, 252, 251);
        public static readonly Color SurfaceStrong = Color.White;
        public static readonly Color SurfaceSubtle = Color.FromArgb(241, 243, 240);
        public static readonly Color SurfaceSelected = Color.FromArgb(229, 233, 228);
        public static readonly Color TextPrimary = Color.FromArgb(38, 38, 35);
        public static readonly Color TextSecondary = Color.FromArgb(104, 106, 101);
        public static readonly Color TextQuiet = Color.FromArgb(111, 115, 109);
        public static readonly Color Border = Color.FromArgb(228, 228, 224);
        public static readonly Color BorderStrong = Color.FromArgb(215, 217, 212);
        public static readonly Color Attention = Color.FromArgb(213, 111, 104);
        public static readonly Color AttentionSoft = Color.FromArgb(247, 225, 223);
        public static readonly Color AttentionText = Color.FromArgb(147, 65, 60);
        public static readonly Color Running = Color.FromArgb(220, 152, 60);
        public static readonly Color RunningSoft = Color.FromArgb(247, 234, 212);
        public static readonly Color RunningText = Color.FromArgb(124, 77, 14);
        public static readonly Color Success = Color.FromArgb(77, 143, 101);
        public static readonly Color SuccessSoft = Color.FromArgb(228, 240, 231);
        public static readonly Color SuccessText = Color.FromArgb(47, 107, 67);
        public static readonly Color Offline = Color.FromArgb(151, 153, 147);
        public static readonly Color OfflineSoft = Color.FromArgb(236, 237, 234);
        public static readonly Color OfflineText = Color.FromArgb(95, 98, 93);

        public const string BellIcon = "\uEA8F";
        public const string PinIcon = "\uE840";
        public const string CloseIcon = "\uE8BB";
        public const string OpenIcon = "\uE8A7";

        public static Font CreateIconFont(float pixelSize)
        {
            string[] families = { "Segoe Fluent Icons", "Segoe MDL2 Assets" };
            foreach (string family in families)
            {
                Font font = new Font(family, pixelSize, FontStyle.Regular, GraphicsUnit.Pixel);
                if (string.Equals(font.FontFamily.Name, family, StringComparison.OrdinalIgnoreCase))
                {
                    return font;
                }

                font.Dispose();
            }

            return UiDrawing.CreateFont(pixelSize, FontStyle.Regular);
        }

        public static Font CreateSemiboldFont(float pixelSize)
        {
            Font chineseFont = new Font("Microsoft YaHei UI", pixelSize, FontStyle.Bold, GraphicsUnit.Pixel);
            if (string.Equals(chineseFont.FontFamily.Name, "Microsoft YaHei UI", StringComparison.OrdinalIgnoreCase))
            {
                return chineseFont;
            }

            chineseFont.Dispose();
            string[] families = { "Segoe UI Variable Text Semibold", "Segoe UI Semibold" };
            foreach (string family in families)
            {
                Font font = new Font(family, pixelSize, FontStyle.Regular, GraphicsUnit.Pixel);
                if (string.Equals(font.FontFamily.Name, family, StringComparison.OrdinalIgnoreCase))
                {
                    return font;
                }

                font.Dispose();
            }

            return UiDrawing.CreateFont(pixelSize, FontStyle.Bold);
        }

        public static Font CreateRegularFont(float pixelSize)
        {
            Font font = new Font("Microsoft YaHei UI", pixelSize, FontStyle.Regular, GraphicsUnit.Pixel);
            if (string.Equals(font.FontFamily.Name, "Microsoft YaHei UI", StringComparison.OrdinalIgnoreCase))
            {
                return font;
            }

            font.Dispose();
            return UiDrawing.CreateFont(pixelSize, FontStyle.Regular);
        }

        public static Color StateColor(CodexTaskState state)
        {
            return state == CodexTaskState.NeedsAttention ? Attention :
                (state == CodexTaskState.Running ? Running : Success);
        }

        public static void DrawWatchStar(
            Graphics graphics,
            Rectangle bounds,
            bool selected,
            bool hovered,
            bool pressed,
            float scale)
        {
            if (hovered || pressed)
            {
                using (GraphicsPath hoverPath = UiDrawing.CreateRoundedPath(bounds, Math.Max(6F, 8F * scale)))
                using (SolidBrush hoverBrush = new SolidBrush(pressed ? SurfaceSelected : SurfaceSubtle))
                {
                    graphics.FillPath(hoverBrush, hoverPath);
                }
            }

            float centerX = bounds.Left + bounds.Width / 2F;
            float centerY = bounds.Top + bounds.Height / 2F + (pressed ? Math.Max(0.5F, 0.7F * scale) : 0F);
            float outerRadius = Math.Min(bounds.Width, bounds.Height) * 0.245F;
            using (GraphicsPath star = CreateStarPath(centerX, centerY, outerRadius, outerRadius * 0.48F))
            {
                if (selected)
                {
                    using (SolidBrush fill = new SolidBrush(Success))
                    using (Pen outline = new Pen(SuccessText, Math.Max(1F, 0.9F * scale)))
                    {
                        graphics.FillPath(fill, star);
                        graphics.DrawPath(outline, star);
                    }
                }
                else
                {
                    using (Pen outline = new Pen(TextQuiet, Math.Max(1.2F, 1.25F * scale)))
                    {
                        outline.LineJoin = LineJoin.Round;
                        graphics.DrawPath(outline, star);
                    }
                }
            }
        }

        public static GraphicsPath CreatePill(RectangleF bounds)
        {
            return UiDrawing.CreateRoundedPath(bounds, bounds.Height / 2F);
        }

        private static GraphicsPath CreateStarPath(float centerX, float centerY, float outerRadius, float innerRadius)
        {
            PointF[] points = new PointF[10];
            for (int index = 0; index < points.Length; index++)
            {
                double angle = -Math.PI / 2D + index * Math.PI / 5D;
                float radius = index % 2 == 0 ? outerRadius : innerRadius;
                points[index] = new PointF(
                    centerX + (float)Math.Cos(angle) * radius,
                    centerY + (float)Math.Sin(angle) * radius);
            }

            GraphicsPath path = new GraphicsPath();
            path.AddPolygon(points);
            path.CloseFigure();
            return path;
        }
    }
}
