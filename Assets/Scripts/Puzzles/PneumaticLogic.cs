using System;
using System.Collections.Generic;
using System.Linq;

namespace AutoSurvival.Puzzles
{
    // ───────────────────────────────────────────────────────────────
    // 순수 C# 공압 로직 (UnityEngine 의존 없음 -> EditMode 테스트 가능).
    // 5/2 방향제어밸브 + 복동 실린더를 '물리적으로' 판정한다. 정답 배선 비교가 아니라 시뮬레이션이다.
    //
    // 5/2 밸브 (ISO 5599 포트 번호): 1=공급(P), 2=B출구, 3=B의 배기(R), 4=A출구, 5=A의 배기(S)
    //   복귀 위치(스프링/12 신호):  1→2 가압,  4→5 배기
    //   작동 위치(14 신호):         1→4 가압,  2→3 배기
    // 복동 실린더: Cap(피스톤쪽/헤드) 챔버에 압력이 걸리면 전진, Rod(로드쪽) 챔버에 걸리면 후진.
    //   전진 힘 = P·A_cap − P_rod·A_annulus,  후진 힘 = P·A_annulus (단위: bar x cm² x 10 = N)
    //
    // 판정에 반영되는 것: 배선 오류(반대 연결·미연결·단락), 공급 끊김, 호스 누설, 배기구 막힘(갇힌 공기가
    // 압축되어 실린더를 세움), 레귤레이터 압력 부족/과다(호스 파열).
    // ───────────────────────────────────────────────────────────────

    public enum CylPort { None, Cap, Rod }                       // 밸브 출구 호스가 꽂힌 실린더 포트
    public enum ValveActuation { SpringReturn, Bistable }        // 편측 스프링 복귀형 / 양측 파일럿(메모리)형
    public enum PneuAction { Wait, Press14, Release14, Pulse14, Pulse12 }
    public enum PosExpect { Any, Extended, Retracted }
    public enum ChamberSource { Atmosphere, Pressure, NoSupply, Vent, Trapped, Short }

    public enum PneumaticFault
    {
        None,
        OverPressure,        // 레귤레이터 설정 > 호스 허용압 -> 호스 파열
        NoSupply,            // 1번 포트에 공기가 오지 않음
        Leak,                // 압력이 걸린 출구에 호스가 없어 공기가 샘
        ShortCircuit,        // 두 출구가 같은 챔버로 -> 공기가 곧바로 배기로 빠짐
        ExhaustBlocked,      // 배기구가 막혀 갇힌 공기가 실린더를 막음
        ReversedConnection,  // 출구가 반대로 연결됨
        NotConnected,        // 가압돼야 할 챔버에 호스가 없음
        InsufficientForce    // 힘 부족(또는 너무 느림)
    }

    public class PneuStep
    {
        public string description;
        public PneuAction action;
        public int durationMs = 3500;
        public PosExpect expect;
    }

    public class PneumaticLevel
    {
        public string id, title, story;
        public ValveActuation actuation;
        public float boreCm = 5f, rodCm = 1.6f;      // 실린더 안지름, 로드 지름
        public float loadExtendN;                    // 전진을 방해하는 하중
        public float frictionN;                      // 양방향 마찰
        public float hoseMaxBar = 8f;                // 호스 허용압
        public float[] regulatorBars;                // 선택 가능한 레귤레이터 설정
        public bool supplyConnectedAtStart = true;
        public bool plug3AtStart, plug5AtStart;      // true면 해당 배기구가 막힌 채로 시작 (소음기 막힘 등)
        public int timeLimitSec = 90;
        public List<PneuStep> scenario;

        public float CapAreaCm2 => (float)(Math.PI * boreCm * boreCm / 4.0);
        public float RodAreaCm2 => (float)(Math.PI * (boreCm * boreCm - rodCm * rodCm) / 4.0);   // 로드쪽 환형 면적

