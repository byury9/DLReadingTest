using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace DLReadingTest.Yolo
{
    /// <summary>
    /// Ultralytics YOLO(ONNX) 추론기.
    /// - YOLO26 end-to-end(NMS-free) 출력 [1, N, 6] (x1,y1,x2,y2,score,class)
    /// - YOLOv8 / YOLO11 계열 출력 [1, 4+클래스수, 앵커수] (cx,cy,w,h,클래스 점수...)
    /// 두 형식을 모두 자동 판별해서 처리한다.
    /// </summary>
    public sealed class YoloDetector : IDisposable
    {
        private readonly InferenceSession _session;
        private readonly string _inputName;

        public string ModelPath { get; private set; }
        public int InputWidth { get; private set; }
        public int InputHeight { get; private set; }
        public string[] ClassNames { get; private set; }

        /// <summary>모델 메타데이터의 설명 (예: "Ultralytics YOLO26n model ...")</summary>
        public string Description { get; private set; }

        public YoloDetector(string modelPath)
        {
            ModelPath = modelPath;
            var options = new SessionOptions();
            options.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;
            options.IntraOpNumThreads = Math.Max(1, Environment.ProcessorCount / 2);
            _session = new InferenceSession(modelPath, options);

            _inputName = _session.InputMetadata.Keys.First();
            int[] dims = _session.InputMetadata[_inputName].Dimensions;
            Dictionary<string, string> meta = _session.ModelMetadata.CustomMetadataMap;

            int w = dims.Length == 4 ? dims[3] : -1;
            int h = dims.Length == 4 ? dims[2] : -1;
            if (w <= 0 || h <= 0)
            {
                // 동적 입력 크기로 export 된 경우 메타데이터 imgsz 사용 (예: "[640, 640]")
                int[] imgsz = ParseIntList(GetMeta(meta, "imgsz"));
                h = imgsz.Length >= 1 ? imgsz[0] : 640;
                w = imgsz.Length >= 2 ? imgsz[1] : h;
            }
            InputWidth = w;
            InputHeight = h;

            Description = GetMeta(meta, "description") ?? Path.GetFileName(modelPath);
            ClassNames = LoadClassNames(modelPath, meta);
        }

        /// <summary>
        /// 이미지에서 문자를 검출한다.
        /// </summary>
        /// <param name="image">추론할 이미지 (ROI 로 잘라낸 이미지)</param>
        /// <param name="offset">image 의 좌상단이 원본 이미지에서 갖는 좌표 (ROI 위치)</param>
        /// <param name="confThreshold">신뢰도 임계값</param>
        /// <param name="iouThreshold">중복 제거(NMS) IoU 임계값</param>
        public List<Detection> Detect(Bitmap image, Point offset, float confThreshold, float iouThreshold)
        {
            float scale;
            int padX, padY;
            DenseTensor<float> input = Preprocess(image, out scale, out padX, out padY);

            var inputs = new List<NamedOnnxValue> { NamedOnnxValue.CreateFromTensor(_inputName, input) };
            List<Detection> raw;
            using (IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results = _session.Run(inputs))
            {
                Tensor<float> output = results.First().AsTensor<float>();
                int[] od = output.Dimensions.ToArray();
                float[] data = output.ToArray();
                raw = ParseOutput(od, data, confThreshold);
            }

            // 레터박스 좌표 -> 원본(ROI) 좌표 -> 전체 이미지 좌표
            var list = new List<Detection>(raw.Count);
            foreach (Detection d in raw)
            {
                float x1 = (d.Box.Left - padX) / scale;
                float y1 = (d.Box.Top - padY) / scale;
                float x2 = (d.Box.Right - padX) / scale;
                float y2 = (d.Box.Bottom - padY) / scale;
                x1 = Clamp(x1, 0, image.Width);
                x2 = Clamp(x2, 0, image.Width);
                y1 = Clamp(y1, 0, image.Height);
                y2 = Clamp(y2, 0, image.Height);
                if (x2 - x1 < 1 || y2 - y1 < 1) continue;
                d.Box = new RectangleF(x1 + offset.X, y1 + offset.Y, x2 - x1, y2 - y1);
                d.Label = d.ClassId >= 0 && d.ClassId < ClassNames.Length
                    ? ClassNames[d.ClassId]
                    : d.ClassId.ToString(CultureInfo.InvariantCulture);
                list.Add(d);
            }

            // 문자 인식은 한 위치에 한 글자이므로 클래스 무관(agnostic) NMS 적용
            return Nms(list, iouThreshold);
        }

        // ------------------------------------------------------------------ 전처리
        private DenseTensor<float> Preprocess(Bitmap image, out float scale, out int padX, out int padY)
        {
            int inW = InputWidth, inH = InputHeight;
            scale = Math.Min((float)inW / image.Width, (float)inH / image.Height);
            int newW = Math.Max(1, (int)Math.Round(image.Width * scale));
            int newH = Math.Max(1, (int)Math.Round(image.Height * scale));
            padX = (inW - newW) / 2;
            padY = (inH - newH) / 2;

            var tensor = new DenseTensor<float>(new[] { 1, 3, inH, inW });
            using (var canvas = new Bitmap(inW, inH, PixelFormat.Format24bppRgb))
            {
                using (Graphics g = Graphics.FromImage(canvas))
                {
                    g.Clear(Color.FromArgb(114, 114, 114)); // Ultralytics 레터박스 색
                    g.InterpolationMode = scale < 1f ? InterpolationMode.HighQualityBilinear : InterpolationMode.Bilinear;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.SmoothingMode = SmoothingMode.None;
                    using (var attr = new ImageAttributes())
                    {
                        attr.SetWrapMode(WrapMode.TileFlipXY); // 가장자리 번짐 방지
                        g.DrawImage(image, new Rectangle(padX, padY, newW, newH),
                            0, 0, image.Width, image.Height, GraphicsUnit.Pixel, attr);
                    }
                }

                BitmapData bd = canvas.LockBits(new Rectangle(0, 0, inW, inH), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
                try
                {
                    int stride = bd.Stride;
                    var buffer = new byte[stride * inH];
                    Marshal.Copy(bd.Scan0, buffer, 0, buffer.Length);

                    Span<float> dst = tensor.Buffer.Span;
                    int plane = inW * inH;
                    const float inv = 1f / 255f;
                    for (int y = 0; y < inH; y++)
                    {
                        int row = y * stride;
                        int o = y * inW;
                        for (int x = 0; x < inW; x++)
                        {
                            int p = row + x * 3; // 메모리 순서: B, G, R
                            dst[o + x] = buffer[p + 2] * inv;             // R
                            dst[plane + o + x] = buffer[p + 1] * inv;     // G
                            dst[2 * plane + o + x] = buffer[p] * inv;     // B
                        }
                    }
                }
                finally
                {
                    canvas.UnlockBits(bd);
                }
            }
            return tensor;
        }

        // ------------------------------------------------------------------ 후처리
        private static List<Detection> ParseOutput(int[] od, float[] data, float conf)
        {
            var list = new List<Detection>();
            if (od.Length != 3)
                throw new NotSupportedException("지원하지 않는 출력 형식입니다: [" + string.Join(",", od) + "]");

            int d1 = od[1], d2 = od[2];

            if (d2 == 6 && d1 != 6)
            {
                // YOLO26 end-to-end: [1, N, 6] = x1, y1, x2, y2, score, class
                for (int i = 0; i < d1; i++)
                {
                    int o = i * 6;
                    float score = data[o + 4];
                    if (score < conf) continue;
                    list.Add(new Detection
                    {
                        ClassId = (int)Math.Round(data[o + 5]),
                        Confidence = score,
                        Box = RectangleF.FromLTRB(data[o], data[o + 1], data[o + 2], data[o + 3])
                    });
                }
                return list;
            }

            // YOLOv8/11: [1, 4+nc, A] (A 가 더 큼). 드물게 [1, A, 4+nc] 로 전치된 모델도 처리
            bool transposed = d1 > d2;
            int channels = transposed ? d2 : d1;
            int anchors = transposed ? d1 : d2;
            int nc = channels - 4;
            if (nc <= 0)
                throw new NotSupportedException("지원하지 않는 출력 형식입니다: [" + string.Join(",", od) + "]");

            for (int a = 0; a < anchors; a++)
            {
                int best = -1;
                float bestScore = conf;
                for (int c = 0; c < nc; c++)
                {
                    float s = transposed ? data[a * channels + 4 + c] : data[(4 + c) * anchors + a];
                    if (s > bestScore)
                    {
                        bestScore = s;
                        best = c;
                    }
                }
                if (best < 0) continue;

                float cx, cy, w, h;
                if (transposed)
                {
                    int o = a * channels;
                    cx = data[o]; cy = data[o + 1]; w = data[o + 2]; h = data[o + 3];
                }
                else
                {
                    cx = data[a]; cy = data[anchors + a]; w = data[2 * anchors + a]; h = data[3 * anchors + a];
                }
                list.Add(new Detection
                {
                    ClassId = best,
                    Confidence = bestScore,
                    Box = new RectangleF(cx - w / 2f, cy - h / 2f, w, h)
                });
            }
            return list;
        }

        private static List<Detection> Nms(List<Detection> dets, float iouThreshold)
        {
            var sorted = dets.OrderByDescending(d => d.Confidence).ToList();
            var keep = new List<Detection>();
            var removed = new bool[sorted.Count];
            for (int i = 0; i < sorted.Count; i++)
            {
                if (removed[i]) continue;
                keep.Add(sorted[i]);
                for (int j = i + 1; j < sorted.Count; j++)
                {
                    if (!removed[j] && IoU(sorted[i].Box, sorted[j].Box) > iouThreshold)
                        removed[j] = true;
                }
            }
            return keep;
        }

        private static float IoU(RectangleF a, RectangleF b)
        {
            float x1 = Math.Max(a.Left, b.Left), y1 = Math.Max(a.Top, b.Top);
            float x2 = Math.Min(a.Right, b.Right), y2 = Math.Min(a.Bottom, b.Bottom);
            float inter = Math.Max(0, x2 - x1) * Math.Max(0, y2 - y1);
            float union = a.Width * a.Height + b.Width * b.Height - inter;
            return union <= 0 ? 0 : inter / union;
        }

        // ------------------------------------------------------------------ 메타데이터
        private static string[] LoadClassNames(string modelPath, Dictionary<string, string> meta)
        {
            // 1) 모델과 같은 이름의 .txt (한 줄에 클래스 하나) 가 있으면 우선 사용
            string txt = Path.ChangeExtension(modelPath, ".txt");
            if (File.Exists(txt))
            {
                string[] lines = File.ReadAllLines(txt).Select(l => l.Trim()).Where(l => l.Length > 0).ToArray();
                if (lines.Length > 0) return lines;
            }

            // 2) Ultralytics 가 ONNX 메타데이터에 넣어주는 names: "{0: '0', 1: '1', 10: 'A'}"
            string names = GetMeta(meta, "names");
            if (!string.IsNullOrEmpty(names))
            {
                var map = new SortedDictionary<int, string>();
                foreach (Match m in Regex.Matches(names, @"(\d+)\s*:\s*(?:'((?:[^'\\]|\\.)*)'|""((?:[^""\\]|\\.)*)"")"))
                {
                    int id = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                    map[id] = m.Groups[2].Success && m.Groups[2].Length > 0 ? m.Groups[2].Value : m.Groups[3].Value;
                }
                if (map.Count > 0)
                {
                    var arr = new string[map.Keys.Max() + 1];
                    for (int i = 0; i < arr.Length; i++)
                        arr[i] = map.ContainsKey(i) ? map[i] : i.ToString(CultureInfo.InvariantCulture);
                    return arr;
                }
            }

            return new string[0];
        }

        private static string GetMeta(Dictionary<string, string> meta, string key)
        {
            string v;
            return meta != null && meta.TryGetValue(key, out v) ? v : null;
        }

        private static int[] ParseIntList(string s)
        {
            if (string.IsNullOrEmpty(s)) return new int[0];
            return Regex.Matches(s, @"\d+").Cast<Match>()
                .Select(m => int.Parse(m.Value, CultureInfo.InvariantCulture)).ToArray();
        }

        private static float Clamp(float v, float lo, float hi)
        {
            return v < lo ? lo : (v > hi ? hi : v);
        }

        public void Dispose()
        {
            _session.Dispose();
        }
    }
}
