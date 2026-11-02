using System.Collections.Generic;
using UnityEngine;
using AutoSurvival.Core;
using AutoSurvival.Survival;

namespace AutoSurvival.Puzzles
{
    /// <summary>
    /// 퍼즐 B: PLC 제어반 복구. 챌린지(LadderChallenge) 데이터를 받아 '빈 접점 슬롯'을 만들고,
    /// [시험 가동]을 누르면 LadderTester가 시나리오를 시뮬레이션한다.
    /// 티어별 난이도: 자기유지(T1~2) -> 인터락 정·역(T3) -> TON 경보 기동(T4) -> 인터락+TON 복합(T5).
    /// 에셋: machine-window, screen-panel-wide, lever-single, button-floor-round, indicator-special-*.
    /// </summary>
    public class LadderPuzzle : PuzzleSession
    {
        [System.Serializable]
        public class Slot
        {
            public int rungIndex, branchIndex, contactIndex;
            public Contact? placed;       // null = 비어 있음(열린 회로)
            public bool locked;           // 힌트로 미리 채워진 접점은 수정 불가
        }

        public event System.Action<LadderTestReport, string> OnTestRan;   // (리포트, 월드 속 설명 문구)

        public LadderChallenge Challenge { get; private set; }
        public LadderProgram Program { get; private set; }
        public IReadOnlyList<Slot> Slots => _slots;
        public IReadOnlyList<Contact> Palette => _palette;

        private readonly List<Slot> _slots = new();
        private List<Contact> _palette;
        private LadderProgram _answer;
        private bool _solved;
        private int _testRuns;

        protected override void Setup(int machineTier)
        {
            // DB가 챌린지를 지정했으면 그것을, 아니면 티어에 맞는 기본 챌린지를 쓴다.
            string id = Scenario != null ? Scenario.ladderChallengeId : null;
            Challenge = !string.IsNullOrEmpty(id)
                ? LadderChallengeLibrary.ById(id) ?? LadderChallengeLibrary.ForTier(machineTier)
                : LadderChallengeLibrary.ForTier(machineTier);

            concept = Challenge.concept;
            baseTimeLimit = Challenge.timeLimitSec;

            _answer = Challenge.buildAnswer();
            _palette = Challenge.BuildPalette();

            _slots.Clear();
            foreach (var pos in Challenge.hidden)
                _slots.Add(new Slot { rungIndex = pos.rung, branchIndex = pos.branch, contactIndex = pos.index });

            // 힌트: 앞쪽 N개 슬롯을 정답으로 미리 채워 잠근다.
            int hints = Challenge.HintsFor(machineTier);
            for (int i = 0; i < hints && i < _slots.Count; i++)
            {
                var s = _slots[i];
                s.placed = _answer.rungs[s.rungIndex].branches[s.branchIndex][s.contactIndex];
                s.locked = true;
            }

            RebuildProgram();
        }

        /// <summary>플레이어가 접점 블록을 슬롯에 꽂을 때 호출 (마우스 드래그/컨트롤러 모두 대응).</summary>
        public bool PlaceContact(int slotIndex, Contact contact)
        {
            if (!IsRunning || slotIndex < 0 || slotIndex >= _slots.Count || _slots[slotIndex].locked) return false;
            _slots[slotIndex].placed = contact;
            RebuildProgram();
            return true;
        }

        public bool ClearSlot(int slotIndex)
        {
            if (!IsRunning || slotIndex < 0 || slotIndex >= _slots.Count || _slots[slotIndex].locked) return false;
            _slots[slotIndex].placed = null;
            RebuildProgram();
            return true;
        }

        /// <summary>
        /// 정답 회로의 뼈대(렁·가지·고정 접점)에 슬롯의 접점을 채워 프로그램을 만든다.
        /// 슬롯이 하나라도 비어 있는 가지는 '배선이 끊긴 열린 회로'로 취급한다(빈 가지는 Scan에서 무시됨).
        /// </summary>
        private void RebuildProgram()
        {
            var p = new LadderProgram { ScanMs = _answer.ScanMs };

            for (int r = 0; r < _answer.rungs.Count; r++)
            {
                var src = _answer.rungs[r];
                var rung = new Rung { coil = src.coil, kind = src.kind, presetMs = src.presetMs };

                for (int b = 0; b < src.branches.Count; b++)
                {
                    var branch = new List<Contact>();
                    bool open = false;

                    for (int c = 0; c < src.branches[b].Count; c++)
                    {
                        var slot = FindSlot(r, b, c);
                        if (slot == null) branch.Add(src.branches[b][c]);      // 고정 접점
                        else if (slot.placed.HasValue) branch.Add(slot.placed.Value);
                        else open = true;                                      // 빈 슬롯 -> 이 가지는 끊김
                    }

                    rung.branches.Add(open ? new List<Contact>() : branch);
                }
                p.rungs.Add(rung);
            }
            Program = p;
        }

        private Slot FindSlot(int r, int b, int c)
        {
            foreach (var s in _slots)
                if (s.rungIndex == r && s.branchIndex == b && s.contactIndex == c) return s;
            return null;
        }

        /// <summary>[시험 가동] 레버. 실패하면 시간 패널티, 인터락 위반(합선)은 더 큰 패널티와 감전.</summary>
        public LadderTestReport RunTest()
        {
            if (!IsRunning) return null;
            _testRuns++;

            var report = LadderTester.Run(Program, Challenge.scenario, Challenge.alwaysExclusive);
            string message = Describe(report);

            if (report.passed) _solved = true;
            else if (report.interlockViolation)
            {
                Mistake(8f);
                if (Player != null) Player.Modify(StatType.Health, -5f);   // 합선 스파크
            }
            else Mistake(4f);

            OnTestRan?.Invoke(report, message);
            return report;
        }

        /// <summary>월드/UI에 띄울 '왜 틀렸는지' 문구. 정답을 알려주지 않고 어긋난 상황만 보여준다.</summary>
        public string Describe(LadderTestReport r)
        {
            if (r == null) return string.Empty;
            if (r.passed) return "시험 가동 성공! 모든 상황에서 정상 동작합니다.";

            string step = $"{r.failedStepIndex + 1}번째 상황 - {r.failedStepDescription}";
            if (r.interlockViolation)
                return $"{step}\n합선 발생! {r.address.Replace("&", " 와 ")} 이(가) 동시에 켜졌습니다. (+{r.elapsedMsInStep / 1000f:0.0}초 시점)";

            string name = Challenge.Label(r.address);
            if (r.unstable)
                return $"{step}\n{name} 이(가) 계속 깜빡거립니다! 출력이 자기 자신을 켰다 껐다 하는 회로가 되었는지 확인해 보세요.";
            return $"{step}\n{name} 이(가) {(r.expected ? "켜져" : "꺼져")} 있어야 하는데 {(r.actual ? "켜져" : "꺼져")} 있습니다.";
        }

        protected override bool CheckSolved() => _solved;
    }
}