        public PneumaticWiring NewWiring() => new PneumaticWiring
        {
            port2 = CylPort.None, port4 = CylPort.None,
            supplyConnected = supplyConnectedAtStart,
            exhaust3Open = !plug3AtStart, exhaust5Open = !plug5AtStart,
            regulatorIndex = 0
        };
    }

    /// <summary>플레이어가 만든 배선 상태.</summary>
    public class PneumaticWiring
    {
        public CylPort port2, port4;
        public bool supplyConnected;
        public bool exhaust3Open, exhaust5Open;
        public int regulatorIndex;

        public PneumaticWiring Clone() => (PneumaticWiring)MemberwiseClone();
    }

    /// <summary>시간 스텝 시뮬레이터. 챔버 공기량(n = P_abs·V)을 추적해 갇힌 공기의 압축을 표현한다.</summary>
    public sealed class PneumaticSimulator
    {
        public const float DeadVolume = 0.04f;    // 행정 대비 데드 볼륨
        public const float MaxSpeed = 0.8f;       // 행정/초

        private readonly PneumaticLevel _lv;
        private readonly PneumaticWiring _w;
        private float _nCap, _nRod;

        public bool Actuated { get; private set; }
        public float X { get; private set; }                // 0 = 후진 끝, 1 = 전진 끝
        public ChamberSource CapSource { get; private set; }
        public ChamberSource RodSource { get; private set; }
        public float CapGaugeBar { get; private set; }
        public float RodGaugeBar { get; private set; }
        public float NetForceN { get; private set; }        // + 전진 방향
        public bool Leaking { get; private set; }

        public PneumaticSimulator(PneumaticLevel level, PneumaticWiring wiring)
        {
            _lv = level; _w = wiring;
            _nCap = VolCap; _nRod = VolRod;                  // 대기압 1bar(abs)
        }

        private float VolCap => DeadVolume + X;
        private float VolRod => DeadVolume + (1f - X) * _lv.RodAreaCm2 / _lv.CapAreaCm2;

        public void Command(PneuAction a)
        {
            switch (a)
            {
                case PneuAction.Press14: Actuated = true; break;
                case PneuAction.Release14: if (_lv.actuation == ValveActuation.SpringReturn) Actuated = false; break;
                case PneuAction.Pulse14: if (_lv.actuation == ValveActuation.Bistable) Actuated = true; break;
                case PneuAction.Pulse12: if (_lv.actuation == ValveActuation.Bistable) Actuated = false; break;
            }
        }

        private enum Route { Pressure, ExhaustR3, ExhaustS5 }

        private Route RouteOf(int port) =>
            port == 2 ? (Actuated ? Route.ExhaustR3 : Route.Pressure)
                      : (Actuated ? Route.Pressure : Route.ExhaustS5);

        private ChamberSource Resolve(CylPort chamber, float supplyGauge, float volume, ref float n)
        {
            int count = (_w.port2 == chamber ? 1 : 0) + (_w.port4 == chamber ? 1 : 0);
            if (count == 0) { n = volume; return ChamberSource.Atmosphere; }   // 호스 없음 = 대기 개방
            if (count == 2) { n = volume; return ChamberSource.Short; }        // 가압 출구와 배기 출구가 한 챔버에

            var route = RouteOf(_w.port2 == chamber ? 2 : 4);
            if (route == Route.Pressure)
            {
                if (!_w.supplyConnected) { n = volume; return ChamberSource.NoSupply; }
                n = (1f + supplyGauge) * volume;
                return ChamberSource.Pressure;
            }

            bool open = route == Route.ExhaustR3 ? _w.exhaust3Open : _w.exhaust5Open;
            if (open) { n = volume; return ChamberSource.Vent; }
            return ChamberSource.Trapped;                                       // n 유지: 부피가 줄면 압력이 오른다
        }

