using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace YoloDemo
{
    public sealed class YoloPoseDetector : IDisposable
    {
        private const int InputWidth = 640;
        private const int InputHeight = 640;
        private const int DefaultKeypointCount = 17;
        private const int DefaultKeypointDimensions = 3;

        private readonly InferenceSession _session;
        private readonly string _inputName;
        private readonly string _outputName;
        private readonly string[] _classNames;
        private readonly int _keypointCount;
        private readonly int _keypointDimensions;

        public YoloPoseDetector(string modelPath)
        {
            SessionOptions options = new SessionOptions
            {
                GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
                IntraOpNumThreads = Math.Max(1, Environment.ProcessorCount / 2)
            };

            _session = new InferenceSession(modelPath, options);
            _inputName = _session.InputMetadata.Keys.First();
            _outputName = _session.OutputMetadata.Keys.First();

            IReadOnlyDictionary<string, string> metadata = _session.ModelMetadata.CustomMetadataMap;
            _classNames = ParseClassNames(metadata);
            ParseKeypointShape(metadata, out _keypointCount, out _keypointDimensions);
        }

        public List<PoseDetection> Detect(Mat frame, float confidenceThreshold, float iouThreshold)
        {
            if (frame == null || frame.Empty())
            {
                return new List<PoseDetection>();
            }

            LetterboxInfo letterbox;
            DenseTensor<float> input = CreateInputTensor(frame, out letterbox);
            using (IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results = _session.Run(new[]
            {
                NamedOnnxValue.CreateFromTensor(_inputName, input)
            }))
            {
                Tensor<float> output = results.First(v => v.Name == _outputName).AsTensor<float>();
                List<PoseDetection> detections = ParseOutput(output, letterbox, frame.Width, frame.Height, confidenceThreshold);
                return ApplyNms(detections, iouThreshold);
            }
        }

        private DenseTensor<float> CreateInputTensor(Mat source, out LetterboxInfo letterbox)
        {
            int sourceWidth = source.Width;
            int sourceHeight = source.Height;
            float gain = Math.Min(InputWidth / (float)sourceWidth, InputHeight / (float)sourceHeight);
            int resizedWidth = Math.Max(1, (int)Math.Round(sourceWidth * gain));
            int resizedHeight = Math.Max(1, (int)Math.Round(sourceHeight * gain));
            int padX = (InputWidth - resizedWidth) / 2;
            int padY = (InputHeight - resizedHeight) / 2;

            letterbox = new LetterboxInfo(gain, padX, padY);

            using (Mat rgb = new Mat())
            using (Mat resized = new Mat())
            using (Mat padded = new Mat(new OpenCvSharp.Size(InputWidth, InputHeight), MatType.CV_8UC3, new Scalar(114, 114, 114)))
            {
                Cv2.CvtColor(source, rgb, ColorConversionCodes.BGR2RGB);
                Cv2.Resize(rgb, resized, new OpenCvSharp.Size(resizedWidth, resizedHeight));
                using (Mat roi = new Mat(padded, new Rect(padX, padY, resizedWidth, resizedHeight)))
                {
                    resized.CopyTo(roi);
                }

                byte[] pixels = new byte[InputWidth * InputHeight * 3];
                Marshal.Copy(padded.Data, pixels, 0, pixels.Length);

                float[] tensorData = new float[1 * 3 * InputHeight * InputWidth];
                int channelSize = InputHeight * InputWidth;
                for (int y = 0; y < InputHeight; y++)
                {
                    int rowOffset = y * InputWidth;
                    for (int x = 0; x < InputWidth; x++)
                    {
                        int pixelIndex = (rowOffset + x) * 3;
                        int tensorIndex = rowOffset + x;
                        tensorData[tensorIndex] = pixels[pixelIndex] / 255.0f;
                        tensorData[channelSize + tensorIndex] = pixels[pixelIndex + 1] / 255.0f;
                        tensorData[channelSize * 2 + tensorIndex] = pixels[pixelIndex + 2] / 255.0f;
                    }
                }

                return new DenseTensor<float>(tensorData, new[] { 1, 3, InputHeight, InputWidth });
            }
        }

        private List<PoseDetection> ParseOutput(Tensor<float> output, LetterboxInfo letterbox, int imageWidth, int imageHeight, float confidenceThreshold)
        {
            int[] dimensions = output.Dimensions.ToArray();
            float[] data = output.ToArray();
            OutputLayout layout = ResolveOutputLayout(dimensions);
            List<PoseDetection> detections = new List<PoseDetection>();

            int keypointValueCount = _keypointCount * _keypointDimensions;
            bool endToEndLayout = layout.AttributeCount >= 6 + keypointValueCount && layout.DetectionCount <= 1000;
            int classScoreCount = Math.Max(1, layout.AttributeCount - 4 - keypointValueCount);

            for (int i = 0; i < layout.DetectionCount; i++)
            {
                float score;
                int classId;
                int keypointStart;
                float x1;
                float y1;
                float x2;
                float y2;

                if (endToEndLayout)
                {
                    x1 = layout.Get(data, i, 0);
                    y1 = layout.Get(data, i, 1);
                    x2 = layout.Get(data, i, 2);
                    y2 = layout.Get(data, i, 3);
                    score = layout.Get(data, i, 4);
                    classId = Clamp((int)Math.Round(layout.Get(data, i, 5)), 0, _classNames.Length - 1);
                    keypointStart = 6;

                    if (LooksNormalized(x1, y1, x2, y2))
                    {
                        x1 *= InputWidth;
                        x2 *= InputWidth;
                        y1 *= InputHeight;
                        y2 *= InputHeight;
                    }
                }
                else
                {
                    float cx = layout.Get(data, i, 0);
                    float cy = layout.Get(data, i, 1);
                    float width = layout.Get(data, i, 2);
                    float height = layout.Get(data, i, 3);

                    score = 0;
                    classId = 0;
                    for (int c = 0; c < classScoreCount; c++)
                    {
                        float classScore = layout.Get(data, i, 4 + c);
                        if (classScore > score)
                        {
                            score = classScore;
                            classId = Clamp(c, 0, _classNames.Length - 1);
                        }
                    }

                    keypointStart = 4 + classScoreCount;

                    if (LooksNormalized(cx, cy, width, height))
                    {
                        cx *= InputWidth;
                        width *= InputWidth;
                        cy *= InputHeight;
                        height *= InputHeight;
                    }

                    x1 = cx - width / 2.0f;
                    y1 = cy - height / 2.0f;
                    x2 = cx + width / 2.0f;
                    y2 = cy + height / 2.0f;
                }

                if (score < confidenceThreshold || float.IsNaN(score) || float.IsInfinity(score))
                {
                    continue;
                }

                RectangleF box = MapBox(x1, y1, x2, y2, letterbox, imageWidth, imageHeight);
                if (box.Width < 2 || box.Height < 2)
                {
                    continue;
                }

                Keypoint[] keypoints = ParseKeypoints(layout, data, i, keypointStart, letterbox, imageWidth, imageHeight);
                PoseDetection detection = new PoseDetection
                {
                    Box = box,
                    Confidence = score,
                    ClassId = classId,
                    Label = _classNames.Length > classId ? _classNames[classId] : "person",
                    Keypoints = keypoints
                };
                detection.Behavior = EstimateBehavior(detection);
                detections.Add(detection);
            }

            return detections;
        }

        private Keypoint[] ParseKeypoints(OutputLayout layout, float[] data, int detectionIndex, int keypointStart, LetterboxInfo letterbox, int imageWidth, int imageHeight)
        {
            Keypoint[] keypoints = new Keypoint[_keypointCount];

            for (int k = 0; k < _keypointCount; k++)
            {
                int offset = keypointStart + k * _keypointDimensions;
                if (offset + 1 >= layout.AttributeCount)
                {
                    keypoints[k] = new Keypoint();
                    continue;
                }

                float x = layout.Get(data, detectionIndex, offset);
                float y = layout.Get(data, detectionIndex, offset + 1);
                float confidence = _keypointDimensions >= 3 && offset + 2 < layout.AttributeCount
                    ? layout.Get(data, detectionIndex, offset + 2)
                    : 1.0f;

                if (LooksNormalized(x, y, 0, 0))
                {
                    x *= InputWidth;
                    y *= InputHeight;
                }

                keypoints[k] = new Keypoint
                {
                    X = Clamp((x - letterbox.PadX) / letterbox.Gain, 0, imageWidth - 1),
                    Y = Clamp((y - letterbox.PadY) / letterbox.Gain, 0, imageHeight - 1),
                    Confidence = confidence
                };
            }

            return keypoints;
        }

        private static RectangleF MapBox(float x1, float y1, float x2, float y2, LetterboxInfo letterbox, int imageWidth, int imageHeight)
        {
            float left = Clamp((Math.Min(x1, x2) - letterbox.PadX) / letterbox.Gain, 0, imageWidth - 1);
            float top = Clamp((Math.Min(y1, y2) - letterbox.PadY) / letterbox.Gain, 0, imageHeight - 1);
            float right = Clamp((Math.Max(x1, x2) - letterbox.PadX) / letterbox.Gain, 0, imageWidth - 1);
            float bottom = Clamp((Math.Max(y1, y2) - letterbox.PadY) / letterbox.Gain, 0, imageHeight - 1);
            return RectangleF.FromLTRB(left, top, right, bottom);
        }

        private static OutputLayout ResolveOutputLayout(int[] dimensions)
        {
            if (dimensions.Length == 3)
            {
                int first = dimensions[1];
                int second = dimensions[2];
                bool rowsAreDetections = second >= 5 && second <= 256;
                return rowsAreDetections
                    ? new OutputLayout(first, second, false)
                    : new OutputLayout(second, first, true);
            }

            if (dimensions.Length == 2)
            {
                int first = dimensions[0];
                int second = dimensions[1];
                bool rowsAreDetections = second >= 5 && second <= 256;
                return rowsAreDetections
                    ? new OutputLayout(first, second, false)
                    : new OutputLayout(second, first, true);
            }

            throw new NotSupportedException("不支持的 YOLO 输出维度：" + string.Join("x", dimensions));
        }

        private static List<PoseDetection> ApplyNms(List<PoseDetection> detections, float iouThreshold)
        {
            List<PoseDetection> ordered = detections.OrderByDescending(d => d.Confidence).ToList();
            List<PoseDetection> kept = new List<PoseDetection>();

            foreach (PoseDetection detection in ordered)
            {
                bool overlaps = false;
                foreach (PoseDetection selected in kept)
                {
                    if (detection.ClassId == selected.ClassId && CalculateIou(detection.Box, selected.Box) > iouThreshold)
                    {
                        overlaps = true;
                        break;
                    }
                }

                if (!overlaps)
                {
                    kept.Add(detection);
                }
            }

            return kept;
        }

        private static float CalculateIou(RectangleF a, RectangleF b)
        {
            float left = Math.Max(a.Left, b.Left);
            float top = Math.Max(a.Top, b.Top);
            float right = Math.Min(a.Right, b.Right);
            float bottom = Math.Min(a.Bottom, b.Bottom);
            float intersection = Math.Max(0, right - left) * Math.Max(0, bottom - top);
            float union = a.Width * a.Height + b.Width * b.Height - intersection;
            return union <= 0 ? 0 : intersection / union;
        }

        private static string EstimateBehavior(PoseDetection detection)
        {
            RectangleF box = detection.Box;
            Keypoint[] kpts = detection.Keypoints;

            if (box.Width > box.Height * 1.15f)
            {
                return "fall?";
            }

            if (Has(kpts, 5) && Has(kpts, 6) && Has(kpts, 11) && Has(kpts, 12))
            {
                PointF shoulder = Mid(kpts[5], kpts[6]);
                PointF hip = Mid(kpts[11], kpts[12]);
                float dx = Math.Abs(shoulder.X - hip.X);
                float dy = Math.Abs(shoulder.Y - hip.Y);
                if (dx > dy * 1.2f)
                {
                    return "fall?";
                }
            }

            if ((Has(kpts, 11) && Has(kpts, 13) && Math.Abs(kpts[11].Y - kpts[13].Y) < box.Height * 0.22f) ||
                (Has(kpts, 12) && Has(kpts, 14) && Math.Abs(kpts[12].Y - kpts[14].Y) < box.Height * 0.22f))
            {
                return "sit/squat";
            }

            if (box.Height > box.Width * 1.25f)
            {
                return "stand/walk";
            }

            return "person";
        }

        private static PointF Mid(Keypoint a, Keypoint b)
        {
            return new PointF((a.X + b.X) / 2.0f, (a.Y + b.Y) / 2.0f);
        }

        private static bool Has(Keypoint[] keypoints, int index)
        {
            return keypoints != null &&
                index >= 0 &&
                index < keypoints.Length &&
                keypoints[index].Confidence >= PoseMetadata.ReliableKeypointConfidence;
        }

        private static bool LooksNormalized(float a, float b, float c, float d)
        {
            float max = Math.Max(Math.Max(Math.Abs(a), Math.Abs(b)), Math.Max(Math.Abs(c), Math.Abs(d)));
            return max > 0 && max <= 2.0f;
        }

        private static string[] ParseClassNames(IReadOnlyDictionary<string, string> metadata)
        {
            string names;
            if (!metadata.TryGetValue("names", out names) || string.IsNullOrWhiteSpace(names))
            {
                return new[] { "person" };
            }

            MatchCollection matches = Regex.Matches(names, @"(\d+)\s*:\s*'([^']+)'");
            if (matches.Count == 0)
            {
                return new[] { "person" };
            }

            SortedDictionary<int, string> parsed = new SortedDictionary<int, string>();
            foreach (Match match in matches)
            {
                int index;
                if (int.TryParse(match.Groups[1].Value, out index))
                {
                    parsed[index] = match.Groups[2].Value;
                }
            }

            return parsed.Count == 0 ? new[] { "person" } : parsed.Values.ToArray();
        }

        private static void ParseKeypointShape(IReadOnlyDictionary<string, string> metadata, out int count, out int dimensions)
        {
            count = DefaultKeypointCount;
            dimensions = DefaultKeypointDimensions;

            string shape;
            if (!metadata.TryGetValue("kpt_shape", out shape))
            {
                return;
            }

            Match match = Regex.Match(shape, @"\[\s*(\d+)\s*,\s*(\d+)\s*\]");
            if (!match.Success)
            {
                return;
            }

            int parsedCount;
            int parsedDimensions;
            if (int.TryParse(match.Groups[1].Value, out parsedCount) && int.TryParse(match.Groups[2].Value, out parsedDimensions))
            {
                count = Math.Max(1, parsedCount);
                dimensions = Math.Max(2, parsedDimensions);
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

        private static float Clamp(float value, float min, float max)
        {
            if (max < min)
            {
                return min;
            }

            return Math.Max(min, Math.Min(max, value));
        }

        public void Dispose()
        {
            _session.Dispose();
        }

        private struct LetterboxInfo
        {
            public LetterboxInfo(float gain, int padX, int padY)
            {
                Gain = gain;
                PadX = padX;
                PadY = padY;
            }

            public float Gain { get; }
            public int PadX { get; }
            public int PadY { get; }
        }

        private struct OutputLayout
        {
            public OutputLayout(int detectionCount, int attributeCount, bool transposed)
            {
                DetectionCount = detectionCount;
                AttributeCount = attributeCount;
                Transposed = transposed;
            }

            public int DetectionCount { get; }
            public int AttributeCount { get; }
            public bool Transposed { get; }

            public float Get(float[] data, int detectionIndex, int attributeIndex)
            {
                return Transposed
                    ? data[attributeIndex * DetectionCount + detectionIndex]
                    : data[detectionIndex * AttributeCount + attributeIndex];
            }
        }
    }

    public sealed class PoseDetection
    {
        public RectangleF Box { get; set; }
        public float Confidence { get; set; }
        public int ClassId { get; set; }
        public string Label { get; set; }
        public string Behavior { get; set; }
        public Keypoint[] Keypoints { get; set; }
    }

    public struct Keypoint
    {
        public float X { get; set; }
        public float Y { get; set; }
        public float Confidence { get; set; }
    }
}
