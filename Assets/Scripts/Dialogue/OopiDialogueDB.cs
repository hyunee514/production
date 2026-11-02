using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using AutoSurvival.Core;
using AutoSurvival.Puzzles;

namespace AutoSurvival.Dialogue
{
    public enum OopiSituation
    {
        PuzzleEnter,      // 퍼즐 진입
        TimeLow,          // 시간 촉박
        Diagnosis,        // 실패 원인 진단 (cause 키로 세분화)
        Failure,          // 일반 실패 (시간 초과 등)
        Success,          // 퍼즐 성공
        Repaired,         // 처음 수리 완료
        TierUp,           // 티어 업그레이드
        SurvivalWarning,  // 생존 수치 경고 (cause = hunger/thirst/warmth/health/stamina)
        IslandArrive,     // 섬 도착
        IslandCleared,    // 섬 클리어
        Idle              // 한가할 때 혼잣말
    }

    public enum OopiPuzzleFilter { Any, Pipe, Ladder, Hydraulic, Pneumatic }

    /// <summary>표정·목소리 연출용 힌트 (UI가 아이콘/애니메이션을 고르는 데 사용).</summary>
    public enum OopiMood { Neutral, Cheer, Worry, Joke, Serious }

    [Serializable]
    public class OopiLine
    {
        public string id;
        public OopiSituation situation;
        public OopiPuzzleFilter puzzle = OopiPuzzleFilter.Any;
        [Tooltip("실패 원인 키 (예: pneu.ExhaustBlocked). 비우면 원인과 무관한 대사")] public string cause;
        public bool restrictConcept;
        public ConceptTag concept;
        [Range(1, 5)] public int minTier = 1;
        [Range(1, 5)] public int maxTier = 5;
        public OopiMood mood;
        [TextArea] public string text;
        [Tooltip("기능사 지식 한 줄 팁 (선택). UI가 작은 글씨로 덧붙인다")] [TextArea] public string tip;
        public float weight = 1f;
        [Tooltip("게임 전체에서 한 번만 말한다 (저장됨)")] public bool once;
    }

    /// <summary>"지금 오피가 무슨 말을 해야 하는가"의 상황 묘사.</summary>
    public class OopiQuery
    {
        public OopiSituation situation;
        public OopiPuzzleFilter puzzle = OopiPuzzleFilter.Any;
        public string cause;
        public bool hasConcept;
        public ConceptTag concept;
        public int tier = 1;
        public readonly Dictionary<string, string> vars = new();

        public OopiQuery(OopiSituation situation) { this.situation = situation; }

        public OopiQuery Puzzle(OopiPuzzleFilter p) { puzzle = p; return this; }
        public OopiQuery Cause(string c) { cause = c; return this; }
        public OopiQuery Concept(ConceptTag c) { hasConcept = true; concept = c; return this; }
        public OopiQuery Tier(int t) { tier = t; return this; }
        public OopiQuery Var(string key, string value) { vars[key] = value; return this; }
    }

    public sealed class OopiSpeech
    {
        public OopiLine line;
        public string text;      // 변수 치환 완료
        public string tip;       // 치환 완료, 없으면 빈 문자열
        public OopiMood mood => line.mood;
    }

    /// <summary>
    /// 실패 원인 키. 퍼즐이 내는 판정(enum)을 대사 DB의 cause 문자열로 바꾼다.
    /// 새 실패 유형을 만들면 여기에 키를 추가하고, DB 점검(Validate)이 대사가 빠진 키를 알려 준다.
    /// </summary>
    public static class OopiCauses
    {
        public const string Timeout = "timeout";

        public static string From(PneumaticFault f) => f == PneumaticFault.None ? null : "pneu." + f;

        public static string From(HydraulicOutcome o) => o == HydraulicOutcome.Success ? null : "hyd." + o;

        /// <summary>유압은 '과열'이 별도의 연출 신호다. 과열의 원인이 느린 설계(TooSlow)면 그쪽 대사를 쓴다.</summary>
        public static string From(HydraulicCue cue, HydraulicReport report)
        {
            switch (cue)
            {
                case HydraulicCue.UnsafeRelief: return "hyd.UnsafeRelief";
                case HydraulicCue.ReliefVent: return "hyd.ReliefOpens";
                case HydraulicCue.HoseBurst: return "hyd.HoseBurst";
                case HydraulicCue.Strain: return "hyd.InsufficientForce";
                case HydraulicCue.Overheat:
                    return report != null && report.outcome == HydraulicOutcome.TooSlow ? "hyd.TooSlow" : "hyd.Overheat";
                default: return null;
            }
        }

