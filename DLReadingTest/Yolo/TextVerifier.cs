using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;

namespace DLReadingTest.Yolo
{
    /// <summary>검증 기준값</summary>
    public sealed class VerifyOptions
    {
        /// <summary>자리마다 "기대 문자"의 점수가 이 값 이상이면 OK (기본 0.10)</summary>
        public float PassScore { get; set; }

        /// <summary>글자 후보를 넓게 찾기 위한 검출 기준 (읽기의 신뢰도보다 훨씬 낮게, 기본 0.05)</summary>
        public float CandidateScore { get; set; }

        /// <summary>점수가 낮을 때 "다른 문자" 로 판단할 읽은 글자 점수 (기본 0.25)</summary>
        public float WrongScore { get; set; }

        public VerifyOptions()
        {
            PassScore = 0.10f;
            CandidateScore = 0.05f;
            WrongScore = 0.25f;
        }
    }

    public enum CharStatus
    {
        /// <summary>기대 문자 확인</summary>
        Ok,
        /// <summary>그 자리에 글자는 있지만 기대 문자 점수가 기준 미달</summary>
        Low,
        /// <summary>그 자리에 다른 글자가 확실하게 읽힘</summary>
        Wrong,
        /// <summary>그 자리에 글자를 찾지 못함</summary>
        Missing
    }

    /// <summary>기대 문자열의 한 글자(자리)에 대한 검증 결과</summary>
    public sealed class CharResult
    {
        public int Line { get; set; }
        public int Index { get; set; }
        public char Expected { get; set; }

        /// <summary>그 자리 글자의 "기대 문자" 점수 (0~1)</summary>
        public float Score { get; set; }

        /// <summary>그 자리에서 가장 점수가 높은 글자 (읽기 결과)와 점수</summary>
        public string ReadLabel { get; set; }
        public float ReadScore { get; set; }

        /// <summary>그 자리에 대응된 검출. null 이면 못 찾음</summary>
        public Detection Detection { get; set; }

        /// <summary>표시용 위치 (원본 이미지 좌표). 못 찾은 자리는 앞뒤 글자 간격으로 추정</summary>
        public RectangleF Box { get; set; }
        public bool Estimated { get; set; }

        public CharStatus Status { get; set; }

        public bool Ok { get { return Status == CharStatus.Ok; } }

        public string Reason
        {
            get
            {
                switch (Status)
                {
                    case CharStatus.Ok:
                        return ReadLabel == Expected.ToString() ? "" : string.Format("읽기는 {0} ({1:0.00})", ReadLabel, ReadScore);
                    case CharStatus.Low:
                        return string.Format("점수 낮음 (읽기 {0} {1:0.00})", ReadLabel, ReadScore);
                    case CharStatus.Wrong:
                        return string.Format("다른 문자 {0} ({1:0.00})", ReadLabel, ReadScore);
                    default:
                        return Estimated ? "글자 없음 (위치 추정)" : "글자 없음";
                }
            }
        }

        /// <summary>결과 문자열에 표시할 글자: OK 는 기대 문자, 다른 문자는 읽은 글자, 점수 낮음은 ?, 없음은 _</summary>
        public char DisplayChar
        {
            get
            {
                switch (Status)
                {
                    case CharStatus.Ok: return Expected;
                    case CharStatus.Wrong: return string.IsNullOrEmpty(ReadLabel) ? '?' : ReadLabel[0];
                    case CharStatus.Low: return '?';
                    default: return '_';
                }
            }
        }
    }

    /// <summary>기대 문자열 한 줄의 검증 결과</summary>
    public sealed class LineResult
    {
        public string Expected { get; set; }
        public List<CharResult> Chars { get; set; }

        public bool Ok { get { return Chars.All(c => c.Ok); } }

        public float MinScore { get { return Chars.Count == 0 ? 0f : Chars.Min(c => c.Score); } }

        public string DisplayText { get { return new string(Chars.Select(c => c.DisplayChar).ToArray()); } }
    }

    public sealed class VerifyResult
    {
        public List<LineResult> Lines { get; set; }

        public bool Ok { get { return Lines.Count > 0 && Lines.All(l => l.Ok); } }

