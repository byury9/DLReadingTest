using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using Cognex.VisionPro;
using Cognex.VisionPro.CalibFix;
using Cognex.VisionPro.ImageProcessing;
using Cognex.VisionPro.PMAlign;

namespace DLReadingTest.Yolo
{
    /// <summary>
    /// 박스(인쇄 영역) 1개의 검증 결과
    /// </summary>
    public sealed class CognexBoxResult
    {
        /// <summary>위치 순서 (0부터, 위→아래 / 왼쪽→오른쪽). 못 찾은 박스는 뒤에 붙습니다.</summary>
        public int Index { get; internal set; }

        /// <summary>cogPMalign.Results 의 인덱스 (-1 = PMAlign 이 못 찾음)</summary>
        public int PmIndex { get; internal set; }

        public bool Found { get { return PmIndex >= 0; } }

        public double PmScore { get; internal set; }
        public double PoseX { get; internal set; }
        public double PoseY { get; internal set; }
        public double PoseRotation { get; internal set; }

        /// <summary>YOLO 검증 결과 (박스 좌표는 크롭 이미지 픽셀 좌표). 실패 시 null</summary>
        public VerifyResult Verify { get; internal set; }

        /// <summary>실패 사유 (PMAlign 못 찾음, Fixture/크롭 실패, 추론 예외 등). 정상이면 null</summary>
        public string Error { get; internal set; }

        /// <summary>박스 OK = PMAlign 으로 찾았고, 모든 줄 / 모든 자리가 OK</summary>
        public bool Ok { get { return Found && Error == null && Verify != null && Verify.Ok; } }

        /// <summary>ROI 4 꼭짓점의 루트("@") 좌표: 원점, X쪽, 대각, Y쪽 순서</summary>
        public PointF[] RoiCorners { get; internal set; }

        /// <summary>크롭 이미지 크기 (픽셀)</summary>
        public Size CropSize { get; internal set; }

        /// <summary>CreateGraphics=true 일 때 만들어지는 그래픽 (루트 "@" 좌표)</summary>
        public CogGraphicCollection Graphics { get; internal set; }

        /// <summary>줄 n 의 표시 문자열 (문제 자리는 '_' = 없음, '?' = 점수 낮음). 줄이 없으면 ""</summary>
        public string GetLineText(int line)
        {
            return Verify != null && line >= 0 && line < Verify.Lines.Count ? Verify.Lines[line].DisplayText : "";
        }

        /// <summary>줄 n 의 OK 여부</summary>
        public bool GetLineOk(int line)
        {
            return Found && Error == null && Verify != null && line >= 0 && line < Verify.Lines.Count && Verify.Lines[line].Ok;
        }

        /// <summary>크롭 이미지 픽셀 좌표 → 루트("@") 좌표</summary>
        public PointF CropToRoot(float x, float y)
        {
            if (RoiCorners == null || CropSize.Width <= 0 || CropSize.Height <= 0) return PointF.Empty;
            PointF o = RoiCorners[0], cx = RoiCorners[1], cy = RoiCorners[3];
            float u = x / CropSize.Width, v = y / CropSize.Height;
            return new PointF(o.X + u * (cx.X - o.X) + v * (cy.X - o.X),
                              o.Y + u * (cx.Y - o.Y) + v * (cy.Y - o.Y));
        }
    }

    /// <summary>
    /// 이미지 1장의 검증 결과
    /// </summary>
    public sealed class CognexYoloResult
    {
        public List<CognexBoxResult> Boxes { get; internal set; }
        public int ExpectedBoxCount { get; internal set; }
        public int FoundCount { get { return Boxes.Count(b => b.Found); } }

        /// <summary>전체 OK = 박스를 모두 찾았고 모든 박스가 OK</summary>
        public bool Ok { get { return Boxes.Count >= ExpectedBoxCount && Boxes.All(b => b.Ok); } }

        public long CropMs { get; internal set; }
        public long InferMs { get; internal set; }
        public long TotalMs { get; internal set; }
    }

