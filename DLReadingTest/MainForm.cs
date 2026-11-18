using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using DLReadingTest.Controls;
using DLReadingTest.Yolo;

namespace DLReadingTest
{
    public partial class MainForm : Form
    {
        private static readonly HashSet<string> ImageExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".bmp", ".jpg", ".jpeg", ".png", ".tif", ".tiff", ".gif"
        };

        private const float NmsIou = 0.5f;

        private AppSettings _settings;
        private YoloDetector _detector;
        private List<string> _files = new List<string>();
        private int _index = -1;
        private Bitmap _current;
        private bool _busy;

        // 마지막으로 누른 동작 (자동 실행 시 반복): false = 읽기, true = 검증
        private bool _verifyMode;
        private readonly VerifyOptions _verifyOptions = new VerifyOptions();

        // 마지막 검증의 ROI 별 글자 후보 (검증 기준만 바꿨을 때 추론 없이 바로 다시 판정하기 위해 보관)
        private RoiRun[] _lastRuns;
        private Bitmap _lastRunsImage;
        private Rectangle[] _lastRunsRois;
        private YoloDetector _lastRunsDetector;

        public MainForm()
        {
            InitializeComponent();
            // x64 / x86 빌드 구분 (DLReadingTest.x86 프로젝트는 32비트로 실행됨)
            Text += Environment.Is64BitProcess ? " [x64]" : " [x86]";
        }

        // ================================================================== 시작 / 종료
        private void MainForm_Load(object sender, EventArgs e)
        {
            _settings = AppSettings.Load();
            numInterval.Value = Math.Max(numInterval.Minimum, Math.Min(numInterval.Maximum, _settings.IntervalMs));
            numConf.Value = Math.Max(numConf.Minimum, Math.Min(numConf.Maximum, _settings.Confidence));
            chkAutoRead.Checked = _settings.AutoRead;
            txtExpected.Text = _settings.ExpectedText ?? "";
            new ToolTip(components).SetToolTip(txtExpected,
                "인쇄 내용을 줄 순서대로 입력 (줄은 공백이나 쉼표로 구분)\r\n예: GWTB017P 2030.03.16\r\n'.' 은 무시, 영문 O 는 숫자 0 으로 봄");
            numPass.Value = Math.Max(numPass.Minimum, Math.Min(numPass.Maximum, (decimal)_settings.VerifyPassScore));
            _verifyOptions.PassScore = (float)numPass.Value;
            timerRotate.Interval = (int)numInterval.Value;

            // 모델: 설정에 저장된 경로 -> 실행 폴더\Models\*.onnx 순서로 찾음
            string model = _settings.ModelPath;
            if (string.IsNullOrEmpty(model) || !File.Exists(model))
            {
                string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Models");
                model = Directory.Exists(dir) ? Directory.GetFiles(dir, "*.onnx").OrderBy(f => f).FirstOrDefault() : null;
            }
            if (model != null) LoadModel(model, showError: false);

            viewer.SetRois(_settings.Rois);
            if (!string.IsNullOrEmpty(_settings.LastFolder) && Directory.Exists(_settings.LastFolder))
                LoadFolder(_settings.LastFolder);

            UpdateButtons();
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            timerRotate.Stop();
            _settings.IntervalMs = (int)numInterval.Value;
            _settings.Confidence = numConf.Value;
            _settings.AutoRead = chkAutoRead.Checked;
            _settings.ExpectedText = txtExpected.Text;
            _settings.VerifyPassScore = (float)numPass.Value;
            _settings.Rois = viewer.Rois.ToList();
            _settings.Save();

            viewer.Image = null;
            if (_current != null) _current.Dispose();
            if (_detector != null) _detector.Dispose();
        }

