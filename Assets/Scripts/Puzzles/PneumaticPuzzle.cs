using System;
using UnityEngine;
using AutoSurvival.Core;
using AutoSurvival.Survival;

namespace AutoSurvival.Puzzles
{
    /// <summary>플레이어에게 보여줄 연출 신호. VFX/SFX는 구독자가 처리한다 (이 클래스는 로직만).</summary>
    public enum PneumaticCue
    {
        HoseConnected,    // 호스가 포트에 꽂힘: 딸깍
        HoseRemoved,      // 호스 분리
        ExhaustToggled,   // 소음기/배기구 개방·막힘 전환
        TestStart,        // 시험 가동 시작: 오피가 "가동한다!"
        StepOk,           // 시나리오 한 단계 통과
        TestPassed,       // 전 단계 통과: 실린더가 시원하게 왕복
        Leak,             // 열린 호스 끝에서 '쉬이익'
        ShortCircuit,     // 두 출구가 한 챔버로: 배기구에서 공기가 곧장 샘
        ExhaustBlocked,   // 배기구 막힘: 실린더가 끝에서 덜컹, 소음기에서 '푸슉' 소리 없음
        Reversed,         // 반대 방향으로 움직임
        NotConnected,     // 호스 없음: 아무 일도 안 일어남
        NoSupply,         // 공기 없음: 컴프레서 소리만
        Strain,           // 힘 부족: 로드가 부들부들 떨다 멈춤
        HoseBurst,        // 호스 파열: 체력 피해
    }

    /// <summary>
    /// 퍼즐 D: 공압 회로 구성 (5/2 밸브 + 복동 실린더).
    /// [배선]  밸브 출구 2(B)·4(A)를 실린더의 Cap/Rod 포트에 연결하고, 공급 라인·배기구(소음기)를 정비하고, 레귤레이터 압력을 고른다.
    /// [확인]  밸브 조작(Press/Release/Pulse)에 따라 실린더가 실시간으로 움직이는 모습을 볼 수 있다 (라이브 시뮬레이션).
    /// [시험]  '시험 가동'을 누르면 시나리오(예: 누르면 전진, 떼면 후진)를 물리 시뮬레이션으로 검사한다. 통과하면 성공.
    /// 에셋: Factory Kit의 piston-round(실린더), pipe-glass-*(호스), lever-single(밸브 레버), tank(공기탱크), button-*(14/12 신호).
    /// </summary>
    public class PneumaticPuzzle : PuzzleSession
    {
        [Header("씬 연결 (선택)")]
        [SerializeField] private Transform rodTransform;          // 로드(피스톤 막대) 오브젝트
        [SerializeField] private float strokeVisualMeters = 0.8f;

        [Header("라이브 조작 키")]
        [SerializeField] private KeyCode key14 = KeyCode.E;       // 14번(작동) 신호
        [SerializeField] private KeyCode key12 = KeyCode.Q;       // 12번(복귀) 신호 (양측 파일럿형만)

        public PneumaticLevel Level { get; private set; }
        public PneumaticWiring Wiring { get; private set; }
        public PneumaticSimulator Live { get; private set; }
        public PneumaticReport LastReport { get; private set; }
        public bool Testing { get; private set; }
        public int TestsRun { get; private set; }

        public KeyCode Key14 => key14;
        public KeyCode Key12 => key12;

        public event Action<PneumaticCue, PneumaticReport> OnCue;
        public event Action OnWiringChanged;                     // UI 갱신용 (호스, 압력계, 로드 위치)

        private Vector3 _rodStart;
        private bool _solved;

        protected override void Setup(int machineTier)
        {
            string id = Scenario != null ? Scenario.pneumaticLevelId : null;
            Level = (!string.IsNullOrEmpty(id) ? PneumaticLevelLibrary.ById(id) : null) ?? PneumaticLevelLibrary.ForTier(machineTier);

            concept = ConceptTag.PneumaticCylinder;
            baseTimeLimit = Level.timeLimitSec;
            Wiring = Level.NewWiring();
            Live = new PneumaticSimulator(Level, Wiring);
            if (rodTransform != null) _rodStart = rodTransform.localPosition;
        }

        // ───────── 배선 API (UI 버튼/드래그가 호출) ─────────

        /// <summary>밸브 출구(2=B, 4=A)를 실린더 포트에 연결. CylPort.None이면 호스 분리.</summary>
        public void Connect(int valvePort, CylPort target)
        {
            if (!CanEdit() || (valvePort != 2 && valvePort != 4)) return;
            if (valvePort == 2) Wiring.port2 = target; else Wiring.port4 = target;
            Touched(target == CylPort.None ? PneumaticCue.HoseRemoved : PneumaticCue.HoseConnected);
        }

        public void SetSupply(bool connected)
        {
            if (!CanEdit()) return;
            Wiring.supplyConnected = connected;
            Touched(connected ? PneumaticCue.HoseConnected : PneumaticCue.HoseRemoved);
        }

