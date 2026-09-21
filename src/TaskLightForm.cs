using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Windows.Forms;

namespace CodexQuotaOverlay
{
    internal sealed class TaskLightForm : Form
    {
        private const int DesignHeight = 38;
        private const int DesignMixedWidth = 166;
        private const int DesignAlertWidth = 96;
        private const int DesignRunningWidth = 96;
        private const int DesignIdleWidth = 84;
        private const int DesignOfflineWidth = 112;
        private const int DesignPaddingLeft = 12;
        private const int DesignPaddingTop = 10;
        private const int DesignPaddingRight = 12;
        private const int DesignPaddingBottom = 15;
        private const int DragThreshold = 5;
        private const int HitNone = -1;
        private const int HitSummary = -2;
        private const int HitHeader = WatchedTaskCard.HitHeader;
        private const int HitDragSurface = WatchedTaskCard.HitDragSurface;

        private readonly TaskLightSettings settings;
        private readonly WatchedTaskCollection watchedTasks;
        private readonly WatchedTaskCard watchedCard = new WatchedTaskCard();
        private readonly TaskLightDetailsForm detailsPopup;
        private readonly ToolTip tooltip;
        private readonly Timer motionTimer;
        private readonly bool motionEnabled;
        private readonly UiMotionValue hoverMotion = new UiMotionValue(0F);
        private readonly UiMotionValue pressMotion = new UiMotionValue(0F);
        private readonly UiMotionValue feedbackMotion = new UiMotionValue(0F);
        private readonly UiMotionValue entranceMotion = new UiMotionValue(1F);
        private TaskListSnapshot snapshot = new TaskListSnapshot(null);
        private IList<WatchedTaskView> watchedViews = new List<WatchedTaskView>().AsReadOnly();
        private bool connected;
        private string connectionStatus = "正在连接 Codex";
        private float scale = 1F;
        private bool pressed;
        private bool dragging;
        private bool shownOnce;
        private int hoveredHit = HitNone;
        private int pressedHit = HitNone;
        private Point dragStartCursor;
        private Point dragStartLocation;

        public event EventHandler<TaskActivatedEventArgs> TaskActivated;

        private bool IsWatchedCardMode
        {
            get { return watchedViews.Count > 0 && (detailsPopup == null || !detailsPopup.Visible); }
        }

        public TaskLightForm()
            : this(TaskLightSettings.Load())
        {
        }

        internal TaskLightForm(TaskLightSettings initialSettings)
        {
            settings = initialSettings ?? TaskLightSettings.CreateTransient();
            watchedTasks = new WatchedTaskCollection(settings.WatchedTasks);
            bool ignoredRecordsChanged;
            watchedViews = watchedTasks.BuildViews(snapshot, false, out ignoredRecordsChanged);
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
            AccessibleDescription = "显示 Codex 任务状态与重点关注任务。拖动可移动，点击任务可打开对应对话。";

            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw |
                     ControlStyles.UserPaint, true);

            detailsPopup = new TaskLightDetailsForm();
            detailsPopup.VisibleChanged += OnDetailsVisibilityChanged;
            detailsPopup.AlwaysOnTopChanged += OnAlwaysOnTopChanged;
            detailsPopup.TaskActivated += ForwardTaskActivated;
            detailsPopup.TaskWatchToggled += OnTaskWatchToggled;
            detailsPopup.UpdateWatchedTasks(watchedTasks.Records);
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

        public static bool RunWatchIntegrationSelfTest(out string result)
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            TaskSnapshot task = new TaskSnapshot(
                "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa",
                "重点关注集成测试",
                string.Empty,
                string.Empty,
                CodexTaskState.Running,
                "Codex 正在处理",
                now);
            try
            {
                using (TaskLightForm form = new TaskLightForm(TaskLightSettings.CreateTransient()))
                {
                    form.UpdateTasks(new TaskListSnapshot(new[] { task }));
                    form.ToggleWatchedTask(task, false);
                    bool expanded = form.watchedViews.Count == 1 &&
                                    form.watchedCard.RowCount == 1 &&
                                    form.GetDesignWidth() == WatchedTaskCard.DesignWidth &&
                                    form.GetDesignHeight() > DesignHeight &&
                                    form.watchedViews[0].Available;
                    form.UpdateConnection(false, "Codex 暂未运行");
                    bool retainedUnavailable = form.watchedViews.Count == 1 &&
                                               !form.watchedViews[0].Available;
                    form.ToggleWatchedTask(task, false);
                    bool collapsed = form.watchedViews.Count == 0 &&
                                     form.GetDesignHeight() == DesignHeight;
                    bool success = expanded && retainedUnavailable && collapsed;
                    result = success
                        ? "{\"ok\":true,\"test\":\"watched-task-ui-integration\"}"
                        : "{\"ok\":false,\"test\":\"watched-task-ui-integration\"}";
                    return success;
                }
            }
            catch (Exception exception)
            {
                result = "{\"ok\":false,\"test\":\"watched-task-ui-integration\",\"status\":\"" +
                         EscapeSelfTestValue(exception.Message) + "\"}";
                return false;
            }
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
            RefreshWatchedViews(true);
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

