using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using DLReadingTest.Yolo;

namespace DLReadingTest.Controls
{
    /// <summary>
    /// 이미지를 창 크기에 맞춰 보여주고, 여러 개의 ROI 를 마우스로 선택/이동/크기조절 하며,
    /// 검출 결과(박스 + 읽은 값)를 겹쳐 그리는 컨트롤.
    ///  - ROI 테두리 선 위: 이동 (커서 ✥)
    ///  - 선택된 ROI 의 모서리 네모: 크기 조절
    ///  - ROI 안쪽 클릭: 선택, 빈 곳 클릭: 선택 해제
    /// </summary>
    public class ImageViewer : Control
    {
        private enum Hit { None, Inside, Line, TopLeft, TopRight, BottomLeft, BottomRight }

        private const int LineTolerance = 5;   // 테두리 선 판정 폭 (화면 픽셀)
        private const int CornerTolerance = 7; // 모서리 판정 폭 (화면 픽셀)
        private const int MinRoiSize = 8;      // ROI 최소 크기 (이미지 픽셀)

        private static readonly Color SelectedRoiColor = Color.Yellow;
        private static readonly Color RoiColor = Color.DeepSkyBlue;

        private Bitmap _image;
        private readonly List<Rectangle> _rois = new List<Rectangle>();
        private int _selected = -1;
        private IList<Detection> _detections = new List<Detection>();
        private int _highlightIndex = -1;

        // 드래그 상태
        private int _dragIndex = -1;
        private Hit _dragHit = Hit.None;
        private PointF _dragStart;
        private Rectangle _roiAtDragStart;

        /// <summary>ROI 추가/삭제/이동/크기 변경 시 (드래그는 마우스를 놓았을 때)</summary>
        public event EventHandler RoisChanged;
        /// <summary>선택된 ROI 가 바뀌었을 때</summary>
        public event EventHandler SelectedRoiChanged;
        public event EventHandler<ImageMouseEventArgs> ImageMouseMove;