        // ================================================================== 모델
        private void btnOpenModel_Click(object sender, EventArgs e)
        {
            using (var dlg = new OpenFileDialog())
            {
                dlg.Title = "YOLO ONNX 모델 선택";
                dlg.Filter = "ONNX 모델 (*.onnx)|*.onnx|모든 파일 (*.*)|*.*";
                if (_detector != null) dlg.InitialDirectory = Path.GetDirectoryName(_detector.ModelPath);
                if (dlg.ShowDialog(this) == DialogResult.OK) LoadModel(dlg.FileName, showError: true);
            }
        }

        private void LoadModel(string path, bool showError)
        {
            if (_busy) return;
            try
            {
                Cursor = Cursors.WaitCursor;
                var det = new YoloDetector(path);
                det.LabelMap = YoloDetector.ParseLabelMap(_settings.LabelMap);
                if (_detector != null) _detector.Dispose();
                _detector = det;
                _settings.ModelPath = path;
                lblStatusModel.Text = string.Format("모델: {0} | 입력 {1}x{2} | 클래스 {3}개",
                    Path.GetFileName(path), det.InputWidth, det.InputHeight, det.ClassNames.Length);
                lblStatusModel.ToolTipText = det.Description;
            }
            catch (Exception ex)
            {
                lblStatusModel.Text = "모델 로드 실패";
                if (showError)
                    MessageBox.Show(this, "모델을 불러오지 못했습니다.\r\n\r\n" + ex.Message, "모델 오류",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                Cursor = Cursors.Default;
                UpdateButtons();
            }
        }

        // ================================================================== 폴더 / 이미지 순환
        private void btnOpenFolder_Click(object sender, EventArgs e)
        {
            using (var dlg = new FolderBrowserDialog())
            {
                dlg.Description = "이미지가 들어 있는 폴더를 선택하세요";
                dlg.ShowNewFolderButton = false;
                if (!string.IsNullOrEmpty(_settings.LastFolder) && Directory.Exists(_settings.LastFolder))
                    dlg.SelectedPath = _settings.LastFolder;
                if (dlg.ShowDialog(this) == DialogResult.OK) LoadFolder(dlg.SelectedPath);
            }
        }

        private void LoadFolder(string folder)
        {
            StopPlay();
            List<string> files;
            try
            {
                files = Directory.GetFiles(folder)
                    .Where(f => ImageExtensions.Contains(Path.GetExtension(f)))
                    .ToList();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "폴더 오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            files.Sort(NaturalCompare); // 탐색기와 같은 순서 (img2 < img10)

            _settings.LastFolder = folder;
            _files = files;
            _index = -1;
            if (_files.Count == 0)
            {
                SetImage(null);
                lblStatusFile.Text = "이미지 없음: " + folder;
                ClearResults();
            }
            else
            {
                ShowImage(0);
            }
            UpdateButtons();
        }

        private async void ShowImage(int index)
        {
            if (_files.Count == 0) return;
            index = ((index % _files.Count) + _files.Count) % _files.Count; // 처음/끝에서 순환
            string path = _files[index];

            Bitmap bmp;
            try
            {
                bmp = LoadBitmap(path);
            }
            catch (Exception ex)
            {
                _index = index;
                SetImage(null);
                ClearResults();
                lblStatusFile.Text = string.Format("[{0}/{1}] {2} - 열기 실패: {3}", index + 1, _files.Count, Path.GetFileName(path), ex.Message);
                return;
            }

            _index = index;
            SetImage(bmp);
            ClearResults();
            lblStatusFile.Text = string.Format("[{0}/{1}] {2}", index + 1, _files.Count, Path.GetFileName(path));
            lblStatusSize.Text = string.Format("{0} x {1}", bmp.Width, bmp.Height);
            UpdateRoiStatus();

            if (chkAutoRead.Checked && _detector != null)
                await RunCurrentModeAsync();
        }

        /// <summary>파일을 잠그지 않도록 메모리로 복사해서 연다.</summary>
        private static Bitmap LoadBitmap(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            using (var ms = new MemoryStream(bytes))
            using (Image img = Image.FromStream(ms))
            {
                var bmp = new Bitmap(img.Width, img.Height, PixelFormat.Format24bppRgb);
                bmp.SetResolution(96, 96);
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.White);
                    g.DrawImage(img, 0, 0, img.Width, img.Height);
                }
                return bmp;
            }
        }

