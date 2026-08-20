using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.Windows.Forms;

namespace CodexQuotaOverlay
{
    internal sealed class DetailsPopupForm : Form
    {
        private const int DesignWidth = 220;
        private const int DesignHeight = 148;
        private const int DesignHeaderHeight = 66;
        private const int DesignGap = 8;
        private const int DesignSidebarRight = 307;
        private const int DesignPaddingLeft = 16;
        private const int DesignPaddingTop = 12;
        private const int DesignPaddingRight = 16;
        private const int DesignPaddingBottom = 20;
        private const int NoRow = -1;
        private const int RefreshRow = 0;
        private const int ExitRow = 1;
        private const int VkEscape = 0x1B;
        private const int VkLeftButton = 0x01;
        private const int VkRightButton = 0x02;

        private readonly Timer outsideInputTimer;
        private readonly Timer motionTimer;
        private readonly bool motionEnabled;
        private readonly UiMotionValue visibilityMotion = new UiMotionValue(0F);
        private QuotaSnapshot snapshot;
        private string statusText = "正在读取本周额度";
        private Rectangle anchorBounds;
        private Rectangle codexClientBounds;
        private Rectangle refreshBounds;
        private Rectangle exitBounds;
        private Size cardSize;
        private float uiScale = 1F;
        private int hoveredRow = NoRow;
        private int pressedRow = NoRow;
        private bool escapeWasDown;
        private bool leftButtonWasDown;
        private bool rightButtonWasDown;
        private bool opensBelow;
        private bool closing;

        public event EventHandler RefreshRequested;
        public event EventHandler ExitRequested;

        public bool IsClosing
        {
            get { return closing; }
        }

