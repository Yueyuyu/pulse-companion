using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.Windows.Forms;

namespace CodexQuotaOverlay
{
    internal sealed class TaskLightDetailsForm : Form
    {
        private const int DesignWidth = 356;
        private const int DesignHeaderHeight = 72;
        private const int DesignFooterHeight = 50;
        private const int DesignGroupHeight = 32;
        private const int DesignRowHeight = 49;
        private const int DesignEmptyHeight = 128;
        private const int DesignGap = 8;
        private const int DesignPaddingLeft = 16;
        private const int DesignPaddingTop = 12;
        private const int DesignPaddingRight = 16;
        private const int DesignPaddingBottom = 20;
        private const int MaximumVisibleRows = 6;
        private const int HitNone = -1;
        private const int HitClose = -2;
        private const int HitToggle = -3;
        private const int VkLeftButton = 0x01;
        private const int VkRightButton = 0x02;
        private const int VkEscape = 0x1B;

        private sealed class RowLayout
        {
            public TaskSnapshot Task;
            public Rectangle Bounds;
        }

        private sealed class GroupLayout
        {
            public string Label;
            public int Count;
            public Rectangle Bounds;
        }

        private readonly Timer outsideInputTimer;
        private readonly Timer motionTimer;
        private readonly bool motionEnabled;
        private readonly UiMotionValue visibilityMotion = new UiMotionValue(0F);
        private readonly UiMotionValue toggleMotion = new UiMotionValue(1F);
        private readonly List<GroupLayout> groups = new List<GroupLayout>();
        private readonly List<RowLayout> rows = new List<RowLayout>();
        private TaskListSnapshot snapshot = new TaskListSnapshot(null);
        private Rectangle anchorBounds;
        private Rectangle closeBounds;
        private Rectangle toggleBounds;
        private Rectangle footerBounds;
        private Rectangle emptyBounds;
        private Size cardSize;
        private float scale = 1F;
        private bool connected;
        private bool alwaysOnTop = true;
        private bool opensBelow = true;
        private bool closing;
        private int hoveredHit = HitNone;
        private int pressedHit = HitNone;
        private bool escapeWasDown;
        private bool leftButtonWasDown;
        private bool rightButtonWasDown;
        private long lastClockSecond;
        private int hiddenTaskCount;

        public event EventHandler AlwaysOnTopChanged;
        public event EventHandler<TaskActivatedEventArgs> TaskActivated;

        public bool AlwaysOnTop
        {
            get { return alwaysOnTop; }
        }

        public bool IsClosing
        {
            get { return closing; }
        }

        public TaskLightDetailsForm()
        {
            motionEnabled = NativeMethods.AreClientAreaAnimationsEnabled();
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Color.Black;
            ControlBox = false;
            FormBorderStyle = FormBorderStyle.None;
            MaximizeBox = false;
            MinimizeBox = false;
            Name = "CodexTaskLightDetails";
            ShowIcon = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Text = "Codex 任务详情";
            AccessibleName = "Codex 任务详情";
            AccessibleDescription = "显示当前需要处理和执行中的 Codex 任务。";

            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw |
                     ControlStyles.UserPaint, true);

            outsideInputTimer = new Timer();
            outsideInputTimer.Interval = 50;
            outsideInputTimer.Tick += OnOutsideInputTick;

            motionTimer = new Timer();
            motionTimer.Interval = 15;
            motionTimer.Tick += OnMotionTick;