        private void SetImage(Bitmap bmp)
        {
            Bitmap old = _current;
            _current = bmp;
            viewer.Image = bmp;
            if (old != null) old.Dispose();
            if (bmp == null) lblStatusSize.Text = "";
        }

        private void btnPrev_Click(object sender, EventArgs e)
        {
            StopPlay();
            ShowImage(_index - 1);
        }

        private void btnNext_Click(object sender, EventArgs e)
        {
            StopPlay();
            ShowImage(_index + 1);
        }

        private void btnPlay_Click(object sender, EventArgs e)
        {
            if (timerRotate.Enabled) StopPlay();
            else StartPlay();
        }

        private void StartPlay()
        {
            if (_files.Count == 0) return;
            timerRotate.Interval = (int)numInterval.Value;
            timerRotate.Start();
            btnPlay.Text = "❚❚ 정지";
        }

        private void StopPlay()
        {
            timerRotate.Stop();
            btnPlay.Text = "▶ 재생";
        }

        private void timerRotate_Tick(object sender, EventArgs e)
        {
            if (_busy) return; // 이전 이미지 읽기가 끝나지 않았으면 이번 틱은 건너뜀
            ShowImage(_index + 1);
        }

        private void numInterval_ValueChanged(object sender, EventArgs e)
        {
            timerRotate.Interval = (int)numInterval.Value;
        }

        // ================================================================== ROI
        /// <summary>ROI 추가: 기본 영역 (10+n, 10+n, 100, 100), n = 기존 ROI 개수 x 10 (겹치지 않게 조금씩 비켜서 생성)</summary>
        private void btnAddRoi_Click(object sender, EventArgs e)
        {
            int n = viewer.Rois.Count * 10;
            var roi = new Rectangle(10 + n, 10 + n, 100, 100);
            if (_current != null)
            {
                // 이미지가 작으면 이미지 안으로
                roi.Width = Math.Min(roi.Width, _current.Width);
                roi.Height = Math.Min(roi.Height, _current.Height);
                roi.X = Math.Max(0, Math.Min(roi.X, _current.Width - roi.Width));
                roi.Y = Math.Max(0, Math.Min(roi.Y, _current.Height - roi.Height));
            }
            viewer.AddRoi(roi);
            viewer.Focus();
        }

        private void btnDeleteRoi_Click(object sender, EventArgs e)
        {
            viewer.DeleteSelectedRoi();
        }

        private void viewer_RoisChanged(object sender, EventArgs e)
        {
            UpdateRoiStatus();
            if (_settings != null) _settings.Rois = viewer.Rois.ToList();
            ClearResults();
        }

        private void viewer_SelectedRoiChanged(object sender, EventArgs e)
        {
            UpdateRoiStatus();
            UpdateButtons();
        }

        private void UpdateRoiStatus()
        {
            IReadOnlyList<Rectangle> rois = viewer.Rois;
            if (rois.Count == 0)
            {
                lblStatusRoi.Text = "ROI: 전체";
                return;
            }
            int sel = viewer.SelectedRoiIndex;
            lblStatusRoi.Text = sel < 0
                ? string.Format("ROI {0}개", rois.Count)
                : string.Format("ROI {0}개 | 선택 {1}: {2},{3} {4}x{5}", rois.Count, sel + 1,
                    rois[sel].X, rois[sel].Y, rois[sel].Width, rois[sel].Height);
        }

        private void viewer_ImageMouseMove(object sender, ImageMouseEventArgs e)
        {
            lblStatusMouse.Text = e.InsideImage ? string.Format("X {0}, Y {1}", e.Location.X, e.Location.Y) : "";
        }

        // ================================================================== 읽기 / 검증
        private async void btnRead_Click(object sender, EventArgs e)
        {
            _verifyMode = false;
            await ReadAsync();
        }

