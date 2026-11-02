using System;
using System.Collections.Generic;
using System.Linq;

namespace AutoSurvival.Puzzles
{
    // ───────────────────────────────────────────────────────────────
    // 순수 C# PLC 래더 로직 (UnityEngine 의존 없음 -> EditMode 테스트 가능).
    // 스캔 사이클 방식으로 실제 PLC 동작을 흉내낸다.
    //   - 접점: a접점(NO) / b접점(NC), 렁 안에서 직렬(AND), 가지끼리 병렬(OR)
    //   - 출력: 일반 코일, 또는 TON(온딜레이 타이머)
    //   - 코일/타이머 완료 비트는 입력과 같은 주소 공간을 쓰므로 자기유지·인터락이 자연스럽게 성립한다.
    // ───────────────────────────────────────────────────────────────

    public enum ContactType { NO, NC }

    public readonly struct Contact
    {
        public readonly string address;   // "X0"(입력) "Y0"(출력) "M0"(내부릴레이) "T0"(타이머 완료 비트)
        public readonly ContactType type;
        public Contact(string address, ContactType type) { this.address = address; this.type = type; }
        public override string ToString() => type == ContactType.NO ? $"-| {address} |-" : $"-|/{address} |-";
    }

    public enum RungKind
    {
        Coil,   // 렁 전원 -> coil 주소 ON/OFF
        TON     // 렁 전원이 presetMs 동안 계속 ON 이면 coil 주소(완료 비트)가 ON. 전원이 끊기면 즉시 리셋.
    }

    /// <summary>branches: 병렬 가지들(OR). 각 가지 안의 접점들은 직렬(AND). 빈 가지는 '배선 안 됨(열린 회로)'.</summary>
    public class Rung
    {
        public readonly List<List<Contact>> branches = new();
        public string coil;
        public RungKind kind = RungKind.Coil;
        public int presetMs;              // TON 전용
    }

    public class LadderProgram
    {
        public readonly List<Rung> rungs = new();

        /// <summary>한 스캔이 흘려보내는 시간(ms). 타이머 정밀도 = 이 값.</summary>
        public int ScanMs = 100;

        private readonly Dictionary<string, bool> _state = new();
        private readonly Dictionary<string, int> _timerElapsed = new();

        public bool Get(string address) => _state.TryGetValue(address, out var v) && v;
        public void Set(string address, bool value) => _state[address] = value;
        public int TimerElapsedMs(string address) => _timerElapsed.TryGetValue(address, out var e) ? e : 0;

        public void Reset()
        {
            _state.Clear();
            _timerElapsed.Clear();
        }

        /// <summary>한 스캔: 위에서 아래로 모든 렁을 평가해 코일/타이머를 갱신.</summary>
        public void Scan()
        {
            foreach (var rung in rungs)
            {
                bool power = EvaluatePower(rung);

                if (rung.kind == RungKind.TON)
                {
                    int elapsed = TimerElapsedMs(rung.coil);
                    elapsed = power ? Math.Min(elapsed + ScanMs, rung.presetMs) : 0;
                    _timerElapsed[rung.coil] = elapsed;
                    Set(rung.coil, power && elapsed >= rung.presetMs);
                }
                else
                {
                    Set(rung.coil, power);
                }
            }
        }

        private bool EvaluatePower(Rung rung)
        {
            foreach (var branch in rung.branches)
            {
                if (branch.Count == 0) continue;   // 비어 있는 가지 = 아직 배선 안 됨 (열린 회로)
                bool pass = true;
                foreach (var c in branch)
                {
                    bool v = Get(c.address);
                    if (c.type == ContactType.NC) v = !v;
                    if (!v) { pass = false; break; }
                }
                if (pass) return true;
            }
            return false;
        }
    }

    /// <summary>한 번의 입력 상황과 그때 기대하는 출력. 모든 스텝은 이전 스캔의 상태(코일, 타이머)를 이어받는다.</summary>
    public class LadderStep
    {
        public string description;                         // 게임용 문구: "START에서 손을 뗀다" 등
        public Dictionary<string, bool> inputs = new();    // 이 스텝에서 바뀌는 입력 값 (적지 않은 입력은 이전 값 유지)
        public Dictionary<string, bool> expected = new();  // 스텝 종료 시점에 기대되는 값
        public int scans = 2;                              // durationMs가 0일 때 스캔 횟수
        public int durationMs = 0;                         // >0 이면 이 시간만큼 스캔 (타이머 시나리오용)
        public List<string[]> exclusive = new();           // 이 스텝 동안 매 스캔 '동시에 ON이면 안 되는' 주소 묶음
    }

    public class LadderTestReport
    {
        public bool passed = true;
        public int failedStepIndex = -1;
        public string failedStepDescription;
        public string address;     // 틀린 코일 (인터락 위반이면 "Y0&Y1")
        public bool expected, actual;
        public bool interlockViolation;   // 상호 배타 위반(동시 ON)
        public bool unstable;             // 스텝 끝에서 출력이 계속 깜빡임(발진). 예: 자기 자신의 b접점으로 자기 코일을 끄는 회로
        public int elapsedMsInStep;       // 위반이 발생한 시점(스텝 시작 기준 ms)
    }