            BuildLayout();
        }

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams parameters = base.CreateParams;
                parameters.ExStyle |= (int)(NativeMethods.WsExToolWindow |
                                             NativeMethods.WsExNoActivate |
                                             NativeMethods.WsExLayered);
                return parameters;
            }
        }

        public void UpdateTasks(TaskListSnapshot newSnapshot)
        {
            snapshot = newSnapshot ?? new TaskListSnapshot(null);
            connected = true;
            BuildLayout();
            if (Visible)
            {
                Reposition(anchorBounds);
                RenderLayeredWindow();
            }
        }

        public void UpdateConnection(bool isConnected)
        {
            connected = isConnected;
            if (!connected)
            {
                snapshot = new TaskListSnapshot(null);
            }

            BuildLayout();
            if (Visible)
            {
                Reposition(anchorBounds);
                RenderLayeredWindow();
            }
        }

        public void ShowAnchored(IWin32Window owner, Rectangle newAnchorBounds, bool shouldStayOnTop)
        {
            alwaysOnTop = shouldStayOnTop;
            toggleMotion.JumpTo(alwaysOnTop ? 1F : 0F);
            UpdateScale(newAnchorBounds);
            Reposition(newAnchorBounds);
            hoveredHit = HitNone;
            pressedHit = HitNone;
            PrimeInputState();
            closing = false;

            if (!Visible)
            {
                visibilityMotion.JumpTo(0F);
                Show(owner);
            }

            ApplyWindowPosition();
            RenderLayeredWindow();
            visibilityMotion.AnimateTo(1F, motionEnabled ? 180 : 120, UiMotionCurve.EaseOut);
            StartMotion();
            outsideInputTimer.Start();
        }

        public void Reposition(Rectangle newAnchorBounds)
        {
            anchorBounds = newAnchorBounds;
            Rectangle available = Screen.FromRectangle(anchorBounds).WorkingArea;
            int gap = S(DesignGap);
            int cardX = anchorBounds.Right - cardSize.Width;
            int below = anchorBounds.Bottom + gap;
            int above = anchorBounds.Top - gap - cardSize.Height;
            opensBelow = below + cardSize.Height <= available.Bottom || above < available.Top;
            int cardY = opensBelow ? below : above;
            int outerX = cardX - S(DesignPaddingLeft);
            int outerY = cardY - S(DesignPaddingTop);

            outerX = Math.Max(available.Left, Math.Min(outerX, available.Right - Width));
            outerY = Math.Max(available.Top, Math.Min(outerY, available.Bottom - Height));
            Bounds = new Rectangle(outerX, outerY, Width, Height);

            if (Visible)
            {
                ApplyWindowPosition();
                RenderLayeredWindow();
            }
        }

        public void SetAlwaysOnTop(bool value)
        {
            if (alwaysOnTop == value)
            {
                toggleMotion.JumpTo(value ? 1F : 0F);
                return;
            }

            alwaysOnTop = value;
            toggleMotion.JumpTo(value ? 1F : 0F);
            if (Visible)
            {
                ApplyWindowPosition();
                RenderLayeredWindow();
            }
        }

        public void ClosePopup(bool animate)
        {
            outsideInputTimer.Stop();
            Capture = false;
            hoveredHit = HitNone;
            pressedHit = HitNone;
            if (!Visible)
            {
                return;
            }

            if (!animate)
            {
                closing = false;
                visibilityMotion.JumpTo(0F);
                Hide();
                return;
            }

            closing = true;
            visibilityMotion.AnimateTo(0F, motionEnabled ? 120 : 90, UiMotionCurve.EaseOut);
            StartMotion();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            RenderLayeredWindow();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int hit = GetHitAt(ToCardPoint(e.Location));
            if (hit != hoveredHit)
            {
                hoveredHit = hit;
                Cursor = hit == HitNone ? Cursors.Default : Cursors.Hand;
                RenderLayeredWindow();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (!Capture && hoveredHit != HitNone)
            {
                hoveredHit = HitNone;
                Cursor = Cursors.Default;
                RenderLayeredWindow();
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left)
            {
                return;
            }

            pressedHit = GetHitAt(ToCardPoint(e.Location));
            if (pressedHit != HitNone)
            {
                Capture = true;
                RenderLayeredWindow();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left)
            {
                return;
            }

            int releasedHit = GetHitAt(ToCardPoint(e.Location));
            int action = pressedHit;
            pressedHit = HitNone;
            Capture = false;
            RenderLayeredWindow();
            if (action == HitNone || action != releasedHit)
            {
                return;
            }

            if (action == HitClose)
            {
                ClosePopup(true);
            }
            else if (action == HitToggle)
            {
                alwaysOnTop = !alwaysOnTop;
                if (motionEnabled)
                {
                    toggleMotion.AnimateTo(alwaysOnTop ? 1F : 0F, 160, UiMotionCurve.EaseInOut);
                }
                else
                {
                    toggleMotion.JumpTo(alwaysOnTop ? 1F : 0F);
                }
                ApplyWindowPosition();
                StartMotion();
                EventHandler handler = AlwaysOnTopChanged;
                if (handler != null)
                {
                    handler(this, EventArgs.Empty);
                }
            }
            else if (action >= 0 && action < rows.Count)
            {
                TaskSnapshot selectedTask = rows[action].Task;
                EventHandler<TaskActivatedEventArgs> handler = TaskActivated;
                if (handler != null)
                {
                    handler(this, new TaskActivatedEventArgs(selectedTask));
                }

                ClosePopup(true);
            }
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            RenderLayeredWindow();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                outsideInputTimer.Dispose();
                motionTimer.Dispose();
            }

            base.Dispose(disposing);
        }

        public Bitmap CaptureSurfaceForPreview()
        {
            return RenderSurfaceBitmap(1F);
        }

        private void RenderLayeredWindow()
        {
            if (!Visible || !IsHandleCreated || IsDisposed || ClientSize.Width <= 0 || ClientSize.Height <= 0)
            {
                return;
            }

            float progress = visibilityMotion.Current;
            using (Bitmap surface = RenderSurfaceBitmap(progress))
            {
                byte opacity = (byte)Math.Round(255F * progress);
                LayeredWindowRenderer.Present(Handle, Location, surface, opacity);
            }
        }

        private Bitmap RenderSurfaceBitmap(float progress)
        {
            Bitmap surface = new Bitmap(ClientSize.Width, ClientSize.Height, PixelFormat.Format32bppArgb);
            using (Bitmap cardBitmap = RenderCardBitmap())
            using (Graphics graphics = Graphics.FromImage(surface))
            {
                graphics.Clear(Color.Transparent);
                UiDrawing.Configure(graphics);

                RectangleF finalBounds = GetCardSurfaceBounds();
                float cardScale = motionEnabled ? 0.97F + 0.03F * progress : 1F;
                PointF origin = new PointF(
                    finalBounds.Right - SFloat(20F),
                    opensBelow ? finalBounds.Top : finalBounds.Bottom);
                RectangleF animatedBounds = ScaleBoundsFromOrigin(finalBounds, origin, cardScale);

                UiDrawing.DrawPanelShadow(graphics, animatedBounds, SFloat(14F), scale);
                graphics.DrawImage(cardBitmap, animatedBounds);
            }

            return surface;
        }

        private Bitmap RenderCardBitmap()
        {
            Bitmap bitmap = new Bitmap(cardSize.Width, cardSize.Height, PixelFormat.Format32bppArgb);
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(Color.Transparent);
                UiDrawing.Configure(graphics);
                RectangleF cardBounds = new RectangleF(0.75F, 0.75F, cardSize.Width - 1.5F, cardSize.Height - 1.5F);
                using (GraphicsPath card = UiDrawing.CreateRoundedPath(cardBounds, SFloat(14F)))
                using (SolidBrush background = new SolidBrush(TaskLightVisualStyle.Paper))
                using (Pen border = new Pen(TaskLightVisualStyle.Border, Math.Max(1F, scale)))
                {
                    graphics.FillPath(background, card);
                    graphics.DrawPath(border, card);
                }

                DrawHeader(graphics);
                DrawContent(graphics);
                DrawFooter(graphics);
            }

            return bitmap;
        }

        private void DrawHeader(Graphics graphics)
        {
            int headerHeight = S(DesignHeaderHeight);
            using (Pen separator = new Pen(TaskLightVisualStyle.Border, Math.Max(1F, scale)))
            {
                graphics.DrawLine(separator, 0, headerHeight - 1, cardSize.Width, headerHeight - 1);
            }

            using (Font titleFont = TaskLightVisualStyle.CreateSemiboldFont(SFloat(12F)))
            using (SolidBrush titleBrush = new SolidBrush(TaskLightVisualStyle.TextPrimary))
            {
                graphics.DrawString("Codex 活动任务", titleFont, titleBrush, new PointF(SFloat(16F), SFloat(12F)));
            }

            DrawHeaderChips(graphics);

            bool closeHovered = hoveredHit == HitClose;
            bool closePressed = pressedHit == HitClose;
            if (closeHovered || closePressed)
            {
                using (GraphicsPath hoverPath = UiDrawing.CreateRoundedPath(closeBounds, SFloat(8F)))
                using (SolidBrush hoverBrush = new SolidBrush(closePressed
                    ? TaskLightVisualStyle.SurfaceSelected
                    : TaskLightVisualStyle.SurfaceSubtle))
                {
                    graphics.FillPath(hoverBrush, hoverPath);
                }
            }

            using (Font closeFont = TaskLightVisualStyle.CreateIconFont(SFloat(12F)))
            using (SolidBrush closeBrush = new SolidBrush(TaskLightVisualStyle.TextQuiet))
            using (StringFormat format = CenterFormat())
            {
                Rectangle closeGlyphBounds = closeBounds;
                if (closePressed)
                {
                    closeGlyphBounds.Offset(0, S(1));
                }

                graphics.DrawString(TaskLightVisualStyle.CloseIcon, closeFont, closeBrush, closeGlyphBounds, format);
            }
        }

        private void DrawHeaderChips(Graphics graphics)
        {
            float x = SFloat(16F);
            float y = SFloat(38F);
            if (!connected)
            {
                DrawHeaderChip(graphics, ref x, y, "Codex 离线", TaskLightVisualStyle.OfflineSoft, TaskLightVisualStyle.OfflineText, false);
                return;
            }

            if (snapshot.AttentionCount == 0 && snapshot.RunningCount == 0)
            {
                DrawHeaderChip(graphics, ref x, y, "当前空闲", TaskLightVisualStyle.SuccessSoft, TaskLightVisualStyle.SuccessText, true);
                return;
            }

            if (snapshot.AttentionCount > 0)
            {
                DrawHeaderChip(
                    graphics,
                    ref x,
                    y,
                    "需处理 " + snapshot.AttentionCount.ToString(CultureInfo.InvariantCulture),
                    TaskLightVisualStyle.AttentionSoft,
                    TaskLightVisualStyle.AttentionText,
                    false);
            }

            if (snapshot.RunningCount > 0)
            {
                DrawHeaderChip(
                    graphics,
                    ref x,
                    y,
                    "执行中 " + snapshot.RunningCount.ToString(CultureInfo.InvariantCulture),
                    TaskLightVisualStyle.RunningSoft,
                    TaskLightVisualStyle.RunningText,
                    false);
            }
        }

        private void DrawHeaderChip(
            Graphics graphics,
            ref float x,
            float y,
            string text,
            Color surface,
            Color foreground,
            bool drawDot)
        {
            using (Font font = TaskLightVisualStyle.CreateSemiboldFont(SFloat(11F)))
            {
                SizeF measured = graphics.MeasureString(text, font, int.MaxValue, StringFormat.GenericTypographic);
                float dotSpace = drawDot ? SFloat(12F) : 0F;
                float width = measured.Width + SFloat(16F) + dotSpace;
                RectangleF bounds = new RectangleF(x, y, width, SFloat(22F));
                using (GraphicsPath path = TaskLightVisualStyle.CreatePill(bounds))
                using (SolidBrush surfaceBrush = new SolidBrush(surface))
                using (SolidBrush textBrush = new SolidBrush(foreground))
                using (StringFormat format = new StringFormat())
                {
                    graphics.FillPath(surfaceBrush, path);
                    format.Alignment = StringAlignment.Near;
                    format.LineAlignment = StringAlignment.Center;
                    format.FormatFlags = StringFormatFlags.NoWrap;
                    float textLeft = bounds.Left + SFloat(8F);
                    if (drawDot)
                    {
                        graphics.FillEllipse(
                            textBrush,
                            bounds.Left + SFloat(8F),
                            bounds.Top + (bounds.Height - SFloat(6F)) / 2F,
                            SFloat(6F),
                            SFloat(6F));
                        textLeft += SFloat(12F);
                    }

                    graphics.DrawString(
                        text,
                        font,
                        textBrush,
                        new RectangleF(textLeft, bounds.Top, bounds.Right - textLeft - SFloat(6F), bounds.Height),
                        format);
                }

                x += width + SFloat(6F);
            }
        }

        private void DrawContent(Graphics graphics)
        {
            if (rows.Count == 0)
            {
                DrawEmptyState(graphics);
                return;
            }

            foreach (GroupLayout group in groups)
            {
                DrawGroupLabel(graphics, group);
            }

            for (int index = 0; index < rows.Count; index++)
            {
                DrawTaskRow(graphics, index, rows[index]);
            }
        }

        private void DrawGroupLabel(Graphics graphics, GroupLayout group)
        {
            using (Font font = TaskLightVisualStyle.CreateSemiboldFont(SFloat(11F)))
            using (SolidBrush brush = new SolidBrush(TaskLightVisualStyle.TextQuiet))
            using (StringFormat left = new StringFormat())
            using (StringFormat right = new StringFormat())
            {
                left.Alignment = StringAlignment.Near;
                left.LineAlignment = StringAlignment.Center;
                left.FormatFlags = StringFormatFlags.NoWrap;
                right.Alignment = StringAlignment.Far;
                right.LineAlignment = StringAlignment.Center;
                right.FormatFlags = StringFormatFlags.NoWrap;
                Rectangle labelBounds = new Rectangle(
                    group.Bounds.Left + S(8),
                    group.Bounds.Top,
                    group.Bounds.Width - S(16),
                    group.Bounds.Height);
                graphics.DrawString(group.Label, font, brush, labelBounds, left);
                graphics.DrawString(group.Count.ToString(CultureInfo.InvariantCulture), font, brush, labelBounds, right);
            }
        }

        private void DrawTaskRow(Graphics graphics, int index, RowLayout row)
        {
            bool hovered = hoveredHit == index;
            bool pressed = pressedHit == index;
            bool needsAttention = row.Task.State == CodexTaskState.NeedsAttention;
            if (hovered || pressed)
            {
                Color background = pressed
                    ? TaskLightVisualStyle.SurfaceSelected
                    : TaskLightVisualStyle.SurfaceSubtle;
                using (GraphicsPath path = UiDrawing.CreateRoundedPath(row.Bounds, SFloat(9F)))
                using (SolidBrush brush = new SolidBrush(background))
                {
                    graphics.FillPath(brush, path);
                }
            }

            RectangleF railBounds = new RectangleF(
                row.Bounds.Left + SFloat(10F),
                row.Bounds.Top + (row.Bounds.Height - SFloat(24F)) / 2F,
                SFloat(5F),
                SFloat(24F));
            using (GraphicsPath rail = TaskLightVisualStyle.CreatePill(railBounds))
            using (SolidBrush railBrush = new SolidBrush(needsAttention
                ? TaskLightVisualStyle.Attention
                : TaskLightVisualStyle.Running))
            {
                graphics.FillPath(railBrush, rail);
            }

            int textLeft = row.Bounds.Left + S(26);
            int timeWidth = S(52);
            Rectangle titleBounds = new Rectangle(
                textLeft,
                row.Bounds.Top + S(6),
                row.Bounds.Right - textLeft - timeWidth - S(8),
                S(19));
            Rectangle detailBounds = new Rectangle(
                textLeft,
                row.Bounds.Top + S(27),
                row.Bounds.Right - textLeft - timeWidth - S(8),
                S(17));
            Rectangle timeBounds = new Rectangle(
                row.Bounds.Right - timeWidth - S(8),
                row.Bounds.Top,
                timeWidth,
                row.Bounds.Height);

            using (Font titleFont = TaskLightVisualStyle.CreateSemiboldFont(SFloat(12F)))
            using (Font detailFont = TaskLightVisualStyle.CreateRegularFont(SFloat(11F)))
            using (Font timeFont = TaskLightVisualStyle.CreateRegularFont(SFloat(11F)))
            using (SolidBrush titleBrush = new SolidBrush(TaskLightVisualStyle.TextPrimary))
            using (SolidBrush secondaryBrush = new SolidBrush(TaskLightVisualStyle.TextQuiet))
            using (StringFormat titleFormat = EllipsisFormat())
            using (StringFormat detailFormat = EllipsisFormat())
            using (StringFormat timeFormat = new StringFormat())
            {
                timeFormat.Alignment = StringAlignment.Far;
                timeFormat.LineAlignment = StringAlignment.Center;
                graphics.DrawString(row.Task.Title, titleFont, titleBrush, titleBounds, titleFormat);
                graphics.DrawString(row.Task.Detail, detailFont, secondaryBrush, detailBounds, detailFormat);
                graphics.DrawString(BuildTimeText(row.Task), timeFont, secondaryBrush, timeBounds, timeFormat);
            }
        }

        private void DrawEmptyState(Graphics graphics)
        {
            int centerX = emptyBounds.Left + emptyBounds.Width / 2;
            int dotY = emptyBounds.Top + S(31);
            Color dot = connected ? Color.FromArgb(73, 143, 99) : Color.FromArgb(143, 148, 140);
            Color halo = connected ? Color.FromArgb(30, 73, 143, 99) : Color.FromArgb(26, 143, 148, 140);
            using (SolidBrush haloBrush = new SolidBrush(halo))
            using (SolidBrush dotBrush = new SolidBrush(dot))
            {
                graphics.FillEllipse(haloBrush, centerX - S(9), dotY - S(9), S(18), S(18));
                graphics.FillEllipse(dotBrush, centerX - S(4), dotY - S(4), S(8), S(8));
            }

            string title = connected ? "当前没有任务执行" : "Codex 暂未运行";
            string detail = connected ? "所有任务都已完成" : "打开 Codex 后会自动连接";
            using (Font titleFont = TaskLightVisualStyle.CreateSemiboldFont(SFloat(12F)))
            using (Font detailFont = TaskLightVisualStyle.CreateRegularFont(SFloat(11F)))
            using (SolidBrush titleBrush = new SolidBrush(Color.FromArgb(55, 58, 52)))
            using (SolidBrush detailBrush = new SolidBrush(Color.FromArgb(137, 141, 132)))
            using (StringFormat format = CenterFormat())
            {
                Rectangle titleBounds = new Rectangle(emptyBounds.Left, emptyBounds.Top + S(50), emptyBounds.Width, S(20));
                Rectangle detailBounds = new Rectangle(emptyBounds.Left, emptyBounds.Top + S(74), emptyBounds.Width, S(18));
                graphics.DrawString(title, titleFont, titleBrush, titleBounds, format);
                graphics.DrawString(detail, detailFont, detailBrush, detailBounds, format);
            }
        }

        private void DrawFooter(Graphics graphics)
        {
            using (Pen separator = new Pen(TaskLightVisualStyle.Border, Math.Max(1F, scale)))
            {
                graphics.DrawLine(separator, 0, footerBounds.Top, cardSize.Width, footerBounds.Top);
            }

            string footerText = hiddenTaskCount > 0
                ? "另有 " + hiddenTaskCount.ToString(CultureInfo.InvariantCulture) + " 个未显示"
                : "仅在完成或需处理时通知";
            using (Font iconFont = TaskLightVisualStyle.CreateIconFont(SFloat(13F)))
            using (Font font = TaskLightVisualStyle.CreateRegularFont(SFloat(11F)))
            using (SolidBrush brush = new SolidBrush(TaskLightVisualStyle.TextQuiet))
            using (StringFormat leftFormat = new StringFormat())
            using (StringFormat centerFormat = CenterFormat())
            {
                leftFormat.Alignment = StringAlignment.Near;
                leftFormat.LineAlignment = StringAlignment.Center;
                Rectangle bell = new Rectangle(footerBounds.Left + S(14), footerBounds.Top, S(18), footerBounds.Height);
                Rectangle left = new Rectangle(footerBounds.Left + S(36), footerBounds.Top, S(210), footerBounds.Height);
                Rectangle pin = new Rectangle(toggleBounds.Left - S(26), footerBounds.Top, S(20), footerBounds.Height);
                graphics.DrawString(TaskLightVisualStyle.BellIcon, iconFont, brush, bell, centerFormat);
                graphics.DrawString(footerText, font, brush, left, leftFormat);
                graphics.DrawString(TaskLightVisualStyle.PinIcon, iconFont, brush, pin, centerFormat);
            }

            Color offColor = TaskLightVisualStyle.BorderStrong;
            Color onColor = TaskLightVisualStyle.Success;
            Color switchColor = UiDrawing.Blend(offColor, onColor, toggleMotion.Current);
            if (hoveredHit == HitToggle)
            {
                switchColor = UiDrawing.Blend(switchColor, TaskLightVisualStyle.SuccessText, 0.18F);
            }

            using (GraphicsPath switchPath = UiDrawing.CreateRoundedPath(toggleBounds, toggleBounds.Height / 2F))
            using (SolidBrush switchBrush = new SolidBrush(switchColor))
            {
                graphics.FillPath(switchBrush, switchPath);
            }

            int knobSize = toggleBounds.Height - S(6);
            float knobLeft = toggleBounds.Left + S(3);
            float knobRight = toggleBounds.Right - S(3) - knobSize;
            float knobX = knobLeft + (knobRight - knobLeft) * toggleMotion.Current;
            float knobY = toggleBounds.Top + (toggleBounds.Height - knobSize) / 2F;
            using (SolidBrush knobBrush = new SolidBrush(Color.White))
            using (Pen knobBorder = new Pen(Color.FromArgb(28, 39, 45, 38), Math.Max(1F, scale)))
            {
                graphics.FillEllipse(knobBrush, knobX, knobY, knobSize, knobSize);
                graphics.DrawEllipse(knobBorder, knobX, knobY, knobSize, knobSize);
            }
        }

        private void BuildLayout()
        {
            groups.Clear();
            rows.Clear();
            hiddenTaskCount = 0;

            int y = S(DesignHeaderHeight + 8);
            int contentLeft = S(8);
            int contentWidth = S(DesignWidth - 16);
            int visibleRows = 0;
            visibleRows = AddGroup(
                CodexTaskState.NeedsAttention,
                "需要处理",
                snapshot.AttentionCount,
                ref y,
                contentLeft,
                contentWidth,
                visibleRows);
            visibleRows = AddGroup(
                CodexTaskState.Running,
                "执行中",
                snapshot.RunningCount,
                ref y,
                contentLeft,
                contentWidth,
                visibleRows);

            int activeCount = snapshot.AttentionCount + snapshot.RunningCount;
            hiddenTaskCount = Math.Max(0, activeCount - visibleRows);
            if (rows.Count == 0)
            {
                emptyBounds = new Rectangle(0, S(DesignHeaderHeight), S(DesignWidth), S(DesignEmptyHeight));
                y = emptyBounds.Bottom;
            }
            else
            {
                emptyBounds = Rectangle.Empty;
                y += S(8);
            }

            footerBounds = new Rectangle(0, y, S(DesignWidth), S(DesignFooterHeight));
            cardSize = new Size(S(DesignWidth), footerBounds.Bottom);
            closeBounds = new Rectangle(cardSize.Width - S(42), S(8), S(32), S(32));
            toggleBounds = new Rectangle(
                cardSize.Width - S(52),
                footerBounds.Top + (footerBounds.Height - S(22)) / 2,
                S(36),
                S(22));
            ClientSize = new Size(
                cardSize.Width + S(DesignPaddingLeft + DesignPaddingRight),
                cardSize.Height + S(DesignPaddingTop + DesignPaddingBottom));
        }

        private int AddGroup(
            CodexTaskState state,
            string label,
            int stateCount,
            ref int y,
            int contentLeft,
            int contentWidth,
            int visibleRows)
        {
            if (stateCount <= 0 || visibleRows >= MaximumVisibleRows)
            {
                return visibleRows;
            }

            groups.Add(new GroupLayout
            {
                Label = label,
                Count = stateCount,
                Bounds = new Rectangle(contentLeft, y, contentWidth, S(DesignGroupHeight))
            });
            y += S(DesignGroupHeight);

            foreach (TaskSnapshot task in snapshot.Tasks)
            {
                if (task.State != state || visibleRows >= MaximumVisibleRows)
                {
                    continue;
                }

                rows.Add(new RowLayout
                {
                    Task = task,
                    Bounds = new Rectangle(contentLeft, y, contentWidth, S(DesignRowHeight))
                });
                y += S(DesignRowHeight);
                visibleRows++;
            }

            return visibleRows;
        }

        private int GetHitAt(Point location)
        {
            if (closeBounds.Contains(location))
            {
                return HitClose;
            }

            if (toggleBounds.Contains(location))
            {
                return HitToggle;
            }

            for (int index = 0; index < rows.Count; index++)
            {
                if (rows[index].Bounds.Contains(location))
                {
                    return index;
                }
            }

            return HitNone;
        }

        private Point ToCardPoint(Point point)
        {
            return new Point(point.X - S(DesignPaddingLeft), point.Y - S(DesignPaddingTop));
        }

        private void UpdateScale(Rectangle newAnchorBounds)
        {
            float newScale = newAnchorBounds.Height > 0
                ? Math.Max(1F, Math.Min(3F, newAnchorBounds.Height / 40F))
                : 1F;
            if (Math.Abs(newScale - scale) < 0.01F)
            {
                return;
            }

            scale = newScale;
            BuildLayout();
        }

        private void ApplyWindowPosition()
        {
            NativeMethods.SetWindowPos(
                Handle,
                alwaysOnTop ? NativeMethods.HwndTopMost : NativeMethods.HwndNoTopMost,
                Bounds.Left,
                Bounds.Top,
                Bounds.Width,
                Bounds.Height,
                NativeMethods.SwpNoActivate | NativeMethods.SwpShowWindow | NativeMethods.SwpNoOwnerZOrder);
        }

        private RectangleF GetCardSurfaceBounds()
        {
            return new RectangleF(SFloat(DesignPaddingLeft), SFloat(DesignPaddingTop), cardSize.Width, cardSize.Height);
        }

        private Rectangle GetVisualBoundsScreen()
        {
            RectangleF card = GetCardSurfaceBounds();
            return Rectangle.Round(new RectangleF(Left + card.Left, Top + card.Top, card.Width, card.Height));
        }

        private void PrimeInputState()
        {
            escapeWasDown = IsKeyDown(VkEscape);
            leftButtonWasDown = IsKeyDown(VkLeftButton);
            rightButtonWasDown = IsKeyDown(VkRightButton);
        }

        private void OnOutsideInputTick(object sender, EventArgs args)
        {
            bool escapeIsDown = IsKeyDown(VkEscape);
            bool leftButtonIsDown = IsKeyDown(VkLeftButton);
            bool rightButtonIsDown = IsKeyDown(VkRightButton);

            if (escapeIsDown && !escapeWasDown)
            {
                // 键盘关闭属于高频命令面，保持即时响应，不播放位移动画。
                ClosePopup(false);
                return;
            }

            bool outsidePress = (leftButtonIsDown && !leftButtonWasDown) ||
                                (rightButtonIsDown && !rightButtonWasDown);
            if (outsidePress)
            {
                Point cursor = Cursor.Position;
                if (!GetVisualBoundsScreen().Contains(cursor) && !anchorBounds.Contains(cursor))
                {
                    ClosePopup(true);
                    return;
                }
            }

            long currentSecond = DateTime.UtcNow.Ticks / TimeSpan.TicksPerSecond;
            if (currentSecond != lastClockSecond && snapshot.RunningCount > 0)
            {
                lastClockSecond = currentSecond;
                RenderLayeredWindow();
            }

            escapeWasDown = escapeIsDown;
            leftButtonWasDown = leftButtonIsDown;
            rightButtonWasDown = rightButtonIsDown;
        }

        private void OnMotionTick(object sender, EventArgs args)
        {
            bool running = visibilityMotion.Update();
            running = toggleMotion.Update() || running;
            RenderLayeredWindow();
            if (closing && !visibilityMotion.IsRunning && visibilityMotion.Current <= 0.001F)
            {
                closing = false;
                Hide();
            }

            if (!running)
            {
                motionTimer.Stop();
            }
        }

        private void StartMotion()
        {
            if (!motionTimer.Enabled)
            {
                motionTimer.Start();
            }

            RenderLayeredWindow();
        }

        private static bool IsKeyDown(int virtualKey)
        {
            return (NativeMethods.GetAsyncKeyState(virtualKey) & 0x8000) != 0;
        }

        private static string BuildTimeText(TaskSnapshot task)
        {
            if (task.ActivityAtUtc == DateTimeOffset.MinValue)
            {
                return string.Empty;
            }

            TimeSpan age = DateTimeOffset.UtcNow - task.ActivityAtUtc;
            if (age < TimeSpan.Zero)
            {
                age = TimeSpan.Zero;
            }

            if (task.State == CodexTaskState.Running)
            {
                if (age.TotalHours >= 24)
                {
                    return ((int)age.TotalHours).ToString(CultureInfo.InvariantCulture) + "小时";
                }

                if (age.TotalHours >= 1)
                {
                    return ((int)age.TotalHours).ToString(CultureInfo.InvariantCulture) + ":" +
                           age.Minutes.ToString("00", CultureInfo.InvariantCulture);
                }

                return ((int)age.TotalMinutes).ToString("00", CultureInfo.InvariantCulture) + ":" +
                       age.Seconds.ToString("00", CultureInfo.InvariantCulture);
            }

            if (age.TotalMinutes < 1)
            {
                return "刚刚";
            }

            if (age.TotalHours < 1)
            {
                return ((int)age.TotalMinutes).ToString(CultureInfo.InvariantCulture) + "分钟前";
            }

            DateTimeOffset local = task.ActivityAtUtc.ToLocalTime();
            return age.TotalDays < 1
                ? local.ToString("HH:mm", CultureInfo.CurrentCulture)
                : local.ToString("M/d", CultureInfo.CurrentCulture);
        }

        private int S(int value)
        {
            return Math.Max(1, (int)Math.Round(value * scale));
        }

        private float SFloat(float value)
        {
            return value * scale;
        }

        private static RectangleF ScaleBoundsFromOrigin(RectangleF bounds, PointF origin, float scaleFactor)
        {
            return new RectangleF(
                origin.X + (bounds.Left - origin.X) * scaleFactor,
                origin.Y + (bounds.Top - origin.Y) * scaleFactor,
                bounds.Width * scaleFactor,
                bounds.Height * scaleFactor);
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