        private async void btnVerify_Click(object sender, EventArgs e)
        {
            _verifyMode = true;
            await VerifyAsync();
        }

        /// <summary>자동 실행(이미지 바뀔 때)은 마지막으로 누른 동작(읽기/검증)을 반복한다.</summary>
        private Task RunCurrentModeAsync()
        {
            return _verifyMode ? VerifyAsync() : ReadAsync();
        }

        /// <summary>ROI 하나의 처리 결과</summary>
        private sealed class RoiRun
        {
            /// <summary>ROI 번호 (0부터). -1 = ROI 가 없어서 전체 이미지</summary>
            public int Index;
            /// <summary>실제 처리 영역 (이미지 안으로 잘린 ROI)</summary>
            public Rectangle Region;
            public List<Detection> Candidates;
            // 읽기 결과
            public string[] Lines;
            public List<Detection> Ordered;
            // 검증 결과
            public VerifyResult Verify;

            public string Name { get { return Index < 0 ? "전체" : (Index + 1).ToString(CultureInfo.InvariantCulture); } }
        }

        // ------------------------------------------------------------------ 읽기
        private async Task ReadAsync()
        {
            RoiRun[] runs = await RunPerRoiAsync((float)numConf.Value, run =>
            {
                List<Detection> ordered;
                run.Lines = TextAssembler.Assemble(run.Candidates, out ordered);
                run.Ordered = ordered;
            });
            if (runs != null) ShowReadResults(runs);
        }

        // ------------------------------------------------------------------ 검증
        private async Task VerifyAsync()
        {
            if (_busy || _current == null) return;
            if (_detector == null)
            {
                await RunPerRoiAsync(0f, null); // 모델 없음 안내
                return;
            }
            string[] expected = TextVerifier.ParseExpected(txtExpected.Text, _detector.ClassNames, _detector.LabelMap);
            if (expected.Length == 0)
            {
                StopPlay();
                MessageBox.Show(this, "검증할 문자를 입력하세요.\r\n줄은 공백이나 쉼표(,)로 구분합니다.\r\n\r\n예: GWTB017P 2030.03.16",
                    "검증할 문자 없음", MessageBoxButtons.OK, MessageBoxIcon.Information);
                txtExpected.Focus();
                return;
            }

            // 작업 스레드들이 같은 값을 보도록 기준값을 복사해서 넘김 (실행 중 화면에서 바꿔도 영향 없음)
            YoloDetector detector = _detector;
            VerifyOptions opt = CopyVerifyOptions();
            Rectangle[] rois = viewer.Rois.ToArray();
            // 검증은 기대 글자의 점수가 낮아도 찾을 수 있도록 후보를 낮은 기준으로 넓게 검출
            RoiRun[] runs = await RunPerRoiAsync(opt.CandidateScore, run =>
                run.Verify = TextVerifier.Verify(run.Candidates, expected, detector.ClassNames, detector.LabelMap, opt));
            if (runs == null) return;

            _lastRuns = runs;
            _lastRunsImage = _current;
            _lastRunsRois = rois;
            _lastRunsDetector = detector;
            ShowVerifyResults(runs);
        }

        private VerifyOptions CopyVerifyOptions()
        {
            return new VerifyOptions
            {
                PassScore = _verifyOptions.PassScore,
                CandidateScore = _verifyOptions.CandidateScore,
                WrongScore = _verifyOptions.WrongScore
            };
        }

        /// <summary>검증 기준 변경: 화면의 마지막 검증 결과를 추론 없이 새 기준으로 다시 판정</summary>
        private void numPass_ValueChanged(object sender, EventArgs e)
        {
            _verifyOptions.PassScore = (float)numPass.Value;
            if (!_verifyMode || _busy || _detector == null || _lastRuns == null) return;
            if (!ReferenceEquals(_lastRunsImage, _current) || !ReferenceEquals(_lastRunsDetector, _detector)) return;
            if (!viewer.Rois.SequenceEqual(_lastRunsRois)) return;
            string[] expected = TextVerifier.ParseExpected(txtExpected.Text, _detector.ClassNames, _detector.LabelMap);
            if (expected.Length == 0) return;
            VerifyOptions opt = CopyVerifyOptions();
            foreach (RoiRun run in _lastRuns)
                run.Verify = TextVerifier.Verify(run.Candidates, expected, _detector.ClassNames, _detector.LabelMap, opt);
            ShowVerifyResults(_lastRuns);
        }