        public void Step(float dt)
        {
            // 압력이 걸린 출구에 호스가 없으면 공기가 샌다 -> 공급 압력이 크게 떨어진다.
            Leaking = _w.supplyConnected &&
                      ((RouteOf(2) == Route.Pressure && _w.port2 == CylPort.None) ||
                       (RouteOf(4) == Route.Pressure && _w.port4 == CylPort.None));

            float reg = _lv.regulatorBars[_w.regulatorIndex];
            float supplyGauge = reg * (Leaking ? 0.3f : 1f);

            float vCap = VolCap, vRod = VolRod;
            CapSource = Resolve(CylPort.Cap, supplyGauge, vCap, ref _nCap);
            RodSource = Resolve(CylPort.Rod, supplyGauge, vRod, ref _nRod);

            CapGaugeBar = _nCap / vCap - 1f;
            RodGaugeBar = _nRod / vRod - 1f;
            NetForceN = (CapGaugeBar * _lv.CapAreaCm2 - RodGaugeBar * _lv.RodAreaCm2) * 10f;

            float thrExtend = _lv.frictionN + _lv.loadExtendN;
            float thrRetract = _lv.frictionN;
            float v = 0f;
            if (NetForceN > thrExtend && X < 1f)
                v = MaxSpeed * Math.Min(1f, (NetForceN - thrExtend) / (0.5f * thrExtend + 100f));
            else if (NetForceN < -thrRetract && X > 0f)
                v = -MaxSpeed * Math.Min(1f, (-NetForceN - thrRetract) / (0.5f * thrRetract + 100f));

            X = Math.Max(0f, Math.Min(1f, X + v * dt));
        }

        public float RequiredForceN(bool extending) => extending ? _lv.frictionN + _lv.loadExtendN : _lv.frictionN;
    }

    public class PneumaticReport
    {
        public bool passed = true;
        public int failedStepIndex = -1;
        public string failedStepDescription;
        public PneumaticFault fault = PneumaticFault.None;
        public float finalX;
        public float netForceN, requiredForceN;
        public string message;
    }

    public static class PneumaticTester
    {
        public const float Dt = 0.01f;
        public const float ExtendedAt = 0.97f, RetractedAt = 0.03f;

        public static PneumaticReport Run(PneumaticLevel lv, PneumaticWiring wiring)
        {
            var report = new PneumaticReport();
            float reg = lv.regulatorBars[wiring.regulatorIndex];

            // 가동 전 점검: 레귤레이터가 호스 허용압을 넘으면 첫 가압에서 호스가 터진다.
            if (reg > lv.hoseMaxBar)
            {
                report.passed = false; report.failedStepIndex = 0;
                report.failedStepDescription = lv.scenario.Count > 0 ? lv.scenario[0].description : "";
                report.fault = PneumaticFault.OverPressure;
                report.message = Describe(report.fault, lv, reg, 0f, 0f);
                return report;
            }

            var sim = new PneumaticSimulator(lv, wiring);
            for (int i = 0; i < lv.scenario.Count; i++)
            {
                var step = lv.scenario[i];
                sim.Command(step.action);

                int ticks = Math.Max(1, (int)Math.Round(step.durationMs / 1000.0 / Dt));
                for (int t = 0; t < ticks; t++) sim.Step(Dt);

                bool met = step.expect == PosExpect.Any ||
                           (step.expect == PosExpect.Extended && sim.X >= ExtendedAt) ||
                           (step.expect == PosExpect.Retracted && sim.X <= RetractedAt);

                PneumaticFault fault = PneumaticFault.None;
                if (!met) fault = Diagnose(sim, wiring, step.expect);
                else if (sim.Leaking) fault = PneumaticFault.Leak;   // 움직이더라도 공기가 새면 정비 불합격

                if (fault != PneumaticFault.None)
                {
                    report.passed = false;
                    report.failedStepIndex = i;
                    report.failedStepDescription = step.description;
                    report.fault = fault;
                    report.finalX = sim.X;
                    report.netForceN = sim.NetForceN;
                    report.requiredForceN = sim.RequiredForceN(step.expect != PosExpect.Retracted);
                    report.message = Describe(fault, lv, reg, report.netForceN, report.requiredForceN);
                    return report;
                }
                report.finalX = sim.X;
            }
            return report;
        }

