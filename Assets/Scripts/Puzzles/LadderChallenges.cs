using System;
using System.Collections.Generic;
using System.Linq;
using AutoSurvival.Core;

namespace AutoSurvival.Puzzles
{
    public readonly struct SlotPos
    {
        public readonly int rung, branch, index;
        public SlotPos(int rung, int branch, int index) { this.rung = rung; this.branch = branch; this.index = index; }
    }

    /// <summary>
    /// 래더 퍼즐 한 판의 데이터: 정답 회로(answer) 중 일부 접점을 숨기고(hidden),
    /// 플레이어가 조립한 회로가 시나리오(scenario)대로 '동작'하는지만 검증한다.
    /// </summary>
    public class LadderChallenge
    {
        public string id;
        public string title;
        public string story;                 // 월드 속 상황 설명 (오피의 대사 톤)
        public ConceptTag concept;
        public int minTier, maxTier;
        public int baseHints;                // minTier에서 미리 채워 주는 접점 수. 티어가 오를수록 1개씩 줄어든다.
        public int timeLimitSec = 90;
        public Func<LadderProgram> buildAnswer;
        public List<SlotPos> hidden = new();
        public List<LadderStep> scenario;
        public List<string[]> alwaysExclusive = new();
        public Dictionary<string, string> labels = new();   // 주소 -> 월드 속 이름 ("X0" -> "정회전 START")
        public List<string> extraPaletteAddresses = new();  // 함정용(안 쓰는) 접점 주소

        public int HintsFor(int tier) => Math.Max(0, baseHints - Math.Max(0, tier - minTier));

        public string Label(string address) => labels.TryGetValue(address, out var l) ? $"{address} {l}" : address;

        /// <summary>플레이어가 고를 수 있는 접점 블록 목록. 정답에 쓰인 주소 x {a접점, b접점} + 함정 주소.</summary>
        public List<Contact> BuildPalette()
        {
            var addresses = new List<string>();
            foreach (var rung in buildAnswer().rungs)
                foreach (var branch in rung.branches)
                    foreach (var c in branch)
                        if (!addresses.Contains(c.address)) addresses.Add(c.address);
            foreach (var a in extraPaletteAddresses)
                if (!addresses.Contains(a)) addresses.Add(a);

            var palette = new List<Contact>();
            foreach (var a in addresses)
            {
                palette.Add(new Contact(a, ContactType.NO));
                palette.Add(new Contact(a, ContactType.NC));
            }
            return palette;
        }
    }

    /// <summary>티어별 래더 챌린지 라이브러리. 새 시나리오는 여기 추가하고 PuzzleScenarioDB에서 id로 참조한다.</summary>
    public static class LadderChallengeLibrary
    {
        public const string SelfHold = "ladder.selfhold";
        public const string Interlock = "ladder.interlock";
        public const string TonAlarm = "ladder.ton_alarm";
        public const string FwdRevTimer = "ladder.fwdrev_timer";

        private static List<LadderChallenge> _all;
        public static IReadOnlyList<LadderChallenge> All => _all ??= Build();

        public static LadderChallenge ById(string id) => All.FirstOrDefault(c => c.id == id);

        public static LadderChallenge ForTier(int tier)
        {
            var list = All.Where(c => tier >= c.minTier && tier <= c.maxTier).ToList();
            return list.Count > 0 ? list[0] : All[All.Count - 1];
        }

        // ───────── 회로 작성 헬퍼: "X0"=a접점, "/X1"=b접점 ─────────
        private static Contact C(string s) => s.StartsWith("/")
            ? new Contact(s.Substring(1), ContactType.NC)
            : new Contact(s, ContactType.NO);

        private static List<Contact> B(params string[] contacts) => contacts.Select(C).ToList();

        private static Rung R(string coil, params List<Contact>[] branches)
        {
            var r = new Rung { coil = coil };
            r.branches.AddRange(branches);
            return r;
        }

        private static Rung Ton(string timer, int presetMs, params List<Contact>[] branches)
        {
            var r = R(timer, branches);
            r.kind = RungKind.TON;
            r.presetMs = presetMs;
            return r;
        }

        private static LadderProgram P(params Rung[] rungs)
        {
            var p = new LadderProgram();
            p.rungs.AddRange(rungs);
            return p;
        }

        private static List<LadderChallenge> Build() => new()
        {
            BuildSelfHold(), BuildInterlock(), BuildTonAlarm(), BuildFwdRevTimer()
        };

        // ───────── Tier 1~2: 자기유지 ─────────
        private static LadderChallenge BuildSelfHold() => new LadderChallenge
        {
            id = SelfHold, title = "발전기 기동 회로", concept = ConceptTag.LadderSelfHold,
            story = "제어반이 타서 접점이 빠졌어. START에서 손을 떼도 발전기가 계속 돌아야 해. STOP은 언제나 최우선!",
            minTier = 1, maxTier = 2, baseHints = 3, timeLimitSec = 75,
            buildAnswer = () => P(R("Y0", B("X0", "/X1"), B("Y0", "/X1"))),
            hidden = { new SlotPos(0, 0, 0), new SlotPos(0, 0, 1), new SlotPos(0, 1, 0), new SlotPos(0, 1, 1) },
            scenario = LadderTester.MotorSelfHoldScenario(),
            labels = { ["X0"] = "START", ["X1"] = "STOP", ["Y0"] = "발전기 모터" },
        };

