using System;
using UnityEngine;
using AutoSurvival.Core;
using AutoSurvival.Data;
using AutoSurvival.Survival;

namespace AutoSurvival.Puzzles
{
    public enum PuzzleResult { Success, Fail, Aborted }

    /// <summary>
    /// 모든 '기능사 퍼즐'의 공통 베이스. 월드의 기계에서 시작되며, 월드는 멈추지 않는다(실시간).
    /// 제한시간은 플레이어의 생존 위급도(Distress)에 따라 줄어든다 -> "춥고 배고픈데 빨리 고쳐야 한다".
    /// </summary>
    public abstract class PuzzleSession : MonoBehaviour
    {
        [Header("공통")]
        [SerializeField] protected ConceptTag concept;
        [SerializeField] protected float baseTimeLimit = 60f;
        [Tooltip("위급도 1일 때 제한시간 배율")] [SerializeField] protected float minTimeScale = 0.5f;

        public ConceptTag Concept => concept;
        public float TimeLeft { get; private set; }
        public bool IsRunning { get; private set; }

        /// <summary>DB가 골라 준 시나리오(없으면 null -> 퍼즐이 티어 기본값 사용).</summary>
        public PuzzleScenarioEntry Scenario { get; private set; }

        protected SurvivalStats Player => _player;

        public event Action<PuzzleResult> OnFinished;

        private SurvivalStats _player;

        /// <summary>machineTier가 높을수록 난이도(격자 크기, 회로 길이)가 올라간다.</summary>
        public void Begin(SurvivalStats player, int machineTier, PuzzleScenarioEntry scenario = null)
        {
            _player = player;
            Scenario = scenario;

            // Setup이 챌린지별 제한시간(baseTimeLimit)을 정할 수 있도록 먼저 호출한다.
            Setup(machineTier);

            float distress = player != null ? player.Distress : 0f;
            TimeLeft = baseTimeLimit * Mathf.Lerp(1f, minTimeScale, distress);
            IsRunning = true;
        }

        protected abstract void Setup(int machineTier);

        /// <summary>서브클래스가 매 프레임 성공 조건을 판정.</summary>
        protected abstract bool CheckSolved();

        /// <summary>실패 패널티 (스파크 감전, 파이프 파열 등).</summary>
        protected virtual void ApplyFailPenalty()
        {
            if (_player != null) _player.Modify(StatType.Health, -10f);
        }

        /// <summary>플레이어가 실수할 때(잘못된 연결 등) 서브클래스가 호출.</summary>
        protected void Mistake(float timePenalty = 3f)
        {
            TimeLeft -= timePenalty;
        }

        protected virtual void Update()
        {
            if (!IsRunning) return;

            TimeLeft -= Time.deltaTime;
            if (CheckSolved()) { Finish(PuzzleResult.Success); return; }
            if (TimeLeft <= 0f) Finish(PuzzleResult.Fail);
        }

        public void Abort() { if (IsRunning) Finish(PuzzleResult.Aborted); }

        /// <summary>서브클래스가 시간 초과 전에 즉시 실패/성공으로 끝낼 때 사용 (예: 펌프 과열).</summary>
        protected void ForceFail() { if (IsRunning) Finish(PuzzleResult.Fail); }
        protected void ForceSuccess() { if (IsRunning) Finish(PuzzleResult.Success); }

        private void Finish(PuzzleResult result)
        {
            IsRunning = false;
            if (result == PuzzleResult.Fail) ApplyFailPenalty();
            if (result != PuzzleResult.Aborted && KnowledgeTracker.Instance != null)
                KnowledgeTracker.Instance.Report(concept, result == PuzzleResult.Success);
            OnFinished?.Invoke(result);
        }
    }
}