        private static PneumaticFault Diagnose(PneumaticSimulator sim, PneumaticWiring w, PosExpect expect)
        {
            if (!w.supplyConnected) return PneumaticFault.NoSupply;
            if (sim.CapSource == ChamberSource.Short || sim.RodSource == ChamberSource.Short) return PneumaticFault.ShortCircuit;
            if (sim.Leaking) return PneumaticFault.Leak;

            bool wantExtend = expect == PosExpect.Extended;
            var drive = wantExtend ? sim.CapSource : sim.RodSource;   // 가압되어야 할 챔버
            var vent = wantExtend ? sim.RodSource : sim.CapSource;    // 배기되어야 할 챔버

            if (vent == ChamberSource.Trapped) return PneumaticFault.ExhaustBlocked;
            if (drive == ChamberSource.Atmosphere) return PneumaticFault.NotConnected;
            if (drive != ChamberSource.Pressure)
                return vent == ChamberSource.Pressure ? PneumaticFault.ReversedConnection : PneumaticFault.NotConnected;
            return PneumaticFault.InsufficientForce;
        }

        public static string Describe(PneumaticFault f, PneumaticLevel lv, float regBar, float netN, float needN)
        {
            switch (f)
            {
                case PneumaticFault.OverPressure:
                    return $"레귤레이터 {regBar:0.#}bar가 호스 허용압 {lv.hoseMaxBar:0.#}bar를 넘어 호스가 터졌습니다. 공압은 보통 5~7bar로 씁니다.";
                case PneumaticFault.NoSupply:
                    return "압축공기가 들어오지 않습니다. 공급 라인(1번 포트)이 연결됐는지 확인하세요.";
                case PneumaticFault.Leak:
                    return "열린 호스 끝에서 공기가 새고 있습니다. 압력이 걸리는 포트에 호스가 꽂혀 있는지 보세요.";
                case PneumaticFault.ShortCircuit:
                    return "밸브의 두 출구가 같은 챔버에 연결되어 공기가 곧바로 배기로 빠집니다.";
                case PneumaticFault.ExhaustBlocked:
                    return "배기구가 막혀 빠져나가야 할 공기가 갇혔습니다. 갇힌 공기가 압축되어 실린더가 끝까지 가지 못합니다.";
                case PneumaticFault.ReversedConnection:
                    return "출구 연결이 반대입니다. 밸브를 조작하면 실린더가 반대 방향으로 힘을 받습니다.";
                case PneumaticFault.NotConnected:
                    return "가압되어야 할 실린더 챔버에 호스가 연결되어 있지 않습니다.";
                case PneumaticFault.InsufficientForce:
                    return $"힘이 모자라거나 너무 느립니다. 지금 낼 수 있는 힘 약 {Math.Max(0f, Math.Abs(netN)):0}N, 하중+마찰은 {needN:0}N입니다. 여유 있게 밀려면 압력을 올려 보세요. (F = P × A)";
                default:
                    return "시험 가동 성공! 배선과 압력이 모두 정상입니다.";
            }
        }
    }

    /// <summary>티어별 공압 회로 레벨. 새 레벨은 여기 추가하고 PuzzleScenarioDB에서 id로 참조한다.</summary>
    public static class PneumaticLevelLibrary
    {
        public const string Lv1 = "pneu.lv1";
        public const string Lv2 = "pneu.lv2";
        public const string Lv3 = "pneu.lv3";

        private static List<PneumaticLevel> _all;
        public static IReadOnlyList<PneumaticLevel> All => _all ??= Build();
        public static PneumaticLevel ById(string id) => All.FirstOrDefault(l => l.id == id);
        public static PneumaticLevel ForTier(int tier) => tier <= 2 ? All[0] : tier <= 4 ? All[1] : All[2];

