using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using AutoSurvival.Core;
using AutoSurvival.Puzzles;

namespace AutoSurvival.Data
{
    /// <summary>
    /// 기출문제 + 퍼즐 시나리오 데이터베이스 (ScriptableObject).
    ///  - questions: ConceptTag로 분류된 4지선다 문제 (자체 문제 + 교사가 가져온 기출)
    ///  - scenarios: 어떤 퍼즐을 어떤 파라미터로 열지, 끝난 뒤 어떤 문제를 '현장 노트'로 보여줄지
    /// 출제는 KnowledgeTracker의 숙련도 가중치를 따라 약한 개념이 더 자주 나온다.
    /// </summary>
    [CreateAssetMenu(fileName = "PuzzleScenarioDB", menuName = "AutoSurvival/Puzzle Scenario DB")]
    public class PuzzleScenarioDB : ScriptableObject
    {
        [SerializeField] private List<ExamQuestion> questions = new();
        [SerializeField] private List<PuzzleScenarioEntry> scenarios = new();

        public IReadOnlyList<ExamQuestion> Questions => questions;
        public IReadOnlyList<PuzzleScenarioEntry> Scenarios => scenarios;

        private Dictionary<string, ExamQuestion> _byId;

        private Dictionary<string, ExamQuestion> Index =>
            _byId ??= questions.Where(q => !string.IsNullOrEmpty(q.id)).GroupBy(q => q.id).ToDictionary(g => g.Key, g => g.First());

        private void OnEnable() => _byId = null;

        // ───────── 조회 ─────────

        public ExamQuestion GetQuestion(string id) => id != null && Index.TryGetValue(id, out var q) ? q : null;

        public IEnumerable<ExamQuestion> QuestionsFor(ConceptTag concept) => questions.Where(q => q.concept == concept);

        public IEnumerable<ExamQuestion> QuestionsFor(ConceptTag concept, QuestionOrigin origin) =>
            questions.Where(q => q.concept == concept && q.origin == origin);

        // ───────── 출제 ─────────

        /// <summary>
        /// 기계 종류(퍼즐 종류)와 티어에 맞는 시나리오를 뽑는다.
        /// 후보 중 숙련도가 낮은 개념(KnowledgeTracker 가중치)이 더 자주 나온다. rng는 테스트용 주입.
        /// </summary>
        public PuzzleScenarioEntry PickScenario(PuzzleKind kind, int tier, Func<float> rng = null)
        {
            var candidates = scenarios.Where(s => s.Matches(kind, tier)).ToList();
            if (candidates.Count == 0) return null;
            return WeightedPick.Pick(candidates, ConceptWeight, rng ?? (() => UnityEngine.Random.value));
        }

        private static float ConceptWeight(PuzzleScenarioEntry s) =>
            KnowledgeTracker.Instance != null ? KnowledgeTracker.Instance.PickWeight(s.concept) : 1f;

        /// <summary>
        /// 퍼즐 종료 후 보여줄 '현장 노트' 문제. 연결된 문제 중에서 고르되
        /// 같은 개념의 실제 기출(PastExam)이 있으면 우선하고, 연결이 비어 있으면 같은 개념의 문제로 대체한다.
        /// </summary>
        public ExamQuestion PickReviewQuestion(PuzzleScenarioEntry scenario, Func<float> rng = null)
        {
            if (scenario == null) return null;
            rng ??= () => UnityEngine.Random.value;

            var pool = scenario.questionIds.Select(GetQuestion).Where(q => q != null).ToList();
            if (pool.Count == 0) pool = QuestionsFor(scenario.concept).ToList();
            if (pool.Count == 0) return null;

            // 실제 기출 우선: 연결된 문제 중 기출 -> 없으면 같은 개념의 기출 -> 없으면 연결된 자체 문제
            var past = pool.Where(q => q.origin == QuestionOrigin.PastExam).ToList();
            if (past.Count == 0) past = QuestionsFor(scenario.concept, QuestionOrigin.PastExam).ToList();
            if (past.Count > 0) pool = past;

            return pool[Mathf.Clamp((int)(rng() * pool.Count), 0, pool.Count - 1)];
        }

        /// <summary>현장 노트에 대한 플레이어 답. 정오답은 개념 숙련도에 반영된다. 반환: 정답 여부.</summary>
        public bool RecordAnswer(ExamQuestion question, int choiceIndex)
        {
            if (question == null) return false;
            bool correct = choiceIndex == question.answerIndex;
            KnowledgeTracker.Instance?.Report(question.concept, correct);
            return correct;
        }

