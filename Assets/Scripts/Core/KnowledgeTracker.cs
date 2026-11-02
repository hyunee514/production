using System;
using System.Collections.Generic;
using UnityEngine;

namespace AutoSurvival.Core
{
    [Serializable]
    public class ConceptRecord
    {
        public int attempts;
        public int successes;
        public float lastSeenTime;

        // 0~1. 최근 실패가 많을수록 낮음. 신규 개념은 0.
        public float Mastery => attempts == 0 ? 0f : (float)successes / attempts;
    }

    /// <summary>
    /// 개념별 숙련도 추적. 퍼즐 출제기가 '약한 개념'을 더 자주 뽑도록 가중치를 제공한다.
    /// (간격 반복 학습을 게임 루프에 숨기는 장치)
    /// </summary>
    public class KnowledgeTracker : MonoBehaviour
    {
        public static KnowledgeTracker Instance { get; private set; }

        private readonly Dictionary<ConceptTag, ConceptRecord> _records = new();

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        public ConceptRecord Get(ConceptTag tag)
        {
            if (!_records.TryGetValue(tag, out var r))
                _records[tag] = r = new ConceptRecord();
            return r;
        }

        public void Report(ConceptTag tag, bool success)
        {
            var r = Get(tag);
            r.attempts++;
            if (success) r.successes++;
            r.lastSeenTime = Time.time;
        }

        /// <summary>약할수록, 오래 안 봤을수록 높은 가중치.</summary>
        public float PickWeight(ConceptTag tag)
        {
            var r = Get(tag);
            float weakness = 1f - r.Mastery;
            float staleness = Mathf.Clamp01((Time.time - r.lastSeenTime) / 600f);
            return 0.2f + weakness * 0.6f + staleness * 0.2f;
        }

        public ConceptTag PickWeakest(IReadOnlyList<ConceptTag> candidates)
        {
            float total = 0f;
            foreach (var c in candidates) total += PickWeight(c);
            float roll = UnityEngine.Random.value * total;
            foreach (var c in candidates)
            {
                roll -= PickWeight(c);
                if (roll <= 0f) return c;
            }
            return candidates[candidates.Count - 1];
        }

        /// <summary>세이브에서 복원. secondsSinceSeen만큼 '오래 안 본' 상태로 되돌린다 (Time.time은 세션마다 0부터 시작하므로).</summary>
        public void Restore(ConceptTag tag, int attempts, int successes, float secondsSinceSeen)
        {
            var r = Get(tag);
            r.attempts = Mathf.Max(0, attempts);
            r.successes = Mathf.Clamp(successes, 0, r.attempts);
            r.lastSeenTime = Time.time - Mathf.Max(0f, secondsSinceSeen);
        }

        /// <summary>새 게임/불러오기 전에 기록을 비운다.</summary>
        public void ClearAll() => _records.Clear();

        /// <summary>마지막으로 본 지 몇 초 지났는가 (세이브용).</summary>
        public float SecondsSinceSeen(ConceptTag tag) => Mathf.Max(0f, Time.time - Get(tag).lastSeenTime);

        /// <summary>교사용 대시보드/세이브용 요약.</summary>
        public IEnumerable<KeyValuePair<ConceptTag, ConceptRecord>> All() => _records;
    }
}
