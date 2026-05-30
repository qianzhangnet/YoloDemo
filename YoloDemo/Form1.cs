using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using CvPoint = OpenCvSharp.Point;
using CvRect = OpenCvSharp.Rect;
using CvSize = OpenCvSharp.Size;

namespace YoloDemo
{
    public partial class Form1 : Form
    {
        private const int DefaultCameraIndex = 0;
        private const int CameraWidth = 640;
        private const int CameraHeight = 640;
        private const int PreviewSize = 640;
        private const float ConfidenceThreshold = 0.35f;
        private const float IouThreshold = 0.45f;
        private const int DataPrintIntervalMs = 1000;

        private static readonly int[,] SkeletonPairs =
        {
            {15, 13}, {13, 11}, {16, 14}, {14, 12}, {11, 12}, {5, 11}, {6, 12},
            {5, 6}, {5, 7}, {6, 8}, {7, 9}, {8, 10}, {1, 2}, {0, 1},
            {0, 2}, {1, 3}, {2, 4}, {3, 5}, {4, 6}
        };

        private static readonly Scalar BoxColor = new Scalar(255, 42, 4);
        private static readonly Scalar TextColor = new Scalar(255, 255, 255);
        private static readonly Scalar FpsColor = new Scalar(86, 255, 194);
        private static readonly Scalar ShadowColor = new Scalar(0, 0, 0);
        private static readonly Scalar JointCenterColor = new Scalar(255, 255, 255);
        private static readonly Scalar[] PosePalette =
        {
            new Scalar(255, 128, 0), new Scalar(255, 153, 51), new Scalar(255, 178, 102),
            new Scalar(230, 230, 0), new Scalar(255, 153, 255), new Scalar(153, 204, 255),
            new Scalar(255, 102, 255), new Scalar(255, 51, 255), new Scalar(102, 178, 255),
            new Scalar(51, 153, 255), new Scalar(255, 153, 153), new Scalar(255, 102, 102),
            new Scalar(255, 51, 51), new Scalar(153, 255, 153), new Scalar(102, 255, 102),
            new Scalar(51, 255, 51), new Scalar(0, 255, 0), new Scalar(0, 0, 255),
            new Scalar(255, 0, 0), new Scalar(255, 255, 255)
        };
        private static readonly int[] LimbColorIndexes =
        {
            9, 9, 9, 9, 7, 7, 7, 0, 0, 0, 0, 0, 16, 16, 16, 16, 16, 16, 16
        };
        private static readonly int[] KeypointColorIndexes =
        {
            16, 16, 16, 16, 16, 0, 0, 0, 0, 0, 0, 9, 9, 9, 9, 9, 9
        };
        private static readonly string[] KeypointNames =
        {
            "nose", "left_eye", "right_eye", "left_ear", "right_ear",
            "left_shoulder", "right_shoulder", "left_elbow", "right_elbow",
            "left_wrist", "right_wrist", "left_hip", "right_hip",
            "left_knee", "right_knee", "left_ankle", "right_ankle"
        };

        private readonly object _stopLock = new object();
        private readonly string _modelPath;
        private YoloPoseDetector _detector;
        private CancellationTokenSource _captureCts;
        private Task _captureTask;
        private bool _closing;
        private double _smoothFps;
        private int _uiFramePending;
        private readonly Stopwatch _dataPrintWatch = Stopwatch.StartNew();
        private long _dataSequence;

        public Form1()
        {
            InitializeComponent();
            this.FormBorderStyle = FormBorderStyle.None;
            _modelPath = ResolveModelPath();
            LoadDetector();
        }

        private static string ResolveModelPath()
        {
            string startupPath = Application.StartupPath;
            string defaultPath = Path.Combine(startupPath, "Mode", "yolo26n-pose.onnx");
            if (File.Exists(defaultPath))
            {
                return defaultPath;
            }

            string lowerModePath = Path.Combine(startupPath, "mode", "yolo26n-pose.onnx");
            if (File.Exists(lowerModePath))
            {
                return lowerModePath;
            }

            return defaultPath;
        }

