using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace DLReadingTest
{
    /// <summary>실행 파일 옆 DLReadingTest.ini 에 마지막 설정(폴더, ROI, 모델 등)을 저장한다.</summary>
    public class AppSettings
    {
        public string LastFolder { get; set; }
        public string ModelPath { get; set; }
        /// <summary>ROI 목록 (이미지 좌표). 비어 있으면 전체 영역</summary>
        public List<Rectangle> Rois { get; set; }
        public int IntervalMs { get; set; }
        public decimal Confidence { get; set; }
        public bool AutoRead { get; set; }
        /// <summary>글자 치환 표 (예: "O:0" = 영문 O 를 숫자 0 으로). 비우면 치환 안 함.</summary>
        public string LabelMap { get; set; }
        /// <summary>마지막에 입력한 검증할 문자</summary>
        public string ExpectedText { get; set; }
        /// <summary>검증 OK 기준: 자리마다 기대 문자 점수가 이 값 이상 (기본 0.10)</summary>
        public float VerifyPassScore { get; set; }

        public AppSettings()
        {
            IntervalMs = 1000;
            Rois = new List<Rectangle>();
            Confidence = 0.25m;
            AutoRead = true;
            LabelMap = "O:0";
            ExpectedText = "";
            VerifyPassScore = 0.10f;
        }

        private static string FilePath
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "DLReadingTest.ini"); }
        }

        public static AppSettings Load()
        {
            var s = new AppSettings();
            try
            {
                if (!File.Exists(FilePath)) return s;
                var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (string line in File.ReadAllLines(FilePath, Encoding.UTF8))
                {
                    int i = line.IndexOf('=');
                    if (i > 0) map[line.Substring(0, i).Trim()] = line.Substring(i + 1).Trim();
                }
                string v;
                if (map.TryGetValue("LastFolder", out v)) s.LastFolder = v;
                if (map.TryGetValue("ModelPath", out v)) s.ModelPath = v;
                int n;
                if (map.TryGetValue("IntervalMs", out v) && int.TryParse(v, out n)) s.IntervalMs = n;
                decimal d;
                if (map.TryGetValue("Confidence", out v) && decimal.TryParse(v, NumberStyles.Number, CultureInfo.InvariantCulture, out d))
                    s.Confidence = d;
                bool b;
                if (map.TryGetValue("AutoRead", out v) && bool.TryParse(v, out b)) s.AutoRead = b;
                if (map.TryGetValue("LabelMap", out v)) s.LabelMap = v;
                if (map.TryGetValue("ExpectedText", out v)) s.ExpectedText = v;
                float f;
                if (map.TryGetValue("VerifyPassScore", out v) && float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out f))
                    s.VerifyPassScore = f;
                // Rois=x,y,w,h;x,y,w,h ... (이전 버전의 Roi=x,y,w,h 한 개도 읽음)
                if (map.TryGetValue("Rois", out v) || map.TryGetValue("Roi", out v))
                {
                    foreach (string item in v.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        string[] p = item.Split(',');
                        int x, y, w, h;
                        if (p.Length == 4 && int.TryParse(p[0], out x) && int.TryParse(p[1], out y) &&
                            int.TryParse(p[2], out w) && int.TryParse(p[3], out h) && w > 0 && h > 0)
                            s.Rois.Add(new Rectangle(x, y, w, h));
                    }
                }
            }
            catch
            {
                // 설정 파일이 깨져 있으면 기본값 사용
            }
            return s;
        }

        public void Save()
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("LastFolder=" + (LastFolder ?? ""));
                sb.AppendLine("ModelPath=" + (ModelPath ?? ""));
                sb.AppendLine("Rois=" + string.Join(";", (Rois ?? new List<Rectangle>()).Select(r =>
                    string.Format(CultureInfo.InvariantCulture, "{0},{1},{2},{3}", r.X, r.Y, r.Width, r.Height))));
                sb.AppendLine("IntervalMs=" + IntervalMs.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("Confidence=" + Confidence.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("AutoRead=" + AutoRead);
                sb.AppendLine("LabelMap=" + (LabelMap ?? ""));
                sb.AppendLine("ExpectedText=" + (ExpectedText ?? ""));
                sb.AppendLine("VerifyPassScore=" + VerifyPassScore.ToString(CultureInfo.InvariantCulture));
                File.WriteAllText(FilePath, sb.ToString(), Encoding.UTF8);
            }
            catch
            {
                // 실행 폴더에 쓰기 권한이 없으면 저장 생략
            }
        }
    }
}