    public static class LadderTester
    {
        /// <summary>
        /// 플레이어가 조립한 프로그램을 시나리오로 검증. 정답 회로와의 '모양' 비교가 아니라 '동작' 검증이므로
        /// 시나리오를 만족하는 모든 올바른 회로를 인정한다.
        /// alwaysExclusive: 시나리오 전체에서 매 스캔 지켜야 하는 상호 배타 묶음 (정·역 코일 등).
        /// </summary>
        public static LadderTestReport Run(LadderProgram program, IList<LadderStep> steps,
                                           IList<string[]> alwaysExclusive = null)
        {
            var report = new LadderTestReport();
            program.Reset();

            for (int i = 0; i < steps.Count; i++)
            {
                var step = steps[i];
                foreach (var kv in step.inputs) program.Set(kv.Key, kv.Value);

                int scanCount = step.durationMs > 0
                    ? (step.durationMs + program.ScanMs - 1) / program.ScanMs
                    : Math.Max(1, step.scans);

                Dictionary<string, bool> beforeLast = null;
                for (int s = 0; s < scanCount; s++)
                {
                    // 마지막 스캔 직전 값을 기억해 두었다가, 끝에서 같은지 확인한다(발진 회로가 스캔 횟수 우연으로 통과하는 것 방지)
                    if (s == scanCount - 1 && scanCount >= 2)
                    {
                        beforeLast = new Dictionary<string, bool>();
                        foreach (var key in step.expected.Keys) beforeLast[key] = program.Get(key);
                    }
                    program.Scan();
                    var group = FindViolation(program, step.exclusive) ?? FindViolation(program, alwaysExclusive);
                    if (group != null)
                    {
                        report.passed = false;
                        report.failedStepIndex = i;
                        report.failedStepDescription = step.description;
                        report.address = string.Join("&", group);
                        report.expected = false;
                        report.actual = true;
                        report.interlockViolation = true;
                        report.elapsedMsInStep = (s + 1) * program.ScanMs;
                        return report;
                    }
                }

                foreach (var kv in step.expected)
                {
                    bool actual = program.Get(kv.Key);
                    bool flicker = beforeLast != null && beforeLast[kv.Key] != actual;
                    if (actual != kv.Value || flicker)
                    {
                        report.passed = false;
                        report.failedStepIndex = i;
                        report.failedStepDescription = step.description;
                        report.address = kv.Key;
                        report.expected = kv.Value;
                        report.actual = actual;
                        report.unstable = flicker;
                        report.elapsedMsInStep = scanCount * program.ScanMs;
                        return report;
                    }
                }
            }
            return report;
        }

        private static string[] FindViolation(LadderProgram p, IList<string[]> groups)
        {
            if (groups == null) return null;
            foreach (var g in groups)
                if (g.Count(a => p.Get(a)) > 1) return g;
            return null;
        }

        // ───────── 시나리오 작성 헬퍼 ─────────

        /// <summary>
        /// 간단 표기로 스텝 생성. inputs: "X0=1,X1=0", expected: "Y0=1,Y1=0".
        /// exclusiveGroups: "Y0|Y1" 처럼 동시에 ON 되면 안 되는 묶음.
        /// </summary>
        public static LadderStep Step(string description, string inputs, string expected,
                                      int durationMs = 0, params string[] exclusiveGroups)
        {
            return new LadderStep
            {
                description = description,
                inputs = ParsePairs(inputs),
                expected = ParsePairs(expected),
                durationMs = durationMs,
                exclusive = exclusiveGroups.Select(g => g.Split('|').Select(a => a.Trim()).ToArray()).ToList()
            };
        }

        private static Dictionary<string, bool> ParsePairs(string text)
        {
            var d = new Dictionary<string, bool>();
            if (string.IsNullOrWhiteSpace(text)) return d;
            foreach (var part in text.Split(','))
            {
                var kv = part.Split('=');
                if (kv.Length != 2) throw new FormatException($"잘못된 표기: '{part}' (예: X0=1)");
                d[kv[0].Trim()] = kv[1].Trim() == "1";
            }
            return d;
        }

        // ───────── 기본 시나리오: 자기유지 (티어 1~2) ─────────

        /// <summary>표준 모터 기동/정지 자기유지. X0=START(a), X1=STOP(b로 배선), Y0=모터.</summary>
        public static List<LadderStep> MotorSelfHoldScenario() => new List<LadderStep>
        {
            Step("대기 상태: 모터는 꺼져 있어야 한다",             "X0=0,X1=0", "Y0=0"),
            Step("START를 누르면 모터가 돈다",                      "X0=1,X1=0", "Y0=1"),
            Step("START에서 손을 떼도 모터는 계속 돈다(자기유지!)", "X0=0,X1=0", "Y0=1"),
            Step("STOP을 누르면 모터가 선다",                       "X0=0,X1=1", "Y0=0"),
            Step("STOP에서 손을 떼도 모터는 꺼져 있어야 한다",       "X0=0,X1=0", "Y0=0"),
            Step("START와 STOP을 동시에 누르면 STOP이 우선",         "X0=1,X1=1", "Y0=0"),
        };

        /// <summary>참고용 정답 래더 (디자이너/테스트용).</summary>
        public static LadderProgram MotorSelfHoldAnswer()
        {
            var p = new LadderProgram();
            var r = new Rung { coil = "Y0" };
            r.branches.Add(new List<Contact> { new Contact("X0", ContactType.NO), new Contact("X1", ContactType.NC) });
            r.branches.Add(new List<Contact> { new Contact("Y0", ContactType.NO), new Contact("X1", ContactType.NC) });
            p.rungs.Add(r);
            return p;
        }
    }
}