        public DetailsPopupForm()
        {
            motionEnabled = NativeMethods.AreClientAreaAnimationsEnabled();
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Color.Black;
            ControlBox = false;
            FormBorderStyle = FormBorderStyle.None;
            MaximizeBox = false;
            MinimizeBox = false;
            Name = "CodexQuotaDetails";
            ShowIcon = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Text = "Codex 周额度详情";
            AccessibleName = "Codex 周额度详情";
            AccessibleDescription = "显示本周剩余额度、重置时间以及刷新和退出操作。";

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

        public void UpdateQuota(QuotaSnapshot newSnapshot)
        {
            if (newSnapshot == null)
            {
                return;
            }

            snapshot = newSnapshot;
            RenderLayeredWindow();
        }

        public void UpdateStatus(string status)
        {
            if (string.IsNullOrWhiteSpace(status))
            {
                return;
            }

            statusText = status;
            if (snapshot == null)
            {
                RenderLayeredWindow();
            }
        }

        public void ShowAnchored(
            IWin32Window owner,
            Rectangle newAnchorBounds,
            Rectangle newCodexClientBounds,
            QuotaSnapshot newSnapshot,
            string newStatusText)
        {
            snapshot = newSnapshot;
            if (!string.IsNullOrWhiteSpace(newStatusText))
            {
                statusText = newStatusText;
            }

            UpdateScale(newAnchorBounds);
            Reposition(newAnchorBounds, newCodexClientBounds);
            hoveredRow = NoRow;
            pressedRow = NoRow;
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

        public void Reposition(Rectangle newAnchorBounds, Rectangle newCodexClientBounds)
        {
            anchorBounds = newAnchorBounds;
            codexClientBounds = newCodexClientBounds;

            Rectangle available = codexClientBounds;
            if (available.Width <= 0 || available.Height <= 0)
            {
                available = Screen.FromRectangle(anchorBounds).WorkingArea;
            }

            int margin = S(8);
            int sidebarRight = Math.Min(
                available.Right - margin,
                available.Left + S(DesignSidebarRight));
            int cardX = anchorBounds.Left + (anchorBounds.Width - cardSize.Width) / 2;
            int outerX = cardX - S(DesignPaddingLeft);
            int minimumOuterX = available.Left;
            int maximumOuterX = Math.Min(
                available.Right - Width,
                sidebarRight - cardSize.Width - S(DesignPaddingLeft));
            maximumOuterX = Math.Max(minimumOuterX, maximumOuterX);
            outerX = Math.Max(minimumOuterX, Math.Min(maximumOuterX, outerX));

            int aboveCardY = anchorBounds.Top - S(DesignGap) - cardSize.Height;
            int belowCardY = anchorBounds.Bottom + S(DesignGap);
            opensBelow = aboveCardY - S(DesignPaddingTop) < available.Top &&
                         belowCardY + cardSize.Height + S(DesignPaddingBottom) <= available.Bottom;
            int cardY = opensBelow ? belowCardY : aboveCardY;
            int outerY = cardY - S(DesignPaddingTop);
            outerY = Math.Max(available.Top, Math.Min(available.Bottom - Height, outerY));

            Bounds = new Rectangle(outerX, outerY, Width, Height);
            if (Visible)
            {
                ApplyWindowPosition();
                RenderLayeredWindow();
            }
        }

        public void ClosePopup()
        {
            ClosePopup(true);
        }

        public void ClosePopup(bool animate)
        {
            outsideInputTimer.Stop();
            Capture = false;
            hoveredRow = NoRow;
            pressedRow = NoRow;
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
            int newHoveredRow = GetRowAt(ToCardPoint(e.Location));
            if (hoveredRow != newHoveredRow)
            {
                hoveredRow = newHoveredRow;
                Cursor = hoveredRow == NoRow ? Cursors.Default : Cursors.Hand;
                RenderLayeredWindow();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (!Capture && hoveredRow != NoRow)
            {
                hoveredRow = NoRow;
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

            pressedRow = GetRowAt(ToCardPoint(e.Location));
            if (pressedRow != NoRow)
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

            int releasedRow = GetRowAt(ToCardPoint(e.Location));
            int actionRow = pressedRow;
            Capture = false;
            pressedRow = NoRow;
            RenderLayeredWindow();
            if (actionRow == NoRow || actionRow != releasedRow)
            {
                return;
            }

            ClosePopup(true);
            if (actionRow == RefreshRow)
            {
                RaiseRefreshRequested();
            }
            else if (actionRow == ExitRow)
            {
                RaiseExitRequested();
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
                float anchorCenterX = anchorBounds.Left + anchorBounds.Width / 2F - Left;
                float originX = Math.Max(finalBounds.Left, Math.Min(finalBounds.Right, anchorCenterX));
                PointF origin = new PointF(originX, opensBelow ? finalBounds.Top : finalBounds.Bottom);
                RectangleF animatedBounds = ScaleBoundsFromOrigin(finalBounds, origin, cardScale);

                UiDrawing.DrawPanelShadow(graphics, animatedBounds, SFloat(15F), uiScale);
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
                using (Pen border = new Pen(Color.FromArgb(221, 224, 217), Math.Max(1F, uiScale)))
                {
                    graphics.FillPath(background, card);
                    graphics.DrawPath(border, card);
                }

                DrawHeader(graphics);
                DrawActionRow(graphics, RefreshRow, refreshBounds, "立即刷新");
                DrawActionRow(graphics, ExitRow, exitBounds, "退出额度显示");
            }

            return bitmap;
        }

        private void DrawHeader(Graphics graphics)
        {
            string percentageText = snapshot == null
                ? "--"
                : snapshot.RemainingPercent.ToString(CultureInfo.InvariantCulture) + "%";
            string resetText = BuildResetText();
            using (Pen separator = new Pen(QuotaVisualStyle.Separator, Math.Max(1F, uiScale)))
            {
                float y = SFloat(DesignHeaderHeight) - Math.Max(1F, uiScale);
                graphics.DrawLine(separator, 0F, y, cardSize.Width, y);
            }

            using (Font titleFont = UiDrawing.CreateFont(SFloat(14F), FontStyle.Bold))
            using (Font percentageFont = UiDrawing.CreateFont(SFloat(14F), FontStyle.Bold))
            using (Font resetFont = UiDrawing.CreateFont(SFloat(11F), FontStyle.Regular))
            using (SolidBrush titleBrush = new SolidBrush(Color.FromArgb(43, 46, 41)))
            using (SolidBrush percentageBrush = new SolidBrush(QuotaVisualStyle.TextPrimary))
            using (SolidBrush resetBrush = new SolidBrush(QuotaVisualStyle.TextSecondary))
            using (StringFormat percentageFormat = new StringFormat())
            {
                percentageFormat.Alignment = StringAlignment.Near;
                percentageFormat.LineAlignment = StringAlignment.Center;
                percentageFormat.FormatFlags = StringFormatFlags.NoWrap;
                percentageFormat.Trimming = StringTrimming.None;
                graphics.DrawString("本周额度", titleFont, titleBrush, new PointF(SFloat(16F), SFloat(12F)));
                graphics.DrawString(resetText, resetFont, resetBrush, new PointF(SFloat(16F), SFloat(37F)));

                SizeF textSize = graphics.MeasureString(
                    percentageText,
                    percentageFont,
                    int.MaxValue,
                    StringFormat.GenericTypographic);
                float textWidth = Math.Max(SFloat(42F), textSize.Width + SFloat(8F));
                float contentLeft = SFloat(204F) - textWidth;
                RectangleF textBounds = new RectangleF(
                    contentLeft,
                    SFloat(8F),
                    textWidth,
                    SFloat(28F));
                graphics.DrawString(percentageText, percentageFont, percentageBrush, textBounds, percentageFormat);
            }
        }

        private void DrawActionRow(Graphics graphics, int row, Rectangle bounds, string text)
        {
            bool isHovered = hoveredRow == row;
            bool isPressed = pressedRow == row;
            RectangleF visualBounds = new RectangleF(bounds.X, bounds.Y, bounds.Width, bounds.Height);
            if (isPressed)
            {
                visualBounds = ScaleBounds(visualBounds, 0.985F);
            }

            Color background = Color.Transparent;
            if (row == ExitRow)
            {
                background = isPressed
                    ? Color.FromArgb(249, 232, 230)
                    : (isHovered ? Color.FromArgb(253, 242, 240) : Color.Transparent);
            }
            else
            {
                background = isPressed
                    ? Color.FromArgb(231, 233, 227)
                    : (isHovered ? Color.FromArgb(242, 244, 239) : Color.Transparent);
            }

            if (background != Color.Transparent)
            {
                using (GraphicsPath hoverPath = UiDrawing.CreateRoundedPath(visualBounds, SFloat(9F)))
                using (SolidBrush hoverBrush = new SolidBrush(background))
                {
                    graphics.FillPath(hoverBrush, hoverPath);
                }
            }

            Color foreground = row == ExitRow
                ? Color.FromArgb(178, 75, 70)
                : QuotaVisualStyle.TextPrimary;
            RectangleF textBounds = new RectangleF(
                visualBounds.Left + SFloat(42F),
                visualBounds.Top,
                visualBounds.Width - SFloat(54F),
                visualBounds.Height);
            using (Font actionFont = UiDrawing.CreateFont(SFloat(12F), FontStyle.Regular))
            using (SolidBrush textBrush = new SolidBrush(foreground))
            using (StringFormat format = CreateTextFormat(StringAlignment.Near))
            {
                graphics.DrawString(text, actionFont, textBrush, textBounds, format);
            }

            if (row == RefreshRow)
            {
                DrawRefreshIcon(graphics, foreground, visualBounds);
            }
            else
            {
                DrawExitIcon(graphics, foreground, visualBounds);
            }
        }

        private void DrawRefreshIcon(Graphics graphics, Color color, RectangleF rowBounds)
        {
            float iconLeft = rowBounds.Left + SFloat(11F);
            float iconTop = rowBounds.Top + (rowBounds.Height - SFloat(14F)) / 2F;
            RectangleF arcBounds = new RectangleF(iconLeft, iconTop, SFloat(13F), SFloat(13F));
            using (Pen pen = CreateIconPen(color))
            {
                graphics.DrawArc(pen, arcBounds, -55F, 285F);
                PointF arrowPoint = new PointF(iconLeft + SFloat(12.5F), iconTop + SFloat(1F));
                graphics.DrawLine(pen, arrowPoint, new PointF(iconLeft + SFloat(8.8F), iconTop + SFloat(0.8F)));
                graphics.DrawLine(pen, arrowPoint, new PointF(iconLeft + SFloat(12.2F), iconTop + SFloat(4.5F)));
            }
        }

        private void DrawExitIcon(Graphics graphics, Color color, RectangleF rowBounds)
        {
            float iconLeft = rowBounds.Left + SFloat(11F);
            float iconTop = rowBounds.Top + (rowBounds.Height - SFloat(14F)) / 2F;
            RectangleF arcBounds = new RectangleF(iconLeft, iconTop, SFloat(13F), SFloat(13F));
            using (Pen pen = CreateIconPen(color))
            {
                graphics.DrawArc(pen, arcBounds, -43F, 266F);
                float centerX = iconLeft + SFloat(6.5F);
                graphics.DrawLine(pen, centerX, iconTop - SFloat(0.5F), centerX, iconTop + SFloat(6.5F));
            }
        }

        private Pen CreateIconPen(Color color)
        {
            Pen pen = new Pen(color, Math.Max(1.25F, SFloat(1.35F)));
            pen.StartCap = LineCap.Round;
            pen.EndCap = LineCap.Round;
            pen.LineJoin = LineJoin.Round;
            return pen;
        }

        private void OnOutsideInputTick(object sender, EventArgs e)
        {
            bool escapeIsDown = IsKeyDown(VkEscape);
            bool leftButtonIsDown = IsKeyDown(VkLeftButton);
            bool rightButtonIsDown = IsKeyDown(VkRightButton);
            if (escapeIsDown && !escapeWasDown)
            {
                ClosePopup(false);
                return;
            }

            bool outsidePress = (leftButtonIsDown && !leftButtonWasDown) ||
                                (rightButtonIsDown && !rightButtonWasDown);
            if (outsidePress)
            {
                Point cursorPosition = Cursor.Position;
                if (!GetVisualBoundsScreen().Contains(cursorPosition) && !anchorBounds.Contains(cursorPosition))
                {
                    ClosePopup(true);
                    return;
                }
            }

            escapeWasDown = escapeIsDown;
            leftButtonWasDown = leftButtonIsDown;
            rightButtonWasDown = rightButtonIsDown;
        }

        private void OnMotionTick(object sender, EventArgs e)
        {
            bool running = visibilityMotion.Update();
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

        private void PrimeInputState()
        {
            escapeWasDown = IsKeyDown(VkEscape);
            leftButtonWasDown = IsKeyDown(VkLeftButton);
            rightButtonWasDown = IsKeyDown(VkRightButton);
        }

        private static bool IsKeyDown(int virtualKey)
        {
            return (NativeMethods.GetAsyncKeyState(virtualKey) & 0x8000) != 0;
        }

        private int GetRowAt(Point location)
        {
            if (refreshBounds.Contains(location))
            {
                return RefreshRow;
            }

            if (exitBounds.Contains(location))
            {
                return ExitRow;
            }

            return NoRow;
        }

        private Point ToCardPoint(Point point)
        {
            return new Point(point.X - S(DesignPaddingLeft), point.Y - S(DesignPaddingTop));
        }

        private void UpdateScale(Rectangle newAnchorBounds)
        {
            float newScale = Math.Max(1F, Math.Min(3F, newAnchorBounds.Height / 26F));
            if (Math.Abs(uiScale - newScale) < 0.01F)
            {
                return;
            }

            uiScale = newScale;
            BuildLayout();
        }

        private void BuildLayout()
        {
            cardSize = new Size(S(DesignWidth), S(DesignHeight));
            refreshBounds = new Rectangle(S(8), S(72), S(DesignWidth - 16), S(32));
            exitBounds = new Rectangle(S(8), S(108), S(DesignWidth - 16), S(32));
            ClientSize = new Size(
                cardSize.Width + S(DesignPaddingLeft + DesignPaddingRight),
                cardSize.Height + S(DesignPaddingTop + DesignPaddingBottom));
        }

        private void ApplyWindowPosition()
        {
            NativeMethods.SetWindowPos(
                Handle,
                NativeMethods.HwndTopMost,
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

        private string BuildResetText()
        {
            if (snapshot == null)
            {
                return statusText;
            }

            if (snapshot.ResetsAtUtc == DateTimeOffset.MinValue)
            {
                return "重置时间暂不可用";
            }

            return snapshot.ResetsAtUtc.ToLocalTime().ToString(
                "M月d日 HH:mm '重置'",
                CultureInfo.CurrentCulture);
        }

        private int S(int value)
        {
            return Math.Max(1, (int)Math.Round(value * uiScale));
        }

        private float SFloat(float value)
        {
            return value * uiScale;
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

        private static RectangleF ScaleBoundsFromOrigin(RectangleF bounds, PointF origin, float scaleFactor)
        {
            return new RectangleF(
                origin.X + (bounds.Left - origin.X) * scaleFactor,
                origin.Y + (bounds.Top - origin.Y) * scaleFactor,
                bounds.Width * scaleFactor,
                bounds.Height * scaleFactor);
        }

        private static StringFormat CreateTextFormat(StringAlignment alignment)
        {
            StringFormat format = new StringFormat();
            format.Alignment = alignment;
            format.LineAlignment = StringAlignment.Center;
            format.FormatFlags = StringFormatFlags.NoWrap;
            format.Trimming = StringTrimming.EllipsisCharacter;
            return format;
        }

        private void RaiseRefreshRequested()
        {
            EventHandler handler = RefreshRequested;
            if (handler != null)
            {
                handler(this, EventArgs.Empty);
            }
        }

        private void RaiseExitRequested()
        {
            EventHandler handler = ExitRequested;
            if (handler != null)
            {
                handler(this, EventArgs.Empty);
            }
        }
    }
}
