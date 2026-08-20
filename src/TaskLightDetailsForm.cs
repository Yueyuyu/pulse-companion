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
        private const int DesignWidth = 344;
        private const int DesignHeaderHeight = 64;
        private const int DesignFooterHeight = 44;
        private const int DesignRowHeight = 58;
        private const int DesignEmptyHeight = 112;
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

        private readonly Timer outsideInputTimer;
        private readonly Timer motionTimer;
        private readonly bool motionEnabled;
        private readonly UiMotionValue visibilityMotion = new UiMotionValue(0F);
        private readonly UiMotionValue toggleMotion = new UiMotionValue(1F);
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
        private int selectedRow = HitNone;
        private bool escapeWasDown;
        private bool leftButtonWasDown;
        private bool rightButtonWasDown;
        private long lastClockSecond;
        private int hiddenTaskCount;

        public event EventHandler AlwaysOnTopChanged;

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
                selectedRow = action;
                RenderLayeredWindow();
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

        private void RenderLayeredWindow()
        {
            if (!Visible || !IsHandleCreated || IsDisposed || ClientSize.Width <= 0 || ClientSize.Height <= 0)
            {
                return;
            }

            using (Bitmap cardBitmap = RenderCardBitmap())
            using (Bitmap surface = new Bitmap(ClientSize.Width, ClientSize.Height, PixelFormat.Format32bppArgb))
            using (Graphics graphics = Graphics.FromImage(surface))
            {
                graphics.Clear(Color.Transparent);
                UiDrawing.Configure(graphics);

                RectangleF finalBounds = GetCardSurfaceBounds();
                float progress = visibilityMotion.Current;
                float cardScale = motionEnabled ? 0.97F + 0.03F * progress : 1F;
                PointF origin = new PointF(
                    finalBounds.Right - SFloat(20F),
                    opensBelow ? finalBounds.Top : finalBounds.Bottom);
                RectangleF animatedBounds = ScaleBoundsFromOrigin(finalBounds, origin, cardScale);

                UiDrawing.DrawPanelShadow(graphics, animatedBounds, SFloat(15F), scale);
                graphics.DrawImage(cardBitmap, animatedBounds);

                byte opacity = (byte)Math.Round(255F * progress);
                LayeredWindowRenderer.Present(Handle, Location, surface, opacity);
            }
        }

        private Bitmap RenderCardBitmap()
        {
            Bitmap bitmap = new Bitmap(cardSize.Width, cardSize.Height, PixelFormat.Format32bppArgb);
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(Color.Transparent);
                UiDrawing.Configure(graphics);
                RectangleF cardBounds = new RectangleF(0.75F, 0.75F, cardSize.Width - 1.5F, cardSize.Height - 1.5F);
                using (GraphicsPath card = UiDrawing.CreateRoundedPath(cardBounds, SFloat(15F)))
                using (SolidBrush background = new SolidBrush(Color.FromArgb(255, 254, 252)))
                using (Pen border = new Pen(Color.FromArgb(221, 224, 217), Math.Max(1F, scale)))
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
            using (Pen separator = new Pen(Color.FromArgb(235, 237, 232), Math.Max(1F, scale)))
            {
                graphics.DrawLine(separator, 0, headerHeight - 1, cardSize.Width, headerHeight - 1);
            }

            using (Font titleFont = UiDrawing.CreateFont(SFloat(14F), FontStyle.Bold))
            using (Font summaryFont = UiDrawing.CreateFont(SFloat(11F), FontStyle.Regular))
            using (SolidBrush titleBrush = new SolidBrush(Color.FromArgb(43, 46, 41)))
            using (SolidBrush summaryBrush = new SolidBrush(Color.FromArgb(126, 130, 122)))
            {
                graphics.DrawString("Codex 任务", titleFont, titleBrush, new PointF(SFloat(16F), SFloat(12F)));
                graphics.DrawString(BuildHeaderSummary(), summaryFont, summaryBrush, new PointF(SFloat(16F), SFloat(35F)));
            }

            bool closeHovered = hoveredHit == HitClose;
            bool closePressed = pressedHit == HitClose;
            if (closeHovered || closePressed)
            {
                using (GraphicsPath hoverPath = UiDrawing.CreateRoundedPath(closeBounds, SFloat(8F)))
                using (SolidBrush hoverBrush = new SolidBrush(closePressed
                    ? Color.FromArgb(226, 228, 222)
                    : Color.FromArgb(241, 243, 238)))
                {
                    graphics.FillPath(hoverBrush, hoverPath);
                }
            }

            using (Font closeFont = UiDrawing.CreateFont(SFloat(15F), FontStyle.Regular))
            using (SolidBrush closeBrush = new SolidBrush(Color.FromArgb(128, 132, 124)))
            using (StringFormat format = CenterFormat())
            {
                Rectangle closeGlyphBounds = closeBounds;
                if (closePressed)
                {
                    closeGlyphBounds.Offset(0, S(1));
                }

                graphics.DrawString("×", closeFont, closeBrush, closeGlyphBounds, format);
            }
        }

        private string BuildHeaderSummary()
        {
            if (!connected)
            {
                return "Codex 暂未连接";
            }

            if (snapshot.AttentionCount == 0 && snapshot.RunningCount == 0)
            {
                return "当前空闲 · 所有任务已完成";
            }

            if (snapshot.AttentionCount > 0 && snapshot.RunningCount > 0)
            {
                return snapshot.AttentionCount.ToString(CultureInfo.InvariantCulture) +
                       " 个需要处理 · " +
                       snapshot.RunningCount.ToString(CultureInfo.InvariantCulture) +
                       " 个执行中";
            }

            return snapshot.AttentionCount > 0
                ? snapshot.AttentionCount.ToString(CultureInfo.InvariantCulture) + " 个需要处理"
                : snapshot.RunningCount.ToString(CultureInfo.InvariantCulture) + " 个执行中";
        }

        private void DrawContent(Graphics graphics)
        {
            if (rows.Count == 0)
            {
                DrawEmptyState(graphics);
                return;
            }

            for (int index = 0; index < rows.Count; index++)
            {
                DrawTaskRow(graphics, index, rows[index]);
            }
        }

        private void DrawTaskRow(Graphics graphics, int index, RowLayout row)
        {
            bool selected = selectedRow == index;
            bool hovered = hoveredHit == index;
            bool pressed = pressedHit == index;
            bool needsAttention = row.Task.State == CodexTaskState.NeedsAttention;
            if (needsAttention || selected || hovered || pressed)
            {
                Color background = pressed
                    ? Color.FromArgb(231, 233, 227)
                    : (selected
                        ? Color.FromArgb(237, 239, 233)
                        : (hovered ? Color.FromArgb(242, 244, 239) : Color.FromArgb(247, 247, 244)));
                using (GraphicsPath path = UiDrawing.CreateRoundedPath(row.Bounds, SFloat(10F)))
                using (SolidBrush brush = new SolidBrush(background))
                {
                    graphics.FillPath(brush, path);
                }
            }

            int statusCenterX = row.Bounds.Left + S(21);
            int statusCenterY = row.Bounds.Top + row.Bounds.Height / 2;
            if (needsAttention)
            {
                using (SolidBrush haloBrush = new SolidBrush(Color.FromArgb(250, 224, 221)))
                using (SolidBrush iconBrush = new SolidBrush(Color.FromArgb(204, 84, 78)))
                using (Font iconFont = UiDrawing.CreateFont(SFloat(11F), FontStyle.Bold))
                using (StringFormat iconFormat = CenterFormat())
                {
                    Rectangle iconBounds = new Rectangle(statusCenterX - S(10), statusCenterY - S(10), S(20), S(20));
                    graphics.FillEllipse(haloBrush, iconBounds);
                    graphics.DrawString("!", iconFont, iconBrush, iconBounds, iconFormat);
                }
            }
            else
            {
                using (SolidBrush haloBrush = new SolidBrush(Color.FromArgb(249, 234, 208)))
                using (SolidBrush dotBrush = new SolidBrush(Color.FromArgb(221, 145, 41)))
                {
                    graphics.FillEllipse(haloBrush, statusCenterX - S(10), statusCenterY - S(10), S(20), S(20));
                    graphics.FillEllipse(dotBrush, statusCenterX - S(2), statusCenterY - S(2), S(5), S(5));
                }
            }

            int textLeft = row.Bounds.Left + S(44);
            int timeWidth = S(54);
            Rectangle titleBounds = new Rectangle(
                textLeft,
                row.Bounds.Top + S(8),
                row.Bounds.Right - textLeft - timeWidth - S(8),
                S(19));
            Rectangle detailBounds = new Rectangle(
                textLeft,
                row.Bounds.Top + S(30),
                row.Bounds.Right - textLeft - timeWidth - S(8),
                S(17));
            Rectangle timeBounds = new Rectangle(
                row.Bounds.Right - timeWidth - S(8),
                row.Bounds.Top,
                timeWidth,
                row.Bounds.Height);

            using (Font titleFont = UiDrawing.CreateFont(SFloat(12F), FontStyle.Bold))
            using (Font detailFont = UiDrawing.CreateFont(SFloat(11F), FontStyle.Regular))
            using (Font timeFont = UiDrawing.CreateFont(SFloat(11F), FontStyle.Regular))
            using (SolidBrush titleBrush = new SolidBrush(Color.FromArgb(48, 51, 46)))
            using (SolidBrush secondaryBrush = new SolidBrush(Color.FromArgb(139, 143, 134)))
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
            using (Font titleFont = UiDrawing.CreateFont(SFloat(12F), FontStyle.Bold))
            using (Font detailFont = UiDrawing.CreateFont(SFloat(11F), FontStyle.Regular))
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
            using (Pen separator = new Pen(Color.FromArgb(235, 237, 232), Math.Max(1F, scale)))
            {
                graphics.DrawLine(separator, 0, footerBounds.Top, cardSize.Width, footerBounds.Top);
            }

            string footerText = hiddenTaskCount > 0
                ? "另有 " + hiddenTaskCount.ToString(CultureInfo.InvariantCulture) + " 个活动任务"
                : "只读 · 每 2 秒刷新";
            using (Font font = UiDrawing.CreateFont(SFloat(11F), FontStyle.Regular))
            using (Font labelFont = UiDrawing.CreateFont(SFloat(11F), FontStyle.Bold))
            using (SolidBrush brush = new SolidBrush(Color.FromArgb(112, 116, 108)))
            using (StringFormat leftFormat = new StringFormat())
            using (StringFormat rightFormat = new StringFormat())
            {
                leftFormat.Alignment = StringAlignment.Near;
                leftFormat.LineAlignment = StringAlignment.Center;
                rightFormat.Alignment = StringAlignment.Far;
                rightFormat.LineAlignment = StringAlignment.Center;
                Rectangle left = new Rectangle(footerBounds.Left + S(16), footerBounds.Top, S(180), footerBounds.Height);
                Rectangle label = new Rectangle(toggleBounds.Left - S(45), footerBounds.Top, S(38), footerBounds.Height);
                graphics.DrawString(footerText, font, brush, left, leftFormat);
                graphics.DrawString("置顶", labelFont, brush, label, rightFormat);
            }

            Color offColor = Color.FromArgb(215, 218, 211);
            Color onColor = Color.FromArgb(76, 146, 103);
            Color switchColor = UiDrawing.Blend(offColor, onColor, toggleMotion.Current);
            if (hoveredHit == HitToggle)
            {
                switchColor = UiDrawing.Blend(switchColor, Color.FromArgb(59, 126, 84), 0.18F);
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
            rows.Clear();
            hiddenTaskCount = 0;

            int y = S(DesignHeaderHeight + 8);
            int contentLeft = S(8);
            int contentWidth = S(DesignWidth - 16);
            int visibleRows = 0;
            visibleRows = AddRows(CodexTaskState.NeedsAttention, y, contentLeft, contentWidth, visibleRows);
            y += visibleRows * S(DesignRowHeight);
            int runningBefore = visibleRows;
            visibleRows = AddRows(CodexTaskState.Running, y, contentLeft, contentWidth, visibleRows);
            y += (visibleRows - runningBefore) * S(DesignRowHeight);

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
                cardSize.Width - S(46),
                footerBounds.Top + (footerBounds.Height - S(20)) / 2,
                S(30),
                S(20));
            ClientSize = new Size(
                cardSize.Width + S(DesignPaddingLeft + DesignPaddingRight),
                cardSize.Height + S(DesignPaddingTop + DesignPaddingBottom));
        }

        private int AddRows(
            CodexTaskState state,
            int startY,
            int contentLeft,
            int contentWidth,
            int visibleRows)
        {
            int y = startY;
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