        /// <summary>배기구(3=R, 5=S)의 막힘을 뚫거나 막는다 (소음기 청소).</summary>
        public void SetExhaustOpen(int exhaustPort, bool open)
        {
            if (!CanEdit() || (exhaustPort != 3 && exhaustPort != 5)) return;
            if (exhaustPort == 3) Wiring.exhaust3Open = open; else Wiring.exhaust5Open = open;
            Touched(PneumaticCue.ExhaustToggled);
        }

        public void SelectRegulator(int index)
        {
            if (!CanEdit()) return;
            Wiring.regulatorIndex = Mathf.Clamp(index, 0, Level.regulatorBars.Length - 1);
            Touched(PneumaticCue.ExhaustToggled);
        }

        public float RegulatorBar => Level.regulatorBars[Wiring.regulatorIndex];

        private bool CanEdit() => IsRunning && !Testing;

        private void Touched(PneumaticCue cue)
        {
            // 배선이 바뀌면 라이브 시뮬레이션을 새로 시작한다 (갇힌 공기 등 이전 상태 제거).
            Live = new PneumaticSimulator(Level, Wiring);
            OnCue?.Invoke(cue, null);
            OnWiringChanged?.Invoke();
            ApplyRodVisual();
        }

        // ───────── 라이브 조작 ─────────

        protected override void Update()
        {
            if (IsRunning && !Testing)
            {
                if (Level.actuation == ValveActuation.SpringReturn)
                    Live.Command(Input.GetKey(key14) ? PneuAction.Press14 : PneuAction.Release14);
                else
                {
                    if (Input.GetKeyDown(key14)) Live.Command(PneuAction.Pulse14);
                    if (Input.GetKeyDown(key12)) Live.Command(PneuAction.Pulse12);
                }

                // 호스 파열 상태에서는 라이브 가동 불가: 안전하게 정지
                if (RegulatorBar <= Level.hoseMaxBar) Live.Step(Time.deltaTime);
                ApplyRodVisual();
            }
            base.Update();
        }

        /// <summary>UI 버튼용 라이브 신호 (터치/마우스).</summary>
        public void LiveCommand(PneuAction action) { if (CanEdit()) Live.Command(action); }

        public float RodPosition => Live != null ? Live.X : 0f;

        // ───────── 시험 가동 ─────────

        /// <summary>[시험 가동]. 시나리오 전체를 검사한다. 실패하면 원인에 맞는 연출과 패널티를 주고 배선 단계로 돌아간다.</summary>
        public PneumaticReport RunTest()
        {
            if (!CanEdit()) return null;
            Testing = true;
            TestsRun++;
            OnCue?.Invoke(PneumaticCue.TestStart, null);

            var report = PneumaticTester.Run(Level, Wiring);
            LastReport = report;
            Testing = false;

            if (report.passed)
            {
                _solved = true;
                OnCue?.Invoke(PneumaticCue.TestPassed, report);
                ForceSuccess();
                return report;
            }

            OnCue?.Invoke(CueFor(report.fault), report);
            switch (report.fault)
            {
                case PneumaticFault.OverPressure:
                    Mistake(6f);
                    if (Player != null) Player.Modify(StatType.Health, -15f);   // 터진 호스가 튄다
                    break;
                case PneumaticFault.InsufficientForce:
                case PneumaticFault.ExhaustBlocked:
                    Mistake(4f);
                    break;
                default:
                    Mistake(3f);
                    break;
            }
            // 시험 후 라이브 시뮬레이션을 초기화한다 (플레이어가 고친 뒤 다시 확인).
            Live = new PneumaticSimulator(Level, Wiring);
            OnWiringChanged?.Invoke();
            return report;
        }

        public static PneumaticCue CueFor(PneumaticFault f)
        {
            switch (f)
            {
                case PneumaticFault.OverPressure: return PneumaticCue.HoseBurst;
                case PneumaticFault.NoSupply: return PneumaticCue.NoSupply;
                case PneumaticFault.Leak: return PneumaticCue.Leak;
                case PneumaticFault.ShortCircuit: return PneumaticCue.ShortCircuit;
                case PneumaticFault.ExhaustBlocked: return PneumaticCue.ExhaustBlocked;
                case PneumaticFault.ReversedConnection: return PneumaticCue.Reversed;
                case PneumaticFault.NotConnected: return PneumaticCue.NotConnected;
                default: return PneumaticCue.Strain;
            }
        }

        private void ApplyRodVisual()
        {
            if (rodTransform != null && Live != null)
                rodTransform.localPosition = _rodStart + Vector3.forward * (strokeVisualMeters * Live.X);
        }

        protected override bool CheckSolved() => _solved;

        protected override void ApplyFailPenalty()
        {
            // 시간 초과: 압축공기 분출에 맞아 가벼운 피해 (기본 패널티 -10 → 공압은 소음·분출 위주라 절반)
            if (Player != null) Player.Modify(StatType.Health, -5f);
        }
    }
}