    /// <summary>
    /// CogPMAlignTool + CogFixtureTool 로 찾은 박스마다 ROI 를 잘라 YOLO 로 문자 검증(OCV)을 수행합니다.
    /// <para>
    /// 순서: ① PMAlign 결과 정렬(위치 순) → ② 박스마다 Fixture + AffineTransform 으로 회전 보정 크롭 (호출 스레드)
    /// → ③ YOLO 검출 + TextVerifier 검증 (박스별 병렬) → ④ 결과 그래픽 생성 (호출 스레드)
    /// </para>
    /// <para>
    /// Cognex 툴은 스레드 안전하지 않으므로 ②, ④ 는 Run 을 호출한 스레드에서만 실행합니다.
    /// Run 은 내부 lock 으로 한 번에 하나만 실행되므로, 카메라가 여러 대면 카메라마다 인스턴스를 만드세요
    /// (YoloDetector 는 생성자로 넘겨서 공유할 수 있습니다).
    /// </para>
    /// </summary>
    public sealed class CognexYoloVerifier : IDisposable
    {
        private const float NmsIou = 0.5f;

        private readonly YoloDetector _detector;
        private readonly bool _ownsDetector;
        private readonly CogAffineTransformTool _affine = new CogAffineTransformTool();
        private readonly CogFixtureTool _fixture = new CogFixtureTool();   // 내부 전용 (Job 툴 상태를 건드리지 않음)
        private readonly object _runLock = new object();

        private string _expectedText;
        private string[] _expectedLines;

        /// <summary>모델 파일로 생성 (Dispose 시 모델도 해제)</summary>
        /// <param name="modelPath">ONNX 모델 경로</param>
        /// <param name="labelMap">결과 글자 치환 (기본 "O:0" = 영문 O 를 숫자 0 으로)</param>
        public CognexYoloVerifier(string modelPath, string labelMap = "O:0")
        {
            _detector = new YoloDetector(modelPath);
            _detector.LabelMap = YoloDetector.ParseLabelMap(labelMap);
            _ownsDetector = true;
            Init();
        }

        /// <summary>이미 만든 YoloDetector 를 공유해서 생성 (Dispose 해도 detector 는 해제하지 않음)</summary>
        public CognexYoloVerifier(YoloDetector detector)
        {
            if (detector == null) throw new ArgumentNullException("detector");
            _detector = detector;
            _ownsDetector = false;
            Init();
        }

        private void Init()
        {
            Options = new VerifyOptions();
            ExpectedBoxCount = 10;
            SortByPosition = true;
            CreateGraphics = true;
            MaxParallel = Math.Max(1, Environment.ProcessorCount / 2);
            FixtureSpaceName = "YoloFixture";
        }

        /// <summary>
        /// 내부 Fixture 툴의 좌표계 이름 (기본 "YoloFixture").
        /// <para>
        /// Job 의 Fixture 와 같은 이름("Fixture" 등)을 쓰면, 검사 이미지에 그 이름의 좌표계가 마지막 박스 Pose 로 남아서
        /// 같은 좌표계 이름으로 남아 있던 다른 그래픽(예전 OCRMax 결과 등)이 마지막 박스 위치에 그려질 수 있습니다.
        /// </para>
        /// </summary>
        public string FixtureSpaceName
        {
            get { return _fixture.RunParams.FixturedSpaceName; }
            set { _fixture.RunParams.FixturedSpaceName = value; }
        }

        public YoloDetector Detector { get { return _detector; } }

        /// <summary>검증 기준 (PassScore 기본 0.10 등). Run 시작 시점 값이 사용됩니다.</summary>
        public VerifyOptions Options { get; private set; }

        /// <summary>찾아야 할 박스 수 (기본 10). PMAlign 이 이보다 적게 찾으면 부족분은 NG 박스로 추가됩니다.</summary>
        public int ExpectedBoxCount { get; set; }

        /// <summary>결과를 위치 순(위→아래, 왼쪽→오른쪽)으로 정렬 (기본 true). false 면 PMAlign 점수 순</summary>
        public bool SortByPosition { get; set; }

        /// <summary>같은 줄(행)로 볼 Y 차이 (루트 좌표, 0 = 자동: 이웃 박스 간격의 절반)</summary>
        public double RowTolerance { get; set; }