        public static string From(LadderTestReport r)
        {
            if (r == null || r.passed) return null;
            if (r.interlockViolation) return "ladder.Interlock";
            if (r.unstable) return "ladder.Unstable";
            return r.expected ? "ladder.ShouldBeOn" : "ladder.ShouldBeOff";
        }

        /// <summary>DB가 반드시 2개 이상의 대사를 가져야 하는 진단 키 목록.</summary>
        public static IEnumerable<string> RequiredDiagnosisKeys()
        {
            foreach (PneumaticFault f in Enum.GetValues(typeof(PneumaticFault)))
                if (f != PneumaticFault.None) yield return From(f);
            foreach (HydraulicOutcome o in Enum.GetValues(typeof(HydraulicOutcome)))
                if (o != HydraulicOutcome.Success) yield return From(o);
            yield return "hyd.Overheat";
            yield return "ladder.Interlock"; yield return "ladder.Unstable";
            yield return "ladder.ShouldBeOn"; yield return "ladder.ShouldBeOff";
        }
    }

    /// <summary>{이름} 자리표시자 치환 (UnityEngine 의존 없음). 모르는 이름은 그대로 둔다.</summary>
    public static class OopiText
    {
        /// <summary>대사에서 쓸 수 있는 자리표시자.</summary>
        public static readonly string[] KnownVars =
            { "player", "machine", "tier", "nexttier", "concept", "seconds", "island", "percent", "item" };

        public static string Format(string text, IDictionary<string, string> vars)
        {
            if (string.IsNullOrEmpty(text) || vars == null || vars.Count == 0) return text ?? "";

            var sb = new StringBuilder(text.Length + 16);
            int i = 0;
            while (i < text.Length)
            {
                char c = text[i];
                if (c == '{')
                {
                    int close = text.IndexOf('}', i + 1);
                    if (close > i)
                    {
                        string key = text.Substring(i + 1, close - i - 1);
                        if (vars.TryGetValue(key, out var value)) { sb.Append(value); i = close + 1; continue; }
                    }
                }
                sb.Append(c);
                i++;
            }
            return sb.ToString();
        }

        /// <summary>대사 속 자리표시자 이름 목록 (점검용).</summary>
        public static List<string> Placeholders(string text)
        {
            var list = new List<string>();
            if (string.IsNullOrEmpty(text)) return list;
            int i = 0;
            while ((i = text.IndexOf('{', i)) >= 0)
            {
                int close = text.IndexOf('}', i + 1);
                if (close < 0) break;
                list.Add(text.Substring(i + 1, close - i - 1));
                i = close + 1;
            }
            return list;
        }
    }

    /// <summary>
    /// 같은 대사가 반복되지 않게 하는 기억. 최근 N개는 피하고, 많이 한 말은 가중치를 낮춘다.
    /// counts는 저장되어 'once' 대사와 반복 억제가 세션을 넘어 유지된다.
    /// </summary>
    public sealed class OopiMemory
    {
        private readonly Dictionary<string, int> _counts = new();
        private readonly Queue<string> _recent = new();
        public int RecentWindow { get; set; } = 4;

        public int Count(string id) => _counts.TryGetValue(id, out var c) ? c : 0;
        public bool IsRecent(string id) => _recent.Contains(id);

        public void Record(string id)
        {
            _counts[id] = Count(id) + 1;
            _recent.Enqueue(id);
            while (_recent.Count > RecentWindow) _recent.Dequeue();
        }

        public IEnumerable<KeyValuePair<string, int>> Export() => _counts;

        public void Import(IEnumerable<KeyValuePair<string, int>> counts)
        {
            _counts.Clear(); _recent.Clear();
            foreach (var kv in counts) if (kv.Value > 0) _counts[kv.Key] = kv.Value;
        }

        public void Clear() { _counts.Clear(); _recent.Clear(); }
    }

