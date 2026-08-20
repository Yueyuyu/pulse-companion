using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.Windows.Forms;

namespace CodexQuotaOverlay
{
    internal sealed class QuotaOverlayForm : Form
    {
        private readonly ToolTip tooltip;
        private readonly DetailsPopupForm detailsPopup;
        private readonly Timer motionTimer;
        private readonly bool motionEnabled;
        private readonly UiMotionValue hoverMotion = new UiMotionValue(0F);
        private readonly UiMotionValue pressMotion = new UiMotionValue(0F);
        private QuotaSnapshot snapshot;
        private Rectangle codexClientBounds;
        private string displayText = "--";
        private string statusText = "正在读取本周额度";
        private bool pressed;
        private MouseButtons pressedButton = MouseButtons.None;

        public event EventHandler RefreshRequested;
        public event EventHandler ExitRequested;

        public QuotaOverlayForm()
        {
            motionEnabled = NativeMethods.AreClientAreaAnimationsEnabled();
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Color.Black;
            ClientSize = new Size(58, 26);
            ControlBox = false;
            Cursor = Cursors.Hand;
            FormBorderStyle = FormBorderStyle.None;
            MaximizeBox = false;
            MinimizeBox = false;
            Name = "CodexQuotaOverlay";
            ShowIcon = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Text = "Codex 周额度";
            AccessibleName = "Codex 本周剩余额度";
            AccessibleDescription = "显示本周剩余额度百分比，点击查看重置时间和操作。";

            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw |
                     ControlStyles.UserPaint, true);

            tooltip = new ToolTip();
            tooltip.AutoPopDelay = 8000;
            tooltip.InitialDelay = 350;
            tooltip.ReshowDelay = 100;
            tooltip.ShowAlways = true;
            tooltip.SetToolTip(this, statusText);

            detailsPopup = new DetailsPopupForm();
            detailsPopup.RefreshRequested += delegate { RaiseRefreshRequested(); };
            detailsPopup.ExitRequested += delegate { RaiseExitRequested(); };

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

        public void UpdateQuota(QuotaSnapshot newSnapshot)
        {
            if (newSnapshot == null)
            {
                return;
            }

            snapshot = newSnapshot;
            displayText = newSnapshot.RemainingPercent.ToString(CultureInfo.InvariantCulture) + "%";
            statusText = BuildTooltipText(newSnapshot);
            tooltip.SetToolTip(this, statusText);
            detailsPopup.UpdateQuota(newSnapshot);
            RenderLayeredWindow();
        }

        public void UpdateStatus(string status)
        {
            if (string.IsNullOrWhiteSpace(status))
            {
                return;
            }

            if (snapshot == null)
            {
                statusText = status;
                tooltip.SetToolTip(this, statusText);
                detailsPopup.UpdateStatus(status);
                RenderLayeredWindow();
            }
        }

        public void ShowAt(IntPtr codexWindow, Rectangle bounds)
        {
            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                HideOverlay();
                return;
            }

            if (ClientSize != bounds.Size)
            {
                ClientSize = bounds.Size;
            }

            if (Bounds != bounds)
            {
                Bounds = bounds;
            }

            codexClientBounds = GetClientScreenBounds(codexWindow);
            if (!Visible)
            {
                Show();
            }

            NativeMethods.SetWindowPos(
                Handle,
                NativeMethods.HwndTopMost,
                bounds.Left,
                bounds.Top,
                bounds.Width,
                bounds.Height,
                NativeMethods.SwpNoActivate | NativeMethods.SwpShowWindow | NativeMethods.SwpNoOwnerZOrder);

            RenderLayeredWindow();
            if (detailsPopup.Visible)
            {
                detailsPopup.Reposition(bounds, codexClientBounds);
            }
        }