        public IEnumerable<CharResult> AllChars { get { return Lines.SelectMany(l => l.Chars); } }
    }

    /// <summary>
    /// 문자 검증 (OCV). 인쇄할 내용(기대 문자열)을 알고 있으므로 "무슨 글자인가?"를 맞히는 대신
    /// 자리마다 "기대한 글자의 점수"를 본다. 쓸려서 다른 글자로 읽히더라도 기대 글자의 점수가 남아 있으면 OK,
    /// 실제로 다른 글자가 인쇄되어 있으면 기대 글자의 점수는 거의 0 이 되어 NG 가 된다.
    ///
    /// 순서:
    ///  1. 낮은 기준(CandidateScore)으로 글자 후보를 넓게 검출한다 (YoloDetector.Detect).
    ///  2. 후보를 줄로 묶고, 간격이 크게 벌어진 곳에서 끊어 "글자 묶음"(segment)을 만든다.
    ///  3. 기대 문자열 줄마다 가장 잘 맞는 묶음을 위에서부터 차례로 고른다.
    ///  4. 묶음 안의 후보들과 기대 문자열을 순서대로 대응시킨다 (동적 계획법, 빠진 글자/잡음 허용).
    ///  5. 자리마다 기대 문자 점수로 OK / 점수 낮음 / 다른 문자 / 없음 을 판정한다.
    /// </summary>
    public static class TextVerifier
    {
        // 잡음(대응시키지 않은 후보)을 건너뛸 때 감점 비율. 확실한 글자를 건너뛸수록 손해
        private const float SkipPenalty = 0.2f;
        // 점수가 0 이어도 아무 후보와 대응시키는 쪽을 약간 선호 (없음 보다 "다른 문자" 로 보고하기 위해)
        private const float MatchBonus = 0.01f;