        // ───────── 데이터 관리 ─────────

        /// <summary>자체(Original) 문제와 기본 시나리오를 시드 데이터로 교체한다. PastExam 문제는 건드리지 않는다.</summary>
        [ContextMenu("기본 데이터 채우기 (자체 문제만 교체)")]
        public void SeedDefaults()
        {
            questions.RemoveAll(q => q.origin == QuestionOrigin.Original);
            questions.AddRange(DefaultScenarioData.Questions());
            scenarios = DefaultScenarioData.Entries();
            _byId = null;
            LogIssues();
        }

        /// <summary>CSV 텍스트의 문제를 id 기준으로 병합(같은 id는 덮어쓰기). 오류 목록을 반환한다.</summary>
        public List<string> ImportCsv(string csvText)
        {
            var result = ExamCsvParser.Parse(csvText);
            foreach (var q in result.questions)
            {
                int i = questions.FindIndex(x => x.id == q.id);
                if (i >= 0) questions[i] = q; else questions.Add(q);
            }
            _byId = null;
            return result.errors;
        }

        /// <summary>데이터 오류 점검: 정답 범위, 중복 id, 존재하지 않는 문제 id, 개념별 문제 수 부족 등.</summary>
        public List<string> Validate()
        {
            var issues = new List<string>();

            foreach (var g in questions.GroupBy(q => q.id).Where(g => g.Count() > 1))
                issues.Add($"문제 id 중복: {g.Key}");
            foreach (var q in questions)
                if (!q.IsValid(out string err)) issues.Add($"문제 '{q.id}': {err}");

            foreach (var g in scenarios.GroupBy(s => s.id).Where(g => g.Count() > 1))
                issues.Add($"시나리오 id 중복: {g.Key}");

            foreach (var s in scenarios)
            {
                foreach (var qid in s.questionIds)
                    if (GetQuestion(qid) == null) issues.Add($"시나리오 '{s.id}': 존재하지 않는 문제 id '{qid}'");

                if (s.minTier > s.maxTier) issues.Add($"시나리오 '{s.id}': minTier가 maxTier보다 큼");

                if (s.kind == PuzzleKind.Ladder && LadderChallengeLibrary.ById(s.ladderChallengeId) == null)
                    issues.Add($"시나리오 '{s.id}': 알 수 없는 래더 챌린지 '{s.ladderChallengeId}'");
                if (s.kind == PuzzleKind.Hydraulic && HydraulicLevelLibrary.ById(s.hydraulicLevelId) == null)
                    issues.Add($"시나리오 '{s.id}': 알 수 없는 유압 레벨 '{s.hydraulicLevelId}'");
                if (s.kind == PuzzleKind.Pneumatic && PneumaticLevelLibrary.ById(s.pneumaticLevelId) == null)
                    issues.Add($"시나리오 '{s.id}': 알 수 없는 공압 레벨 '{s.pneumaticLevelId}'");
            }

            // 모든 (퍼즐 종류, 티어) 조합에 시나리오가 있어야 기계 상호작용이 비지 않는다.
            foreach (PuzzleKind kind in Enum.GetValues(typeof(PuzzleKind)))
                for (int tier = 1; tier <= 5; tier++)
                    if (!scenarios.Any(s => s.Matches(kind, tier)))
                        issues.Add($"{kind} 퍼즐의 티어 {tier}에 해당하는 시나리오가 없음");

            return issues;
        }

        private void LogIssues()
        {
            foreach (var issue in Validate()) Debug.LogWarning($"[PuzzleScenarioDB] {issue}", this);
        }

#if UNITY_EDITOR
        private void OnValidate() => _byId = null;
#endif
    }

    /// <summary>가중치 기반 무작위 선택. UnityEngine 의존 없음(테스트 가능).</summary>
    public static class WeightedPick
    {
        public static T Pick<T>(IList<T> items, Func<T, float> weight, Func<float> rng)
        {
            if (items == null || items.Count == 0) return default;

            float total = 0f;
            foreach (var it in items) total += Math.Max(0f, weight(it));
            if (total <= 0f) return items[Math.Min(items.Count - 1, Math.Max(0, (int)(rng() * items.Count)))];

            float roll = rng() * total;
            foreach (var it in items)
            {
                roll -= Math.Max(0f, weight(it));
                if (roll <= 0f) return it;
            }
            return items[items.Count - 1];
        }
    }
}