        public void HideOverlay()
        {
            detailsPopup.ClosePopup(false);
            if (Visible)
            {
                Hide();
            }
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
            if (e.Button != MouseButtons.Left && e.Button != MouseButtons.Right)
            {
                return;
            }

            pressed = true;
            pressedButton = e.Button;
            Capture = true;
            pressMotion.AnimateTo(1F, 110, UiMotionCurve.EaseOut);
            StartMotion();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (!pressed || e.Button != pressedButton)
            {
                return;
            }

            bool activate = ClientRectangle.Contains(e.Location);
            pressed = false;
            pressedButton = MouseButtons.None;
            Capture = false;
            pressMotion.AnimateTo(0F, 140, UiMotionCurve.EaseOut);
            StartMotion();
            if (activate)
            {
                ToggleDetails();
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

                float scale = Math.Max(0.92F, ClientSize.Height / 26F);
                RectangleF cardBounds = new RectangleF(0.75F, 0.75F, ClientSize.Width - 1.5F, ClientSize.Height - 1.5F);
                float pressScale = motionEnabled ? 1F - pressMotion.Current * 0.018F : 1F;
                cardBounds = ScaleBounds(cardBounds, pressScale);
                if (motionEnabled)
                {
                    cardBounds.Offset(0F, 0.45F * scale * pressMotion.Current);
                }

                Color background = UiDrawing.Blend(
                    QuotaVisualStyle.GetPillSurface(snapshot),
                    QuotaVisualStyle.GetPillHoverSurface(snapshot),
                    hoverMotion.Current);
                background = UiDrawing.Blend(
                    background,
                    QuotaVisualStyle.GetPillPressedSurface(snapshot),
                    pressMotion.Current);
                Color borderColor = UiDrawing.Blend(
                    QuotaVisualStyle.GetPillBorder(snapshot),
                    QuotaVisualStyle.BorderHover,
                    hoverMotion.Current);
                using (GraphicsPath card = UiDrawing.CreateRoundedPath(cardBounds, cardBounds.Height / 2F))
                using (SolidBrush surface = new SolidBrush(background))
                using (Pen border = new Pen(borderColor, Math.Max(1F, scale)))
                {
                    graphics.FillPath(surface, card);
                    graphics.DrawPath(border, card);
                }

                DrawContent(graphics, cardBounds, scale);
                LayeredWindowRenderer.Present(Handle, Location, bitmap, 255);
            }
        }

        private void DrawContent(Graphics graphics, RectangleF cardBounds, float scale)
        {
            using (Font font = UiDrawing.CreateFont(12F * scale, FontStyle.Regular))
            using (SolidBrush textBrush = new SolidBrush(QuotaVisualStyle.GetPillText(snapshot)))
            using (StringFormat format = new StringFormat())
            {
                format.Alignment = StringAlignment.Center;
                format.LineAlignment = StringAlignment.Center;
                format.FormatFlags = StringFormatFlags.NoWrap;
                graphics.DrawString(displayText, font, textBrush, cardBounds, format);
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
                detailsPopup.ShowAnchored(this, Bounds, codexClientBounds, snapshot, statusText);
            }
        }

        private void OnMotionTick(object sender, EventArgs e)
        {
            bool running = hoverMotion.Update();
            running = pressMotion.Update() || running;
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

        private static Rectangle GetClientScreenBounds(IntPtr window)
        {
            NativeMethods.RECT client;
            NativeMethods.POINT origin = new NativeMethods.POINT();
            if (window == IntPtr.Zero ||
                !NativeMethods.GetClientRect(window, out client) ||
                !NativeMethods.ClientToScreen(window, ref origin))
            {
                return Rectangle.Empty;
            }

            return new Rectangle(
                origin.X,
                origin.Y,
                Math.Max(0, client.Right - client.Left),
                Math.Max(0, client.Bottom - client.Top));
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

        private static string BuildTooltipText(QuotaSnapshot value)
        {
            string text = "Codex 本周剩余 " + value.RemainingPercent.ToString(CultureInfo.InvariantCulture) + "%";
            string reset = BuildResetText(value);
            if (!string.IsNullOrEmpty(reset))
            {
                text += Environment.NewLine + reset;
            }

            return text;
        }

        private static string BuildResetText(QuotaSnapshot value)
        {
            if (value.ResetsAtUtc == DateTimeOffset.MinValue)
            {
                return "重置时间暂不可用";
            }

            return "重置：" + value.ResetsAtUtc.ToLocalTime().ToString("M月d日 HH:mm", CultureInfo.CurrentCulture);
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
