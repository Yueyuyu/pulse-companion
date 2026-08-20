using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.Windows.Forms;

namespace CodexQuotaOverlay
{
    internal sealed class TaskLightForm : Form
    {
        private const int DesignHeight = 40;
        private const int DesignMixedWidth = 188;
        private const int DesignAlertWidth = 108;
        private const int DesignRunningWidth = 106;
        private const int DesignIdleWidth = 84;
        private const int DesignOfflineWidth = 124;
        private const int DesignPaddingLeft = 12;
        private const int DesignPaddingTop = 10;
        private const int DesignPaddingRight = 12;
        private const int DesignPaddingBottom = 15;
        private const int DragThreshold = 5;

        private readonly TaskLightSettings settings;
        private readonly TaskLightDetailsForm detailsPopup;
        private readonly ToolTip tooltip;
        private readonly Timer motionTimer;
        private readonly bool motionEnabled;
        private readonly UiMotionValue hoverMotion = new UiMotionValue(0F);
        private readonly UiMotionValue pressMotion = new UiMotionValue(0F);
        private readonly UiMotionValue feedbackMotion = new UiMotionValue(0F);
        private readonly UiMotionValue entranceMotion = new UiMotionValue(1F);
        private TaskListSnapshot snapshot = new TaskListSnapshot(null);
        private bool connected;
        private string connectionStatus = "正在连接 Codex";
        private float scale = 1F;
        private bool pressed;
        private bool dragging;
        private bool shownOnce;
        private Point dragStartCursor;
        private Point dragStartLocation;

        public TaskLightForm()
        {
            settings = TaskLightSettings.Load();
            motionEnabled = NativeMethods.AreClientAreaAnimationsEnabled();
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Color.Black;
            ClientSize = GetSurfaceSize(DesignOfflineWidth);
            ControlBox = false;
            Cursor = Cursors.Hand;
            FormBorderStyle = FormBorderStyle.None;
            MaximizeBox = false;
            MinimizeBox = false;
            Name = "CodexTaskLight";
            ShowIcon = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Text = "Codex 任务灯";
            AccessibleName = "Codex 任务灯";
            AccessibleDescription = "显示需要处理和正在执行的 Codex 任务数量。拖动可移动，点击查看详情。";

            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw |
                     ControlStyles.UserPaint, true);

            detailsPopup = new TaskLightDetailsForm();
            detailsPopup.AlwaysOnTopChanged += OnAlwaysOnTopChanged;
            detailsPopup.SetAlwaysOnTop(settings.AlwaysOnTop);

            tooltip = new ToolTip();
            tooltip.AutoPopDelay = 8000;
            tooltip.InitialDelay = 350;
            tooltip.ReshowDelay = 100;
            tooltip.ShowAlways = true;
            UpdateTooltip();

            motionTimer = new Timer();
            motionTimer.Interval = 15;
            motionTimer.Tick += OnMotionTick;
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

        public void ShowTaskLight()
        {
            IntPtr unused = Handle;
            UpdateScaleFromWindow();
            UpdateSize(false);
            if (settings.HasPosition)
            {
                EnsureRightAnchor();
                Location = GetAnchoredLocation(settings.AnchorRight, settings.Top);
            }
            else
            {
                Location = GetDefaultLocation();
            }

            if (!Visible)
            {
                if (!shownOnce)
                {
                    entranceMotion.JumpTo(0F);
                }

                Show();
            }

            ApplyWindowPosition();
            RenderLayeredWindow();
            if (!shownOnce)
            {
                shownOnce = true;
                entranceMotion.AnimateTo(1F, motionEnabled ? 180 : 120, UiMotionCurve.EaseOut);
                StartMotion();
            }
        }

        public void UpdateTasks(TaskListSnapshot newSnapshot)
        {
            bool shouldSignalChange = connected && newSnapshot != null &&
                                      (snapshot.AttentionCount != newSnapshot.AttentionCount ||
                                       snapshot.RunningCount != newSnapshot.RunningCount);
            snapshot = newSnapshot ?? new TaskListSnapshot(null);
            connected = true;
            connectionStatus = "Codex 任务已更新";
            detailsPopup.UpdateTasks(snapshot);
            UpdateSize(true);
            UpdateTooltip();
            if (shouldSignalChange)
            {
                TriggerFeedback();
            }

            RenderLayeredWindow();
        }

