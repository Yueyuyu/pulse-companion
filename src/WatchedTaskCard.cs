using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;

namespace CodexQuotaOverlay
{
    /// <summary>
    /// 重点关注卡片的布局、命中区域与绘制。它不读取设置、不触发跳转，
    /// 只把用户选择顺序稳定地映射为一张 Quiet Workspace 紧凑卡片。
    /// </summary>
    internal sealed class WatchedTaskCard
    {
        public const int DesignWidth = 332;
        public const int DesignHeaderHeight = 48;
        public const int DesignRowHeight = 52;
        public const int DesignBottomPadding = 8;
        public const int HitHeader = -3;
        public const int HitDragSurface = -4;
        public const int HitRowBase = 100;
        public const int HitUnwatchBase = 200;

        private sealed class RowLayout
        {
            public WatchedTaskView View;
            public Rectangle Bounds;
            public Rectangle WatchBounds;
        }

        private readonly List<RowLayout> rows = new List<RowLayout>();
        private Rectangle headerBounds;

        public int RowCount
        {
            get { return rows.Count; }
        }

        public static int GetDesignHeight(int count)
        {
            return DesignHeaderHeight + Math.Max(0, count) * DesignRowHeight + DesignBottomPadding;
        }

        public static bool RunSelfTest(out string result)
        {
            TaskSnapshot task = new TaskSnapshot(
                "dddddddd-dddd-4ddd-8ddd-dddddddddddd",
                "卡片命中测试",
                string.Empty,
                string.Empty,
                CodexTaskState.Running,
                "Codex 正在处理",
                DateTimeOffset.UtcNow);
            WatchedTaskCard card = new WatchedTaskCard();
            card.Build(
                new Rectangle(12, 10, DesignWidth, GetDesignHeight(1)),
                1F,
                new List<WatchedTaskView> { new WatchedTaskView(task, true) });
            int headerHit = card.HitTest(new Point(24, 24));
            int rowHit = card.HitTest(new Point(44, 10 + DesignHeaderHeight + DesignRowHeight / 2));
            int starHit = card.HitTest(new Point(DesignWidth - 6, 10 + DesignHeaderHeight + DesignRowHeight / 2));
            TaskSnapshot rowTask;
            TaskSnapshot starTask;
            bool success = card.RowCount == 1 &&
                           headerHit == HitHeader &&
                           rowHit == HitRowBase &&
                           starHit == HitUnwatchBase &&
                           card.TryGetRowTask(rowHit, out rowTask) &&
                           card.TryGetUnwatchTask(starHit, out starTask) &&
                           object.ReferenceEquals(task, rowTask) &&
                           object.ReferenceEquals(task, starTask);
            result = success
                ? "{\"ok\":true,\"test\":\"watched-task-card-hit-targets\"}"
                : "{\"ok\":false,\"test\":\"watched-task-card-hit-targets\"}";
            return success;
        }

        public void Build(Rectangle cardBounds, float scale, IList<WatchedTaskView> views)
        {
            rows.Clear();
            headerBounds = Rectangle.Empty;
            if (views == null || views.Count == 0 || cardBounds.Width <= 0 || cardBounds.Height <= 0)
            {
                return;
            }

            headerBounds = new Rectangle(
                cardBounds.Left,
                cardBounds.Top,
                cardBounds.Width,
                S(DesignHeaderHeight, scale));
            int rowTop = headerBounds.Bottom;
            for (int index = 0; index < views.Count; index++)
            {
                Rectangle rowBounds = new Rectangle(
                    cardBounds.Left + S(8, scale),
                    rowTop,
                    cardBounds.Width - S(16, scale),
                    S(DesignRowHeight, scale));
                rows.Add(new RowLayout
                {
                    View = views[index],
                    Bounds = rowBounds,
                    WatchBounds = new Rectangle(
                        rowBounds.Right - S(38, scale),
                        rowBounds.Top + (rowBounds.Height - S(32, scale)) / 2,
                        S(32, scale),
                        S(32, scale))
                });
                rowTop += S(DesignRowHeight, scale);
            }
        }

