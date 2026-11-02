using System;
using UnityEngine;
using AutoSurvival.Core;
using AutoSurvival.Survival;
using AutoSurvival.UI;

namespace AutoSurvival.Puzzles
{
    /// <summary>플레이어에게 보여줄 연출 신호. VFX/SFX/카메라는 구독자가 처리한다 (이 클래스는 로직만).</summary>
    public enum HydraulicCue
    {
        PumpGood,        // 박자에 맞춘 펌핑: 경쾌한 '칙' + 피스톤 상승
        PumpMiss,        // 박자 어긋남: 헛바람 소리
        PumpEmpty,       // 스태미나 바닥: 숨 고르기
        ReliefVent,      // 릴리프 밸브가 열려 기름이 '푸슉' 분출, 하중은 그대로
        HoseBurst,       // 호스 파열: 기름 폭발 연출 + 화면 흔들림 + 체력 피해
        Strain,          // 힘 부족: 크레인 프레임이 삐걱, 와이어가 팽팽해졌다 풀림
        Overheat,        // 펌프 과열: 김이 오르고 피스톤이 서서히 내려앉음
        UnsafeRelief,    // 오피가 거부: 릴리프 설정이 위험하다는 경고음 (피해 없음)
        LiftStart,       // 설계 승인: 크레인 와이어 팽팽, 하중이 바닥에서 떨어짐
        LiftComplete     // 목표 높이 도달: 컨베이어/자석이 하중을 베이스로 이송
    }

    /// <summary>
    /// 퍼즐 C: 유압 크레인 (파스칼의 원리).
    /// 1단계 [설계]: 입력(펌프) 피스톤, 출력(리프트) 피스톤, 릴리프 압력을 고른다. 압력계에는 계산에 필요한 값만 보이고
    ///               판정은 [가동 승인]을 눌러야 알 수 있다.
    /// 2단계 [펌핑]: 오피의 메트로놈 박자에 맞춰 펌프를 눌러 하중을 올린다. 스태미나가 소모되고, 면적비가 클수록 많이 펌핑해야 한다.
    /// 에셋: Factory Kit의 crane, crane-lift, crane-magnet, piston-round, pipe-glass-large(기름 흐름), lever-single(펌프 손잡이),
    ///       Survival Kit의 box-large / rock-* / chest(하중).
    /// </summary>
    public class HydraulicPuzzle : PuzzleSession
    {
        public enum Phase { Design, Pumping }

        [Header("씬 연결 (선택)")]
        [SerializeField] private Transform loadTransform;       // 올라갈 하중 오브젝트
        [SerializeField] private float liftVisualMeters = 1.2f; // 목표 높이에 해당하는 월드 상승 거리 (연출용)

        [Header("펌핑 리듬")]
        [SerializeField] private float beatSeconds = 0.5f;
        [SerializeField] private float perfectWindow = 0.12f;
        [SerializeField] private float okWindow = 0.2f;   // 박자 간격(0.5s)의 절반 미만이어야 헛바람(Miss)이 존재한다
        [SerializeField] private float staminaPerStroke = 1.5f;  // SurvivalStats 스태미나 최대 100 기준 (회복 초당 2)
        [SerializeField] private KeyCode pumpKey = KeyCode.Space;

        public HydraulicLevel Level { get; private set; }
        public Phase CurrentPhase { get; private set; }
        public HydraulicDesign Design { get; private set; }
        public HydraulicReport LastReport { get; private set; }
        public float Progress { get; private set; }             // 0~1 (목표 높이 대비)
        public int StrokesUsed { get; private set; }

        // 리듬 파라미터는 퍼즐이 단일 출처다. MetronomeUI가 이 값으로 같은 판정기(RhythmJudge)를 만든다.
        public float BeatSeconds => beatSeconds;
        public float PerfectWindow => perfectWindow;
        public float OkWindow => okWindow;
        public KeyCode PumpKey => pumpKey;
        public SurvivalStats PlayerStats => Player;

        /// <summary>true면 박자 시계·키 입력·판정을 MetronomeUI가 맡고, 퍼즐은 ApplyStroke(품질)만 받는다.</summary>
        public bool UseExternalRhythm { get; set; }