    /// <summary>
    /// 대사 선택 규칙 (UnityEngine 의존 없음 -> 테스트 가능).
    ///  1) 상황·퍼즐·티어·개념 조건이 맞고, cause가 비었거나 같은 대사만 후보
    ///  2) 구체적일수록 우선: cause 일치(4) > 퍼즐 지정(2) > 개념 지정(1)
    ///  3) 가장 구체적인 단계부터 '최근에 안 한 말'이 있으면 거기서 가중 추첨 (once 대사는 이미 했으면 제외)
    ///  4) 전부 최근에 한 말이면 최근 제한을 풀고 같은 단계에서 다시 추첨
    /// </summary>
    public static class OopiSelector
    {
        public static int Specificity(OopiLine l, OopiQuery q) =>
            (!string.IsNullOrEmpty(l.cause) ? 4 : 0) + (l.puzzle != OopiPuzzleFilter.Any ? 2 : 0) + (l.restrictConcept ? 1 : 0);

        public static bool Matches(OopiLine l, OopiQuery q)
        {
            if (l.situation != q.situation) return false;
            if (l.puzzle != OopiPuzzleFilter.Any && l.puzzle != q.puzzle) return false;
            if (q.tier < l.minTier || q.tier > l.maxTier) return false;
            if (l.restrictConcept && (!q.hasConcept || l.concept != q.concept)) return false;
            if (!string.IsNullOrEmpty(l.cause) && l.cause != q.cause) return false;
            return true;
        }

        public static OopiLine Pick(IEnumerable<OopiLine> lines, OopiQuery q, OopiMemory memory, Func<float> rng)
        {
            var candidates = lines.Where(l => Matches(l, q) && !(l.once && memory.Count(l.id) > 0)).ToList();
            if (candidates.Count == 0) return null;

            var byLevel = candidates.GroupBy(l => Specificity(l, q)).OrderByDescending(g => g.Key).Select(g => g.ToList()).ToList();

            foreach (var level in byLevel)
            {
                var fresh = level.Where(l => !memory.IsRecent(l.id)).ToList();
                if (fresh.Count > 0) return Weighted(fresh, memory, rng);
            }
            return Weighted(byLevel[0], memory, rng);
        }

        private static OopiLine Weighted(List<OopiLine> pool, OopiMemory memory, Func<float> rng)
        {
            float W(OopiLine l) => Math.Max(0.01f, l.weight) / (1f + 0.5f * memory.Count(l.id));
            float total = 0f;
            foreach (var l in pool) total += W(l);

            float roll = Math.Min(0.999999f, Math.Max(0f, rng())) * total;
            foreach (var l in pool)
            {
                roll -= W(l);
                if (roll < 0f) return l;
            }
            return pool[pool.Count - 1];
        }
    }

    /// <summary>
    /// 동행 AI 오피(OOPI)의 대사 데이터베이스 (ScriptableObject).
    /// Create ▸ AutoSurvival ▸ Oopi Dialogue DB 로 만들고, 우클릭 메뉴 '기본 대사 채우기'로 샘플을 넣는다.
    /// </summary>
    [CreateAssetMenu(fileName = "OopiDialogueDB", menuName = "AutoSurvival/Oopi Dialogue DB")]
    public class OopiDialogueDB : ScriptableObject
    {
        [SerializeField] private string playerName = "엔지니어";
        [SerializeField] private List<OopiLine> lines = new();

        public IReadOnlyList<OopiLine> Lines => lines;
        public string PlayerName => playerName;

        /// <summary>실행 중 기억. 세이브에 싣고 내리려면 ExportMemory/ImportMemory를 사용한다.</summary>
        public OopiMemory Memory { get; } = new();

