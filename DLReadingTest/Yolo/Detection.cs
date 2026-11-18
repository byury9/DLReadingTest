using System.Drawing;

namespace DLReadingTest.Yolo
{
    /// <summary>검출된 문자 하나. Box 는 원본 이미지 좌표(픽셀).</summary>
    public sealed class Detection
    {
        public int ClassId { get; set; }
        public string Label { get; set; }
        public float Confidence { get; set; }
        public RectangleF Box { get; set; }

        /// <summary>읽기 순서상의 줄 번호 (TextAssembler 가 채움, 0부터)</summary>
        public int Line { get; set; }

        /// <summary>클래스별 점수 전체 (검증에서 "기대 문자"의 점수를 볼 때 사용). end-to-end 출력 모델은 null</summary>
        public float[] Scores { get; set; }

        /// <summary>화면 표시 색 지정 (null 이면 신뢰도에 따라 자동)</summary>
        public Color? DisplayColor { get; set; }

        /// <summary>실제 검출이 아니라 추정한 위치 (점선으로 표시)</summary>
        public bool Estimated { get; set; }

        public PointF Center
        {
            get { return new PointF(Box.X + Box.Width / 2f, Box.Y + Box.Height / 2f); }
        }

        public override string ToString()
        {
            return string.Format("{0} ({1:0.00}) [{2:0},{3:0},{4:0},{5:0}]",
                Label, Confidence, Box.X, Box.Y, Box.Width, Box.Height);
        }
    }
}