        private void LoadDetector()
        {
            try
            {
                if (!File.Exists(_modelPath))
                {
                    MessageBox.Show(this, "Model file not found: " + _modelPath, "YOLO Pose Demo", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                _detector = new YoloPoseDetector(_modelPath);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Model load failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void Form1_Shown(object sender, EventArgs e)
        {
            if (_detector != null)
            {
                StartCapture(DefaultCameraIndex);
            }
        }

        private void Form1_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
            {
                Close();
            }
        }

        private void StartCapture(int cameraIndex)
        {
            StopCapture(true);

            _smoothFps = 0;
            _captureCts = new CancellationTokenSource();
            CancellationToken token = _captureCts.Token;
            _captureTask = Task.Run(() => CaptureLoop(cameraIndex, token), token);
        }

        private void StopCapture(bool waitForExit)
        {
            lock (_stopLock)
            {
                CancellationTokenSource cts = _captureCts;
                _captureCts = null;
                if (cts != null)
                {
                    cts.Cancel();
                    cts.Dispose();
                }
            }

            if (waitForExit && _captureTask != null && !_captureTask.IsCompleted)
            {
                try
                {
                    _captureTask.Wait(800);
                }
                catch (AggregateException)
                {
                }
            }
        }

        private void CaptureLoop(int cameraIndex, CancellationToken token)
        {
            using (VideoCapture capture = new VideoCapture(cameraIndex))
            using (Mat frame = new Mat())
            using (Mat previewFrame = new Mat())
            {
                if (!capture.IsOpened())
                {
                    PostError("Camera open failed: " + cameraIndex);
                    return;
                }

                ConfigureCamera(capture);

                while (!token.IsCancellationRequested)
                {
                    Stopwatch frameWatch = Stopwatch.StartNew();
                    if (!capture.Read(frame) || frame.Empty())
                    {
                        Thread.Sleep(5);
                        continue;
                    }

                    MakeSquarePreview(frame, previewFrame);

                    List<PoseDetection> detections;
                    try
                    {
                        detections = _detector.Detect(previewFrame, ConfidenceThreshold, IouThreshold);
                    }
                    catch (Exception ex)
                    {
                        PostError("Inference failed: " + ex.Message);
                        break;
                    }

                    DrawDetections(previewFrame, detections);

                    double instantFps = 1000.0 / Math.Max(1.0, frameWatch.Elapsed.TotalMilliseconds);
                    _smoothFps = _smoothFps <= 0 ? instantFps : _smoothFps * 0.85 + instantFps * 0.15;
                    PrintDetectionData(detections, _smoothFps);
                    DrawFps(previewFrame, _smoothFps);

                    Bitmap bitmap = MatToBitmap(previewFrame);
                    PostFrame(bitmap);
                }
            }
        }

        private static void ConfigureCamera(VideoCapture capture)
        {
            capture.Set(VideoCaptureProperties.FrameWidth, CameraWidth);
            capture.Set(VideoCaptureProperties.FrameHeight, CameraHeight);
            capture.Set(VideoCaptureProperties.Fps, 30);
        }

        private static void MakeSquarePreview(Mat source, Mat target)
        {
            int side = Math.Min(source.Width, source.Height);
            int x = Math.Max(0, (source.Width - side) / 2);
            int y = Math.Max(0, (source.Height - side) / 2);

            using (Mat roi = new Mat(source, new CvRect(x, y, side, side)))
            {
                if (side == PreviewSize)
                {
                    roi.CopyTo(target);
                }
                else
                {
                    Cv2.Resize(roi, target, new CvSize(PreviewSize, PreviewSize), 0, 0, InterpolationFlags.Linear);
                }
            }
        }

        private static void DrawDetections(Mat frame, IList<PoseDetection> detections)
        {
            int stroke = GetStroke(frame);
            foreach (PoseDetection detection in detections)
            {
                RectangleF box = detection.Box;
                CvRect rect = new CvRect(
                    Math.Max(0, (int)Math.Round(box.Left)),
                    Math.Max(0, (int)Math.Round(box.Top)),
                    Math.Max(1, (int)Math.Round(box.Width)),
                    Math.Max(1, (int)Math.Round(box.Height)));

                DrawStyledBox(frame, rect, stroke);
                DrawSkeleton(frame, detection.Keypoints, stroke);
                DrawDetectionLabel(frame, rect, detection, stroke);
            }
        }

        private static void DrawStyledBox(Mat frame, CvRect rect, int stroke)
        {
            Cv2.Rectangle(frame, rect, ShadowColor, stroke + 2, LineTypes.AntiAlias);
            Cv2.Rectangle(frame, rect, BoxColor, stroke, LineTypes.AntiAlias);
        }

        private static void DrawDetectionLabel(Mat frame, CvRect rect, PoseDetection detection, int stroke)
        {
            string label = string.Format("{0} {1:0.00}", detection.Label, detection.Confidence);
            double scale = stroke / 3.0;
            int textThickness = Math.Max(1, stroke - 1);
            int baseline;
            CvSize textSize = Cv2.GetTextSize(label, HersheyFonts.HersheySimplex, scale, textThickness, out baseline);

            int labelHeight = textSize.Height + 3;
            bool outside = rect.Y >= labelHeight;
            int x = Clamp(rect.X, 0, Math.Max(0, frame.Width - textSize.Width));
            int y1 = outside ? rect.Y - labelHeight : rect.Y;
            int y2 = outside ? rect.Y : rect.Y + labelHeight;

            CvRect labelRect = new CvRect(x, y1, textSize.Width + 6, Math.Max(1, y2 - y1));
            Cv2.Rectangle(frame, labelRect, ShadowColor, -1, LineTypes.AntiAlias);
            Cv2.Rectangle(frame, new CvRect(labelRect.X + 2, labelRect.Y + 2, Math.Max(1, labelRect.Width - 4), Math.Max(1, labelRect.Height - 4)), BoxColor, -1, LineTypes.AntiAlias);
            Cv2.PutText(
                frame,
                label,
                new CvPoint(x + 3, outside ? rect.Y - 3 : rect.Y + labelHeight - 2),
                HersheyFonts.HersheySimplex,
                scale,
                TextColor,
                textThickness,
                LineTypes.AntiAlias);
        }

        private static void DrawSkeleton(Mat frame, Keypoint[] keypoints, int stroke)
        {
            if (keypoints == null || keypoints.Length == 0)
            {
                return;
            }

            int radius = stroke;
            for (int i = 0; i < keypoints.Length; i++)
            {
                Keypoint keypoint = keypoints[i];
                if (keypoint.Confidence < 0.25f || IsInvalidKeypoint(frame, keypoint))
                {
                    continue;
                }

                    CvPoint point = ToCvPoint(keypoint);
                    Cv2.Circle(frame, point, radius + 1, ShadowColor, -1, LineTypes.AntiAlias);
                    Cv2.Circle(frame, point, radius, GetKeypointColor(i), -1, LineTypes.AntiAlias);
                    Cv2.Circle(frame, point, Math.Max(1, radius / 2), JointCenterColor, -1, LineTypes.AntiAlias);
            }

            int lineThickness = Math.Max(1, (int)Math.Ceiling(stroke / 2.0));
            for (int i = 0; i < SkeletonPairs.GetLength(0); i++)
            {
                int first = SkeletonPairs[i, 0];
                int second = SkeletonPairs[i, 1];
                if (first >= keypoints.Length || second >= keypoints.Length)
                {
                    continue;
                }

                Keypoint p1 = keypoints[first];
                Keypoint p2 = keypoints[second];
                if (p1.Confidence >= 0.25f && p2.Confidence >= 0.25f &&
                    !IsInvalidKeypoint(frame, p1) && !IsInvalidKeypoint(frame, p2))
                {
                    CvPoint pos1 = ToCvPoint(p1);
                    CvPoint pos2 = ToCvPoint(p2);
                    Cv2.Line(frame, pos1, pos2, ShadowColor, lineThickness + 2, LineTypes.AntiAlias);
                    Cv2.Line(frame, pos1, pos2, GetLimbColor(i), lineThickness, LineTypes.AntiAlias);
                }
            }
        }

        private static bool IsInvalidKeypoint(Mat frame, Keypoint keypoint)
        {
            int x = (int)Math.Round(keypoint.X);
            int y = (int)Math.Round(keypoint.Y);
            return x <= 0 || y <= 0 || x >= frame.Width || y >= frame.Height;
        }

        private static Scalar GetLimbColor(int index)
        {
            return PosePalette[LimbColorIndexes[index % LimbColorIndexes.Length]];
        }

        private static Scalar GetKeypointColor(int index)
        {
            return PosePalette[KeypointColorIndexes[index % KeypointColorIndexes.Length]];
        }

        private static void DrawFps(Mat frame, double fps)
        {
            string text = string.Format("FPS {0:0.0}", fps);
            double scale = 0.62;
            int thickness = 1;
            int baseline;
            CvSize textSize = Cv2.GetTextSize(text, HersheyFonts.HersheySimplex, scale, thickness, out baseline);

            int x = 14;
            int y = 14;
            CvRect rect = new CvRect(x, y, textSize.Width + 18, textSize.Height + baseline + 12);

            BlendRect(frame, rect, new Scalar(8, 16, 18), 0.42);
            Cv2.Line(frame, new CvPoint(x, y + rect.Height), new CvPoint(x + rect.Width, y + rect.Height), FpsColor, 1, LineTypes.AntiAlias);
            Cv2.PutText(frame, text, new CvPoint(x + 9, y + textSize.Height + 5), HersheyFonts.HersheySimplex, scale, ShadowColor, thickness + 2, LineTypes.AntiAlias);
            Cv2.PutText(frame, text, new CvPoint(x + 9, y + textSize.Height + 5), HersheyFonts.HersheySimplex, scale, FpsColor, thickness, LineTypes.AntiAlias);
        }

        private static void BlendRect(Mat frame, CvRect rect, Scalar color, double alpha)
        {
            int x = Clamp(rect.X, 0, frame.Width - 1);
            int y = Clamp(rect.Y, 0, frame.Height - 1);
            int right = Clamp(rect.X + rect.Width, 0, frame.Width);
            int bottom = Clamp(rect.Y + rect.Height, 0, frame.Height);
            if (right <= x || bottom <= y)
            {
                return;
            }

            using (Mat roi = new Mat(frame, new CvRect(x, y, right - x, bottom - y)))
            using (Mat overlay = new Mat(roi.Size(), roi.Type(), color))
            {
                Cv2.AddWeighted(overlay, alpha, roi, 1.0 - alpha, 0, roi);
            }
        }

        private static CvPoint ToCvPoint(Keypoint keypoint)
        {
            return new CvPoint((int)Math.Round(keypoint.X), (int)Math.Round(keypoint.Y));
        }

        private static int GetStroke(Mat frame)
        {
            return Math.Max(2, (int)Math.Round(Math.Min(frame.Width, frame.Height) / 260.0));
        }

        private static Bitmap MatToBitmap(Mat mat)
        {
            Mat source = mat;
            Mat converted = null;
            if (mat.Type() != MatType.CV_8UC3)
            {
                converted = new Mat();
                Cv2.CvtColor(mat, converted, ColorConversionCodes.GRAY2BGR);
                source = converted;
            }

            Bitmap bitmap = new Bitmap(source.Width, source.Height, PixelFormat.Format24bppRgb);
            BitmapData bitmapData = bitmap.LockBits(
                new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height),
                ImageLockMode.WriteOnly,
                PixelFormat.Format24bppRgb);

            try
            {
                int bytesPerRow = source.Width * source.Channels();
                int sourceStride = (int)source.Step();
                int destinationStride = Math.Abs(bitmapData.Stride);
                byte[] row = new byte[bytesPerRow];

                for (int y = 0; y < source.Height; y++)
                {
                    Marshal.Copy(IntPtr.Add(source.Data, y * sourceStride), row, 0, bytesPerRow);
                    Marshal.Copy(row, 0, IntPtr.Add(bitmapData.Scan0, y * destinationStride), bytesPerRow);
                }
            }
            finally
            {
                bitmap.UnlockBits(bitmapData);
                if (converted != null)
                {
                    converted.Dispose();
                }
            }

            return bitmap;
        }

        private void PostFrame(Bitmap bitmap)
        {
            if (_closing || IsDisposed)
            {
                bitmap.Dispose();
                return;
            }

            if (Interlocked.Exchange(ref _uiFramePending, 1) == 1)
            {
                bitmap.Dispose();
                return;
            }

            try
            {
                BeginInvoke((Action)(() =>
                {
                    try
                    {
                        Image oldImage = pictureBoxPreview.Image;
                        pictureBoxPreview.Image = bitmap;
                        if (oldImage != null)
                        {
                            oldImage.Dispose();
                        }
                    }
                    finally
                    {
                        Interlocked.Exchange(ref _uiFramePending, 0);
                    }
                }));
            }
            catch (InvalidOperationException)
            {
                Interlocked.Exchange(ref _uiFramePending, 0);
                bitmap.Dispose();
            }
        }

        private void PrintDetectionData(IList<PoseDetection> detections, double fps)
        {
            if (_dataPrintWatch.ElapsedMilliseconds < DataPrintIntervalMs)
            {
                return;
            }

            _dataPrintWatch.Restart();
            long sequence = Interlocked.Increment(ref _dataSequence);

            try
            {
                StringBuilder builder = new StringBuilder(2048);
                builder.AppendFormat("[{0:HH:mm:ss.fff}] sample={1} fps={2:0.0} persons={3}",
                    DateTime.Now, sequence, fps, detections.Count);
                builder.AppendLine();

                for (int i = 0; i < detections.Count; i++)
                {
                    PoseDetection detection = detections[i];
                    RectangleF box = detection.Box;
                    builder.AppendFormat(
                        "  person[{0}] label={1} behavior={2} conf={3:0.000} box=x:{4:0.0},y:{5:0.0},w:{6:0.0},h:{7:0.0}",
                        i, detection.Label, detection.Behavior, detection.Confidence, box.X, box.Y, box.Width, box.Height);
                    builder.AppendLine();

                    builder.Append("    joints:");
                    Keypoint[] keypoints = detection.Keypoints ?? new Keypoint[0];
                    for (int k = 0; k < keypoints.Length; k++)
                    {
                        string name = k < KeypointNames.Length ? KeypointNames[k] : "kp" + k;
                        Keypoint keypoint = keypoints[k];
                        builder.AppendFormat(" {0}=({1:0.0},{2:0.0},{3:0.00})", name, keypoint.X, keypoint.Y, keypoint.Confidence);
                    }
                    builder.AppendLine();
                }

                Console.Write(builder.ToString());
            }
            catch (IOException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
        }

        private void PostError(string message)
        {
            if (_closing || IsDisposed)
            {
                return;
            }

            try
            {
                BeginInvoke((Action)(() => MessageBox.Show(this, message, "YOLO Pose Demo", MessageBoxButtons.OK, MessageBoxIcon.Error)));
            }
            catch (InvalidOperationException)
            {
            }
        }

        private static int Clamp(int value, int min, int max)
        {
            if (max < min)
            {
                return min;
            }

            return Math.Max(min, Math.Min(max, value));
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            _closing = true;
            StopCapture(true);
            if (_detector != null)
            {
                _detector.Dispose();
                _detector = null;
            }

            Image oldImage = pictureBoxPreview.Image;
            pictureBoxPreview.Image = null;
            if (oldImage != null)
            {
                oldImage.Dispose();
            }
        }
    }
}