        /// <summary>
        /// 입력 문자열을 기대 줄 목록으로 바꾼다. 줄 구분은 쉼표(,) / 세로줄(|) / 줄바꿈.
        /// 모델이 읽지 않는 문자('.', '-', '/', 공백 등)는 빼고 대문자로 바꾼다.
        /// 예: "GWTB017P, 2030.03.16" -> ["GWTB017P", "20300316"]
        /// </summary>
        public static string[] ParseExpected(string text, string[] classNames, IDictionary<string, string> labelMap)
        {
            if (string.IsNullOrWhiteSpace(text)) return new string[0];
            var known = new HashSet<string>(classNames ?? new string[0]);
            if (labelMap != null)
                foreach (KeyValuePair<string, string> kv in labelMap) known.Add(kv.Value);

            var lines = new List<string>();
            foreach (string part in text.Split(new[] { ',', '|', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var sb = new StringBuilder();
                foreach (char ch in part.ToUpperInvariant())
                {
                    string s = ch.ToString();
                    string mapped;
                    if (labelMap != null && labelMap.TryGetValue(s, out mapped)) s = mapped; // 예: O -> 0
                    if (known.Contains(s)) sb.Append(s);
                }
                if (sb.Length > 0) lines.Add(sb.ToString());
            }
            return lines.ToArray();
        }

        public static VerifyResult Verify(IList<Detection> candidates, string[] expectedLines, string[] classNames,
                                          IDictionary<string, string> labelMap, VerifyOptions options)
        {
            if (options == null) options = new VerifyOptions();
            var scorer = new ExpectedScorer(classNames, labelMap);
            List<List<Detection>> segments = BuildSegments(candidates ?? new List<Detection>());

            var result = new VerifyResult { Lines = new List<LineResult>() };
            float minCenterY = float.MinValue;
            var used = new HashSet<int>();

            for (int li = 0; li < expectedLines.Length; li++)
            {
                string exp = expectedLines[li];

                // 아직 안 쓴 묶음 중 이전 줄보다 아래에 있는 것에서 가장 잘 맞는 묶음 선택
                int bestSeg = -1;
                float bestTotal = float.MinValue;
                Detection[] bestMatch = null;
                for (int s = 0; s < segments.Count; s++)
                {
                    if (used.Contains(s)) continue;
                    if (SegmentCenterY(segments[s]) <= minCenterY) continue;
                    Detection[] match;
                    float total = Align(exp, segments[s], scorer, out match);
                    if (total > bestTotal)
                    {
                        bestTotal = total;
                        bestSeg = s;
                        bestMatch = match;
                    }
                }

                if (bestSeg >= 0 && bestMatch.Any(m => m != null))
                {
                    used.Add(bestSeg);
                    minCenterY = SegmentCenterY(segments[bestSeg]);
                }
                else
                {
                    bestMatch = new Detection[exp.Length]; // 맞는 묶음 없음 -> 전부 없음
                }

                result.Lines.Add(BuildLine(li, exp, bestMatch, scorer, options));
            }
            return result;
        }

        // ------------------------------------------------------------------ 줄/묶음 만들기
        private static List<List<Detection>> BuildSegments(IList<Detection> candidates)
        {
            var segments = new List<List<Detection>>();
            foreach (List<Detection> line in TextAssembler.GroupLines(candidates))
            {
                float medianH = Median(line.Select(d => d.Box.Height));
                var seg = new List<Detection>();
                for (int k = 0; k < line.Count; k++)
                {
                    if (k > 0)
                    {
                        float gap = line[k].Box.Left - line[k - 1].Box.Right;
                        if (gap > medianH * 2.5f) // 옆 박스 / 한글 라벨 등과 분리
                        {
                            segments.Add(seg);
                            seg = new List<Detection>();
                        }
                    }
                    seg.Add(line[k]);
                }
                if (seg.Count > 0) segments.Add(seg);
            }
            return segments;
        }

        private static float SegmentCenterY(List<Detection> seg)
        {
            return seg.Average(d => d.Center.Y);
        }

        // ------------------------------------------------------------------ 대응 (동적 계획법)
        /// <summary>
        /// 기대 문자열(m 글자)과 왼쪽→오른쪽 후보(n 개)를 순서를 지키며 대응시킨다.
        ///  - 대응: 기대 문자 점수 + 작은 보너스
        ///  - 후보 건너뛰기(잡음): 그 후보의 최고 점수 x SkipPenalty 만큼 감점
        ///  - 기대 문자 건너뛰기(글자 없음): 0
        /// match[i] = 기대 문자 i 에 대응된 후보 (없으면 null)
        /// </summary>
        private static float Align(string exp, List<Detection> seg, ExpectedScorer scorer, out Detection[] match)
        {
            int m = exp.Length, n = seg.Count;
            var dp = new float[m + 1, n + 1];
            var how = new byte[m + 1, n + 1]; // 1 = 대응, 2 = 후보 건너뜀, 3 = 기대 문자 건너뜀
            for (int j = 1; j <= n; j++)
            {
                dp[0, j] = dp[0, j - 1] - SkipPenalty * seg[j - 1].Confidence;
                how[0, j] = 2;
            }
            for (int i = 1; i <= m; i++)
            {
                dp[i, 0] = dp[i - 1, 0];
                how[i, 0] = 3;
                for (int j = 1; j <= n; j++)
                {
                    float best = dp[i - 1, j - 1] + scorer.Score(seg[j - 1], exp[i - 1]) + MatchBonus;
                    byte b = 1;
                    float skipDet = dp[i, j - 1] - SkipPenalty * seg[j - 1].Confidence;
                    if (skipDet > best) { best = skipDet; b = 2; }
                    float skipExp = dp[i - 1, j];
                    if (skipExp > best) { best = skipExp; b = 3; }
                    dp[i, j] = best;
                    how[i, j] = b;
                }
            }

            match = new Detection[m];
            int ii = m, jj = n;
            while (ii > 0 || jj > 0)
            {
                byte b = how[ii, jj];
                if (b == 1) { match[ii - 1] = seg[jj - 1]; ii--; jj--; }
                else if (b == 2) jj--;
                else ii--;
            }
            return dp[m, n];
        }

        // ------------------------------------------------------------------ 판정
        private static LineResult BuildLine(int lineIndex, string exp, Detection[] match, ExpectedScorer scorer, VerifyOptions opt)
        {
            var chars = new List<CharResult>();
            for (int i = 0; i < exp.Length; i++)
            {
                Detection d = match[i];
                var c = new CharResult { Line = lineIndex, Index = i, Expected = exp[i], Detection = d };
                if (d == null)
                {
                    c.Status = CharStatus.Missing;
                }
                else
                {
                    c.Score = scorer.Score(d, exp[i]);
                    c.ReadLabel = d.Label;
                    c.ReadScore = d.Confidence;
                    c.Box = d.Box;
                    if (c.Score >= opt.PassScore) c.Status = CharStatus.Ok;
                    else if (d.Label != exp[i].ToString() && d.Confidence >= opt.WrongScore) c.Status = CharStatus.Wrong;
                    else c.Status = CharStatus.Low;
                }
                chars.Add(c);
            }
            EstimateMissingBoxes(chars);
            return new LineResult { Expected = exp, Chars = chars };
        }

        /// <summary>못 찾은 자리의 위치를 찾은 글자들의 간격(직선 맞춤)으로 추정한다.</summary>
        private static void EstimateMissingBoxes(List<CharResult> chars)
        {
            var found = chars.Where(c => c.Detection != null).ToList();
            if (found.Count < 2 || found.Count == chars.Count) return;

            // 중심 X = a + b * 자리번호 (최소제곱)
            double mi = found.Average(c => (double)c.Index);
            double mx = found.Average(c => (double)c.Box.X + c.Box.Width / 2.0);
            double sxx = found.Sum(c => (c.Index - mi) * (c.Index - mi));
            double sxy = found.Sum(c => (c.Index - mi) * (c.Box.X + c.Box.Width / 2.0 - mx));
            double b = sxx > 0 ? sxy / sxx : 0;
            double a = mx - b * mi;
            float cy = (float)found.Average(c => c.Box.Y + c.Box.Height / 2.0);
            float w = Median(found.Select(c => c.Box.Width));
            float h = Median(found.Select(c => c.Box.Height));

            foreach (CharResult c in chars.Where(c => c.Detection == null))
            {
                float cx = (float)(a + b * c.Index);
                c.Box = new RectangleF(cx - w / 2f, cy - h / 2f, w, h);
                c.Estimated = true;
            }
        }

        private static float Median(IEnumerable<float> values)
        {
            float[] v = values.OrderBy(x => x).ToArray();
            if (v.Length == 0) return 0;
            return v.Length % 2 == 1 ? v[v.Length / 2] : (v[v.Length / 2 - 1] + v[v.Length / 2]) / 2f;
        }

        /// <summary>검출 하나에서 "기대 문자"의 점수를 꺼낸다. LabelMap(예: O→0)으로 합쳐지는 클래스는 함께 본다.</summary>
        private sealed class ExpectedScorer
        {
            private readonly Dictionary<char, int[]> _classesFor = new Dictionary<char, int[]>();

            public ExpectedScorer(string[] classNames, IDictionary<string, string> labelMap)
            {
                classNames = classNames ?? new string[0];
                var tmp = new Dictionary<char, List<int>>();
                for (int c = 0; c < classNames.Length; c++)
                {
                    string label = classNames[c];
                    string mapped;
                    if (labelMap != null && labelMap.TryGetValue(label, out mapped)) label = mapped;
                    if (string.IsNullOrEmpty(label) || label.Length != 1) continue;
                    List<int> list;
                    if (!tmp.TryGetValue(label[0], out list)) tmp[label[0]] = list = new List<int>();
                    list.Add(c);
                }
                foreach (KeyValuePair<char, List<int>> kv in tmp) _classesFor[kv.Key] = kv.Value.ToArray();
            }

            public float Score(Detection d, char expected)
            {
                // 클래스별 점수가 없는 모델(end-to-end 출력)은 최고 점수 글자만 비교
                if (d.Scores == null)
                    return d.Label == expected.ToString() ? d.Confidence : 0f;

                int[] classes;
                if (!_classesFor.TryGetValue(expected, out classes)) return 0f;
                float best = 0f;
                foreach (int c in classes)
                    if (c < d.Scores.Length && d.Scores[c] > best) best = d.Scores[c];
                return best;
            }
        }
    }
}
