using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace YoloDemo
{
    public sealed class SkeletonMonitorForm : Form
    {
        private const int DefaultSourceSize = 640;
        private const int DetailUpdateIntervalMs = 180;

        private static readonly Color PageColor = Color.FromArgb(244, 246, 248);
        private static readonly Color PanelColor = Color.White;
        private static readonly Color BorderColor = Color.FromArgb(220, 226, 233);
        private static readonly Color TextColor = Color.FromArgb(32, 41, 54);
        private static readonly Color MutedTextColor = Color.FromArgb(101, 116, 139);
        private static readonly Color AccentColor = Color.FromArgb(37, 99, 235);
        private static readonly Color SoftAccentColor = Color.FromArgb(219, 234, 254);
        private static readonly Color WarningColor = Color.FromArgb(217, 119, 6);
        private static readonly Color[] PosePalette =
        {
            Color.FromArgb(0, 128, 255), Color.FromArgb(51, 153, 255), Color.FromArgb(102, 178, 255),
            Color.FromArgb(0, 230, 230), Color.FromArgb(255, 153, 255), Color.FromArgb(255, 204, 153),
            Color.FromArgb(255, 102, 255), Color.FromArgb(255, 51, 255), Color.FromArgb(255, 178, 102),
            Color.FromArgb(255, 153, 51), Color.FromArgb(153, 153, 255), Color.FromArgb(102, 102, 255),
            Color.FromArgb(51, 51, 255), Color.FromArgb(153, 255, 153), Color.FromArgb(102, 255, 102),
            Color.FromArgb(51, 255, 51), Color.FromArgb(0, 255, 0), Color.FromArgb(255, 0, 0),
            Color.FromArgb(0, 0, 255), Color.White
        };

        private readonly SkeletonCanvas _canvas;
        private readonly TextBox _dataTextBox;
        private readonly Label _sampleValue;
        private readonly Label _fpsValue;
        private readonly Label _personValue;
        private readonly Label _stateValue;
        private readonly Stopwatch _watch = Stopwatch.StartNew();
        private readonly Stopwatch _detailWatch = Stopwatch.StartNew();
        private long _sampleIndex;
        private int _updatePending;

        public SkeletonMonitorForm()
        {
            Text = "Pose Monitor";
            BackColor = PageColor;
            ForeColor = TextColor;
            Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point, 134);
            ClientSize = new Size(1280, 760);
            MinimumSize = new Size(960, 620);
            StartPosition = FormStartPosition.Manual;
            DoubleBuffered = true;
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw,
                true);

            DoubleBufferedTableLayoutPanel root = new DoubleBufferedTableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = PageColor,
                ColumnCount = 2,
                RowCount = 1,
                Padding = new Padding(16)
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 66F));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34F));

            ModernPanel canvasPanel = new ModernPanel
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 0, 12, 0),
                Padding = new Padding(14)
            };

            _canvas = new SkeletonCanvas
            {
                Dock = DockStyle.Fill
            };
            canvasPanel.Controls.Add(_canvas);

            ModernPanel dataPanel = new ModernPanel
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(0),
                Padding = new Padding(16)
            };

            DoubleBufferedTableLayoutPanel dataLayout = new DoubleBufferedTableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = PanelColor,
                ColumnCount = 1,
                RowCount = 4,
                Margin = new Padding(0)
            };
            dataLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 54F));
            dataLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 96F));
            dataLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
            dataLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            Label title = new Label
            {
                Dock = DockStyle.Fill,
                Text = "Pose Data",
                ForeColor = TextColor,
                Font = new Font("Microsoft YaHei UI", 15F, FontStyle.Bold, GraphicsUnit.Point, 134),
                TextAlign = ContentAlignment.MiddleLeft
            };

            DoubleBufferedTableLayoutPanel metrics = new DoubleBufferedTableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = PanelColor,
                ColumnCount = 2,
                RowCount = 2,
                Margin = new Padding(0, 0, 0, 8)
            };
            metrics.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            metrics.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            metrics.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
            metrics.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));

            _sampleValue = CreateMetricLabel("Sample 0", AccentColor);
            _fpsValue = CreateMetricLabel("FPS 0.0", AccentColor);
            _personValue = CreateMetricLabel("Persons 0", WarningColor);
            _stateValue = CreateMetricLabel("Waiting", MutedTextColor);

            metrics.Controls.Add(_sampleValue, 0, 0);
            metrics.Controls.Add(_fpsValue, 1, 0);
            metrics.Controls.Add(_personValue, 0, 1);
            metrics.Controls.Add(_stateValue, 1, 1);

            Label detailTitle = new Label
            {
                Dock = DockStyle.Fill,
                Text = "Details",
                ForeColor = MutedTextColor,
                Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point, 134),
                TextAlign = ContentAlignment.MiddleLeft
            };

            Panel textHost = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(248, 250, 252),
                Padding = new Padding(10)
            };

            _dataTextBox = new TextBox
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(248, 250, 252),
                ForeColor = TextColor,
                BorderStyle = BorderStyle.None,
                Font = new Font("Consolas", 9F, FontStyle.Regular, GraphicsUnit.Point, 0),
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Both,
                WordWrap = false,
                Text = "Waiting for pose data..."
            };

            textHost.Controls.Add(_dataTextBox);
            dataLayout.Controls.Add(title, 0, 0);
            dataLayout.Controls.Add(metrics, 0, 1);
            dataLayout.Controls.Add(detailTitle, 0, 2);
            dataLayout.Controls.Add(textHost, 0, 3);
            dataPanel.Controls.Add(dataLayout);

            root.Controls.Add(canvasPanel, 0, 0);
            root.Controls.Add(dataPanel, 1, 0);
            Controls.Add(root);
        }

        public void UpdateDetections(IList<PoseDetection> detections, double fps, int sourceWidth, int sourceHeight)
        {
            if (IsDisposed || Disposing)
            {
                return;
            }

            if (Interlocked.Exchange(ref _updatePending, 1) == 1)
            {
                return;
            }

            if (!IsHandleCreated)
            {
                Interlocked.Exchange(ref _updatePending, 0);
                return;
            }

            List<PoseDetection> snapshot = CloneDetections(detections);
            Size sourceSize = new Size(Math.Max(1, sourceWidth), Math.Max(1, sourceHeight));
            long sample = Interlocked.Increment(ref _sampleIndex);

            Action apply = delegate
            {
                try
                {
                    if (!IsDisposed && !Disposing)
                    {
                        _sampleValue.Text = string.Format("Sample {0}", sample);
                        _fpsValue.Text = string.Format("FPS {0:0.0}", fps);
                        _personValue.Text = string.Format("Persons {0}", snapshot.Count);
                        _stateValue.Text = snapshot.Count > 0 ? "Tracking" : "Waiting";
                        _stateValue.ForeColor = snapshot.Count > 0 ? AccentColor : MutedTextColor;
                        _canvas.SetDetections(snapshot, sourceSize, fps, sample);

                        if (_detailWatch.ElapsedMilliseconds >= DetailUpdateIntervalMs)
                        {
                            _detailWatch.Restart();
                            _dataTextBox.Text = BuildDetectionText(snapshot, fps, sample, sourceSize, _watch.Elapsed);
                        }
                    }
                }
                finally
                {
                    Interlocked.Exchange(ref _updatePending, 0);
                }
            };

            try
            {
                if (InvokeRequired)
                {
                    BeginInvoke(apply);
                }
                else
                {
                    apply();
                }
            }
            catch (ObjectDisposedException)
            {
                Interlocked.Exchange(ref _updatePending, 0);
            }
            catch (InvalidOperationException)
            {
                Interlocked.Exchange(ref _updatePending, 0);
            }
        }

        private static Label CreateMetricLabel(string text, Color color)
        {
            return new Label
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 0, 8, 8),
                BackColor = Color.FromArgb(248, 250, 252),
                ForeColor = color,
                BorderStyle = BorderStyle.FixedSingle,
                Text = text,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold, GraphicsUnit.Point, 134)
            };
        }

        private static List<PoseDetection> CloneDetections(IList<PoseDetection> detections)
        {
            List<PoseDetection> snapshot = new List<PoseDetection>();
            if (detections == null)
            {
                return snapshot;
            }

            for (int i = 0; i < detections.Count; i++)
            {
                PoseDetection detection = detections[i];
                Keypoint[] keypoints = detection.Keypoints == null
                    ? new Keypoint[0]
                    : (Keypoint[])detection.Keypoints.Clone();

                snapshot.Add(new PoseDetection
                {
                    Box = detection.Box,
                    Confidence = detection.Confidence,
                    ClassId = detection.ClassId,
                    Label = detection.Label,
                    Behavior = detection.Behavior,
                    Keypoints = keypoints
                });
            }

            return snapshot;
        }

        private static string BuildDetectionText(IList<PoseDetection> detections, double fps, long sample, Size sourceSize, TimeSpan elapsed)
        {
            StringBuilder builder = new StringBuilder(4096);
            builder.AppendFormat("sample   : {0}", sample);
            builder.AppendLine();
            builder.AppendFormat("fps      : {0:0.0}", fps);
            builder.AppendLine();
            builder.AppendFormat("source   : {0} x {1}", sourceSize.Width, sourceSize.Height);
            builder.AppendLine();
            builder.AppendFormat("persons  : {0}", detections.Count);
            builder.AppendLine();
            builder.AppendFormat("elapsed  : {0:hh\\:mm\\:ss}", elapsed);
            builder.AppendLine();
            builder.AppendFormat("time     : {0:HH:mm:ss.fff}", DateTime.Now);
            builder.AppendLine();
            builder.AppendLine(new string('-', 48));

            if (detections.Count == 0)
            {
                builder.AppendLine("No person detected.");
                return builder.ToString();
            }

            for (int i = 0; i < detections.Count; i++)
            {
                PoseDetection detection = detections[i];
                RectangleF box = detection.Box;
                builder.AppendFormat("person[{0}]", i);
                builder.AppendLine();
                builder.AppendFormat("  label      : {0}", string.IsNullOrEmpty(detection.Label) ? "person" : detection.Label);
                builder.AppendLine();
                builder.AppendFormat("  behavior   : {0}", string.IsNullOrEmpty(detection.Behavior) ? "-" : detection.Behavior);
                builder.AppendLine();
                builder.AppendFormat("  confidence : {0:0.000}", detection.Confidence);
                builder.AppendLine();
                builder.AppendFormat("  box        : x={0:0.0}, y={1:0.0}, w={2:0.0}, h={3:0.0}",
                    box.X,
                    box.Y,
                    box.Width,
                    box.Height);
                builder.AppendLine();
                builder.AppendLine("  keypoints");

                Keypoint[] keypoints = detection.Keypoints ?? new Keypoint[0];
                for (int k = 0; k < keypoints.Length; k++)
                {
                    string name = k < PoseMetadata.KeypointNames.Length ? PoseMetadata.KeypointNames[k] : "kp" + k;
                    Keypoint keypoint = keypoints[k];
                    builder.AppendFormat(
                        "    {0,-14} x={1,6:0.0}  y={2,6:0.0}  score={3:0.00}",
                        name,
                        keypoint.X,
                        keypoint.Y,
                        keypoint.Confidence);
                    builder.AppendLine();
                }

                if (i < detections.Count - 1)
                {
                    builder.AppendLine();
                }
            }

            return builder.ToString();
        }

        private sealed class ModernPanel : Panel
        {
            public ModernPanel()
            {
                BackColor = PanelColor;
                SetStyle(
                    ControlStyles.AllPaintingInWmPaint |
                    ControlStyles.DoubleBuffer |
                    ControlStyles.OptimizedDoubleBuffer |
                    ControlStyles.ResizeRedraw |
                    ControlStyles.UserPaint,
                    true);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);

                Rectangle rect = ClientRectangle;
                if (rect.Width <= 0 || rect.Height <= 0)
                {
                    return;
                }

                using (SolidBrush brush = new SolidBrush(PanelColor))
                using (Pen border = new Pen(BorderColor, 1F))
                {
                    e.Graphics.FillRectangle(brush, rect);
                    e.Graphics.DrawRectangle(border, 0, 0, rect.Width - 1, rect.Height - 1);
                }
            }
        }

        private sealed class DoubleBufferedTableLayoutPanel : TableLayoutPanel
        {
            public DoubleBufferedTableLayoutPanel()
            {
                SetStyle(
                    ControlStyles.AllPaintingInWmPaint |
                    ControlStyles.DoubleBuffer |
                    ControlStyles.OptimizedDoubleBuffer |
                    ControlStyles.ResizeRedraw |
                    ControlStyles.UserPaint,
                    true);
            }
        }

        private sealed class SkeletonCanvas : Control
        {
            private readonly object _syncRoot = new object();
            private List<PoseDetection> _detections = new List<PoseDetection>();
            private Size _sourceSize = new Size(DefaultSourceSize, DefaultSourceSize);
            private double _fps;
            private long _sample;

            public SkeletonCanvas()
            {
                BackColor = Color.FromArgb(248, 250, 252);
                SetStyle(
                    ControlStyles.AllPaintingInWmPaint |
                    ControlStyles.DoubleBuffer |
                    ControlStyles.OptimizedDoubleBuffer |
                    ControlStyles.ResizeRedraw |
                    ControlStyles.UserPaint,
                    true);
            }

            public void SetDetections(List<PoseDetection> detections, Size sourceSize, double fps, long sample)
            {
                lock (_syncRoot)
                {
                    _detections = detections ?? new List<PoseDetection>();
                    _sourceSize = sourceSize.Width <= 0 || sourceSize.Height <= 0
                        ? new Size(DefaultSourceSize, DefaultSourceSize)
                        : sourceSize;
                    _fps = fps;
                    _sample = sample;
                }

                Invalidate();
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);

                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                Rectangle bounds = ClientRectangle;
                if (bounds.Width <= 1 || bounds.Height <= 1)
                {
                    return;
                }

                using (SolidBrush brush = new SolidBrush(BackColor))
                {
                    e.Graphics.FillRectangle(brush, bounds);
                }

                List<PoseDetection> detections;
                Size sourceSize;
                double fps;
                long sample;
                lock (_syncRoot)
                {
                    detections = new List<PoseDetection>(_detections);
                    sourceSize = _sourceSize;
                    fps = _fps;
                    sample = _sample;
                }

                DrawHeader(e.Graphics, bounds, detections.Count, fps, sample);

                Rectangle stage = new Rectangle(bounds.Left + 12, bounds.Top + 62, bounds.Width - 24, bounds.Height - 74);
                if (stage.Width <= 30 || stage.Height <= 30)
                {
                    return;
                }

                DrawStage(e.Graphics, stage);

                if (detections.Count == 0)
                {
                    DrawEmptyState(e.Graphics, stage);
                    return;
                }

                RectangleF poseArea = CalculatePoseArea(stage, sourceSize);
                DrawCoordinateFrame(e.Graphics, poseArea);

                for (int i = detections.Count - 1; i >= 0; i--)
                {
                    DrawDetection(e.Graphics, detections[i], sourceSize, poseArea, i == 0, i);
                }
            }

            private static void DrawHeader(Graphics graphics, Rectangle bounds, int persons, double fps, long sample)
            {
                using (Font titleFont = new Font("Microsoft YaHei UI", 16F, FontStyle.Bold, GraphicsUnit.Point, 134))
                using (Font metaFont = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point, 134))
                using (SolidBrush titleBrush = new SolidBrush(TextColor))
                using (SolidBrush metaBrush = new SolidBrush(MutedTextColor))
                {
                    graphics.DrawString("Skeleton Preview", titleFont, titleBrush, bounds.Left + 12, bounds.Top + 8);
                    graphics.DrawString(
                        string.Format("Persons {0}   FPS {1:0.0}   Sample {2}", persons, fps, sample),
                        metaFont,
                        metaBrush,
                        bounds.Left + 14,
                        bounds.Top + 38);
                }
            }

            private static void DrawStage(Graphics graphics, Rectangle stage)
            {
                using (SolidBrush brush = new SolidBrush(Color.White))
                using (Pen border = new Pen(BorderColor, 1F))
                {
                    graphics.FillRectangle(brush, stage);
                    graphics.DrawRectangle(border, stage);
                }

                using (Pen gridPen = new Pen(Color.FromArgb(236, 240, 245), 1F))
                {
                    int grid = Math.Max(32, stage.Width / 12);
                    for (int x = stage.Left + grid; x < stage.Right; x += grid)
                    {
                        graphics.DrawLine(gridPen, x, stage.Top, x, stage.Bottom);
                    }

                    for (int y = stage.Top + grid; y < stage.Bottom; y += grid)
                    {
                        graphics.DrawLine(gridPen, stage.Left, y, stage.Right, y);
                    }
                }
            }

            private static void DrawEmptyState(Graphics graphics, Rectangle stage)
            {
                string text = "Waiting for person...";
                using (Font font = new Font("Microsoft YaHei UI", 12F, FontStyle.Regular, GraphicsUnit.Point, 134))
                using (SolidBrush brush = new SolidBrush(MutedTextColor))
                {
                    SizeF size = graphics.MeasureString(text, font);
                    graphics.DrawString(
                        text,
                        font,
                        brush,
                        stage.Left + (stage.Width - size.Width) / 2F,
                        stage.Top + (stage.Height - size.Height) / 2F);
                }
            }

            private static RectangleF CalculatePoseArea(Rectangle bounds, Size sourceSize)
            {
                float scale = Math.Min(
                    bounds.Width / (float)Math.Max(1, sourceSize.Width),
                    bounds.Height / (float)Math.Max(1, sourceSize.Height));
                float width = sourceSize.Width * scale;
                float height = sourceSize.Height * scale;
                float left = bounds.Left + (bounds.Width - width) / 2F;
                float top = bounds.Top + (bounds.Height - height) / 2F;
                return new RectangleF(left, top, width, height);
            }

            private static void DrawCoordinateFrame(Graphics graphics, RectangleF poseArea)
            {
                using (Pen borderPen = new Pen(Color.FromArgb(203, 213, 225), 1F))
                {
                    graphics.DrawRectangle(borderPen, poseArea.X, poseArea.Y, poseArea.Width, poseArea.Height);
                }
            }

            private static void DrawDetection(Graphics graphics, PoseDetection detection, Size sourceSize, RectangleF poseArea, bool primary, int index)
            {
                if (detection == null)
                {
                    return;
                }

                int alpha = primary ? 255 : 150;
                Color boxColor = primary ? Color.FromArgb(59, 130, 246) : Color.FromArgb(147, 197, 253);
                RectangleF box = MapRect(detection.Box, sourceSize, poseArea);

                using (Pen boxPen = new Pen(boxColor, primary ? 2F : 1F))
                {
                    graphics.DrawRectangle(boxPen, box.X, box.Y, box.Width, box.Height);
                }

                Keypoint[] keypoints = detection.Keypoints ?? new Keypoint[0];
                DrawTorso(graphics, keypoints, sourceSize, poseArea, primary);
                DrawLimbs(graphics, keypoints, sourceSize, poseArea, alpha, primary);
                DrawJoints(graphics, keypoints, sourceSize, poseArea, alpha, primary);
                DrawDetectionLabel(graphics, detection, box, index);
            }

            private static void DrawTorso(Graphics graphics, Keypoint[] keypoints, Size sourceSize, RectangleF poseArea, bool primary)
            {
                if (!Has(keypoints, 5) || !Has(keypoints, 6) || !Has(keypoints, 11) || !Has(keypoints, 12))
                {
                    return;
                }

                PointF[] torso =
                {
                    MapPoint(keypoints[5], sourceSize, poseArea),
                    MapPoint(keypoints[6], sourceSize, poseArea),
                    MapPoint(keypoints[12], sourceSize, poseArea),
                    MapPoint(keypoints[11], sourceSize, poseArea)
                };

                using (SolidBrush fill = new SolidBrush(primary ? Color.FromArgb(70, SoftAccentColor) : Color.FromArgb(42, SoftAccentColor)))
                using (Pen edge = new Pen(Color.FromArgb(147, 197, 253), 1F))
                {
                    graphics.FillPolygon(fill, torso);
                    graphics.DrawPolygon(edge, torso);
                }
            }

            private static void DrawLimbs(Graphics graphics, Keypoint[] keypoints, Size sourceSize, RectangleF poseArea, int alpha, bool primary)
            {
                int[,] pairs = PoseMetadata.SkeletonPairs;
                using (Font labelFont = new Font("Consolas", 7.5F, FontStyle.Regular, GraphicsUnit.Point, 0))
                {
                    for (int i = 0; i < pairs.GetLength(0); i++)
                    {
                        int first = pairs[i, 0];
                        int second = pairs[i, 1];
                        if (!Has(keypoints, first) || !Has(keypoints, second))
                        {
                            continue;
                        }

                        PointF p1 = MapPoint(keypoints[first], sourceSize, poseArea);
                        PointF p2 = MapPoint(keypoints[second], sourceSize, poseArea);
                        Color limbColor = GetLimbColor(i, alpha);
                        using (Pen limb = new Pen(limbColor, primary ? 3F : 2F))
                        {
                            limb.StartCap = LineCap.Round;
                            limb.EndCap = LineCap.Round;
                            graphics.DrawLine(limb, p1, p2);
                        }

                        if (primary)
                        {
                            DrawLimbDataLabel(graphics, labelFont, keypoints[first], keypoints[second], p1, p2, poseArea, limbColor);
                        }
                    }
                }
            }

            private static void DrawLimbDataLabel(
                Graphics graphics,
                Font font,
                Keypoint first,
                Keypoint second,
                PointF p1,
                PointF p2,
                RectangleF poseArea,
                Color accent)
            {
                float screenLength = Distance(p1, p2);
                if (screenLength < 52F)
                {
                    return;
                }

                float confidence = (first.Confidence + second.Confidence) / 2F;
                float sourceLength = Distance(first, second);
                string text = string.Format("{0:0}% {1:0}px", confidence * 100F, sourceLength);
                SizeF textSize = graphics.MeasureString(text, font);

                PointF mid = new PointF((p1.X + p2.X) / 2F, (p1.Y + p2.Y) / 2F);
                float dx = p2.X - p1.X;
                float dy = p2.Y - p1.Y;
                float nx = -dy / screenLength;
                float ny = dx / screenLength;

                float width = textSize.Width + 8F;
                float height = textSize.Height + 4F;
                float left = mid.X + nx * 10F - width / 2F;
                float top = mid.Y + ny * 10F - height / 2F;
                left = Clamp(left, poseArea.Left + 2F, poseArea.Right - width - 2F);
                top = Clamp(top, poseArea.Top + 2F, poseArea.Bottom - height - 2F);
                RectangleF labelRect = new RectangleF(left, top, width, height);

                using (SolidBrush fill = new SolidBrush(Color.FromArgb(238, 255, 255, 255)))
                using (SolidBrush textBrush = new SolidBrush(TextColor))
                using (Pen border = new Pen(Color.FromArgb(190, accent), 1F))
                {
                    graphics.FillRectangle(fill, labelRect);
                    graphics.DrawRectangle(border, labelRect.X, labelRect.Y, labelRect.Width, labelRect.Height);
                    graphics.DrawString(text, font, textBrush, labelRect.Left + 4F, labelRect.Top + 2F);
                }
            }

            private static void DrawJoints(Graphics graphics, Keypoint[] keypoints, Size sourceSize, RectangleF poseArea, int alpha, bool primary)
            {
                float radius = primary ? 4.5F : 3.5F;
                using (SolidBrush fill = new SolidBrush(Color.White))
                {
                    for (int i = 0; i < keypoints.Length; i++)
                    {
                        if (!Has(keypoints, i))
                        {
                            continue;
                        }

                        PointF point = MapPoint(keypoints[i], sourceSize, poseArea);
                        using (Pen stroke = new Pen(GetKeypointColor(i, alpha), primary ? 2F : 1.5F))
                        {
                            graphics.FillEllipse(fill, point.X - radius, point.Y - radius, radius * 2F, radius * 2F);
                            graphics.DrawEllipse(stroke, point.X - radius, point.Y - radius, radius * 2F, radius * 2F);
                        }
                    }
                }
            }

            private static void DrawDetectionLabel(Graphics graphics, PoseDetection detection, RectangleF box, int index)
            {
                string label = string.Format("#{0} {1:0.00} {2}", index, detection.Confidence, string.IsNullOrEmpty(detection.Behavior) ? "person" : detection.Behavior);
                using (Font font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Regular, GraphicsUnit.Point, 134))
                using (SolidBrush fill = new SolidBrush(Color.FromArgb(245, 249, 255)))
                using (SolidBrush text = new SolidBrush(TextColor))
                using (Pen edge = new Pen(Color.FromArgb(191, 219, 254), 1F))
                {
                    SizeF size = graphics.MeasureString(label, font);
                    RectangleF labelBox = new RectangleF(box.Left, Math.Max(2F, box.Top - size.Height - 8F), size.Width + 12F, size.Height + 5F);
                    graphics.FillRectangle(fill, labelBox);
                    graphics.DrawRectangle(edge, labelBox.X, labelBox.Y, labelBox.Width, labelBox.Height);
                    graphics.DrawString(label, font, text, labelBox.Left + 6F, labelBox.Top + 2F);
                }
            }

            private static RectangleF MapRect(RectangleF rect, Size sourceSize, RectangleF poseArea)
            {
                float sx = poseArea.Width / Math.Max(1, sourceSize.Width);
                float sy = poseArea.Height / Math.Max(1, sourceSize.Height);
                return new RectangleF(
                    poseArea.Left + rect.X * sx,
                    poseArea.Top + rect.Y * sy,
                    Math.Max(1F, rect.Width * sx),
                    Math.Max(1F, rect.Height * sy));
            }

            private static PointF MapPoint(Keypoint keypoint, Size sourceSize, RectangleF poseArea)
            {
                return new PointF(
                    poseArea.Left + keypoint.X * poseArea.Width / Math.Max(1, sourceSize.Width),
                    poseArea.Top + keypoint.Y * poseArea.Height / Math.Max(1, sourceSize.Height));
            }

            private static float Distance(PointF first, PointF second)
            {
                float dx = first.X - second.X;
                float dy = first.Y - second.Y;
                return (float)Math.Sqrt(dx * dx + dy * dy);
            }

            private static float Distance(Keypoint first, Keypoint second)
            {
                float dx = first.X - second.X;
                float dy = first.Y - second.Y;
                return (float)Math.Sqrt(dx * dx + dy * dy);
            }

            private static float Clamp(float value, float min, float max)
            {
                if (max < min)
                {
                    return min;
                }

                return Math.Max(min, Math.Min(max, value));
            }

            private static bool Has(Keypoint[] keypoints, int index)
            {
                if (keypoints == null || index < 0 || index >= keypoints.Length)
                {
                    return false;
                }

                Keypoint keypoint = keypoints[index];
                return keypoint.Confidence >= PoseMetadata.VisibleKeypointConfidence && keypoint.X > 0 && keypoint.Y > 0;
            }

            private static Color GetLimbColor(int index, int alpha)
            {
                int[] indexes = PoseMetadata.LimbColorIndexes;
                return WithAlpha(PosePalette[indexes[index % indexes.Length]], alpha);
            }

            private static Color GetKeypointColor(int index, int alpha)
            {
                int[] indexes = PoseMetadata.KeypointColorIndexes;
                return WithAlpha(PosePalette[indexes[index % indexes.Length]], alpha);
            }

            private static Color WithAlpha(Color color, int alpha)
            {
                return Color.FromArgb(Math.Max(0, Math.Min(255, alpha)), color);
            }
        }
    }
}