        public void UpdateConnection(bool isConnected, string status)
        {
            bool connectionChanged = connected != isConnected;
            connected = isConnected;
            if (!string.IsNullOrWhiteSpace(status))
            {
                connectionStatus = status;
            }

            if (!connected)
            {
                snapshot = new TaskListSnapshot(null);
            }

            detailsPopup.UpdateConnection(connected);
            UpdateSize(true);
            UpdateTooltip();
            if (connectionChanged)
            {
                TriggerFeedback();
            }
            else
            {
                RenderLayeredWindow();
            }
        }

        public void CloseDetails()
        {
            detailsPopup.ClosePopup(false);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            RenderLayeredWindow();
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            hoverMotion.AnimateTo(1F, 120, UiMotionCurve.EaseOut);
            StartMotion();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (!Capture)
            {
                hoverMotion.AnimateTo(0F, 120, UiMotionCurve.EaseOut);
                StartMotion();
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left)
            {
                return;
            }

            dragStartCursor = Cursor.Position;
            dragStartLocation = Location;
            dragging = false;
            pressed = true;
            Capture = true;
            pressMotion.AnimateTo(1F, 110, UiMotionCurve.EaseOut);
            StartMotion();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!Capture || !pressed)
            {
                return;
            }

            Point cursor = Cursor.Position;
            int deltaX = cursor.X - dragStartCursor.X;
            int deltaY = cursor.Y - dragStartCursor.Y;
            if (!dragging && Math.Abs(deltaX) + Math.Abs(deltaY) >= S(DragThreshold))
            {
                dragging = true;
                pressMotion.AnimateTo(0F, 110, UiMotionCurve.EaseOut);
                StartMotion();
            }

            if (!dragging)
            {
                return;
            }

            Point desired = new Point(dragStartLocation.X + deltaX, dragStartLocation.Y + deltaY);
            Location = ClampLocation(desired);
            if (detailsPopup.Visible)
            {
                detailsPopup.Reposition(GetVisualBoundsScreen());
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left || !pressed)
            {
                return;
            }

            bool wasDragging = dragging;
            pressed = false;
            dragging = false;
            Capture = false;
            pressMotion.AnimateTo(0F, 140, UiMotionCurve.EaseOut);
            StartMotion();
            if (wasDragging)
            {
                SavePosition();
                return;
            }

            ToggleDetails();
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
                tooltip.Dispose();
                motionTimer.Dispose();
                detailsPopup.Dispose();
            }