        private static PneuStep S(string description, PneuAction action, PosExpect expect, int ms = 3500) =>
            new PneuStep { description = description, action = action, expect = expect, durationMs = ms };

        private static List<PneumaticLevel> Build() => new()
        {
            new PneumaticLevel
            {
                id = Lv1, title = "자동 재배기: 수확 암 (스프링 복귀형)",
                story = "수확 암이 안 움직여. 버튼을 누르는 동안 나가고, 손을 떼면 돌아오게 호스를 이어 줘. 압력은 너무 낮아도 높아도 안 돼!",
                actuation = ValveActuation.SpringReturn,
                loadExtendN = 300, frictionN = 50, hoseMaxBar = 8,
                regulatorBars = new[] { 2f, 4f, 6f, 8f, 10f },
                supplyConnectedAtStart = true,
                timeLimitSec = 70,
                scenario = new List<PneuStep>
                {
                    S("대기: 실린더는 후진 상태", PneuAction.Wait, PosExpect.Retracted, 500),
                    S("버튼을 누르고 있으면 전진", PneuAction.Press14, PosExpect.Extended),
                    S("손을 떼면 스프링이 밸브를 되돌려 후진", PneuAction.Release14, PosExpect.Retracted),
                    S("다시 누르면 전진", PneuAction.Press14, PosExpect.Extended),
                },
            },
            new PneumaticLevel
            {
                id = Lv2, title = "정수 플랜트: 펌프 밸브 액추에이터",
                story = "공급 라인이 끊겼고 소음기(배기구)도 막혀 있어. 호스를 잇고 막힌 배기구를 뚫은 다음 무거운 하중을 밀 압력을 골라 줘.",
                actuation = ValveActuation.SpringReturn,
                loadExtendN = 800, frictionN = 60, hoseMaxBar = 8,
                regulatorBars = new[] { 2f, 4f, 6f, 8f, 10f },
                supplyConnectedAtStart = false, plug5AtStart = true,
                timeLimitSec = 95,
                scenario = new List<PneuStep>
                {
                    S("대기: 실린더는 후진 상태", PneuAction.Wait, PosExpect.Retracted, 500),
                    S("버튼을 누르면 하중을 밀며 전진", PneuAction.Press14, PosExpect.Extended),
                    S("손을 떼면 후진", PneuAction.Release14, PosExpect.Retracted),
                    S("다시 누르면 전진", PneuAction.Press14, PosExpect.Extended),
                    S("다시 떼면 후진", PneuAction.Release14, PosExpect.Retracted),
                },
            },
            new PneumaticLevel
            {
                id = Lv3, title = "제련소 도어: 양측 파일럿 밸브 (메모리형)",
                story = "이 밸브는 신호를 한 번만 줘도 위치를 기억해. 14번 신호로 열고 12번 신호로 닫는 회로야. 도어가 무거우니 압력을 정확히 맞춰 줘.",
                actuation = ValveActuation.Bistable,
                loadExtendN = 1100, frictionN = 80, hoseMaxBar = 8,
                regulatorBars = new[] { 2f, 4f, 6f, 8f, 10f },
                supplyConnectedAtStart = true, plug3AtStart = true,
                timeLimitSec = 120,
                scenario = new List<PneuStep>
                {
                    S("대기: 도어는 닫힘(후진)", PneuAction.Wait, PosExpect.Retracted, 500),
                    S("14번 신호 펄스: 도어가 열린다(전진)", PneuAction.Pulse14, PosExpect.Extended),
                    S("신호가 없어도 밸브가 위치를 기억해 열린 채 유지", PneuAction.Wait, PosExpect.Extended, 1500),
                    S("12번 신호 펄스: 도어가 닫힌다(후진)", PneuAction.Pulse12, PosExpect.Retracted),
                    S("닫힌 채 유지", PneuAction.Wait, PosExpect.Retracted, 1500),
                    S("다시 14번 신호: 열림", PneuAction.Pulse14, PosExpect.Extended),
                },
            },
        };
    }
}