        /// <summary>상황에 맞는 대사를 고른다. 진단 대사가 없으면 일반 실패 대사로 대신한다. 없으면 null.</summary>
        public OopiSpeech Say(OopiQuery q, Func<float> rng = null)
        {
            rng ??= () => UnityEngine.Random.value;

            var line = OopiSelector.Pick(lines, q, Memory, rng);
            if (line == null && q.situation == OopiSituation.Diagnosis)
            {
                var fallback = new OopiQuery(OopiSituation.Failure) { puzzle = q.puzzle, tier = q.tier, hasConcept = q.hasConcept, concept = q.concept };
                line = OopiSelector.Pick(lines, fallback, Memory, rng);
            }
            if (line == null) return null;

            Memory.Record(line.id);

            var vars = new Dictionary<string, string>(q.vars);
            if (!vars.ContainsKey("player")) vars["player"] = playerName;
            if (!vars.ContainsKey("tier")) vars["tier"] = q.tier.ToString();
            if (!vars.ContainsKey("nexttier")) vars["nexttier"] = Math.Min(5, q.tier + 1).ToString();
            if (q.hasConcept && !vars.ContainsKey("concept")) vars["concept"] = DisplayNames.Concept(q.concept);

            return new OopiSpeech
            {
                line = line,
                text = OopiText.Format(line.text, vars),
                tip = OopiText.Format(line.tip, vars)
            };
        }

        public List<KeyValuePair<string, int>> ExportMemory() => Memory.Export().ToList();
        public void ImportMemory(IEnumerable<KeyValuePair<string, int>> counts) => Memory.Import(counts);

        // ───────── 데이터 관리 ─────────

        [ContextMenu("기본 대사 채우기 (현재 대사를 교체)")]
        public void SeedDefaults()
        {
            lines = DefaultOopiData.Lines();
            foreach (var issue in Validate()) Debug.LogWarning($"[OopiDialogueDB] {issue}", this);
        }

        /// <summary>데이터 오류 점검: 중복 id, 빈 대사, 모르는 자리표시자, 대사가 빠진 상황·진단 키.</summary>
        public List<string> Validate()
        {
            var issues = new List<string>();
            var known = new HashSet<string>(OopiText.KnownVars);

            foreach (var g in lines.GroupBy(l => l.id).Where(g => g.Count() > 1)) issues.Add($"대사 id 중복: {g.Key}");

            foreach (var l in lines)
            {
                if (string.IsNullOrWhiteSpace(l.id)) issues.Add("id가 비어 있는 대사가 있음");
                if (string.IsNullOrWhiteSpace(l.text)) issues.Add($"'{l.id}': 대사가 비어 있음");
                if (l.minTier > l.maxTier) issues.Add($"'{l.id}': minTier가 maxTier보다 큼");
                if (l.situation == OopiSituation.Diagnosis && string.IsNullOrEmpty(l.cause) && l.puzzle == OopiPuzzleFilter.Any)
                    issues.Add($"'{l.id}': 진단 대사에 cause도 퍼즐도 지정되지 않음 (모든 진단에 나오게 됨)");
                foreach (var ph in OopiText.Placeholders(l.text).Concat(OopiText.Placeholders(l.tip)))
                    if (!known.Contains(ph)) issues.Add($"'{l.id}': 알 수 없는 자리표시자 {{{ph}}}");
            }

            foreach (OopiSituation s in Enum.GetValues(typeof(OopiSituation)))
                if (!lines.Any(l => l.situation == s)) issues.Add($"{s} 상황의 대사가 하나도 없음");

            foreach (var key in OopiCauses.RequiredDiagnosisKeys())
                if (lines.Count(l => l.situation == OopiSituation.Diagnosis && l.cause == key) < 2)
                    issues.Add($"진단 '{key}' 대사가 2개 미만 (같은 말 반복 방지)");

            foreach (OopiPuzzleFilter p in Enum.GetValues(typeof(OopiPuzzleFilter)))
            {
                if (p == OopiPuzzleFilter.Any) continue;
                foreach (var s in new[] { OopiSituation.PuzzleEnter, OopiSituation.Success, OopiSituation.TimeLow })
                    if (!lines.Any(l => l.situation == s && l.puzzle == p))
                        issues.Add($"{p} 퍼즐의 {s} 대사가 없음");
                if (!lines.Any(l => l.situation == OopiSituation.Failure && l.puzzle == p && l.cause == OopiCauses.Timeout))
                    issues.Add($"{p} 퍼즐의 시간 초과(timeout) 실패 대사가 없음");
            }

            for (int tier = 2; tier <= 5; tier++)       // 업그레이드로 도달하는 티어는 2~5
            {
                var q = new OopiQuery(OopiSituation.TierUp) { tier = tier };
                if (!lines.Any(l => OopiSelector.Matches(l, q))) issues.Add($"티어 {tier} 업그레이드 대사가 없음");
            }
            return issues;
        }
    }
}
