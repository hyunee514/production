using System;
using UnityEngine;

namespace AutoSurvival.Survival
{
    public enum StatType { Health, Hunger, Thirst, Warmth, Stamina }

    /// <summary>
    /// 플레이어 생존 수치. 퍼즐 '압박'(PuzzleSession.Pressure)과 연결되어,
    /// 수치가 낮을수록 퍼즐 제한시간이 줄어드는 긴장감을 만든다.
    /// </summary>
    public class SurvivalStats : MonoBehaviour
    {
        [Serializable]
        public class Stat
        {
            public StatType type;
            public float max = 100f;
            public float current = 100f;
            [Tooltip("초당 감소량 (음수면 회복)")] public float decayPerSecond = 0.1f;
            public float Normalized => max <= 0f ? 0f : current / max;
        }

        [SerializeField] private Stat[] stats =
        {
            new Stat { type = StatType.Health,  decayPerSecond = 0f },
            new Stat { type = StatType.Hunger,  decayPerSecond = 0.08f },
            new Stat { type = StatType.Thirst,  decayPerSecond = 0.12f },
            new Stat { type = StatType.Warmth,  decayPerSecond = 0.05f },
            new Stat { type = StatType.Stamina, decayPerSecond = -2f },
        };

        public event Action<StatType, float> OnStatChanged;
        public event Action OnDeath;

        /// <summary>외부(모닥불, 눈보라 등)가 설정하는 추가 감소 배율.</summary>
        public float WarmthDecayMultiplier { get; set; } = 1f;

        private bool _dead;

        private Stat Find(StatType t)
        {
            foreach (var s in stats) if (s.type == t) return s;
            return null;
        }

        public float Get(StatType t) => Find(t)?.Normalized ?? 0f;

        public bool IsDead => _dead;

        /// <summary>실제 값(최대치 기준 아님). 세이브용.</summary>
        public float GetCurrent(StatType t) => Find(t)?.current ?? 0f;

        /// <summary>불러오기용: 값을 직접 지정한다 (범위로 자르고 변화 이벤트를 낸다).</summary>
        public void SetCurrent(StatType t, float value)
        {
            var s = Find(t);
            if (s == null) return;
            s.current = Mathf.Clamp(value, 0f, s.max);
            OnStatChanged?.Invoke(t, s.Normalized);
        }

        /// <summary>사망 상태 해제 (리스폰/불러오기 직후).</summary>
        public void Revive() => _dead = false;

        public void Modify(StatType t, float delta)
        {
            var s = Find(t);
            if (s == null || _dead) return;
            float before = s.current;
            s.current = Mathf.Clamp(s.current + delta, 0f, s.max);
            if (!Mathf.Approximately(before, s.current))
                OnStatChanged?.Invoke(t, s.Normalized);
        }

        private void Update()
        {
            if (_dead) return;
            float dt = Time.deltaTime;

            foreach (var s in stats)
            {
                if (s.type == StatType.Health) continue;
                float mult = s.type == StatType.Warmth ? WarmthDecayMultiplier : 1f;
                Modify(s.type, -s.decayPerSecond * mult * dt);
            }

            // 결핍 시 체력 감소
            float starving = (Get(StatType.Hunger) <= 0f ? 1f : 0f)
                           + (Get(StatType.Thirst) <= 0f ? 1f : 0f)
                           + (Get(StatType.Warmth) <= 0f ? 1f : 0f);
            if (starving > 0f) Modify(StatType.Health, -starving * 1.5f * dt);

            if (Get(StatType.Health) <= 0f)
            {
                _dead = true;
                OnDeath?.Invoke();
            }
        }

        /// <summary>
        /// 0(여유)~1(위급). 퍼즐 시간 압박 계산에 사용.
        /// 가장 낮은 수치(체력 가중)를 기준으로 한다.
        /// </summary>
        public float Distress
        {
            get
            {
                float lowest = Mathf.Min(Get(StatType.Hunger), Get(StatType.Thirst), Get(StatType.Warmth));
                float hp = Get(StatType.Health);
                return Mathf.Clamp01(1f - Mathf.Min(lowest * 1.2f, hp));
            }
        }
    }
}