        public int HitTest(Point location)
        {
            for (int index = 0; index < rows.Count; index++)
            {
                if (rows[index].WatchBounds.Contains(location))
                {
                    return HitUnwatchBase + index;
                }

                if (rows[index].Bounds.Contains(location))
                {
                    return HitRowBase + index;
                }
            }

            return headerBounds.Contains(location) ? HitHeader : HitDragSurface;
        }

        public bool TryGetUnwatchTask(int hit, out TaskSnapshot task)
        {
            return TryGetTask(hit, HitUnwatchBase, out task);
        }

        public bool TryGetRowTask(int hit, out TaskSnapshot task)
        {
            return TryGetTask(hit, HitRowBase, out task);
        }

        public void Draw(Graphics graphics, RectangleF cardBounds, int hoveredHit, int pressedHit, float scale)
        {
            if (rows.Count == 0)
            {
                return;
            }

            bool headerHovered = hoveredHit == HitHeader;
            bool headerPressed = pressedHit == HitHeader;
            Rectangle headerFeedbackBounds = headerBounds;
            headerFeedbackBounds.Inflate(-S(6, scale), -S(6, scale));
            if (headerHovered || headerPressed)
            {
                using (GraphicsPath path = UiDrawing.CreateRoundedPath(headerFeedbackBounds, SF(8F, scale)))
                using (SolidBrush brush = new SolidBrush(headerPressed
                    ? TaskLightVisualStyle.SurfaceSelected
                    : TaskLightVisualStyle.SurfaceSubtle))
                {
                    graphics.FillPath(brush, path);
                }
            }

            int headerShift = headerPressed ? S(1, scale) : 0;
            using (Font titleFont = TaskLightVisualStyle.CreateSemiboldFont(SF(13F, scale)))
            using (Font countFont = TaskLightVisualStyle.CreateSemiboldFont(SF(10F, scale)))
            using (Font actionFont = TaskLightVisualStyle.CreateRegularFont(SF(11F, scale)))
            using (SolidBrush titleBrush = new SolidBrush(TaskLightVisualStyle.TextPrimary))
            using (SolidBrush actionBrush = new SolidBrush(TaskLightVisualStyle.TextQuiet))
            using (StringFormat leftFormat = new StringFormat())
            using (StringFormat rightFormat = new StringFormat())
            {
                leftFormat.Alignment = StringAlignment.Near;
                leftFormat.LineAlignment = StringAlignment.Center;
                leftFormat.FormatFlags = StringFormatFlags.NoWrap;
                rightFormat.Alignment = StringAlignment.Far;
                rightFormat.LineAlignment = StringAlignment.Center;
                rightFormat.FormatFlags = StringFormatFlags.NoWrap;

                Rectangle titleBounds = new Rectangle(
                    headerBounds.Left + S(14, scale),
                    headerBounds.Top + headerShift,
                    S(92, scale),
                    headerBounds.Height);
                graphics.DrawString("重点关注", titleFont, titleBrush, titleBounds, leftFormat);

                string countText = rows.Count.ToString(CultureInfo.InvariantCulture) + " / " +
                                   WatchedTaskCollection.MaximumCount.ToString(CultureInfo.InvariantCulture);
                SizeF countSize = graphics.MeasureString(countText, countFont, int.MaxValue, StringFormat.GenericTypographic);
                RectangleF countBounds = new RectangleF(
                    titleBounds.Left + SF(71F, scale),
                    headerBounds.Top + (headerBounds.Height - SF(22F, scale)) / 2F + headerShift,
                    countSize.Width + SF(14F, scale),
                    SF(22F, scale));
                using (GraphicsPath countPath = TaskLightVisualStyle.CreatePill(countBounds))
                using (SolidBrush countSurface = new SolidBrush(TaskLightVisualStyle.SurfaceSubtle))
                using (SolidBrush countBrush = new SolidBrush(TaskLightVisualStyle.TextSecondary))
                using (StringFormat countFormat = CenterFormat())
                {
                    graphics.FillPath(countSurface, countPath);
                    graphics.DrawString(countText, countFont, countBrush, countBounds, countFormat);
                }

                Rectangle actionBounds = new Rectangle(
                    headerBounds.Right - S(82, scale),
                    headerBounds.Top + headerShift,
                    S(68, scale),
                    headerBounds.Height);
                graphics.DrawString("全部任务", actionFont, actionBrush, actionBounds, rightFormat);
            }

            using (Pen separator = new Pen(TaskLightVisualStyle.Border, Math.Max(1F, scale)))
            {
                graphics.DrawLine(
                    separator,
                    cardBounds.Left + SF(14F, scale),
                    headerBounds.Bottom,
                    cardBounds.Right - SF(14F, scale),
                    headerBounds.Bottom);
            }

            for (int index = 0; index < rows.Count; index++)
            {
                DrawRow(graphics, index, rows[index], hoveredHit, pressedHit, scale);
            }
        }