        // ───────── Tier 3: 인터락 (정·역 운전) ─────────
        private static LadderChallenge BuildInterlock() => new LadderChallenge
        {
            id = Interlock, title = "제련소 컨베이어 정·역 회로", concept = ConceptTag.LadderInterlock,
            story = "컨베이어가 정회전과 역회전을 동시에 받으면 모터가 단락돼서 탄다. 한쪽이 돌고 있으면 반대쪽은 절대 안 켜지게 만들어야 해!",
            minTier = 3, maxTier = 3, baseHints = 2, timeLimitSec = 100,
            buildAnswer = () => P(
                R("Y0", B("X0", "/X2", "/Y1"), B("Y0", "/X2", "/Y1")),
                R("Y1", B("X1", "/X2", "/Y0"), B("Y1", "/X2", "/Y0"))),
            hidden =
            {
                new SlotPos(0, 0, 2), new SlotPos(0, 1, 0), new SlotPos(0, 1, 2),
                new SlotPos(1, 0, 2), new SlotPos(1, 1, 0), new SlotPos(1, 1, 2),
            },
            alwaysExclusive = { new[] { "Y0", "Y1" } },   // 시나리오 전체에서 매 스캔 동시 ON 금지
            scenario = new List<LadderStep>
            {
                LadderTester.Step("대기: 모두 꺼져 있다",                       "X0=0,X1=0,X2=0", "Y0=0,Y1=0"),
                LadderTester.Step("정·역 버튼을 동시에 눌러도 둘 다 켜지면 안 된다", "X0=1,X1=1", "", 300),
                LadderTester.Step("STOP: 어떤 상태에서도 전부 꺼진다",           "X0=0,X1=0,X2=1", "Y0=0,Y1=0", 200),
                LadderTester.Step("STOP에서 손을 뗀다",                          "X2=0", "Y0=0,Y1=0"),
                LadderTester.Step("정회전 START",                                 "X0=1", "Y0=1,Y1=0"),
                LadderTester.Step("손을 떼도 정회전 유지",                        "X0=0", "Y0=1,Y1=0"),
                LadderTester.Step("정회전 중 역회전 버튼 -> 무시되어야 한다",       "X1=1", "Y0=1,Y1=0"),
                LadderTester.Step("역 버튼에서 손을 떼도 정회전 유지",             "X1=0", "Y0=1,Y1=0"),
                LadderTester.Step("STOP",                                         "X2=1", "Y0=0,Y1=0"),
                LadderTester.Step("STOP 해제",                                    "X2=0", "Y0=0,Y1=0"),
                LadderTester.Step("역회전 START",                                 "X1=1", "Y0=0,Y1=1"),
                LadderTester.Step("손을 떼도 역회전 유지",                        "X1=0", "Y0=0,Y1=1"),
                LadderTester.Step("역회전 중 정회전 버튼 -> 무시되어야 한다",       "X0=1", "Y0=0,Y1=1"),
                LadderTester.Step("정 버튼에서 손을 뗀다",                        "X0=0", "Y0=0,Y1=1"),
                LadderTester.Step("STOP",                                         "X2=1", "Y0=0,Y1=0"),
            },
            labels = { ["X0"] = "정회전 START", ["X1"] = "역회전 START", ["X2"] = "STOP", ["Y0"] = "정회전", ["Y1"] = "역회전" },
        };

