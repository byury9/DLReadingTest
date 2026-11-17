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
    /// 이미지를 창 크기에 맞춰 보여주고, ROI 를 마우스로 그리기/이동/크기조절 하며,
    /// 검출 결과(박스 + 읽은 값)를 겹쳐 그리는 컨트롤.
    /// </summary>
    public class ImageViewer : Control
    {
        [Flags]
        private enum Edge { None = 0, Left = 1, Top = 2, Right = 4, Bottom = 8, Move = 16 }

        private const int HandleTolerance = 6;

        private Bitmap _image;
        private Rectangle _roi = Rectangle.Empty;
        private bool _roiEditMode;
        private IList<Detection> _detections = new List<Detection>();
        private int _highlightIndex = -1;

        // 드래그 상태
        private Edge _dragEdge = Edge.None;
        private bool _drawingNew;
        private PointF _dragStart;
        private Rectangle _roiAtDragStart;

        public event EventHandler RoiChanged;
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

        /// <summary>이미지 좌표계 ROI. Rectangle.Empty 이면 전체 영역.</summary>
        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Rectangle Roi
        {
            get { return _roi; }
            set
            {
                if (_roi == value) return;
                _roi = value;
                Invalidate();
                OnRoiChanged();
            }
        }

        [DefaultValue(false), Category("Behavior"), Description("true 이면 마우스 드래그로 ROI 를 그리거나 수정한다.")]
        public bool RoiEditMode
        {
            get { return _roiEditMode; }
            set
            {
                _roiEditMode = value;
                Cursor = value ? Cursors.Cross : Cursors.Default;
                Invalidate();
            }
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

        // ------------------------------------------------------------------ 마우스 (ROI 편집)
        private Edge HitTest(Point p)
        {
            if (_roi.IsEmpty) return Edge.None;
            RectangleF r = ImageToScreen(_roi);
            int t = HandleTolerance;
            bool inY = p.Y >= r.Top - t && p.Y <= r.Bottom + t;
            bool inX = p.X >= r.Left - t && p.X <= r.Right + t;
            Edge e = Edge.None;
            if (inY && Math.Abs(p.X - r.Left) <= t) e |= Edge.Left;
            if (inY && Math.Abs(p.X - r.Right) <= t) e |= Edge.Right;
            if (inX && Math.Abs(p.Y - r.Top) <= t) e |= Edge.Top;
            if (inX && Math.Abs(p.Y - r.Bottom) <= t) e |= Edge.Bottom;
            if (e == Edge.None && r.Contains(p)) e = Edge.Move;
            return e;
        }

        private static Cursor CursorFor(Edge e)
        {
            switch (e)
            {
                case Edge.Move: return Cursors.SizeAll;
                case Edge.Left | Edge.Top:
                case Edge.Right | Edge.Bottom: return Cursors.SizeNWSE;
                case Edge.Right | Edge.Top:
                case Edge.Left | Edge.Bottom: return Cursors.SizeNESW;
                case Edge.Left:
                case Edge.Right: return Cursors.SizeWE;
                case Edge.Top:
                case Edge.Bottom: return Cursors.SizeNS;
                default: return Cursors.Cross;
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            if (!_roiEditMode || _image == null || e.Button != MouseButtons.Left) return;

            _dragStart = ClampToImage(ScreenToImage(e.Location));
            _roiAtDragStart = _roi;
            _dragEdge = HitTest(e.Location);
            _drawingNew = _dragEdge == Edge.None;
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

            if (!_roiEditMode) return;

            if (!Capture)
            {
                Cursor = CursorFor(HitTest(e.Location));
                return;
            }

            PointF p = ClampToImage(ip);
            if (_drawingNew)
            {
                _roi = RectFromPoints(_dragStart, p);
            }
            else if (_dragEdge == Edge.Move)
            {
                int dx = (int)Math.Round(p.X - _dragStart.X), dy = (int)Math.Round(p.Y - _dragStart.Y);
                int x = Math.Max(0, Math.Min(_image.Width - _roiAtDragStart.Width, _roiAtDragStart.X + dx));
                int y = Math.Max(0, Math.Min(_image.Height - _roiAtDragStart.Height, _roiAtDragStart.Y + dy));
                _roi = new Rectangle(x, y, _roiAtDragStart.Width, _roiAtDragStart.Height);
            }
            else
            {
                float l = _roiAtDragStart.Left, t = _roiAtDragStart.Top, r = _roiAtDragStart.Right, b = _roiAtDragStart.Bottom;
                if ((_dragEdge & Edge.Left) != 0) l = p.X;
                if ((_dragEdge & Edge.Right) != 0) r = p.X;
                if ((_dragEdge & Edge.Top) != 0) t = p.Y;
                if ((_dragEdge & Edge.Bottom) != 0) b = p.Y;
                _roi = RectFromPoints(new PointF(l, t), new PointF(r, b));
            }
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (!Capture) return;
            Capture = false;
            if (_roi.Width < 4 || _roi.Height < 4)
                _roi = _drawingNew && _roiAtDragStart.IsEmpty ? Rectangle.Empty : _roiAtDragStart; // 너무 작은 드래그는 취소
            _dragEdge = Edge.None;
            _drawingNew = false;
            Invalidate();
            if (_roi != _roiAtDragStart) OnRoiChanged();
        }

        private static Rectangle RectFromPoints(PointF a, PointF b)
        {
            int x1 = (int)Math.Round(Math.Min(a.X, b.X)), y1 = (int)Math.Round(Math.Min(a.Y, b.Y));
            int x2 = (int)Math.Round(Math.Max(a.X, b.X)), y2 = (int)Math.Round(Math.Max(a.Y, b.Y));
            return Rectangle.FromLTRB(x1, y1, x2, y2);
        }

        protected virtual void OnRoiChanged()
        {
            if (RoiChanged != null) RoiChanged(this, EventArgs.Empty);
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

            // ROI 바깥을 어둡게
            if (!_roi.IsEmpty)
            {
                RectangleF rr = ImageToScreen(_roi);
                using (var region = new Region(dr))
                using (var shade = new SolidBrush(Color.FromArgb(110, 0, 0, 0)))
                {
                    region.Exclude(rr);
                    g.FillRegion(shade, region);
                }
                using (var pen = new Pen(Color.Yellow, 2f) { DashStyle = _roiEditMode ? DashStyle.Dash : DashStyle.Solid })
                    g.DrawRectangle(pen, rr.X, rr.Y, rr.Width, rr.Height);

                string roiText = string.Format("ROI {0},{1} {2}x{3}", _roi.X, _roi.Y, _roi.Width, _roi.Height);
                DrawLabel(g, roiText, new PointF(rr.X, rr.Y), Color.Yellow, Color.Black, above: true);

                if (_roiEditMode)
                {
                    foreach (PointF h in new[]
                             {
                                 new PointF(rr.Left, rr.Top), new PointF(rr.Right, rr.Top),
                                 new PointF(rr.Left, rr.Bottom), new PointF(rr.Right, rr.Bottom)
                             })
                        g.FillRectangle(Brushes.Yellow, h.X - 4, h.Y - 4, 8, 8);
                }
            }

            // 검출 결과
            for (int i = 0; i < _detections.Count; i++)
            {
                Detection d = _detections[i];
                RectangleF br = ImageToScreen(d.Box);
                bool hl = i == _highlightIndex;
                Color c = hl ? Color.Magenta : ColorForConfidence(d.Confidence);
                using (var pen = new Pen(c, hl ? 3f : 2f))
                    g.DrawRectangle(pen, br.X, br.Y, br.Width, br.Height);
                DrawLabel(g, d.Label, new PointF(br.X, br.Y), c, Color.Black, above: true);
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