            base.Dispose(disposing);
        }

        private void RenderLayeredWindow()
        {
            if (!Visible || !IsHandleCreated || IsDisposed || ClientSize.Width <= 0 || ClientSize.Height <= 0)
            {
                return;
            }

            using (Bitmap bitmap = new Bitmap(ClientSize.Width, ClientSize.Height, PixelFormat.Format32bppArgb))
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(Color.Transparent);
                UiDrawing.Configure(graphics);

                RectangleF cardBounds = GetCardBounds();
                float pressScale = motionEnabled ? 1F - pressMotion.Current * 0.018F : 1F;
                float verticalShift = motionEnabled ? SFloat(0.6F) * pressMotion.Current : 0F;
                cardBounds = ScaleBounds(cardBounds, pressScale);
                cardBounds.Offset(0F, verticalShift);

                UiDrawing.DrawCompactShadow(graphics, cardBounds, SFloat(14F), scale);
                Color backgroundColor = UiDrawing.Blend(
                    Color.FromArgb(255, 254, 253),
                    Color.FromArgb(248, 249, 246),
                    hoverMotion.Current);
                backgroundColor = UiDrawing.Blend(
                    backgroundColor,
                    Color.FromArgb(244, 245, 242),
                    pressMotion.Current);
                Color borderColor = UiDrawing.Blend(
                    Color.FromArgb(222, 224, 218),
                    Color.FromArgb(207, 211, 203),
                    hoverMotion.Current);
                using (GraphicsPath card = UiDrawing.CreateRoundedPath(cardBounds, SFloat(14F)))
                using (SolidBrush background = new SolidBrush(backgroundColor))
                using (Pen border = new Pen(borderColor, Math.Max(1F, scale)))
                {
                    graphics.FillPath(background, card);
                    graphics.DrawPath(border, card);
                }

                DrawStatusContent(graphics, cardBounds);
                DrawFeedback(graphics, cardBounds);

                byte opacity = (byte)Math.Round(255F * entranceMotion.Current);
                LayeredWindowRenderer.Present(Handle, Location, bitmap, opacity);
            }
        }

        private void DrawStatusContent(Graphics graphics, RectangleF cardBounds)
        {
            if (!connected)
            {
                DrawSegment(graphics, cardBounds, Color.FromArgb(143, 148, 140), "Codex 离线");
                return;
            }

            if (snapshot.AttentionCount > 0 && snapshot.RunningCount > 0)
            {
                float firstWidth = cardBounds.Width / 2F;
                RectangleF alert = new RectangleF(cardBounds.Left, cardBounds.Top, firstWidth, cardBounds.Height);
                RectangleF running = new RectangleF(cardBounds.Left + firstWidth, cardBounds.Top, cardBounds.Width - firstWidth, cardBounds.Height);
                using (Pen separator = new Pen(Color.FromArgb(224, 226, 220), Math.Max(1F, scale)))
                {
                    graphics.DrawLine(
                        separator,
                        alert.Right,
                        cardBounds.Top + SFloat(9F),
                        alert.Right,
                        cardBounds.Bottom - SFloat(9F));
                }

                DrawSegment(
                    graphics,
                    alert,
                    Color.FromArgb(228, 103, 96),
                    "需处理 " + snapshot.AttentionCount.ToString(CultureInfo.InvariantCulture));
                DrawSegment(
                    graphics,
                    running,
                    Color.FromArgb(229, 154, 49),
                    "执行中 " + snapshot.RunningCount.ToString(CultureInfo.InvariantCulture));
            }
            else if (snapshot.AttentionCount > 0)
            {
                DrawSegment(
                    graphics,
                    cardBounds,
                    Color.FromArgb(228, 103, 96),
                    "需处理 " + snapshot.AttentionCount.ToString(CultureInfo.InvariantCulture));
            }
            else if (snapshot.RunningCount > 0)
            {
                DrawSegment(
                    graphics,
                    cardBounds,
                    Color.FromArgb(229, 154, 49),
                    "执行中 " + snapshot.RunningCount.ToString(CultureInfo.InvariantCulture));
            }
            else
            {
                DrawSegment(graphics, cardBounds, Color.FromArgb(73, 143, 99), "空闲");
            }
        }

        private void DrawSegment(Graphics graphics, RectangleF bounds, Color indicatorColor, string text)
        {
            using (Font font = UiDrawing.CreateFont(SFloat(12F), FontStyle.Bold))
            using (SolidBrush textBrush = new SolidBrush(Color.FromArgb(47, 50, 45)))
            {
                SizeF textSize = graphics.MeasureString(text, font, int.MaxValue, StringFormat.GenericTypographic);
                float dotSize = SFloat(7F);
                float gap = SFloat(7F);
                float contentWidth = dotSize + gap + textSize.Width;
                float startX = bounds.Left + (bounds.Width - contentWidth) / 2F;
                float centerY = bounds.Top + bounds.Height / 2F;

                using (SolidBrush haloBrush = new SolidBrush(Color.FromArgb(34, indicatorColor)))
                using (SolidBrush indicatorBrush = new SolidBrush(indicatorColor))
                using (SolidBrush highlightBrush = new SolidBrush(Color.FromArgb(120, 255, 255, 255)))
                {
                    graphics.FillEllipse(
                        haloBrush,
                        startX - SFloat(3F),
                        centerY - dotSize / 2F - SFloat(3F),
                        dotSize + SFloat(6F),
                        dotSize + SFloat(6F));
                    graphics.FillEllipse(indicatorBrush, startX, centerY - dotSize / 2F, dotSize, dotSize);
                    graphics.FillEllipse(
                        highlightBrush,
                        startX + SFloat(1F),
                        centerY - dotSize / 2F + SFloat(1F),
                        SFloat(2F),
                        SFloat(2F));
                }

                RectangleF textBounds = new RectangleF(
                    startX + dotSize + gap,
                    bounds.Top,
                    Math.Max(SFloat(1F), bounds.Right - (startX + dotSize + gap) - SFloat(4F)),
                    bounds.Height);
                using (StringFormat format = new StringFormat())
                {
                    format.Alignment = StringAlignment.Near;
                    format.LineAlignment = StringAlignment.Center;
                    format.FormatFlags = StringFormatFlags.NoWrap;
                    graphics.DrawString(text, font, textBrush, textBounds, format);
                }
            }
        }

        private void DrawFeedback(Graphics graphics, RectangleF cardBounds)
        {
            if (feedbackMotion.Current <= 0.001F)
            {
                return;
            }

            Color feedbackColor = !connected
                ? Color.FromArgb(143, 148, 140)
                : (snapshot.AttentionCount > 0
                    ? Color.FromArgb(228, 103, 96)
                    : (snapshot.RunningCount > 0
                        ? Color.FromArgb(229, 154, 49)
                        : Color.FromArgb(73, 143, 99)));
            int alpha = (int)Math.Round(120F * feedbackMotion.Current);
            RectangleF feedbackBounds = cardBounds;
            feedbackBounds.Inflate(SFloat(1.5F), SFloat(1.5F));
            using (GraphicsPath feedbackPath = UiDrawing.CreateRoundedPath(feedbackBounds, SFloat(15F)))
            using (Pen feedbackPen = new Pen(Color.FromArgb(alpha, feedbackColor), Math.Max(1F, SFloat(1.25F))))
            {
                graphics.DrawPath(feedbackPen, feedbackPath);
            }
        }

        private void ToggleDetails()
        {
            if (detailsPopup.Visible && !detailsPopup.IsClosing)
            {
                detailsPopup.ClosePopup(true);
            }
            else
            {
                detailsPopup.ShowAnchored(this, GetVisualBoundsScreen(), settings.AlwaysOnTop);
            }
        }

        private void OnAlwaysOnTopChanged(object sender, EventArgs args)
        {
            settings.AlwaysOnTop = detailsPopup.AlwaysOnTop;
            settings.Save();
            ApplyWindowPosition();
        }

        private void OnMotionTick(object sender, EventArgs args)
        {
            bool running = hoverMotion.Update();
            running = pressMotion.Update() || running;
            running = feedbackMotion.Update() || running;
            running = entranceMotion.Update() || running;
            RenderLayeredWindow();
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

        private void UpdateScaleFromWindow()
        {
            try
            {
                uint dpi = NativeMethods.GetDpiForWindow(Handle);
                scale = Math.Max(1F, Math.Min(3F, dpi / 96F));
            }
            catch (Exception)
            {
                scale = 1F;
            }
        }

        private void UpdateSize(bool preserveRightEdge)
        {
            int designWidth = GetDesignWidth();
            Size newSize = GetSurfaceSize(designWidth);
            if (ClientSize == newSize)
            {
                return;
            }

            Rectangle currentVisual = GetVisualBoundsScreen();
            int anchorRight = settings.HasPosition && settings.UseRightAnchor
                ? settings.AnchorRight
                : currentVisual.Right;
            int visualTop = currentVisual.Top;
            ClientSize = newSize;
            if (Visible && preserveRightEdge)
            {
                // 状态文字改变宽度时围绕同一可见右边缘伸缩，阴影留白不参与定位。
                Location = GetAnchoredLocation(anchorRight, visualTop);
                if (detailsPopup.Visible)
                {
                    detailsPopup.Reposition(GetVisualBoundsScreen());
                }
            }
        }

        private int GetDesignWidth()
        {
            if (!connected)
            {
                return DesignOfflineWidth;
            }

            if (snapshot.AttentionCount > 0 && snapshot.RunningCount > 0)
            {
                return DesignMixedWidth;
            }

            if (snapshot.AttentionCount > 0)
            {
                return DesignAlertWidth;
            }

            return snapshot.RunningCount > 0 ? DesignRunningWidth : DesignIdleWidth;
        }

        private Size GetSurfaceSize(int designWidth)
        {
            return new Size(
                S(designWidth + DesignPaddingLeft + DesignPaddingRight),
                S(DesignHeight + DesignPaddingTop + DesignPaddingBottom));
        }

        private RectangleF GetCardBounds()
        {
            return new RectangleF(
                SFloat(DesignPaddingLeft),
                SFloat(DesignPaddingTop),
                ClientSize.Width - SFloat(DesignPaddingLeft + DesignPaddingRight),
                ClientSize.Height - SFloat(DesignPaddingTop + DesignPaddingBottom));
        }

        private Rectangle GetVisualBoundsScreen()
        {
            RectangleF card = GetCardBounds();
            return Rectangle.Round(new RectangleF(
                Left + card.Left,
                Top + card.Top,
                card.Width,
                card.Height));
        }

        private Point GetDefaultLocation()
        {
            Rectangle workingArea = Screen.PrimaryScreen.WorkingArea;
            int visualRight = workingArea.Right - S(28);
            int visualTop = workingArea.Top + S(72);
            return GetAnchoredLocation(visualRight, visualTop);
        }

        private Point ClampLocation(Point desired)
        {
            RectangleF card = GetCardBounds();
            Rectangle desiredVisual = Rectangle.Round(new RectangleF(
                desired.X + card.Left,
                desired.Y + card.Top,
                card.Width,
                card.Height));
            Point center = new Point(
                desiredVisual.Left + desiredVisual.Width / 2,
                desiredVisual.Top + desiredVisual.Height / 2);
            Rectangle area = Screen.FromPoint(center).WorkingArea;
            int visualX = Math.Max(area.Left, Math.Min(desiredVisual.Left, area.Right - desiredVisual.Width));
            int visualY = Math.Max(area.Top, Math.Min(desiredVisual.Top, area.Bottom - desiredVisual.Height));
            return new Point(
                visualX - (int)Math.Round(card.Left),
                visualY - (int)Math.Round(card.Top));
        }

        private Point GetAnchoredLocation(int anchorRight, int visualTop)
        {
            RectangleF card = GetCardBounds();
            Point outer = new Point(
                anchorRight - (int)Math.Round(card.Width) - (int)Math.Round(card.Left),
                visualTop - (int)Math.Round(card.Top));
            return ClampLocation(outer);
        }

        private void EnsureRightAnchor()
        {
            if (settings.UseRightAnchor)
            {
                return;
            }

            int legacyWidth = settings.SavedWidth > 0
                ? settings.SavedWidth
                : S(DesignMixedWidth);
            settings.UseRightAnchor = true;
            settings.AnchorRight = settings.Left + legacyWidth;
            settings.SavedWidth = legacyWidth;
            settings.Save();
        }

        private void SavePosition()
        {
            Rectangle visual = GetVisualBoundsScreen();
            settings.HasPosition = true;
            settings.Left = visual.Left;
            settings.Top = visual.Top;
            settings.UseRightAnchor = true;
            settings.AnchorRight = visual.Right;
            settings.SavedWidth = visual.Width;
            settings.Save();
        }

        private void ApplyWindowPosition()
        {
            NativeMethods.SetWindowPos(
                Handle,
                settings.AlwaysOnTop ? NativeMethods.HwndTopMost : NativeMethods.HwndNoTopMost,
                Bounds.Left,
                Bounds.Top,
                Bounds.Width,
                Bounds.Height,
                NativeMethods.SwpNoActivate | NativeMethods.SwpShowWindow | NativeMethods.SwpNoOwnerZOrder);
            detailsPopup.SetAlwaysOnTop(settings.AlwaysOnTop);
        }

        private void UpdateTooltip()
        {
            string text;
            if (!connected)
            {
                text = connectionStatus;
            }
            else if (snapshot.AttentionCount > 0 || snapshot.RunningCount > 0)
            {
                text = "需要处理 " + snapshot.AttentionCount.ToString(CultureInfo.InvariantCulture) +
                       " · 执行中 " + snapshot.RunningCount.ToString(CultureInfo.InvariantCulture);
            }
            else
            {
                text = "Codex 当前空闲，所有任务已完成";
            }

            tooltip.SetToolTip(this, text + Environment.NewLine + "拖动可移动，点击查看详情");
        }

        private void TriggerFeedback()
        {
            feedbackMotion.JumpTo(1F);
            feedbackMotion.AnimateTo(0F, 240, UiMotionCurve.EaseOut);
            StartMotion();
        }

        private int S(int value)
        {
            return Math.Max(1, (int)Math.Round(value * scale));
        }

        private float SFloat(float value)
        {
            return value * scale;
        }

        private static RectangleF ScaleBounds(RectangleF bounds, float scaleFactor)
        {
            float width = bounds.Width * scaleFactor;
            float height = bounds.Height * scaleFactor;
            return new RectangleF(
                bounds.Left + (bounds.Width - width) / 2F,
                bounds.Top + (bounds.Height - height) / 2F,
                width,
                height);
        }
    }
}
