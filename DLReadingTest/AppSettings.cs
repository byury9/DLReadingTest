using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;

namespace DLReadingTest
{
    /// <summary>실행 파일 옆 DLReadingTest.ini 에 마지막 설정(폴더, ROI, 모델 등)을 저장한다.</summary>
    public class AppSettings
    {
        public string LastFolder { get; set; }
        public string ModelPath { get; set; }
        public Rectangle Roi { get; set; }
        public int IntervalMs { get; set; }
        public decimal Confidence { get; set; }
        public bool AutoRead { get; set; }
        /// <summary>글자 치환 표 (예: "O:0" = 영문 O 를 숫자 0 으로). 비우면 치환 안 함.</summary>
        public string LabelMap { get; set; }

        public AppSettings()
        {
            IntervalMs = 1000;
            Confidence = 0.25m;
            AutoRead = true;
            LabelMap = "O:0";
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
                if (map.TryGetValue("Roi", out v))
                {
                    string[] p = v.Split(',');
                    int x, y, w, h;
                    if (p.Length == 4 && int.TryParse(p[0], out x) && int.TryParse(p[1], out y) &&
                        int.TryParse(p[2], out w) && int.TryParse(p[3], out h))
                        s.Roi = new Rectangle(x, y, w, h);
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
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "Roi={0},{1},{2},{3}", Roi.X, Roi.Y, Roi.Width, Roi.Height));
                sb.AppendLine("IntervalMs=" + IntervalMs.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("Confidence=" + Confidence.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("AutoRead=" + AutoRead);
                sb.AppendLine("LabelMap=" + (LabelMap ?? ""));
                File.WriteAllText(FilePath, sb.ToString(), Encoding.UTF8);
            }
            catch
            {
                // 실행 폴더에 쓰기 권한이 없으면 저장 생략
            }
        }
    }
}