        private static void DrawRow(
            Graphics graphics,
            int index,
            RowLayout row,
            int hoveredHit,
            int pressedHit,
            float scale)
        {
            int rowHit = HitRowBase + index;
            int watchHit = HitUnwatchBase + index;
            bool watchHovered = hoveredHit == watchHit;
            bool watchPressed = pressedHit == watchHit;
            bool rowHovered = hoveredHit == rowHit || watchHovered;
            bool rowPressed = pressedHit == rowHit || watchPressed;
            if (rowHovered || rowPressed)
            {
                using (GraphicsPath path = UiDrawing.CreateRoundedPath(row.Bounds, SF(9F, scale)))
                using (SolidBrush brush = new SolidBrush(rowPressed
                    ? TaskLightVisualStyle.SurfaceSelected
                    : TaskLightVisualStyle.SurfaceSubtle))
                {
                    graphics.FillPath(brush, path);
                }
            }

            WatchedTaskView view = row.View;
            TaskSnapshot task = view.Task;
            Color stateColor;
            Color stateSurface;
            Color stateText;
            string stateLabel;
            string detailText;
            if (!view.Available)
            {
                stateColor = TaskLightVisualStyle.Offline;
                stateSurface = TaskLightVisualStyle.OfflineSoft;
                stateText = TaskLightVisualStyle.OfflineText;
                stateLabel = "暂不可用";
                detailText = "暂时无法读取，关注仍保留";
            }
            else if (task.State == CodexTaskState.NeedsAttention)
            {
                stateColor = TaskLightVisualStyle.Attention;
                stateSurface = TaskLightVisualStyle.AttentionSoft;
                stateText = TaskLightVisualStyle.AttentionText;
                stateLabel = "待处理";
                detailText = BuildDetail(task);
            }
            else if (task.State == CodexTaskState.Running)
            {
                stateColor = TaskLightVisualStyle.Running;
                stateSurface = TaskLightVisualStyle.RunningSoft;
                stateText = TaskLightVisualStyle.RunningText;
                stateLabel = "进行中";
                detailText = BuildDetail(task);
            }
            else
            {
                stateColor = TaskLightVisualStyle.Success;
                stateSurface = TaskLightVisualStyle.SuccessSoft;
                stateText = TaskLightVisualStyle.SuccessText;
                stateLabel = "已完成";
                detailText = BuildDetail(task);
            }

            RectangleF railBounds = new RectangleF(
                row.Bounds.Left + SF(8F, scale),
                row.Bounds.Top + (row.Bounds.Height - SF(26F, scale)) / 2F,
                SF(4F, scale),
                SF(26F, scale));
            using (GraphicsPath rail = TaskLightVisualStyle.CreatePill(railBounds))
            using (SolidBrush railBrush = new SolidBrush(stateColor))
            {
                graphics.FillPath(railBrush, rail);
            }

            using (Font stateFont = TaskLightVisualStyle.CreateSemiboldFont(SF(10F, scale)))
            {
                SizeF stateSize = graphics.MeasureString(stateLabel, stateFont, int.MaxValue, StringFormat.GenericTypographic);
                float stateWidth = Math.Max(SF(50F, scale), stateSize.Width + SF(16F, scale));
                RectangleF stateBounds = new RectangleF(
                    row.WatchBounds.Left - SF(7F, scale) - stateWidth,
                    row.Bounds.Top + SF(7F, scale),
                    stateWidth,
                    SF(22F, scale));
                DrawStatePill(graphics, stateBounds, stateLabel, stateSurface, stateText, stateFont);

                int textLeft = row.Bounds.Left + S(22, scale);
                Rectangle titleBounds = new Rectangle(
                    textLeft,
                    row.Bounds.Top + S(5, scale),
                    Math.Max(S(1, scale), (int)Math.Floor(stateBounds.Left) - textLeft - S(7, scale)),
                    S(21, scale));
                Rectangle detailBounds = new Rectangle(
                    textLeft,
                    row.Bounds.Top + S(28, scale),
                    Math.Max(S(1, scale), row.WatchBounds.Left - textLeft - S(7, scale)),
                    S(18, scale));
                using (Font titleFont = TaskLightVisualStyle.CreateSemiboldFont(SF(12F, scale)))
                using (Font detailFont = TaskLightVisualStyle.CreateRegularFont(SF(11F, scale)))
                using (SolidBrush titleBrush = new SolidBrush(view.Available
                    ? TaskLightVisualStyle.TextPrimary
                    : TaskLightVisualStyle.TextSecondary))
                using (SolidBrush detailBrush = new SolidBrush(TaskLightVisualStyle.TextQuiet))
                using (StringFormat ellipsis = EllipsisFormat())
                {
                    graphics.DrawString(task.Title, titleFont, titleBrush, titleBounds, ellipsis);
                    graphics.DrawString(detailText, detailFont, detailBrush, detailBounds, ellipsis);
                }
            }

            TaskLightVisualStyle.DrawWatchStar(
                graphics,
                row.WatchBounds,
                true,
                watchHovered,
                watchPressed,
                scale);
        }