        /// <summary>
        /// ROI 마다 작업을 하나씩 만들어 동시에 실행한다 (ROI 가 없으면 전체 이미지 하나).
        ///  1. UI 스레드에서 ROI 별로 이미지를 잘라 둔다 (같은 Bitmap 을 여러 스레드가 동시에 만지면 GDI+ 예외가 나므로)
        ///  2. ROI 별 Task.Run 으로 검출(YoloDetector.Detect, 세션 하나를 공유해도 안전) + 후처리(post)를 병렬 실행
        ///  3. 모두 끝나면 결과 배열을 돌려준다. 실패했거나 처리 중 이미지가 바뀌면 null.
        /// </summary>
        private async Task<RoiRun[]> RunPerRoiAsync(float conf, Action<RoiRun> post)
        {
            if (_busy || _current == null) return null;
            if (_detector == null)
            {
                MessageBox.Show(this, "먼저 [모델 열기] 로 YOLO ONNX 모델을 불러오세요.", "모델 없음",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return null;
            }

            var full = new Rectangle(0, 0, _current.Width, _current.Height);
            IReadOnlyList<Rectangle> rois = viewer.Rois;
            var runs = new List<RoiRun>();
            if (rois.Count == 0)
                runs.Add(new RoiRun { Index = -1, Region = full });
            else
                for (int i = 0; i < rois.Count; i++)
                    runs.Add(new RoiRun { Index = i, Region = Rectangle.Intersect(rois[i], full) });

            Bitmap[] crops = runs.Select(r => r.Region.Width >= 2 && r.Region.Height >= 2
                ? _current.Clone(r.Region, PixelFormat.Format24bppRgb)
                : null).ToArray(); // 이미지 밖에 있는 ROI 는 null (결과 없음)
            Bitmap source = _current;
            YoloDetector detector = _detector;

            _busy = true;
            UpdateButtons();
            var sw = Stopwatch.StartNew();
            try
            {
                Task[] tasks = runs.Select((run, i) => Task.Run(() =>
                {
                    Bitmap crop = crops[i];
                    if (crop == null)
                    {
                        run.Candidates = new List<Detection>();
                    }
                    else
                    {
                        using (crop) run.Candidates = detector.Detect(crop, run.Region.Location, conf, NmsIou);
                    }
                    if (post != null) post(run);
                })).ToArray();
                await Task.WhenAll(tasks);
                sw.Stop();

                if (!ReferenceEquals(source, _current)) return null; // 처리하는 동안 이미지가 바뀜
                lblStatusTime.Text = runs.Count > 1
                    ? string.Format("{0} ms (ROI {1}개 동시)", sw.ElapsedMilliseconds, runs.Count)
                    : string.Format("{0} ms", sw.ElapsedMilliseconds);
                return runs.ToArray();
            }
            catch (Exception ex)
            {
                StopPlay();
                lblStatusTime.Text = "읽기 실패";
                MessageBox.Show(this, ex.Message, "추론 오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return null;
            }
            finally
            {
                _busy = false;
                UpdateButtons();
            }
        }

        /// <summary>결과창 글꼴: ROI 가 여러 개면 작게 해서 더 많은 줄이 보이게</summary>
        private void SetResultFont(bool small)
        {
            float size = small ? 12f : 18f;
            if (Math.Abs(txtResult.Font.Size - size) > 0.1f)
                txtResult.Font = new Font(txtResult.Font.FontFamily, size, FontStyle.Bold);
        }

        private void ShowReadResults(RoiRun[] runs)
        {
            bool multi = runs.Length > 1;
            var text = new List<string>();
            var shown = new List<Detection>();
            gridResult.SuspendLayout();
            gridResult.Rows.Clear();
            foreach (RoiRun run in runs)
            {
                // ROI 가 여러 개면 줄 앞에 ROI 번호 (R1, R2 ...)
                if (run.Lines.Length == 0)
                    text.Add(multi ? string.Format("R{0} (없음)", run.Name) : "(인식된 문자 없음)");
                for (int k = 0; k < run.Lines.Length; k++)
                    text.Add(multi ? (k == 0 ? "R" + run.Name + " " : new string(' ', run.Name.Length + 2)) + run.Lines[k] : run.Lines[k]);

                foreach (Detection d in run.Ordered)
                {
                    shown.Add(d);
                    gridResult.Rows.Add(
                        shown.Count,
                        run.Name,
                        d.Line + 1,
                        d.Label,
                        d.Confidence.ToString("0.000", CultureInfo.InvariantCulture),
                        Math.Round(d.Box.X),
                        Math.Round(d.Box.Y),
                        Math.Round(d.Box.Width),
                        Math.Round(d.Box.Height),
                        "",
                        "");
                }
            }
            viewer.Detections = shown;
            SetResultFont(multi);
            grpResult.Text = "읽은 결과";
            txtResult.ForeColor = Color.Lime;
            txtResult.Text = string.Join(Environment.NewLine, text);
            gridResult.ClearSelection();
            gridResult.ResumeLayout();
        }

        private void ShowVerifyResults(RoiRun[] runs)
        {
            bool multi = runs.Length > 1;
            int okCount = runs.Count(r => r.Verify.Ok);
            bool allOk = okCount == runs.Length;

            // 결과 문자열: OK 자리는 기대 문자, 다른 문자는 읽은 글자, 점수 낮음은 ?, 없음은 _
            var text = new List<string>();
            if (multi)
            {
                text.Add(string.Format("검증 {0} ({1}/{2})", allOk ? "OK" : "NG", okCount, runs.Length));
                foreach (RoiRun run in runs)
                    text.Add(string.Format("R{0} {1} {2}", run.Name, run.Verify.Ok ? "OK" : "NG",
                        string.Join(" ", run.Verify.Lines.Select(l => l.DisplayText))));
                grpResult.Text = "검증 결과 (ROI 별)";
            }
            else
            {
                text.Add(allOk ? "검증 OK" : "검증 NG");
                foreach (LineResult line in runs[0].Verify.Lines)
                    text.Add(string.Format(CultureInfo.InvariantCulture, "{0}  {1} {2:0.00}",
                        line.DisplayText, line.Ok ? "OK" : "NG", line.MinScore));
                grpResult.Text = "검증 결과 (줄 끝 숫자 = 가장 낮은 자리 점수)";
            }
            SetResultFont(multi);
            txtResult.ForeColor = allOk ? Color.Lime : Color.Red;
            txtResult.Text = string.Join(Environment.NewLine, text);

            // 화면 표시: 자리마다 기대 문자를 초록(OK) / 빨강(NG), 못 찾은 자리는 추정 위치에 점선
            var shown = new List<Detection>();
            gridResult.SuspendLayout();
            gridResult.Rows.Clear();
            foreach (RoiRun run in runs)
            {
                foreach (CharResult c in run.Verify.AllChars)
                {
                    shown.Add(new Detection
                    {
                        ClassId = -1,
                        Label = c.Expected.ToString(),
                        Confidence = c.Score,
                        Box = c.Box,
                        Line = c.Line,
                        Estimated = c.Estimated,
                        DisplayColor = c.Ok ? Color.Lime : Color.Red
                    });

                    bool hasBox = c.Box.Width > 0 && c.Box.Height > 0;
                    int r = gridResult.Rows.Add(
                        shown.Count,
                        run.Name,
                        c.Line + 1,
                        c.Expected.ToString(),
                        c.Detection == null ? "-" : c.Score.ToString("0.000", CultureInfo.InvariantCulture),
                        hasBox ? (object)Math.Round(c.Box.X) : "",
                        hasBox ? (object)Math.Round(c.Box.Y) : "",
                        hasBox ? (object)Math.Round(c.Box.Width) : "",
                        hasBox ? (object)Math.Round(c.Box.Height) : "",
                        c.Ok ? "OK" : "NG",
                        c.Reason);
                    if (!c.Ok)
                    {
                        gridResult.Rows[r].DefaultCellStyle.BackColor = Color.MistyRose;
                        gridResult.Rows[r].DefaultCellStyle.ForeColor = Color.DarkRed;
                    }
                }
            }
            viewer.Detections = shown;
            gridResult.ClearSelection();
            gridResult.ResumeLayout();
        }

        private void ClearResults()
        {
            viewer.Detections = null;
            txtResult.Text = "";
            txtResult.ForeColor = Color.Lime;
            gridResult.Rows.Clear();
            lblStatusTime.Text = "";
        }

        private void gridResult_SelectionChanged(object sender, EventArgs e)
        {
            viewer.HighlightIndex = gridResult.SelectedRows.Count > 0 ? gridResult.SelectedRows[0].Index : -1;
        }

        // ================================================================== 공통
        private void UpdateButtons()
        {
            bool hasFiles = _files.Count > 0;
            btnPrev.Enabled = hasFiles;
            btnNext.Enabled = hasFiles;
            btnPlay.Enabled = hasFiles;
            btnRead.Enabled = !_busy && _current != null && _detector != null;
            btnVerify.Enabled = btnRead.Enabled;
            btnDeleteRoi.Enabled = viewer.SelectedRoiIndex >= 0;
            btnOpenModel.Enabled = !_busy;
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            bool typing = ActiveControl is NumericUpDown || ActiveControl is TextBoxBase;
            switch (keyData)
            {
                case Keys.F5:
                    btnRead.PerformClick();
                    return true;
                case Keys.F6:
                    btnVerify.PerformClick();
                    return true;
                case Keys.Enter:
                    if (ActiveControl != txtExpected) break;
                    btnVerify.PerformClick();
                    return true;
                case Keys.Left:
                    if (typing) break;
                    btnPrev.PerformClick();
                    return true;
                case Keys.Right:
                    if (typing) break;
                    btnNext.PerformClick();
                    return true;
                case Keys.Space:
                    if (typing) break;
                    btnPlay.PerformClick();
                    return true;
                case Keys.Delete:
                    if (typing || viewer.SelectedRoiIndex < 0) break;
                    viewer.DeleteSelectedRoi();
                    return true;
                case Keys.Escape:
                    if (viewer.SelectedRoiIndex < 0) break;
                    viewer.SelectedRoiIndex = -1; // 선택 해제
                    return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        /// <summary>파일명 자연 정렬: 숫자 부분은 값으로 비교 (img2 &lt; img10)</summary>
        private static int NaturalCompare(string pathA, string pathB)
        {
            string a = Path.GetFileName(pathA), b = Path.GetFileName(pathB);
            int i = 0, j = 0;
            while (i < a.Length && j < b.Length)
            {
                if (char.IsDigit(a[i]) && char.IsDigit(b[j]))
                {
                    int si = i, sj = j;
                    while (i < a.Length && char.IsDigit(a[i])) i++;
                    while (j < b.Length && char.IsDigit(b[j])) j++;
                    string na = a.Substring(si, i - si).TrimStart('0'), nb = b.Substring(sj, j - sj).TrimStart('0');
                    int c = na.Length != nb.Length ? na.Length.CompareTo(nb.Length) : string.CompareOrdinal(na, nb);
                    if (c != 0) return c;
                }
                else
                {
                    int c = char.ToUpperInvariant(a[i]).CompareTo(char.ToUpperInvariant(b[j]));
                    if (c != 0) return c;
                    i++;
                    j++;
                }
            }
            return (a.Length - i).CompareTo(b.Length - j);
        }
    }
}