        /// <summary>(연출 신호, 근거 리포트). 설계 실패 시 리포트에 실패 원인과 수치가 담긴다.</summary>
        public event Action<HydraulicCue, HydraulicReport> OnCue;
        public event Action<float> OnProgress;
        public event Action<Phase> OnPhaseChanged;      // 설계 <-> 펌핑 전환 (UI 표시/숨김용)

        private Vector3 _loadStart;
        private float _effectiveStrokes;
        private float _beatClock;
        private bool _completed;
        private RhythmSession _rhythm;

        protected override void Setup(int machineTier)
        {
            string id = Scenario != null ? Scenario.hydraulicLevelId : null;
            Level = (!string.IsNullOrEmpty(id) ? HydraulicLevelLibrary.ById(id) : null) ?? HydraulicLevelLibrary.ForTier(machineTier);

            concept = ConceptTag.HydraulicPressure;
            baseTimeLimit = 40f + Level.maxStrokes * beatSeconds * 0.9f;  // 설계 + 펌핑 시간. 생존 위급도가 높으면 최대 50%까지 줄어든다

            Design = new HydraulicDesign(0, 0, 0);
            CurrentPhase = Phase.Design;
            if (loadTransform != null) _loadStart = loadTransform.localPosition;
        }

        // ───────── 1단계: 설계 ─────────

        public void SelectInput(int index)  { if (CanEdit()) Design = new HydraulicDesign(Clamp(index, Level.inputAreasCm2.Length), Design.outputIndex, Design.reliefIndex); }
        public void SelectOutput(int index) { if (CanEdit()) Design = new HydraulicDesign(Design.inputIndex, Clamp(index, Level.outputAreasCm2.Length), Design.reliefIndex); }
        public void SelectRelief(int index) { if (CanEdit()) Design = new HydraulicDesign(Design.inputIndex, Design.outputIndex, Clamp(index, Level.reliefBars.Length)); }

        private bool CanEdit() => IsRunning && CurrentPhase == Phase.Design;
        private static int Clamp(int i, int len) => Mathf.Clamp(i, 0, len - 1);

        /// <summary>
        /// 압력계 표시값 (판정 없이 계산 재료만 제공): 펌프 압력, 하중 압력, 릴리프 설정, 호스 한계.
        /// 숫자를 읽고 P = F/A를 직접 계산해 보게 하는 것이 학습 포인트다.
        /// </summary>
        public (float pump, float load, float relief, float hose) Gauges()
        {
            var r = HydraulicLogic.Evaluate(Level, Design);
            return (r.pumpPressureBar, r.loadPressureBar, r.reliefBar, r.hoseLimitBar);
        }

        /// <summary>[가동 승인]. 즉시 드러나는 실패는 여기서 연출하고 설계 단계에 머문다.</summary>
        public HydraulicReport Commit()
        {
            if (!CanEdit()) return null;

            var report = HydraulicLogic.Evaluate(Level, Design);
            LastReport = report;

            switch (report.outcome)
            {
                case HydraulicOutcome.UnsafeRelief:
                    OnCue?.Invoke(HydraulicCue.UnsafeRelief, report);
                    Mistake(2f);                                   // 피해 없는 경고 (시간만 약간 소모)
                    return report;

                case HydraulicOutcome.ReliefOpens:
                    OnCue?.Invoke(HydraulicCue.ReliefVent, report);
                    Mistake(4f);
                    return report;

                case HydraulicOutcome.HoseBurst:
                    OnCue?.Invoke(HydraulicCue.HoseBurst, report);
                    Mistake(6f);
                    if (Player != null) Player.Modify(StatType.Health, -15f);
                    return report;

                case HydraulicOutcome.InsufficientForce:
                    OnCue?.Invoke(HydraulicCue.Strain, report);
                    Mistake(4f);
                    return report;

                default:
                    // Success, TooSlow: 힘은 충분하다. TooSlow는 펌핑을 해 봐야 '느림'이 드러난다.
                    CurrentPhase = Phase.Pumping;
                    Progress = 0f; StrokesUsed = 0; _effectiveStrokes = 0f; _beatClock = 0f;
                    _rhythm = new RhythmSession(new RhythmJudge(beatSeconds, perfectWindow, okWindow));
                    OnCue?.Invoke(HydraulicCue.LiftStart, report);
                    OnPhaseChanged?.Invoke(Phase.Pumping);
                    return report;
            }
        }