        // ───────── Tier 4: TON 타이머 (경보 후 기동) ─────────
        private static LadderChallenge BuildTonAlarm() => new LadderChallenge
        {
            id = TonAlarm, title = "경보 후 컨베이어 기동 회로", concept = ConceptTag.LadderTimer,
            story = "START를 누르면 경광등이 먼저 켜지고, 3초 뒤에 컨베이어가 돌아야 해. 작업자가 피할 시간을 줘야지. 도중에 STOP하면 타이머도 처음부터!",
            minTier = 4, maxTier = 4, baseHints = 1, timeLimitSec = 110,
            buildAnswer = () => P(
                R("M0", B("X0", "/X1"), B("M0", "/X1")),
                Ton("T0", 3000, B("M0")),
                R("Y1", B("M0", "/T0")),
                R("Y0", B("T0"))),
            hidden =
            {
                new SlotPos(0, 0, 1), new SlotPos(0, 1, 0),
                new SlotPos(1, 0, 0), new SlotPos(2, 0, 1), new SlotPos(3, 0, 0),
            },
            extraPaletteAddresses = { "Y0" },
            scenario = new List<LadderStep>
            {
                LadderTester.Step("대기",                                   "X0=0,X1=0", "M0=0,Y0=0,Y1=0", 200),
                LadderTester.Step("START: 경광등부터 켜진다",                 "X0=1", "M0=1,Y1=1,Y0=0", 100),
                LadderTester.Step("2초 경과: 아직 경보 중, 컨베이어는 정지",   "X0=0", "M0=1,Y1=1,Y0=0", 2000),
                LadderTester.Step("3초 경과: 경광등 꺼지고 컨베이어 가동",     "", "Y1=0,Y0=1", 1500),
                LadderTester.Step("STOP: 전부 정지",                          "X1=1", "M0=0,Y0=0,Y1=0", 200),
                LadderTester.Step("STOP 해제",                                "X1=0", "Y0=0,Y1=0", 100),
                LadderTester.Step("재기동: 경보부터",                          "X0=1", "Y1=1,Y0=0", 100),
                LadderTester.Step("경보 2초째",                                "X0=0", "Y1=1,Y0=0", 2000),
                LadderTester.Step("경보 중 STOP: 타이머도 리셋",               "X1=1", "Y0=0,Y1=0", 200),
                LadderTester.Step("STOP 해제 후 다시 START",                   "X1=0", "Y0=0,Y1=0", 100),
                LadderTester.Step("다시 경보부터 2초 (이전 시간이 누적되면 안 됨)", "X0=1", "Y1=1,Y0=0", 100),
                LadderTester.Step("경보 2초 경과",                              "X0=0", "Y1=1,Y0=0", 2000),
            },
            labels = { ["X0"] = "START", ["X1"] = "STOP", ["M0"] = "운전 유지 릴레이", ["T0"] = "3초 타이머(TON)", ["Y0"] = "컨베이어", ["Y1"] = "경광등" },
        };

        // ───────── Tier 5: 인터락 + TON 복합 ─────────
        private static LadderChallenge BuildFwdRevTimer() => new LadderChallenge
        {
            id = FwdRevTimer, title = "크레인 윈치 정·역 + 정상운전 램프", concept = ConceptTag.LadderInterlock,
            story = "윈치는 정·역 인터락이 필수고, 방향 상관없이 3초 연속 돌아야 '정상 운전' 램프가 켜져. 그 전엔 기동 경고 램프. 마지막 관문이야, 기계공!",
            minTier = 5, maxTier = 5, baseHints = 0, timeLimitSec = 140,
            buildAnswer = () => P(
                R("Y0", B("X0", "/X2", "/Y1"), B("Y0", "/X2", "/Y1")),
                R("Y1", B("X1", "/X2", "/Y0"), B("Y1", "/X2", "/Y0")),
                Ton("T0", 3000, B("Y0"), B("Y1")),
                R("Y2", B("T0")),
                R("Y3", B("Y0", "/T0"), B("Y1", "/T0"))),
            hidden =
            {
                new SlotPos(0, 1, 0), new SlotPos(0, 1, 2),
                new SlotPos(1, 1, 0), new SlotPos(1, 1, 2),
                new SlotPos(2, 1, 0), new SlotPos(3, 0, 0),
                new SlotPos(4, 0, 1), new SlotPos(4, 1, 1),
            },
            alwaysExclusive = { new[] { "Y0", "Y1" } },
            scenario = new List<LadderStep>
            {
                LadderTester.Step("대기",                                        "X0=0,X1=0,X2=0", "Y0=0,Y1=0,Y2=0,Y3=0", 200),
                LadderTester.Step("정회전 START: 기동 경고 램프",                  "X0=1", "Y0=1,Y1=0,Y2=0,Y3=1", 100),
                LadderTester.Step("손을 떼도 유지, 아직 정상 램프는 꺼짐",          "X0=0", "Y0=1,Y2=0,Y3=1", 2000),
                LadderTester.Step("정회전 중 역회전 버튼 -> 인터락으로 무시",       "X1=1", "Y0=1,Y1=0,Y2=0", 100),
                LadderTester.Step("3초 이상 연속 운전: 정상 램프 ON, 경고 OFF",     "X1=0", "Y0=1,Y2=1,Y3=0", 1500),
                LadderTester.Step("STOP: 전부 OFF, 타이머 리셋",                   "X2=1", "Y0=0,Y1=0,Y2=0,Y3=0", 200),
                LadderTester.Step("STOP 해제",                                    "X2=0", "Y0=0,Y1=0", 100),
                LadderTester.Step("역회전 START: 타이머는 처음부터",                "X1=1", "Y1=1,Y0=0,Y2=0,Y3=1", 100),
                LadderTester.Step("역회전 2초: 정상 램프는 아직",                   "X1=0", "Y1=1,Y2=0,Y3=1", 2000),
                LadderTester.Step("역회전 3초 이상: 정상 램프 ON",                  "", "Y1=1,Y2=1,Y3=0", 1500),
                LadderTester.Step("STOP",                                         "X2=1", "Y0=0,Y1=0,Y2=0,Y3=0", 200),
            },
            labels =
            {
                ["X0"] = "정회전 START", ["X1"] = "역회전 START", ["X2"] = "STOP",
                ["Y0"] = "정회전", ["Y1"] = "역회전", ["T0"] = "3초 타이머(TON)",
                ["Y2"] = "정상 운전 램프", ["Y3"] = "기동 경고 램프",
            },
        };
    }
}