            RefreshWatchedViews(false);
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

        public void ShowDetailsForPreview()
        {
            detailsPopup.ShowAnchored(this, GetVisualBoundsScreen(), settings.AlwaysOnTop);
        }

        public void SavePreviewScreenshot(string outputPath)
        {
            if (string.IsNullOrWhiteSpace(outputPath))
            {
                return;
            }

            Rectangle captureBounds = Bounds;
            if (detailsPopup.Visible)
            {
                captureBounds = Rectangle.Union(captureBounds, detailsPopup.Bounds);
            }

            using (Bitmap lightSurface = RenderSurfaceBitmap())
            using (Bitmap detailsSurface = detailsPopup.Visible ? detailsPopup.CaptureSurfaceForPreview() : null)
            using (Bitmap composite = new Bitmap(captureBounds.Width, captureBounds.Height, PixelFormat.Format32bppArgb))
            using (Graphics graphics = Graphics.FromImage(composite))
            {
                // 仅用于视觉 QA 的统一画布，真实桌面背景不属于核心 UI Token。
                graphics.Clear(Color.FromArgb(232, 238, 234));
                graphics.DrawImageUnscaled(lightSurface, Left - captureBounds.Left, Top - captureBounds.Top);
                if (detailsSurface != null)
                {
                    graphics.DrawImageUnscaled(
                        detailsSurface,
                        detailsPopup.Left - captureBounds.Left,
                        detailsPopup.Top - captureBounds.Top);
                }

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
            UpdateHoveredHit(GetHitAt(PointToClient(Cursor.Position)));
            if (!IsWatchedCardMode)
            {
                hoverMotion.AnimateTo(1F, 120, UiMotionCurve.EaseOut);
                StartMotion();
            }
            else
            {
                RenderLayeredWindow();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (!Capture)
            {
                UpdateHoveredHit(HitNone);
                if (!IsWatchedCardMode)
                {
                    hoverMotion.AnimateTo(0F, 120, UiMotionCurve.EaseOut);
                    StartMotion();
                }
                else
                {
                    RenderLayeredWindow();
                }
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left)
            {
                return;
            }

            int hit = GetHitAt(e.Location);
            if (hit == HitNone)
            {
                return;
            }

            dragStartCursor = Cursor.Position;
            dragStartLocation = Location;
            dragging = false;
            pressed = true;
            pressedHit = hit;
            hoveredHit = hit;
            Capture = true;
            if (!IsWatchedCardMode)
            {
                pressMotion.AnimateTo(1F, 110, UiMotionCurve.EaseOut);
                StartMotion();
            }
            else
            {
                UpdateCursorForHit(hit);
                RenderLayeredWindow();
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!Capture || !pressed)
            {
                UpdateHoveredHit(GetHitAt(e.Location));
                return;
            }

            Point cursor = Cursor.Position;
            int deltaX = cursor.X - dragStartCursor.X;
            int deltaY = cursor.Y - dragStartCursor.Y;
            if (!dragging && Math.Abs(deltaX) + Math.Abs(deltaY) >= S(DragThreshold))
            {
                dragging = true;
                pressedHit = HitNone;
                hoveredHit = HitNone;
                if (!IsWatchedCardMode)
                {
                    pressMotion.AnimateTo(0F, 110, UiMotionCurve.EaseOut);
                    StartMotion();
                }
                UpdateCursorForHit(HitDragSurface);
            }

            if (!dragging)
            {
                UpdateHoveredHit(GetHitAt(e.Location));
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
            int action = pressedHit;
            int releasedHit = GetHitAt(e.Location);
            pressed = false;
            dragging = false;
            pressedHit = HitNone;
            Capture = false;
            UpdateHoveredHit(releasedHit);
            if (!IsWatchedCardMode)
            {
                pressMotion.AnimateTo(0F, 140, UiMotionCurve.EaseOut);
                StartMotion();
            }
            else
            {
                RenderLayeredWindow();
            }
            if (wasDragging)
            {
                SavePosition();
                return;
            }

            if (action != HitNone && action == releasedHit)
            {
                ExecuteHit(action);
            }
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            BuildWatchedLayout();
            RenderLayeredWindow();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                detailsPopup.VisibleChanged -= OnDetailsVisibilityChanged;
                detailsPopup.TaskActivated -= ForwardTaskActivated;
                detailsPopup.TaskWatchToggled -= OnTaskWatchToggled;
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

            using (Bitmap bitmap = RenderSurfaceBitmap())
            {
                byte opacity = (byte)Math.Round(255F * entranceMotion.Current);
                LayeredWindowRenderer.Present(Handle, Location, bitmap, opacity);
            }
        }

        private Bitmap RenderSurfaceBitmap()
        {
            Bitmap bitmap = new Bitmap(ClientSize.Width, ClientSize.Height, PixelFormat.Format32bppArgb);
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(Color.Transparent);
                UiDrawing.Configure(graphics);

                RectangleF cardBounds = GetCardBounds();
                bool hasWatchedTasks = IsWatchedCardMode;
                float pressScale = motionEnabled && !hasWatchedTasks ? 1F - pressMotion.Current * 0.018F : 1F;
                float verticalShift = motionEnabled && !hasWatchedTasks ? SFloat(0.6F) * pressMotion.Current : 0F;
                cardBounds = ScaleBounds(cardBounds, pressScale);
                cardBounds.Offset(0F, verticalShift);

                if (hasWatchedTasks)
                {
                    UiDrawing.DrawPanelShadow(graphics, cardBounds, SFloat(14F), scale);
                }
                else
                {
                    UiDrawing.DrawCompactShadow(graphics, cardBounds, SFloat(11F), scale);
                }

                Color backgroundColor = hasWatchedTasks
                    ? TaskLightVisualStyle.Paper
                    : UiDrawing.Blend(TaskLightVisualStyle.Paper, TaskLightVisualStyle.SurfaceSubtle, hoverMotion.Current);
                if (!hasWatchedTasks)
                {
                    backgroundColor = UiDrawing.Blend(
                        backgroundColor,
                        TaskLightVisualStyle.SurfaceSelected,
                        pressMotion.Current);
                }
                Color borderColor = UiDrawing.Blend(
                    TaskLightVisualStyle.Border,
                    TaskLightVisualStyle.BorderStrong,
                    hasWatchedTasks ? (hoveredHit == HitNone ? 0F : 0.32F) : hoverMotion.Current);
                using (GraphicsPath card = UiDrawing.CreateRoundedPath(cardBounds, SFloat(hasWatchedTasks ? 14F : 11F)))
                using (SolidBrush background = new SolidBrush(backgroundColor))
                using (Pen border = new Pen(borderColor, Math.Max(1F, scale)))
                {
                    graphics.FillPath(background, card);
                    graphics.DrawPath(border, card);
                }

                if (hasWatchedTasks)
                {
                    watchedCard.Draw(graphics, cardBounds, hoveredHit, pressedHit, scale);
                }
                else
                {
                    DrawStatusContent(graphics, cardBounds);
                    DrawFeedback(graphics, cardBounds);
                }
            }

            return bitmap;
        }

        private void DrawStatusContent(Graphics graphics, RectangleF cardBounds)
        {
            if (!connected)
            {
                DrawSegment(graphics, cardBounds, TaskLightVisualStyle.Offline, "Codex 离线");
                return;
            }

            if (snapshot.AttentionCount > 0 && snapshot.RunningCount > 0)
            {
                float firstWidth = cardBounds.Width / 2F;
                RectangleF alert = new RectangleF(cardBounds.Left, cardBounds.Top, firstWidth, cardBounds.Height);
                RectangleF running = new RectangleF(cardBounds.Left + firstWidth, cardBounds.Top, cardBounds.Width - firstWidth, cardBounds.Height);
                using (Pen separator = new Pen(TaskLightVisualStyle.Border, Math.Max(1F, scale)))
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
                    TaskLightVisualStyle.Attention,
                    "需处理 " + snapshot.AttentionCount.ToString(CultureInfo.InvariantCulture));
                DrawSegment(
                    graphics,
                    running,
                    TaskLightVisualStyle.Running,
                    "执行中 " + snapshot.RunningCount.ToString(CultureInfo.InvariantCulture));
            }
            else if (snapshot.AttentionCount > 0)
            {
                DrawSegment(
                    graphics,
                    cardBounds,
                    TaskLightVisualStyle.Attention,
                    "需处理 " + snapshot.AttentionCount.ToString(CultureInfo.InvariantCulture));
            }
            else if (snapshot.RunningCount > 0)
            {
                DrawSegment(
                    graphics,
                    cardBounds,
                    TaskLightVisualStyle.Running,
                    "执行中 " + snapshot.RunningCount.ToString(CultureInfo.InvariantCulture));
            }
            else
            {
                DrawSegment(graphics, cardBounds, TaskLightVisualStyle.Success, "空闲");
            }
        }

        private void DrawSegment(Graphics graphics, RectangleF bounds, Color indicatorColor, string text)
        {
            using (Font font = TaskLightVisualStyle.CreateSemiboldFont(SFloat(11F)))
            using (SolidBrush textBrush = new SolidBrush(TaskLightVisualStyle.TextPrimary))
            {
                SizeF textSize = graphics.MeasureString(text, font, int.MaxValue, StringFormat.GenericTypographic);
                float dotSize = SFloat(8F);
                float gap = SFloat(7F);
                float contentWidth = dotSize + gap + textSize.Width;
                float startX = bounds.Left + (bounds.Width - contentWidth) / 2F;
                float centerY = bounds.Top + bounds.Height / 2F;

                using (SolidBrush indicatorBrush = new SolidBrush(indicatorColor))
                {
                    graphics.FillEllipse(indicatorBrush, startX, centerY - dotSize / 2F, dotSize, dotSize);
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
                ? TaskLightVisualStyle.Offline
                : (snapshot.AttentionCount > 0
                    ? TaskLightVisualStyle.Attention
                    : (snapshot.RunningCount > 0
                        ? TaskLightVisualStyle.Running
                        : TaskLightVisualStyle.Success));
            int alpha = (int)Math.Round(120F * feedbackMotion.Current);
            RectangleF feedbackBounds = cardBounds;
            feedbackBounds.Inflate(SFloat(1.5F), SFloat(1.5F));
            using (GraphicsPath feedbackPath = UiDrawing.CreateRoundedPath(feedbackBounds, SFloat(12F)))
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

        private void OnDetailsVisibilityChanged(object sender, EventArgs args)
        {
            if (IsDisposed)
            {
                return;
            }

            // 选择多个任务时保持详情面板稳定；详情关闭后再展开常驻关注卡片。
            UpdateSize(true);
            UpdateTooltip();
            RenderLayeredWindow();
        }

        private void OnAlwaysOnTopChanged(object sender, EventArgs args)
        {
            settings.AlwaysOnTop = detailsPopup.AlwaysOnTop;
            settings.Save();
            ApplyWindowPosition();
        }

        private void ForwardTaskActivated(object sender, TaskActivatedEventArgs args)
        {
            EventHandler<TaskActivatedEventArgs> handler = TaskActivated;
            if (handler != null && args != null && args.Task != null)
            {
                handler(this, new TaskActivatedEventArgs(args.Task));
            }
        }

        private void OnTaskWatchToggled(object sender, TaskActivatedEventArgs args)
        {
            ToggleWatchedTask(args == null ? null : args.Task, true);
        }

        private void ToggleWatchedTask(TaskSnapshot task, bool showDetailsFeedback)
        {
            WatchToggleResult result = watchedTasks.Toggle(task);
            if (result == WatchToggleResult.InvalidTask)
            {
                if (showDetailsFeedback)
                {
                    detailsPopup.ShowWatchFeedback("这个任务暂时无法关注", true);
                }

                return;
            }

            if (result == WatchToggleResult.LimitReached)
            {
                if (showDetailsFeedback)
                {
                    detailsPopup.ShowWatchFeedback("最多关注 5 个任务", true);
                }

                return;
            }

            PersistWatchedTasks();
            RefreshWatchedViews(false);
            hoveredHit = HitNone;
            pressedHit = HitNone;
            UpdateSize(true);
            UpdateTooltip();
            if (showDetailsFeedback)
            {
                detailsPopup.ShowWatchFeedback(
                    result == WatchToggleResult.Added ? "已加入重点关注" : "已取消重点关注",
                    false);
            }

            RenderLayeredWindow();
        }

        private void RefreshWatchedViews(bool persistRecordChanges)
        {
            bool recordsChanged;
            watchedViews = watchedTasks.BuildViews(snapshot, connected, out recordsChanged);
            detailsPopup.UpdateWatchedTasks(watchedTasks.Records);
            if (recordsChanged && persistRecordChanges)
            {
                PersistWatchedTasks();
            }
        }

        private void PersistWatchedTasks()
        {
            settings.ReplaceWatchedTasks(watchedTasks.Records);
            settings.Save();
        }

        private void ExecuteHit(int hit)
        {
            if (hit == HitSummary || hit == HitHeader)
            {
                ToggleDetails();
                return;
            }

            TaskSnapshot hitTask;
            if (watchedCard.TryGetUnwatchTask(hit, out hitTask))
            {
                ToggleWatchedTask(hitTask, false);
                return;
            }

            if (watchedCard.TryGetRowTask(hit, out hitTask))
            {
                EventHandler<TaskActivatedEventArgs> handler = TaskActivated;
                if (handler != null)
                {
                    handler(this, new TaskActivatedEventArgs(hitTask));
                }
            }
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
                BuildWatchedLayout();
                return;
            }

            Rectangle currentVisual = GetVisualBoundsScreen();
            int anchorRight = settings.HasPosition && settings.UseRightAnchor
                ? settings.AnchorRight
                : currentVisual.Right;
            int visualTop = currentVisual.Top;
            ClientSize = newSize;
            BuildWatchedLayout();
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
            if (IsWatchedCardMode)
            {
                return WatchedTaskCard.DesignWidth;
            }

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
                S(GetDesignHeight() + DesignPaddingTop + DesignPaddingBottom));
        }

        private int GetDesignHeight()
        {
            return IsWatchedCardMode
                ? WatchedTaskCard.GetDesignHeight(watchedViews.Count)
                : DesignHeight;
        }

        private void BuildWatchedLayout()
        {
            if (!IsWatchedCardMode || ClientSize.Width <= 0 || ClientSize.Height <= 0)
            {
                watchedCard.Build(Rectangle.Empty, scale, null);
                return;
            }

            watchedCard.Build(Rectangle.Round(GetCardBounds()), scale, watchedViews);
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

        private int GetHitAt(Point location)
        {
            if (!GetCardBounds().Contains(location))
            {
                return HitNone;
            }

            if (!IsWatchedCardMode)
            {
                return HitSummary;
            }

            return watchedCard.HitTest(location);
        }

        private void UpdateHoveredHit(int hit)
        {
            if (hoveredHit == hit)
            {
                UpdateCursorForHit(hit);
                return;
            }

            hoveredHit = hit;
            UpdateCursorForHit(hit);
            if (IsWatchedCardMode)
            {
                RenderLayeredWindow();
            }
        }

        private void UpdateCursorForHit(int hit)
        {
            Cursor = dragging || hit == HitDragSurface
                ? Cursors.SizeAll
                : (hit == HitNone ? Cursors.Default : Cursors.Hand);
        }

        private void UpdateTooltip()
        {
            string text;
            if (IsWatchedCardMode)
            {
                text = "重点关注 " + watchedViews.Count.ToString(CultureInfo.InvariantCulture) +
                       " 个任务" + Environment.NewLine +
                       "点击任务打开对应对话，点击星标取消关注";
            }
            else if (!connected)
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

            tooltip.SetToolTip(
                this,
                text + Environment.NewLine + (IsWatchedCardMode
                    ? "拖动卡片可移动，点击标题查看全部任务"
                    : "拖动可移动，点击查看详情"));
        }

        private void TriggerFeedback()
        {
            if (IsWatchedCardMode)
            {
                RenderLayeredWindow();
                return;
            }

            feedbackMotion.JumpTo(1F);
            feedbackMotion.AnimateTo(0F, 240, UiMotionCurve.EaseOut);
            StartMotion();
        }


        private static string EscapeSelfTestValue(string value)
        {
            return string.IsNullOrEmpty(value)
                ? string.Empty
                : value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", " ").Replace("\n", " ");
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
