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
                await ReadAsync();
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
            await ReadAsync();
        }

        private async Task ReadAsync()
        {
            if (_busy || _current == null) return;
            if (_detector == null)
            {
                MessageBox.Show(this, "먼저 [모델 열기] 로 YOLO ONNX 모델을 불러오세요.", "모델 없음",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var full = new Rectangle(0, 0, _current.Width, _current.Height);
            Rectangle roi = viewer.Roi.IsEmpty ? full : Rectangle.Intersect(viewer.Roi, full);
            if (roi.Width < 2 || roi.Height < 2) roi = full;

            // 화면 그리기와 동시에 같은 Bitmap 에 접근하지 않도록 ROI 를 UI 스레드에서 복사
            Bitmap crop = _current.Clone(roi, PixelFormat.Format24bppRgb);
            Bitmap source = _current;
            YoloDetector detector = _detector;
            float conf = (float)numConf.Value;

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
                if (!ReferenceEquals(source, _current)) return; // 읽는 동안 이미지가 바뀜
                ShowResults(dets);
                lblStatusTime.Text = string.Format("{0} ms", sw.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                StopPlay();
                lblStatusTime.Text = "읽기 실패";
                MessageBox.Show(this, ex.Message, "추론 오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
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
                    Math.Round(d.Box.Height));
            }
            gridResult.ClearSelection();
            gridResult.ResumeLayout();
        }

        private void ClearResults()
        {
            viewer.Detections = null;
            txtResult.Text = "";
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
