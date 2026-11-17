using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace DLReadingTest.Yolo
{
    /// <summary>
    /// 글자 단위 검출 결과를 사람이 읽는 순서(위→아래 줄, 줄 안에서 왼쪽→오른쪽)로 정렬해 문자열로 만든다.
    /// 글자 사이 간격이 넓으면 공백을 넣는다.
    /// </summary>
    public static class TextAssembler
    {
        public static List<List<Detection>> GroupLines(IEnumerable<Detection> detections)
        {
            var lines = new List<List<Detection>>();
            var dets = detections.OrderBy(d => d.Center.Y).ToList();
            if (dets.Count == 0) return lines;

            float medianH = Median(dets.Select(d => d.Box.Height));

            foreach (Detection d in dets)
            {
                List<Detection> target = null;
                float bestDist = float.MaxValue;
                foreach (List<Detection> line in lines)
                {
                    float lineY = line.Average(x => x.Center.Y);
                    float dist = Math.Abs(lineY - d.Center.Y);
                    if (dist < medianH * 0.5f && dist < bestDist)
                    {
                        bestDist = dist;
                        target = line;
                    }
                }
                if (target == null)
                {
                    target = new List<Detection>();
                    lines.Add(target);
                }
                target.Add(d);
            }

            lines = lines.OrderBy(l => l.Average(x => x.Center.Y)).ToList();
            for (int i = 0; i < lines.Count; i++)
            {
                lines[i].Sort((a, b) => a.Box.X.CompareTo(b.Box.X));
                foreach (Detection d in lines[i]) d.Line = i;
            }
            return lines;
        }

        /// <summary>줄 단위 문자열 배열을 반환하고, ordered 에 읽기 순서로 정렬된 검출 목록을 넘긴다.</summary>
        public static string[] Assemble(IEnumerable<Detection> detections, out List<Detection> ordered)
        {
            List<List<Detection>> lines = GroupLines(detections);
            ordered = lines.SelectMany(l => l).ToList();

            var result = new string[lines.Count];
            for (int i = 0; i < lines.Count; i++)
            {
                List<Detection> line = lines[i];
                float medianW = Median(line.Select(d => d.Box.Width));
                float medianH = Median(line.Select(d => d.Box.Height));
                float gapLimit = Math.Max(medianW, medianH * 0.5f) * 0.8f;
                var sb = new StringBuilder();
                for (int k = 0; k < line.Count; k++)
                {
                    if (k > 0 && line[k].Box.Left - line[k - 1].Box.Right > gapLimit)
                        sb.Append(' ');
                    sb.Append(line[k].Label);
                }
                result[i] = sb.ToString();
            }
            return result;
        }

        private static float Median(IEnumerable<float> values)
        {
            float[] v = values.OrderBy(x => x).ToArray();
            if (v.Length == 0) return 0;
            return v.Length % 2 == 1 ? v[v.Length / 2] : (v[v.Length / 2 - 1] + v[v.Length / 2]) / 2f;
        }
    }
}
