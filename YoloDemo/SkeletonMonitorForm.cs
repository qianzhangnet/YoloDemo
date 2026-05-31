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
        private const float VisibleConfidence = 0.25f;

        private static readonly int[,] SkeletonPairs =
        {
            {15, 13}, {13, 11}, {16, 14}, {14, 12}, {11, 12}, {5, 11}, {6, 12},
            {5, 6}, {5, 7}, {6, 8}, {7, 9}, {8, 10}, {1, 2}, {0, 1},
            {0, 2}, {1, 3}, {2, 4}, {3, 5}, {4, 6}
        };

        private static readonly string[] KeypointNames =
        {
            "nose", "left_eye", "right_eye", "left_ear", "right_ear",
            "left_shoulder", "right_shoulder", "left_elbow", "right_elbow",
            "left_wrist", "right_wrist", "left_hip", "right_hip",
            "left_knee", "right_knee", "left_ankle", "right_ankle"
        };

        private readonly SkeletonCanvas _canvas;
        private readonly Label _summaryLabel;
        private readonly TextBox _dataTextBox;
        private readonly Stopwatch _watch = Stopwatch.StartNew();
        private long _sampleIndex;
        private int _updatePending;

        public SkeletonMonitorForm()
        {
            Text = "YOLO Pose Skeleton Data";
            BackColor = Color.FromArgb(16, 20, 28);
            ForeColor = Color.White;
            Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point, 134);
            ClientSize = new Size(900, 640);
            MinimumSize = new Size(720, 520);
            StartPosition = FormStartPosition.Manual;

            TableLayoutPanel root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = BackColor,
                ColumnCount = 2,
                RowCount = 1,
                Padding = new Padding(12)
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58F));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42F));

            _canvas = new SkeletonCanvas
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 0, 12, 0)
            };

            TableLayoutPanel dataPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(20, 25, 34),
                ColumnCount = 1,
                RowCount = 2,
                Padding = new Padding(10)
            };
            dataPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 36F));
            dataPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            _summaryLabel = new Label
            {
                Dock = DockStyle.Fill,
                AutoEllipsis = true,
                ForeColor = Color.FromArgb(142, 235, 206),
                TextAlign = ContentAlignment.MiddleLeft,
                Text = "Waiting for pose data..."
            };

            _dataTextBox = new TextBox
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(10, 13, 18),
                ForeColor = Color.FromArgb(230, 235, 242),
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Consolas", 9F, FontStyle.Regular, GraphicsUnit.Point, 0),
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                WordWrap = false
            };

            dataPanel.Controls.Add(_summaryLabel, 0, 0);
            dataPanel.Controls.Add(_dataTextBox, 0, 1);
            root.Controls.Add(_canvas, 0, 0);
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
            Size sourceSize = new Size(
                Math.Max(1, sourceWidth),
                Math.Max(1, sourceHeight));
            long sample = Interlocked.Increment(ref _sampleIndex);
            string summary = string.Format(
                "Sample {0}   FPS {1:0.0}   Persons {2}   {3:HH:mm:ss.fff}",
                sample,
                fps,
                snapshot.Count,
                DateTime.Now);
            string details = BuildDetectionText(snapshot, fps, sample, sourceSize, _watch.Elapsed);

            Action apply = delegate
            {
                try
                {
                    if (!IsDisposed && !Disposing)
                    {
                        _summaryLabel.Text = summary;
                        _dataTextBox.Text = details;
                        _canvas.SetDetections(snapshot, sourceSize);
                    }
                }
                finally
                {
                    Interlocked.Exchange(ref _updatePending, 0);
                }
            };

            try
            {
                if (IsHandleCreated && InvokeRequired)
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

        private static string BuildDetectionText(
            IList<PoseDetection> detections,
            double fps,
            long sample,
            Size sourceSize,
            TimeSpan elapsed)
        {
            StringBuilder builder = new StringBuilder(4096);
            builder.AppendFormat("sample       : {0}", sample);
            builder.AppendLine();
            builder.AppendFormat("fps          : {0:0.0}", fps);
            builder.AppendLine();
            builder.AppendFormat("source       : {0} x {1}", sourceSize.Width, sourceSize.Height);
            builder.AppendLine();
            builder.AppendFormat("persons      : {0}", detections.Count);
            builder.AppendLine();
            builder.AppendFormat("elapsed      : {0:hh\\:mm\\:ss}", elapsed);
            builder.AppendLine();
            builder.AppendLine();

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
                    string name = k < KeypointNames.Length ? KeypointNames[k] : "kp" + k;
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

        private sealed class SkeletonCanvas : Control
        {
            private readonly object _syncRoot = new object();
            private List<PoseDetection> _detections = new List<PoseDetection>();
            private Size _sourceSize = new Size(DefaultSourceSize, DefaultSourceSize);

            public SkeletonCanvas()
            {
                BackColor = Color.FromArgb(10, 13, 18);
                SetStyle(
                    ControlStyles.AllPaintingInWmPaint |
                    ControlStyles.OptimizedDoubleBuffer |
                    ControlStyles.ResizeRedraw |
                    ControlStyles.UserPaint,
                    true);
            }

            public void SetDetections(List<PoseDetection> detections, Size sourceSize)
            {
                lock (_syncRoot)
                {
                    _detections = detections ?? new List<PoseDetection>();
                    _sourceSize = sourceSize.Width <= 0 || sourceSize.Height <= 0
                        ? new Size(DefaultSourceSize, DefaultSourceSize)
                        : sourceSize;
                }

                Invalidate();
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);

                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                e.Graphics.Clear(BackColor);

                Rectangle bounds = ClientRectangle;
                bounds.Inflate(-18, -18);
                if (bounds.Width <= 10 || bounds.Height <= 10)
                {
                    return;
                }

                List<PoseDetection> detections;
                Size sourceSize;
                lock (_syncRoot)
                {
                    detections = new List<PoseDetection>(_detections);
                    sourceSize = _sourceSize;
                }

                DrawStage(e.Graphics, bounds);

                if (detections.Count == 0)
                {
                    DrawEmptyState(e.Graphics, bounds);
                    return;
                }

                RectangleF poseArea = CalculatePoseArea(bounds, sourceSize);
                DrawCoordinateFrame(e.Graphics, poseArea);

                for (int i = detections.Count - 1; i >= 0; i--)
                {
                    bool primary = i == 0;
                    DrawDetection(e.Graphics, detections[i], sourceSize, poseArea, primary);
                }
            }

            private static void DrawStage(Graphics graphics, Rectangle bounds)
            {
                using (SolidBrush brush = new SolidBrush(Color.FromArgb(14, 18, 25)))
                using (Pen borderPen = new Pen(Color.FromArgb(52, 68, 82), 1F))
                {
                    graphics.FillRectangle(brush, bounds);
                    graphics.DrawRectangle(borderPen, bounds);
                }
            }

            private void DrawEmptyState(Graphics graphics, Rectangle bounds)
            {
                string text = "Waiting for pose data...";
                using (Font font = new Font(Font.FontFamily, 12F, FontStyle.Regular))
                using (SolidBrush brush = new SolidBrush(Color.FromArgb(150, 165, 180)))
                {
                    SizeF size = graphics.MeasureString(text, font);
                    graphics.DrawString(
                        text,
                        font,
                        brush,
                        bounds.Left + (bounds.Width - size.Width) / 2F,
                        bounds.Top + (bounds.Height - size.Height) / 2F);
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
                using (Pen gridPen = new Pen(Color.FromArgb(28, 45, 55), 1F))
                using (Pen borderPen = new Pen(Color.FromArgb(72, 92, 112), 1F))
                {
                    for (int i = 1; i < 4; i++)
                    {
                        float x = poseArea.Left + poseArea.Width * i / 4F;
                        float y = poseArea.Top + poseArea.Height * i / 4F;
                        graphics.DrawLine(gridPen, x, poseArea.Top, x, poseArea.Bottom);
                        graphics.DrawLine(gridPen, poseArea.Left, y, poseArea.Right, y);
                    }

                    graphics.DrawRectangle(borderPen, poseArea.X, poseArea.Y, poseArea.Width, poseArea.Height);
                }
            }

            private static void DrawDetection(
                Graphics graphics,
                PoseDetection detection,
                Size sourceSize,
                RectangleF poseArea,
                bool primary)
            {
                if (detection == null)
                {
                    return;
                }

                RectangleF box = MapRect(detection.Box, sourceSize, poseArea);
                int alpha = primary ? 230 : 125;
                using (Pen boxPen = new Pen(Color.FromArgb(alpha, 86, 255, 194), primary ? 2F : 1F))
                {
                    graphics.DrawRectangle(boxPen, box.X, box.Y, box.Width, box.Height);
                }

                Keypoint[] keypoints = detection.Keypoints ?? new Keypoint[0];
                DrawTorso(graphics, keypoints, sourceSize, poseArea, alpha);
                DrawLimbs(graphics, keypoints, sourceSize, poseArea, alpha, primary);
                DrawJoints(graphics, keypoints, sourceSize, poseArea, alpha, primary);
            }

            private static void DrawTorso(Graphics graphics, Keypoint[] keypoints, Size sourceSize, RectangleF poseArea, int alpha)
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

                using (SolidBrush brush = new SolidBrush(Color.FromArgb(Math.Min(alpha, 72), 80, 180, 255)))
                using (Pen pen = new Pen(Color.FromArgb(alpha, 101, 197, 255), 1.5F))
                {
                    graphics.FillPolygon(brush, torso);
                    graphics.DrawPolygon(pen, torso);
                }
            }

            private static void DrawLimbs(
                Graphics graphics,
                Keypoint[] keypoints,
                Size sourceSize,
                RectangleF poseArea,
                int alpha,
                bool primary)
            {
                using (Pen limbPen = new Pen(Color.FromArgb(alpha, 255, 178, 76), primary ? 3F : 2F))
                using (Pen shadowPen = new Pen(Color.FromArgb(Math.Min(alpha, 150), 0, 0, 0), primary ? 5F : 4F))
                {
                    shadowPen.StartCap = LineCap.Round;
                    shadowPen.EndCap = LineCap.Round;
                    limbPen.StartCap = LineCap.Round;
                    limbPen.EndCap = LineCap.Round;

                    for (int i = 0; i < SkeletonPairs.GetLength(0); i++)
                    {
                        int first = SkeletonPairs[i, 0];
                        int second = SkeletonPairs[i, 1];
                        if (!Has(keypoints, first) || !Has(keypoints, second))
                        {
                            continue;
                        }

                        PointF p1 = MapPoint(keypoints[first], sourceSize, poseArea);
                        PointF p2 = MapPoint(keypoints[second], sourceSize, poseArea);
                        graphics.DrawLine(shadowPen, p1, p2);
                        graphics.DrawLine(limbPen, p1, p2);
                    }
                }
            }

            private static void DrawJoints(
                Graphics graphics,
                Keypoint[] keypoints,
                Size sourceSize,
                RectangleF poseArea,
                int alpha,
                bool primary)
            {
                float radius = primary ? 4.5F : 3.5F;
                using (SolidBrush jointBrush = new SolidBrush(Color.FromArgb(alpha, 255, 255, 255)))
                using (SolidBrush centerBrush = new SolidBrush(Color.FromArgb(alpha, 86, 255, 194)))
                using (Pen shadowPen = new Pen(Color.FromArgb(Math.Min(alpha, 160), 0, 0, 0), 2F))
                {
                    for (int i = 0; i < keypoints.Length; i++)
                    {
                        if (!Has(keypoints, i))
                        {
                            continue;
                        }

                        PointF point = MapPoint(keypoints[i], sourceSize, poseArea);
                        graphics.DrawEllipse(shadowPen, point.X - radius, point.Y - radius, radius * 2F, radius * 2F);
                        graphics.FillEllipse(jointBrush, point.X - radius, point.Y - radius, radius * 2F, radius * 2F);
                        graphics.FillEllipse(centerBrush, point.X - 1.5F, point.Y - 1.5F, 3F, 3F);
                    }
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

            private static bool Has(Keypoint[] keypoints, int index)
            {
                if (keypoints == null || index < 0 || index >= keypoints.Length)
                {
                    return false;
                }

                Keypoint keypoint = keypoints[index];
                return keypoint.Confidence >= VisibleConfidence && keypoint.X > 0 && keypoint.Y > 0;
            }
        }
    }
}