        private bool TryGetTask(int hit, int hitBase, out TaskSnapshot task)
        {
            int index = hit - hitBase;
            if (index >= 0 && index < rows.Count)
            {
                task = rows[index].View.Task;
                return true;
            }

            task = null;
            return false;
        }

        private static void DrawStatePill(
            Graphics graphics,
            RectangleF bounds,
            string text,
            Color surface,
            Color foreground,
            Font font)
        {
            using (GraphicsPath path = TaskLightVisualStyle.CreatePill(bounds))
            using (SolidBrush surfaceBrush = new SolidBrush(surface))
            using (SolidBrush textBrush = new SolidBrush(foreground))
            using (StringFormat format = CenterFormat())
            {
                graphics.FillPath(surfaceBrush, path);
                graphics.DrawString(text, font, textBrush, bounds, format);
            }
        }

        private static string BuildDetail(TaskSnapshot task)
        {
            string detail = task == null || string.IsNullOrWhiteSpace(task.Detail)
                ? "等待状态更新"
                : task.Detail;
            if (task == null || task.ActivityAtUtc == DateTimeOffset.MinValue)
            {
                return detail;
            }

            TimeSpan age = DateTimeOffset.UtcNow - task.ActivityAtUtc;
            if (age < TimeSpan.Zero)
            {
                age = TimeSpan.Zero;
            }

            string timeText;
            if (age.TotalMinutes < 1D)
            {
                timeText = "刚刚";
            }
            else if (age.TotalHours < 1D)
            {
                timeText = ((int)age.TotalMinutes).ToString(CultureInfo.InvariantCulture) + " 分钟前";
            }
            else if (age.TotalDays < 1D)
            {
                timeText = ((int)age.TotalHours).ToString(CultureInfo.InvariantCulture) + " 小时前";
            }
            else
            {
                timeText = task.ActivityAtUtc.ToLocalTime().ToString("M/d", CultureInfo.CurrentCulture);
            }

            return detail + " · " + timeText;
        }

        private static int S(int value, float scale)
        {
            return Math.Max(1, (int)Math.Round(value * scale));
        }

        private static float SF(float value, float scale)
        {
            return value * scale;
        }

        private static StringFormat CenterFormat()
        {
            StringFormat format = new StringFormat();
            format.Alignment = StringAlignment.Center;
            format.LineAlignment = StringAlignment.Center;
            format.Trimming = StringTrimming.EllipsisCharacter;
            format.FormatFlags = StringFormatFlags.NoWrap;
            return format;
        }

        private static StringFormat EllipsisFormat()
        {
            StringFormat format = new StringFormat();
            format.Alignment = StringAlignment.Near;
            format.LineAlignment = StringAlignment.Center;
            format.Trimming = StringTrimming.EllipsisCharacter;
            format.FormatFlags = StringFormatFlags.NoWrap;
            return format;
        }
    }
}
