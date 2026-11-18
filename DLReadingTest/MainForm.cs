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

        public MainForm()
        {
            InitializeComponent();
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
            _verifyOptions.PassScore = _settings.VerifyPassScore;
            timerRotate.Interval = (int)numInterval.Value;

            // 모델: 설정에 저장된 경로 -> 실행 폴더\Models\*.onnx 순서로 찾음
            string model = _settings.ModelPath;
            if (string.IsNullOrEmpty(model) || !File.Exists(model))
            {
                string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Models");
                model = Directory.Exists(dir) ? Directory.GetFiles(dir, "*.onnx").OrderBy(f => f).FirstOrDefault() : null;
            }
            if (model != null) LoadModel(model, showError: false);

            if (!_settings.Roi.IsEmpty) viewer.Roi = _settings.Roi;
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
            _settings.Roi = viewer.Roi;
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
        private void chkRoiEdit_CheckedChanged(object sender, EventArgs e)
        {
            viewer.RoiEditMode = chkRoiEdit.Checked;
            chkRoiEdit.BackColor = chkRoiEdit.Checked ? Color.Gold : SystemColors.Control;
            if (chkRoiEdit.Checked) StopPlay();
        }

        private void btnClearRoi_Click(object sender, EventArgs e)
        {
            viewer.Roi = Rectangle.Empty;
        }

        private void viewer_RoiChanged(object sender, EventArgs e)
        {
            UpdateRoiStatus();
            _settings.Roi = viewer.Roi;
            ClearResults();
        }

        private void UpdateRoiStatus()
        {
            Rectangle r = viewer.Roi;
            lblStatusRoi.Text = r.IsEmpty
                ? "ROI: 전체"
                : string.Format("ROI: {0},{1} {2}x{3}", r.X, r.Y, r.Width, r.Height);
        }

        private void viewer_ImageMouseMove(object sender, ImageMouseEventArgs e)
        {
            lblStatusMouse.Text = e.InsideImage ? string.Format("X {0}, Y {1}", e.Location.X, e.Location.Y) : "";
        }

        // ================================================================== 읽기
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

        // ------------------------------------------------------------------ 읽기
        private async Task ReadAsync()
        {
            List<Detection> dets = await DetectAsync((float)numConf.Value);
            if (dets != null) ShowResults(dets);
        }

        // ------------------------------------------------------------------ 검증
        private async Task VerifyAsync()
        {
            if (_busy || _current == null) return;
            if (_detector != null)
            {
                string[] expected = TextVerifier.ParseExpected(txtExpected.Text, _detector.ClassNames, _detector.LabelMap);
                if (expected.Length == 0)
                {
                    StopPlay();
                    MessageBox.Show(this, "검증할 문자를 입력하세요.\r\n줄은 공백이나 쉼표(,)로 구분합니다.\r\n\r\n예: GWTB017P 2030.03.16",
                        "검증할 문자 없음", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    txtExpected.Focus();
                    return;
                }

                // 검증은 기대 글자의 점수가 낮아도 찾을 수 있도록 후보를 낮은 기준으로 넓게 검출
                YoloDetector detector = _detector;
                List<Detection> candidates = await DetectAsync(_verifyOptions.CandidateScore);
                if (candidates == null) return;
                VerifyResult result = TextVerifier.Verify(candidates, expected, detector.ClassNames, detector.LabelMap, _verifyOptions);
                ShowVerifyResult(result);
            }
            else
            {
                await DetectAsync(0f); // 모델 없음 안내
            }
        }

        /// <summary>
        /// 현재 이미지의 ROI 영역에서 글자를 검출한다 (백그라운드 실행).
        /// 실패했거나 읽는 동안 이미지가 바뀌면 null.
        /// </summary>
        private async Task<List<Detection>> DetectAsync(float conf)
        {
            if (_busy || _current == null) return null;
            if (_detector == null)
            {
                MessageBox.Show(this, "먼저 [모델 열기] 로 YOLO ONNX 모델을 불러오세요.", "모델 없음",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return null;
            }

            var full = new Rectangle(0, 0, _current.Width, _current.Height);
            Rectangle roi = viewer.Roi.IsEmpty ? full : Rectangle.Intersect(viewer.Roi, full);
            if (roi.Width < 2 || roi.Height < 2) roi = full;

            // 화면 그리기와 동시에 같은 Bitmap 에 접근하지 않도록 ROI 를 UI 스레드에서 복사
            Bitmap crop = _current.Clone(roi, PixelFormat.Format24bppRgb);
            Bitmap source = _current;
            YoloDetector detector = _detector;

            _busy = true;
            UpdateButtons();
            var sw = Stopwatch.StartNew();
            try
            {
                List<Detection> dets = await Task.Run(() =>
                {
                    using (crop) return detector.Detect(crop, roi.Location, conf, NmsIou);
                });
                sw.Stop();
                if (!ReferenceEquals(source, _current)) return null; // 읽는 동안 이미지가 바뀜
                lblStatusTime.Text = string.Format("{0} ms", sw.ElapsedMilliseconds);
                return dets;
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

        private void ShowResults(List<Detection> dets)
        {
            List<Detection> ordered;
            string[] lines = TextAssembler.Assemble(dets, out ordered);

            viewer.Detections = ordered;
            grpResult.Text = "읽은 결과";
            txtResult.ForeColor = Color.Lime;
            txtResult.Text = lines.Length == 0 ? "(인식된 문자 없음)" : string.Join(Environment.NewLine, lines);

            gridResult.SuspendLayout();
            gridResult.Rows.Clear();
            for (int i = 0; i < ordered.Count; i++)
            {
                Detection d = ordered[i];
                gridResult.Rows.Add(
                    i + 1,
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
            gridResult.ClearSelection();
            gridResult.ResumeLayout();
        }

        private void ShowVerifyResult(VerifyResult result)
        {
            // 결과 문자열: OK 자리는 기대 문자, 다른 문자는 읽은 글자, 점수 낮음은 ?, 없음은 _
            var text = new List<string> { result.Ok ? "검증 OK" : "검증 NG" };
            foreach (LineResult line in result.Lines)
                text.Add(string.Format(CultureInfo.InvariantCulture, "{0}  {1} {2:0.00}",
                    line.DisplayText, line.Ok ? "OK" : "NG", line.MinScore));
            grpResult.Text = "검증 결과 (줄 끝 숫자 = 가장 낮은 자리 점수)";
            txtResult.ForeColor = result.Ok ? Color.Lime : Color.Red;
            txtResult.Text = string.Join(Environment.NewLine, text);

            // 화면 표시: 자리마다 기대 문자를 초록(OK) / 빨강(NG), 못 찾은 자리는 추정 위치에 점선
            var shown = new List<Detection>();
            gridResult.SuspendLayout();
            gridResult.Rows.Clear();
            int row = 0;
            foreach (CharResult c in result.AllChars)
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
                    ++row,
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
                case Keys.Escape:
                    if (chkRoiEdit.Checked)
                    {
                        chkRoiEdit.Checked = false;
                        return true;
                    }
                    break;
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
