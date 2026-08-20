using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace CodexQuotaOverlay
{
    internal enum TaskToastKind
    {
        Completed,
        NeedsAttention,
        NavigationError
    }

    /// <summary>
    /// Companion 自己控制的桌面通知，不依赖 Windows 11 可能被吞掉的托盘气泡。
    /// 通知始终从当前屏幕工作区右下角出现，并保持不抢焦点。
    /// </summary>
    internal sealed class TaskToastForm : Form
    {
        private const int DesignCardWidth = 360;
        private const int DesignCardHeight = 112;
        private const int DesignPaddingLeft = 18;
        private const int DesignPaddingTop = 18;
        private const int DesignPaddingRight = 36;
        private const int DesignPaddingBottom = 28;
        private const int VkEscape = 0x1B;

        private readonly Timer motionTimer;
        private readonly Timer dismissTimer;
        private readonly bool motionEnabled;
        private readonly UiMotionValue visibilityMotion = new UiMotionValue(0F);
        private readonly UiMotionValue hoverMotion = new UiMotionValue(0F);
        private readonly UiMotionValue pressMotion = new UiMotionValue(0F);
        private TaskSnapshot task;
        private TaskToastKind kind;
        private string title = string.Empty;
        private string body = string.Empty;
        private string detail = string.Empty;
        private float scale = 1F;
        private bool closing;
        private bool pressed;
        private bool escapeWasDown;
        private Rectangle closeBounds;

        public event EventHandler<TaskActivatedEventArgs> TaskActivated;

        public TaskToastForm()
        {
            motionEnabled = NativeMethods.AreClientAreaAnimationsEnabled();
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Color.Black;
            ControlBox = false;
            Cursor = Cursors.Hand;
            FormBorderStyle = FormBorderStyle.None;
            MaximizeBox = false;
            MinimizeBox = false;
            Name = "CodexTaskToast";
            ShowIcon = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Text = "Codex 任务通知";
            AccessibleName = "Codex 任务通知";
            AccessibleDescription = "显示 Codex 任务完成或需要处理的提醒，点击可打开对应任务。";

            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw |
                     ControlStyles.UserPaint, true);

            motionTimer = new Timer();
            motionTimer.Interval = 15;
            motionTimer.Tick += OnMotionTick;

            dismissTimer = new Timer();
            dismissTimer.Interval = 8000;
            dismissTimer.Tick += delegate
            {
                dismissTimer.Stop();
                CloseToast(true);
            };

            UpdateScale();
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

        public bool ShowNotification(TaskToastKind notificationKind, TaskSnapshot notificationTask, string notificationTitle, string notificationBody, string notificationDetail)
        {
            try
            {
                task = notificationTask;
                kind = notificationKind;
                title = notificationTitle ?? string.Empty;
                body = notificationBody ?? string.Empty;
                detail = notificationDetail ?? string.Empty;
                pressed = false;
                closing = false;
                escapeWasDown = IsKeyDown(VkEscape);

                IntPtr unused = Handle;
                UpdateScale();
                PositionAtWorkingAreaBottomRight();
                if (!Visible)
                {
                    visibilityMotion.JumpTo(0F);
                    Show();
                }

                ApplyWindowPosition();
                visibilityMotion.AnimateTo(1F, motionEnabled ? 180 : 1, UiMotionCurve.EaseOut);
                StartMotion();
                dismissTimer.Stop();
                dismissTimer.Interval = 8000;
                dismissTimer.Start();
                return true;
            }
            catch (Exception)
            {
                dismissTimer.Stop();
                return false;
            }
        }

        public void CloseToast(bool animate)
        {
            dismissTimer.Stop();
            Capture = false;
            pressed = false;
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
            visibilityMotion.AnimateTo(0F, motionEnabled ? 160 : 1, UiMotionCurve.EaseOut);
            StartMotion();
        }

        public void SavePreviewScreenshot(string outputPath)
        {
            if (string.IsNullOrWhiteSpace(outputPath))
            {
                return;
            }

            using (Bitmap surface = RenderSurfaceBitmap(1F))
            using (Bitmap composite = new Bitmap(surface.Width, surface.Height, PixelFormat.Format32bppArgb))
            using (Graphics graphics = Graphics.FromImage(composite))
            {
                graphics.Clear(Color.FromArgb(232, 238, 234));
                graphics.DrawImageUnscaled(surface, 0, 0);
                string directory = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                composite.Save(outputPath, ImageFormat.Png);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            RenderLayeredWindow();
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            dismissTimer.Stop();
            hoverMotion.AnimateTo(1F, 140, UiMotionCurve.EaseOut);
            StartMotion();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (!Capture)
            {
                hoverMotion.AnimateTo(0F, 140, UiMotionCurve.EaseOut);
                StartMotion();
                dismissTimer.Interval = 3000;
                dismissTimer.Start();
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left)
            {
                return;
            }

            pressed = true;
            Capture = true;
            pressMotion.AnimateTo(1F, 100, UiMotionCurve.EaseOut);
            StartMotion();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left || !pressed)
            {
                return;
            }

            bool closeHit = ToCardPoint(e.Location).X >= 0 && closeBounds.Contains(ToCardPoint(e.Location));
            bool cardHit = GetCardSurfaceBounds().Contains(e.Location);
            pressed = false;
            Capture = false;
            pressMotion.AnimateTo(0F, 140, UiMotionCurve.EaseOut);
            StartMotion();

            if (closeHit)
            {
                CloseToast(true);
                return;
            }

            if (cardHit && task != null && kind != TaskToastKind.NavigationError)
            {
                EventHandler<TaskActivatedEventArgs> handler = TaskActivated;
                if (handler != null)
                {
                    handler(this, new TaskActivatedEventArgs(task));
                }

                CloseToast(true);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            Point cardPoint = ToCardPoint(e.Location);
            Cursor = closeBounds.Contains(cardPoint) || (task != null && kind != TaskToastKind.NavigationError)
                ? Cursors.Hand
                : Cursors.Default;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                dismissTimer.Dispose();
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

            float progress = visibilityMotion.Current;
            using (Bitmap bitmap = RenderSurfaceBitmap(progress))
            {
                byte opacity = (byte)Math.Round(255F * progress);
                LayeredWindowRenderer.Present(Handle, Location, bitmap, opacity);
            }
        }

        private Bitmap RenderSurfaceBitmap(float progress)
        {
            Bitmap bitmap = new Bitmap(ClientSize.Width, ClientSize.Height, PixelFormat.Format32bppArgb);
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(Color.Transparent);
                UiDrawing.Configure(graphics);

                RectangleF cardBounds = GetCardSurfaceBounds();
                float slide = motionEnabled ? SFloat(18F) * (1F - progress) : 0F;
                cardBounds.Offset(slide, 0F);
                if (motionEnabled && pressMotion.Current > 0F)
                {
                    cardBounds.Offset(0F, SFloat(1F) * pressMotion.Current);
                }

                UiDrawing.DrawPanelShadow(graphics, cardBounds, SFloat(14F), scale);
                Color baseSurface = kind == TaskToastKind.NeedsAttention
                    ? Color.FromArgb(255, 252, 251)
                    : (kind == TaskToastKind.Completed
                        ? Color.FromArgb(249, 252, 249)
                        : Color.FromArgb(255, 250, 249));
                Color surface = UiDrawing.Blend(baseSurface, TaskLightVisualStyle.SurfaceSubtle, hoverMotion.Current * 0.55F);
                surface = UiDrawing.Blend(surface, TaskLightVisualStyle.SurfaceSelected, pressMotion.Current * 0.58F);
                Color border = UiDrawing.Blend(TaskLightVisualStyle.Border, TaskLightVisualStyle.BorderStrong, hoverMotion.Current);
                using (GraphicsPath card = UiDrawing.CreateRoundedPath(cardBounds, SFloat(14F)))
                using (SolidBrush surfaceBrush = new SolidBrush(surface))
                using (Pen borderPen = new Pen(border, Math.Max(1F, scale)))
                {
                    graphics.FillPath(surfaceBrush, card);
                    graphics.DrawPath(borderPen, card);
                }

                DrawContent(graphics, cardBounds);
            }

            return bitmap;
        }

        private void DrawContent(Graphics graphics, RectangleF cardBounds)
        {
            Color accent = kind == TaskToastKind.NeedsAttention
                ? TaskLightVisualStyle.Attention
                : (kind == TaskToastKind.Completed ? TaskLightVisualStyle.Success : TaskLightVisualStyle.Attention);
            RectangleF railBounds = new RectangleF(
                cardBounds.Left + SFloat(15F),
                cardBounds.Top + SFloat(20F),
                SFloat(5F),
                cardBounds.Height - SFloat(40F));
            using (GraphicsPath rail = TaskLightVisualStyle.CreatePill(railBounds))
            using (SolidBrush railBrush = new SolidBrush(accent))
            {
                graphics.FillPath(railBrush, rail);
            }

            float textLeft = cardBounds.Left + SFloat(34F);
            float textRight = cardBounds.Right - SFloat(42F);
            RectangleF titleBounds = new RectangleF(textLeft, cardBounds.Top + SFloat(15F), textRight - textLeft, SFloat(20F));
            RectangleF bodyBounds = new RectangleF(textLeft, cardBounds.Top + SFloat(39F), textRight - textLeft, SFloat(22F));
            RectangleF detailBounds = new RectangleF(textLeft, cardBounds.Top + SFloat(69F), textRight - textLeft, SFloat(19F));
            using (Font titleFont = TaskLightVisualStyle.CreateSemiboldFont(SFloat(12F)))
            using (Font bodyFont = TaskLightVisualStyle.CreateSemiboldFont(SFloat(13F)))
            using (Font detailFont = TaskLightVisualStyle.CreateRegularFont(SFloat(11F)))
            using (SolidBrush titleBrush = new SolidBrush(kind == TaskToastKind.NavigationError ? TaskLightVisualStyle.AttentionText : accent))
            using (SolidBrush bodyBrush = new SolidBrush(TaskLightVisualStyle.TextPrimary))
            using (SolidBrush detailBrush = new SolidBrush(TaskLightVisualStyle.TextQuiet))
            using (StringFormat ellipsis = EllipsisFormat())
            {
                graphics.DrawString(title, titleFont, titleBrush, titleBounds, ellipsis);
                graphics.DrawString(body, bodyFont, bodyBrush, bodyBounds, ellipsis);
                graphics.DrawString(detail, detailFont, detailBrush, detailBounds, ellipsis);
            }

            RectangleF translatedClose = new RectangleF(
                cardBounds.Left + SFloat(closeBounds.Left),
                cardBounds.Top + SFloat(closeBounds.Top),
                SFloat(closeBounds.Width),
                SFloat(closeBounds.Height));
            Point cursor = PointToClient(Cursor.Position);
            bool closeHovered = translatedClose.Contains(cursor);
            if (closeHovered)
            {
                using (GraphicsPath closeSurface = UiDrawing.CreateRoundedPath(translatedClose, SFloat(7F)))
                using (SolidBrush closeBrush = new SolidBrush(TaskLightVisualStyle.SurfaceSubtle))
                {
                    graphics.FillPath(closeBrush, closeSurface);
                }
            }

            using (Font closeFont = TaskLightVisualStyle.CreateIconFont(SFloat(12F)))
            using (SolidBrush closeIconBrush = new SolidBrush(TaskLightVisualStyle.TextQuiet))
            using (StringFormat center = CenterFormat())
            {
                graphics.DrawString(TaskLightVisualStyle.CloseIcon, closeFont, closeIconBrush, translatedClose, center);
            }

            if (task != null && kind != TaskToastKind.NavigationError)
            {
                RectangleF openBounds = new RectangleF(cardBounds.Right - SFloat(45F), cardBounds.Bottom - SFloat(34F), SFloat(26F), SFloat(22F));
                using (Font openFont = TaskLightVisualStyle.CreateIconFont(SFloat(12F)))
                using (SolidBrush openBrush = new SolidBrush(TaskLightVisualStyle.TextQuiet))
                using (StringFormat center = CenterFormat())
                {
                    graphics.DrawString(TaskLightVisualStyle.OpenIcon, openFont, openBrush, openBounds, center);
                }
            }
        }

        private void OnMotionTick(object sender, EventArgs args)
        {
            bool running = visibilityMotion.Update();
            running = hoverMotion.Update() || running;
            running = pressMotion.Update() || running;
            RenderLayeredWindow();

            if (IsKeyDown(VkEscape) && !escapeWasDown)
            {
                CloseToast(false);
            }
            escapeWasDown = IsKeyDown(VkEscape);

            if (closing && !visibilityMotion.IsRunning && visibilityMotion.Current <= 0.001F)
            {
                closing = false;
                Hide();
            }

            if (!running && !closing)
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

        private void UpdateScale()
        {
            try
            {
                uint dpi = IsHandleCreated ? NativeMethods.GetDpiForWindow(Handle) : 96U;
                scale = Math.Max(1F, Math.Min(3F, dpi / 96F));
            }
            catch (Exception)
            {
                scale = 1F;
            }

            ClientSize = new Size(
                S(DesignPaddingLeft + DesignCardWidth + DesignPaddingRight),
                S(DesignPaddingTop + DesignCardHeight + DesignPaddingBottom));
            closeBounds = new Rectangle(DesignCardWidth - 40, 8, 30, 30);
        }

        private void PositionAtWorkingAreaBottomRight()
        {
            Rectangle area = Screen.FromPoint(Cursor.Position).WorkingArea;
            Location = new Point(area.Right - ClientSize.Width, area.Bottom - ClientSize.Height);
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
            return new RectangleF(SFloat(DesignPaddingLeft), SFloat(DesignPaddingTop), SFloat(DesignCardWidth), SFloat(DesignCardHeight));
        }

        private Point ToCardPoint(Point point)
        {
            return new Point(
                (int)Math.Round((point.X - SFloat(DesignPaddingLeft)) / scale),
                (int)Math.Round((point.Y - SFloat(DesignPaddingTop)) / scale));
        }

        private static bool IsKeyDown(int virtualKey)
        {
            return (NativeMethods.GetAsyncKeyState(virtualKey) & 0x8000) != 0;
        }

        private int S(int value)
        {
            return Math.Max(1, (int)Math.Round(value * scale));
        }

        private float SFloat(float value)
        {
            return value * scale;
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

        private static StringFormat CenterFormat()
        {
            StringFormat format = new StringFormat();
            format.Alignment = StringAlignment.Center;
            format.LineAlignment = StringAlignment.Center;
            format.Trimming = StringTrimming.EllipsisCharacter;
            format.FormatFlags = StringFormatFlags.NoWrap;
            return format;
        }
    }
}