        /// <summary>결과 그래픽 생성 여부 (기본 true)</summary>
        public bool CreateGraphics { get; set; }

        /// <summary>동시에 추론할 박스 수 (기본 코어 수 / 2)</summary>
        public int MaxParallel { get; set; }

        /// <summary>
        /// 검사 실행
        /// </summary>
        /// <param name="image">검사 이미지 (예: job.IPOneImageTool.OutputImage)</param>
        /// <param name="pmResults">실행이 끝난 cogPMalign.Results</param>
        /// <param name="roi">박스 1개 기준 ROI (Fixture 좌표계). 제조번호 + 날짜 2줄을 모두 포함하도록 잡으세요.</param>
        /// <param name="expectedText">기대 문자열. 줄은 공백 / 줄바꿈 / ',' / '|' 로 구분 (예: "GWTC054P 2031.07.07")</param>
        public CognexYoloResult Run(ICogImage image, CogPMAlignResults pmResults, CogRectangleAffine roi, string expectedText)
        {
            return Run(image, pmResults, null, roi, expectedText);
        }

        /// <summary>
        /// 검사 실행 (Fixture 툴 지정)
        /// </summary>
        /// <param name="fixture">사용할 Fixture 툴. null 이면 내부 전용 Fixture (권장). Job 의 Fixture 를 넘기면 위 FixtureSpaceName 설명의 문제가 생길 수 있습니다.</param>
        public CognexYoloResult Run(ICogImage image, CogPMAlignResults pmResults, CogFixtureTool fixture,
                                    CogRectangleAffine roi, string expectedText)
        {
            if (image == null) throw new ArgumentNullException("image");
            if (roi == null) throw new ArgumentNullException("roi");
            if (fixture == null) fixture = _fixture;

            lock (_runLock)
            {
                Stopwatch total = Stopwatch.StartNew();
                string[] expected = GetExpectedLines(expectedText);
                VerifyOptions opts = new VerifyOptions
                {
                    PassScore = Options.PassScore,
                    CandidateScore = Options.CandidateScore,
                    WrongScore = Options.WrongScore
                };

                // ① PMAlign 결과 (점수 순) 중 상위 ExpectedBoxCount 개 → 위치 순 정렬
                List<CognexBoxResult> boxes = PickResults(pmResults);
                if (SortByPosition) boxes = SortRowsThenColumns(boxes);

                // ② 박스마다 회전 보정 크롭 (Cognex: 호출 스레드)
                Stopwatch sw = Stopwatch.StartNew();
                var work = new List<KeyValuePair<CognexBoxResult, Bitmap>>();
                fixture.InputImage = image;
                foreach (CognexBoxResult box in boxes)
                {
                    Bitmap bmp = Crop(pmResults[box.PmIndex].GetPose(), fixture, roi, box);
                    if (bmp != null) work.Add(new KeyValuePair<CognexBoxResult, Bitmap>(box, bmp));
                }
                long cropMs = sw.ElapsedMilliseconds;

                // ③ YOLO 검출 + 검증 (박스별 병렬, Cognex 객체 사용 안 함)
                sw.Restart();
                Parallel.ForEach(work, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, MaxParallel) }, w =>
                {
                    try
                    {
                        List<Detection> dets = _detector.Detect(w.Value, Point.Empty, opts.CandidateScore, NmsIou);
                        w.Key.Verify = TextVerifier.Verify(dets, expected, _detector.ClassNames, _detector.LabelMap, opts);
                    }
                    catch (Exception ex)
                    {
                        w.Key.Error = "YOLO 검증 실패: " + ex.Message;
                    }
                    finally
                    {
                        w.Value.Dispose();
                    }
                });
                long inferMs = sw.ElapsedMilliseconds;

                // 못 찾은 박스는 NG 로 추가 (어느 자리가 빠졌는지는 알 수 없어서 뒤에 붙임)
                for (int k = boxes.Count; k < ExpectedBoxCount; k++)
                    boxes.Add(new CognexBoxResult { PmIndex = -1, Error = "기준 위치(PMAlign) 못 찾음" });
                for (int k = 0; k < boxes.Count; k++) boxes[k].Index = k;