        /// <summary>펌핑 중 설계가 틀렸다고 판단하면 기름을 빼고 처음부터 (시간 5초 손해).</summary>
        public void Redesign()
        {
            if (!IsRunning || CurrentPhase != Phase.Pumping) return;
            CurrentPhase = Phase.Design;
            Progress = 0f; StrokesUsed = 0; _effectiveStrokes = 0f;
            ApplyLoadVisual();
            OnProgress?.Invoke(0f);
            Mistake(5f);
            OnPhaseChanged?.Invoke(Phase.Design);
        }

        // ───────── 2단계: 펌핑 ─────────

        protected override void Update()
        {
            if (IsRunning && CurrentPhase == Phase.Pumping && !UseExternalRhythm)
            {
                _beatClock += Time.deltaTime;
                if (Input.GetKeyDown(pumpKey)) PumpStroke();
            }
            base.Update();
        }

        /// <summary>내장 리듬 모드: 퍼즐 자신의 박자 시계로 판정한다. (UseExternalRhythm이 false일 때)</summary>
        public void PumpStroke()
        {
            if (!IsRunning || CurrentPhase != Phase.Pumping || _completed) return;
            _rhythm ??= new RhythmSession(new RhythmJudge(beatSeconds, perfectWindow, okWindow));
            ApplyStroke(_rhythm.Press(_beatClock).Quality);
        }

        /// <summary>
        /// 품질(완벽 1.0 / 보통 0.5 / 헛바람 0)을 받아 펌핑 1회를 반영한다. MetronomeUI가 판정 후 호출한다.
        /// 반환: 스트로크가 실제로 수행됐는가 (스태미나 부족이면 false -> UI는 '숨 고르기'를 표시).
        /// </summary>
        public bool ApplyStroke(float quality)
        {
            if (!IsRunning || CurrentPhase != Phase.Pumping || _completed) return false;

            if (Player != null && Player.Get(StatType.Stamina) * 100f < staminaPerStroke)
            {
                OnCue?.Invoke(HydraulicCue.PumpEmpty, LastReport);   // 숨 고르는 동안 스태미나가 회복된다
                return false;
            }

            Player?.Modify(StatType.Stamina, -staminaPerStroke);
            StrokesUsed++;

            if (quality <= 0f) { OnCue?.Invoke(HydraulicCue.PumpMiss, LastReport); Mistake(0.5f); }
            else OnCue?.Invoke(HydraulicCue.PumpGood, LastReport);

            _effectiveStrokes += quality;
            Progress = Mathf.Clamp01(_effectiveStrokes / Mathf.Max(1, LastReport.strokesNeeded));
            ApplyLoadVisual();
            OnProgress?.Invoke(Progress);

            if (Progress >= 1f)
            {
                _completed = true;
                OnCue?.Invoke(HydraulicCue.LiftComplete, LastReport);
                ForceSuccess();
            }
            else if (StrokesUsed >= LastReport.maxStrokes)
            {
                // 허용 펌핑 횟수를 다 썼는데 목표 높이에 못 닿음 = 펌프 과열 (면적비가 너무 컸다)
                OnCue?.Invoke(HydraulicCue.Overheat, LastReport);
                ForceFail();
            }
            return true;
        }

        private void ApplyLoadVisual()
        {
            if (loadTransform != null)
                loadTransform.localPosition = _loadStart + Vector3.up * (liftVisualMeters * Progress);
        }

        protected override bool CheckSolved() => _completed;

        protected override void ApplyFailPenalty()
        {
            // 펌핑 도중 실패(과열·시간 초과)는 체력 대신 스태미나를 크게 깎는다. 설계 단계 실수(호스 파열)는 이미 처리됨.
            if (Player != null) Player.Modify(StatType.Stamina, -30f);
        }
    }
}