        public ImageViewer()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.Selectable, true);
            BackColor = Color.FromArgb(32, 32, 32);
            ForeColor = Color.White;
            Font = new Font("Consolas", 10f, FontStyle.Bold);
        }

        // ------------------------------------------------------------------ 속성
        /// <summary>표시할 이미지. 컨트롤은 이미지를 소유(Dispose)하지 않는다.</summary>
        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Bitmap Image
        {
            get { return _image; }
            set
            {
                _image = value;
                Invalidate();
            }
        }

        /// <summary>이미지 좌표계 ROI 목록 (읽기 전용 사본). 비어 있으면 전체 영역을 읽는다.</summary>
        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public IReadOnlyList<Rectangle> Rois
        {
            get { return _rois.ToArray(); }
        }

        /// <summary>선택된 ROI 번호 (0부터, -1 = 선택 없음)</summary>
        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int SelectedRoiIndex
        {
            get { return _selected; }
            set
            {
                int v = value >= 0 && value < _rois.Count ? value : -1;
                if (v == _selected) return;
                _selected = v;
                Invalidate();
                if (SelectedRoiChanged != null) SelectedRoiChanged(this, EventArgs.Empty);
            }
        }

        /// <summary>ROI 목록을 통째로 바꾼다 (설정 불러오기 등). 선택은 해제된다.</summary>
        public void SetRois(IEnumerable<Rectangle> rois)
        {
            _rois.Clear();
            if (rois != null)
                foreach (Rectangle r in rois)
                    if (r.Width > 0 && r.Height > 0) _rois.Add(r);
            _selected = -1;
            Invalidate();
            OnRoisChanged();
            if (SelectedRoiChanged != null) SelectedRoiChanged(this, EventArgs.Empty);
        }

        /// <summary>ROI 를 추가하고 선택한다. 추가된 번호를 돌려준다.</summary>
        public int AddRoi(Rectangle roi)
        {
            _rois.Add(roi);
            Invalidate();
            OnRoisChanged();
            SelectedRoiIndex = _rois.Count - 1;
            return _rois.Count - 1;
        }

        /// <summary>선택된 ROI 를 삭제한다. 삭제했으면 true.</summary>
        public bool DeleteSelectedRoi()
        {
            if (_selected < 0 || _selected >= _rois.Count) return false;
            _rois.RemoveAt(_selected);
            _selected = -1;
            Invalidate();
            OnRoisChanged();
            if (SelectedRoiChanged != null) SelectedRoiChanged(this, EventArgs.Empty);
            return true;
        }

        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public IList<Detection> Detections
        {
            get { return _detections; }
            set
            {
                _detections = value ?? new List<Detection>();
                _highlightIndex = -1;
                Invalidate();
            }
        }

        /// <summary>강조 표시할 검출 결과 인덱스 (-1 = 없음)</summary>
        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int HighlightIndex
        {
            get { return _highlightIndex; }
            set
            {
                _highlightIndex = value;
                Invalidate();
            }
        }

        // ------------------------------------------------------------------ 좌표 변환
        private float ViewScale
        {
            get
            {
                if (_image == null || _image.Width == 0 || _image.Height == 0) return 1f;
                return Math.Min((float)ClientSize.Width / _image.Width, (float)ClientSize.Height / _image.Height);
            }
        }

        private RectangleF ImageDisplayRect
        {
            get
            {
                if (_image == null) return RectangleF.Empty;
                float s = ViewScale;
                float w = _image.Width * s, h = _image.Height * s;
                return new RectangleF((ClientSize.Width - w) / 2f, (ClientSize.Height - h) / 2f, w, h);
            }
        }

        public PointF ScreenToImage(Point p)
        {
            RectangleF r = ImageDisplayRect;
            float s = ViewScale;
            return new PointF((p.X - r.X) / s, (p.Y - r.Y) / s);
        }

        public RectangleF ImageToScreen(RectangleF rc)
        {
            RectangleF r = ImageDisplayRect;
            float s = ViewScale;
            return new RectangleF(r.X + rc.X * s, r.Y + rc.Y * s, rc.Width * s, rc.Height * s);
        }

        private PointF ClampToImage(PointF p)
        {
            return new PointF(Math.Max(0, Math.Min(_image.Width, p.X)), Math.Max(0, Math.Min(_image.Height, p.Y)));
        }

        // ------------------------------------------------------------------ 마우스 (ROI 선택/이동/크기 조절)
        /// <summary>화면 좌표 p 에 있는 ROI 와 부위. 선택된 ROI 를 먼저, 그다음 나중에 그려진(위에 있는) ROI 순으로 본다.</summary>
        private int HitTest(Point p, out Hit hit)
        {
            hit = Hit.None;
            if (_image == null) return -1;
            var order = new List<int>();
            if (_selected >= 0) order.Add(_selected);
            for (int i = _rois.Count - 1; i >= 0; i--)
                if (i != _selected) order.Add(i);

            foreach (int i in order)
            {
                RectangleF r = ImageToScreen(_rois[i]);
                if (i == _selected) // 모서리 크기 조절은 선택된 ROI 만
                {
                    int c = CornerTolerance;
                    if (Near(p, r.Left, r.Top, c)) { hit = Hit.TopLeft; return i; }
                    if (Near(p, r.Right, r.Top, c)) { hit = Hit.TopRight; return i; }
                    if (Near(p, r.Left, r.Bottom, c)) { hit = Hit.BottomLeft; return i; }
                    if (Near(p, r.Right, r.Bottom, c)) { hit = Hit.BottomRight; return i; }
                }
                int t = LineTolerance;
                bool inY = p.Y >= r.Top - t && p.Y <= r.Bottom + t;
                bool inX = p.X >= r.Left - t && p.X <= r.Right + t;
                if ((inY && (Math.Abs(p.X - r.Left) <= t || Math.Abs(p.X - r.Right) <= t)) ||
                    (inX && (Math.Abs(p.Y - r.Top) <= t || Math.Abs(p.Y - r.Bottom) <= t)))
                {
                    hit = Hit.Line;
                    return i;
                }
            }
            foreach (int i in order)
            {
                if (ImageToScreen(_rois[i]).Contains(p))
                {
                    hit = Hit.Inside;
                    return i;
                }
            }
            return -1;
        }

        private static bool Near(Point p, float x, float y, int tol)
        {
            return Math.Abs(p.X - x) <= tol && Math.Abs(p.Y - y) <= tol;
        }

        private static Cursor CursorFor(Hit h)
        {
            switch (h)
            {
                case Hit.Line: return Cursors.SizeAll;          // 테두리 선: 이동
                case Hit.TopLeft:
                case Hit.BottomRight: return Cursors.SizeNWSE;  // 모서리: 크기 조절
                case Hit.TopRight:
                case Hit.BottomLeft: return Cursors.SizeNESW;
                case Hit.Inside: return Cursors.Hand;            // 안쪽: 클릭하면 선택
                default: return Cursors.Default;
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            if (_image == null || e.Button != MouseButtons.Left) return;

            Hit hit;
            int idx = HitTest(e.Location, out hit);
            SelectedRoiIndex = idx; // 빈 곳이면 선택 해제
            if (idx < 0 || hit == Hit.Inside) return;

            _dragIndex = idx;
            _dragHit = hit;
            _dragStart = ScreenToImage(e.Location);
            _roiAtDragStart = _rois[idx];
            Capture = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_image == null) return;

            PointF ip = ScreenToImage(e.Location);
            bool inside = ip.X >= 0 && ip.Y >= 0 && ip.X < _image.Width && ip.Y < _image.Height;
            if (ImageMouseMove != null)
                ImageMouseMove(this, new ImageMouseEventArgs(inside, Point.Truncate(ip)));

            if (!Capture || _dragIndex < 0)
            {
                Hit hit;
                HitTest(e.Location, out hit);
                Cursor = CursorFor(hit);
                return;
            }

            Rectangle r0 = _roiAtDragStart;
            if (_dragHit == Hit.Line)
            {
                // 이동: 이미지 밖으로 나가지 않게
                int dx = (int)Math.Round(ip.X - _dragStart.X), dy = (int)Math.Round(ip.Y - _dragStart.Y);
                int x = Math.Max(0, Math.Min(Math.Max(0, _image.Width - r0.Width), r0.X + dx));
                int y = Math.Max(0, Math.Min(Math.Max(0, _image.Height - r0.Height), r0.Y + dy));
                _rois[_dragIndex] = new Rectangle(x, y, r0.Width, r0.Height);
            }
            else
            {
                // 크기 조절: 잡은 모서리만 움직이고 반대쪽 모서리는 고정
                PointF p = ClampToImage(ip);
                float l = r0.Left, t = r0.Top, r = r0.Right, b = r0.Bottom;
                if (_dragHit == Hit.TopLeft || _dragHit == Hit.BottomLeft) l = Math.Min(p.X, r - MinRoiSize);
                if (_dragHit == Hit.TopRight || _dragHit == Hit.BottomRight) r = Math.Max(p.X, l + MinRoiSize);
                if (_dragHit == Hit.TopLeft || _dragHit == Hit.TopRight) t = Math.Min(p.Y, b - MinRoiSize);
                if (_dragHit == Hit.BottomLeft || _dragHit == Hit.BottomRight) b = Math.Max(p.Y, t + MinRoiSize);
                _rois[_dragIndex] = Rectangle.FromLTRB((int)Math.Round(l), (int)Math.Round(t), (int)Math.Round(r), (int)Math.Round(b));
            }
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (!Capture) return;
            Capture = false;
            bool changed = _dragIndex >= 0 && _rois[_dragIndex] != _roiAtDragStart;
            _dragIndex = -1;
            _dragHit = Hit.None;
            Invalidate();
            if (changed) OnRoisChanged();
        }

        protected virtual void OnRoisChanged()
        {
            if (RoisChanged != null) RoisChanged(this, EventArgs.Empty);
        }

        // ------------------------------------------------------------------ 그리기
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(BackColor);

            if (_image == null)
            {
                TextRenderer.DrawText(g, "폴더를 열어 이미지를 불러오세요", Font, ClientRectangle,
                    Color.Gray, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                return;
            }

            float s = ViewScale;
            RectangleF dr = ImageDisplayRect;
            g.InterpolationMode = s >= 2f ? InterpolationMode.NearestNeighbor : InterpolationMode.Bilinear;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(_image, dr);
            g.PixelOffsetMode = PixelOffsetMode.Default;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            if (_rois.Count > 0)
            {
                // ROI 바깥을 어둡게
                using (var region = new Region(dr))
                using (var shade = new SolidBrush(Color.FromArgb(110, 0, 0, 0)))
                {
                    foreach (Rectangle roi in _rois) region.Exclude(ImageToScreen(roi));
                    g.FillRegion(shade, region);
                }

                // 선택 안 된 ROI 먼저, 선택된 ROI 는 맨 위에
                for (int i = 0; i < _rois.Count; i++)
                    if (i != _selected) DrawRoi(g, i, false);
                if (_selected >= 0 && _selected < _rois.Count) DrawRoi(g, _selected, true);
            }

            // 검출 결과
            for (int i = 0; i < _detections.Count; i++)
            {
                Detection d = _detections[i];
                if (d.Box.Width <= 0 || d.Box.Height <= 0) continue; // 위치 없음 (표에만 표시)
                RectangleF br = ImageToScreen(d.Box);
                bool hl = i == _highlightIndex;
                Color c = hl ? Color.Magenta : (d.DisplayColor ?? ColorForConfidence(d.Confidence));
                using (var pen = new Pen(c, hl ? 3f : 2f))
                {
                    if (d.Estimated) pen.DashStyle = DashStyle.Dash; // 못 찾은 자리(추정 위치)
                    g.DrawRectangle(pen, br.X, br.Y, br.Width, br.Height);
                }
                DrawLabel(g, d.Label, new PointF(br.X, br.Y), c, Color.Black, above: true);
            }
        }

        private void DrawRoi(Graphics g, int i, bool selected)
        {
            Rectangle roi = _rois[i];
            RectangleF rr = ImageToScreen(roi);
            Color c = selected ? SelectedRoiColor : RoiColor;
            using (var pen = new Pen(c, selected ? 2.5f : 1.5f))
                g.DrawRectangle(pen, rr.X, rr.Y, rr.Width, rr.Height);

            string text = selected
                ? string.Format("ROI {0}  {1},{2} {3}x{4}", i + 1, roi.X, roi.Y, roi.Width, roi.Height)
                : string.Format("ROI {0}", i + 1);
            DrawLabel(g, text, new PointF(rr.X, rr.Y), c, Color.Black, above: true);

            if (selected)
            {
                using (var b = new SolidBrush(c))
                    foreach (PointF h in new[]
                             {
                                 new PointF(rr.Left, rr.Top), new PointF(rr.Right, rr.Top),
                                 new PointF(rr.Left, rr.Bottom), new PointF(rr.Right, rr.Bottom)
                             })
                        g.FillRectangle(b, h.X - 4, h.Y - 4, 8, 8);
            }
        }

        private static Color ColorForConfidence(float conf)
        {
            if (conf >= 0.7f) return Color.Lime;
            if (conf >= 0.5f) return Color.Orange;
            return Color.OrangeRed;
        }

        private void DrawLabel(Graphics g, string text, PointF anchor, Color back, Color fore, bool above)
        {
            SizeF sz = g.MeasureString(text, Font);
            float y = above ? anchor.Y - sz.Height : anchor.Y;
            if (y < 0) y = anchor.Y;
            var rc = new RectangleF(anchor.X, y, sz.Width, sz.Height);
            using (var b = new SolidBrush(back))
                g.FillRectangle(b, rc);
            using (var f = new SolidBrush(fore))
                g.DrawString(text, Font, f, rc.Location);
        }
    }

    public class ImageMouseEventArgs : EventArgs
    {
        public ImageMouseEventArgs(bool insideImage, Point location)
        {
            InsideImage = insideImage;
            Location = location;
        }

        public bool InsideImage { get; private set; }
        public Point Location { get; private set; }
    }
}