                // ④ 그래픽 (Cognex: 호출 스레드)
                if (CreateGraphics)
                    foreach (CognexBoxResult box in boxes)
                        if (box.Found && box.RoiCorners != null) box.Graphics = BuildGraphics(box);

                return new CognexYoloResult
                {
                    Boxes = boxes,
                    ExpectedBoxCount = ExpectedBoxCount,
                    CropMs = cropMs,
                    InferMs = inferMs,
                    TotalMs = total.ElapsedMilliseconds
                };
            }
        }

        // ------------------------------------------------------------------ ① 결과 선택 / 정렬
        private List<CognexBoxResult> PickResults(CogPMAlignResults pmResults)
        {
            var list = new List<CognexBoxResult>();
            int n = pmResults == null ? 0 : Math.Min(pmResults.Count, ExpectedBoxCount);
            for (int i = 0; i < n; i++)
            {
                CogTransform2DLinear pose = pmResults[i].GetPose();
                list.Add(new CognexBoxResult
                {
                    PmIndex = i,
                    PmScore = pmResults[i].Score,
                    PoseX = pose.TranslationX,
                    PoseY = pose.TranslationY,
                    PoseRotation = pose.Rotation
                });
            }
            return list;
        }

        /// <summary>Y 로 행을 나눈 뒤 행 안에서 X 순으로 정렬</summary>
        private List<CognexBoxResult> SortRowsThenColumns(List<CognexBoxResult> boxes)
        {
            if (boxes.Count < 2) return boxes;

            double tol = RowTolerance;
            if (tol <= 0)
            {
                // 자동: 각 박스에서 가장 가까운 박스까지 거리의 중앙값 / 2
                var nearest = new List<double>();
                foreach (CognexBoxResult a in boxes)
                {
                    double best = double.MaxValue;
                    foreach (CognexBoxResult b in boxes)
                    {
                        if (ReferenceEquals(a, b)) continue;
                        double dx = a.PoseX - b.PoseX, dy = a.PoseY - b.PoseY;
                        best = Math.Min(best, Math.Sqrt(dx * dx + dy * dy));
                    }
                    nearest.Add(best);
                }
                nearest.Sort();
                tol = nearest[nearest.Count / 2] * 0.5;
            }

            var sorted = new List<CognexBoxResult>();
            var row = new List<CognexBoxResult>();
            double prevY = double.NaN;
            foreach (CognexBoxResult b in boxes.OrderBy(b => b.PoseY))
            {
                if (row.Count > 0 && b.PoseY - prevY > tol)
                {
                    sorted.AddRange(row.OrderBy(r => r.PoseX));
                    row.Clear();
                }
                row.Add(b);
                prevY = b.PoseY;
            }
            sorted.AddRange(row.OrderBy(r => r.PoseX));
            return sorted;
        }

        // ------------------------------------------------------------------ ② 크롭
        private Bitmap Crop(CogTransform2DLinear pose, CogFixtureTool fixture, CogRectangleAffine roi, CognexBoxResult box)
        {
            try
            {
                fixture.RunParams.UnfixturedFromFixturedTransform = pose;
                fixture.Run();
                ICogImage fimg = fixture.OutputImage;
                if (fimg == null)
                {
                    box.Error = "Fixture 실패: " + RunMessage(fixture.RunStatus);
                    return null;
                }

                string space = fimg.SelectedSpaceName;
                var region = (CogRectangleAffine)roi.Copy(CogCopyShapeConstants.GeometryOnly);
                region.SelectedSpaceName = space;

                // ROI 꼭짓점을 루트 좌표로 기록 (그래픽 / 좌표 변환용, 스레드와 무관한 숫자로 보관)
                ICogTransform2D toRoot = fimg.GetTransform("@", space);
                PointF o = Map(toRoot, region.CornerOriginX, region.CornerOriginY);
                PointF cx = Map(toRoot, region.CornerXX, region.CornerXY);
                PointF cy = Map(toRoot, region.CornerYX, region.CornerYY);
                box.RoiCorners = new[] { o, cx, new PointF(cx.X + cy.X - o.X, cx.Y + cy.Y - o.Y), cy };

                // ROI 를 회전 보정해서 직사각형 이미지로
                _affine.InputImage = fimg;
                _affine.Region = region;
                _affine.Run();
                ICogImage crop = _affine.OutputImage;
                if (crop == null)
                {
                    box.Error = "ROI 크롭 실패: " + RunMessage(_affine.RunStatus);
                    return null;
                }

                Bitmap bmp = crop.ToBitmap();
                box.CropSize = new Size(bmp.Width, bmp.Height);
                return bmp;
            }
            catch (Exception ex)
            {
                box.Error = "크롭 예외: " + ex.Message;
                return null;
            }
        }

        private static PointF Map(ICogTransform2D t, double x, double y)
        {
            double mx, my;
            t.MapPoint(x, y, out mx, out my);
            return new PointF((float)mx, (float)my);
        }

        private static string RunMessage(ICogRunStatus status)
        {
            return status == null ? "" : status.Message;
        }

        // ------------------------------------------------------------------ ④ 그래픽
        private static CogGraphicCollection BuildGraphics(CognexBoxResult box)
        {
            var col = new CogGraphicCollection();

            // ROI 테두리: 박스 OK 초록 / NG 빨강
            col.Add(MakePolygon(box.RoiCorners, box.Ok ? CogColorConstants.Green : CogColorConstants.Red, false, 2));

            if (box.Verify == null)
            {
                if (box.Error != null) col.Add(MakeLabel(box.RoiCorners[0], box.Error, CogColorConstants.Red));
                return col;
            }

            foreach (CharResult c in box.Verify.AllChars)
            {
                if (c.Box.Width <= 0 || c.Box.Height <= 0) continue;

                CogColorConstants color = c.Status == CharStatus.Ok ? CogColorConstants.Green
                                        : c.Status == CharStatus.Low ? CogColorConstants.Yellow
                                        : CogColorConstants.Red; // Wrong / Missing

                RectangleF r = c.Box;
                PointF[] pts =
                {
                    box.CropToRoot(r.Left, r.Top),
                    box.CropToRoot(r.Right, r.Top),
                    box.CropToRoot(r.Right, r.Bottom),
                    box.CropToRoot(r.Left, r.Bottom)
                };
                col.Add(MakePolygon(pts, color, c.Estimated, 1)); // 점선 = 글자를 못 찾아 위치를 추정한 자리

                string text = c.Status == CharStatus.Ok
                    ? string.Format("{0} {1:0.00}", c.Expected, c.Score)
                    : string.Format("{0}:{1} {2:0.00}", c.Expected, c.ReadLabel ?? "-", c.Score);
                col.Add(MakeLabel(pts[0], text, color));
            }
            return col;
        }

        private static CogPolygon MakePolygon(PointF[] pts, CogColorConstants color, bool dashed, int width)
        {
            var poly = new CogPolygon();
            poly.NumVertices = pts.Length;
            for (int k = 0; k < pts.Length; k++) poly.SetVertex(k, pts[k].X, pts[k].Y);
            poly.Color = color;
            poly.LineWidthInScreenPixels = width;
            if (dashed) poly.LineStyle = CogGraphicLineStyleConstants.Dash;
            poly.SelectedSpaceName = "@";
            return poly;
        }

        private static CogGraphicLabel MakeLabel(PointF at, string text, CogColorConstants color)
        {
            var label = new CogGraphicLabel();
            label.SetXYText(at.X, at.Y, text);
            label.Alignment = CogGraphicLabelAlignmentConstants.BottomLeft;
            label.Color = color;
            label.SelectedSpaceName = "@";
            return label;
        }

        // ------------------------------------------------------------------ 기타
        private string[] GetExpectedLines(string expectedText)
        {
            expectedText = expectedText ?? "";
            if (_expectedLines == null || _expectedText != expectedText)
            {
                _expectedLines = TextVerifier.ParseExpected(expectedText, _detector.ClassNames, _detector.LabelMap);
                _expectedText = expectedText;
            }
            return _expectedLines;
        }

        public void Dispose()
        {
            _affine.Dispose();
            _fixture.Dispose();
            if (_ownsDetector) _detector.Dispose();
        }
    }
}
