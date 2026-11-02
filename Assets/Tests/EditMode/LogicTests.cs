using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using AutoSurvival.Core;
using AutoSurvival.Data;
using AutoSurvival.Puzzles;
using AutoSurvival.UI;
using AutoSurvival.Save;
using AutoSurvival.Dialogue;
using AutoSurvival.Machines;
using AutoSurvival.Survival;
using AutoSurvival.Level;
using AutoSurvival.Feedback;
using AutoSurvival.Audio;
using AutoSurvival.Settings;
using AutoSurvival.Objectives;
using AutoSurvival.Interaction;

namespace AutoSurvival.Tests
{
    // Window ▸ General ▸ Test Runner ▸ EditMode 에서 실행한다.

    public class LadderLogicTests
    {
        [TestCase(LadderChallengeLibrary.SelfHold)]
        [TestCase(LadderChallengeLibrary.Interlock)]
        [TestCase(LadderChallengeLibrary.TonAlarm)]
        [TestCase(LadderChallengeLibrary.FwdRevTimer)]
        public void ReferenceAnswer_PassesScenario(string id)
        {
            var ch = LadderChallengeLibrary.ById(id);
            var report = LadderTester.Run(ch.buildAnswer(), ch.scenario, ch.alwaysExclusive);
            Assert.IsTrue(report.passed, $"{id}: step {report.failedStepIndex + 1} {report.failedStepDescription} ({report.address})");
        }

        [Test]
        public void SelfHold_WithoutLatchBranch_Fails()
        {
            var p = new LadderProgram();
            var r = new Rung { coil = "Y0" };
            r.branches.Add(new List<Contact> { new Contact("X0", ContactType.NO), new Contact("X1", ContactType.NC) });
            p.rungs.Add(r);
            var report = LadderTester.Run(p, LadderTester.MotorSelfHoldScenario());
            Assert.IsFalse(report.passed);
            Assert.AreEqual(2, report.failedStepIndex);   // START에서 손을 뗀 순간
        }

        [Test]
        public void Interlock_WithoutOppositeBContacts_ViolatesExclusivity()
        {
            var ch = LadderChallengeLibrary.ById(LadderChallengeLibrary.Interlock);
            var p = new LadderProgram();
            var r0 = new Rung { coil = "Y0" };
            r0.branches.Add(new List<Contact> { new Contact("X0", ContactType.NO), new Contact("X2", ContactType.NC) });
            r0.branches.Add(new List<Contact> { new Contact("Y0", ContactType.NO), new Contact("X2", ContactType.NC) });
            var r1 = new Rung { coil = "Y1" };
            r1.branches.Add(new List<Contact> { new Contact("X1", ContactType.NO), new Contact("X2", ContactType.NC) });
            r1.branches.Add(new List<Contact> { new Contact("Y1", ContactType.NO), new Contact("X2", ContactType.NC) });
            p.rungs.Add(r0); p.rungs.Add(r1);

            var report = LadderTester.Run(p, ch.scenario, ch.alwaysExclusive);
            Assert.IsFalse(report.passed);
            Assert.IsTrue(report.interlockViolation);
        }

        [Test]
        public void Ton_ResetsWhenEnableDrops()
        {
            var p = new LadderProgram { ScanMs = 100 };
            var t = new Rung { coil = "T0", kind = RungKind.TON, presetMs = 1000 };
            t.branches.Add(new List<Contact> { new Contact("X0", ContactType.NO) });
            p.rungs.Add(t);

            p.Set("X0", true);
            for (int i = 0; i < 9; i++) p.Scan();          // 900ms: 아직 완료 전
            Assert.IsFalse(p.Get("T0"));
            p.Set("X0", false); p.Scan();                  // 끊기면 0으로 리셋
            Assert.AreEqual(0, p.TimerElapsedMs("T0"));
            p.Set("X0", true);
            for (int i = 0; i < 9; i++) p.Scan();
            Assert.IsFalse(p.Get("T0"));                   // 처음부터 다시 세므로 900ms
            p.Scan();
            Assert.IsTrue(p.Get("T0"));                    // 1000ms 도달
        }

        [Test]
        public void Oscillating_Circuit_IsFlaggedUnstable()
        {
            // Y0 = /Y0 : 스캔마다 켜졌다 꺼졌다 하는 회로. 스캔 횟수가 짝수여서 우연히 '꺼짐'으로 보여도 통과시키면 안 된다.
            var p = new LadderProgram();
            var r = new Rung { coil = "Y0" };
            r.branches.Add(new List<Contact> { new Contact("Y0", ContactType.NC) });
            p.rungs.Add(r);
            var steps = new List<LadderStep> { LadderTester.Step("대기", "X0=0", "Y0=0") };
            var report = LadderTester.Run(p, steps);
            Assert.IsFalse(report.passed);
            Assert.IsTrue(report.unstable);
        }

        [Test]
        public void Challenges_HaveValidSlotsAndHints()
        {
            foreach (var ch in LadderChallengeLibrary.All)
            {
                var answer = ch.buildAnswer();
                foreach (var pos in ch.hidden)
                    Assert.Less(pos.index, answer.rungs[pos.rung].branches[pos.branch].Count, $"{ch.id} 슬롯 범위");
                Assert.GreaterOrEqual(ch.BuildPalette().Count, ch.hidden.Count);
            }
        }
    }

    public class HydraulicLogicTests
    {
        [Test]
        public void EveryLevel_HasAtLeastOneSolution_ButNotTooMany()
        {
            foreach (var lv in HydraulicLevelLibrary.All)
            {
                int n = HydraulicLogic.SolvableDesigns(lv).Count;
                int total = lv.inputAreasCm2.Length * lv.outputAreasCm2.Length * lv.reliefBars.Length;
                Assert.GreaterOrEqual(n, 1, $"{lv.id} 풀이 없음");
                Assert.Less(n, total / 3, $"{lv.id} 풀이가 너무 많음 ({n}/{total})");
            }
        }

        [Test]
        public void Level1_OutcomesMatchPhysics()
        {
            var lv = HydraulicLevelLibrary.ById(HydraulicLevelLibrary.Lv1);
            // 입력 면적 {2,5,10,20}, 출력 {20,50,100,200,400}, 릴리프 {2,5,10,25}
            Assert.AreEqual(HydraulicOutcome.Success,           HydraulicLogic.Evaluate(lv, new HydraulicDesign(1, 2, 1)).outcome); // 5 -> 100, 5bar
            Assert.AreEqual(HydraulicOutcome.ReliefOpens,       HydraulicLogic.Evaluate(lv, new HydraulicDesign(1, 2, 0)).outcome); // 릴리프 2bar < 3.3bar
            Assert.AreEqual(HydraulicOutcome.HoseBurst,         HydraulicLogic.Evaluate(lv, new HydraulicDesign(0, 0, 3)).outcome); // 출력 면적 20 -> 16.5bar > 호스 8bar
            Assert.AreEqual(HydraulicOutcome.InsufficientForce, HydraulicLogic.Evaluate(lv, new HydraulicDesign(2, 2, 1)).outcome); // 펌프 2bar < 3.3bar
            Assert.AreEqual(HydraulicOutcome.TooSlow,           HydraulicLogic.Evaluate(lv, new HydraulicDesign(0, 2, 1)).outcome); // 면적비 50 -> 100회 > 60회
        }

        [Test]
        public void Level2_StrictRelief_RejectsReliefAboveHoseLimit()
        {
            var lv = HydraulicLevelLibrary.ById(HydraulicLevelLibrary.Lv2);
            var r = HydraulicLogic.Evaluate(lv, new HydraulicDesign(1, 2, 4));      // 릴리프 30bar > 호스 20bar
            Assert.AreEqual(HydraulicOutcome.UnsafeRelief, r.outcome);
        }

        [Test]
        public void Pascal_ForceScalesWithAreaRatio()
        {
            // 퀴즈 'q.hyd.jack'과 같은 계산: 10cm², 100N -> 100cm² 에서 1000N
            float p = HydraulicLogic.PressureBar(100f, 10f);          // 1 bar
            Assert.AreEqual(1f, p, 1e-4f);
            Assert.AreEqual(1000f, p * 100f * 10f, 1e-3f);
        }
    }

    public class ExamDataTests
    {
        [Test]
        public void CsvParser_HandlesQuotesCommasNewlinesAndErrors()
        {
            string csv =
                "id,origin,examLabel,concept,difficulty,question,choice1,choice2,choice3,choice4,answer,explanation,fieldNote\r\n" +
                "a.1,기출,2024년 1회,LadderSelfHold,2,\"질문, 쉼표와 \"\"따옴표\"\" 포함\n둘째 줄\",가,나,다,라,3,해설,노트\r\n" +
                "a.2,PastExam,,NoSuchConcept,1,q,1,2,3,4,1,,\r\n" +
                "a.3,Original,,Safety,1,q,1,2,3,4,9,,\r\n";

            var result = ExamCsvParser.Parse(csv);

            Assert.AreEqual(1, result.questions.Count);
            var q = result.questions[0];
            Assert.AreEqual(QuestionOrigin.PastExam, q.origin);
            Assert.AreEqual(ConceptTag.LadderSelfHold, q.concept);
            Assert.AreEqual(2, q.answerIndex);                       // 3번 -> 인덱스 2
            StringAssert.Contains("\"따옴표\"", q.question);
            StringAssert.Contains("\n둘째 줄", q.question);
            Assert.AreEqual(2, result.errors.Count);                 // 알 수 없는 concept, 정답 번호 범위 초과
        }

        [Test]
        public void DefaultSeed_ValidatesWithoutIssues()
        {
            var db = ScriptableObject.CreateInstance<PuzzleScenarioDB>();
            db.SeedDefaults();
            var issues = db.Validate();
            Assert.IsEmpty(issues, string.Join("\n", issues));
            Object.DestroyImmediate(db);
        }

        [Test]
        public void SeedDefaults_KeepsImportedPastExamQuestions()
        {
            var db = ScriptableObject.CreateInstance<PuzzleScenarioDB>();
            db.SeedDefaults();
            db.ImportCsv("p.1,PastExam,2024년 1회,Safety,1,q,a,b,c,d,1,,\n");
            db.SeedDefaults();                                        // 다시 시드해도 기출은 남아야 한다
            Assert.IsNotNull(db.GetQuestion("p.1"));
            Object.DestroyImmediate(db);
        }

        [Test]
        public void ReviewQuestion_PrefersPastExamOverOriginal()
        {
            var db = ScriptableObject.CreateInstance<PuzzleScenarioDB>();
            db.SeedDefaults();
            db.ImportCsv("p.2,PastExam,2024년 1회,LadderSelfHold,1,q,a,b,c,d,1,,\n");
            var scenario = db.Scenarios.First(s => s.id == "ladder.selfhold");
            Assert.AreEqual("p.2", db.PickReviewQuestion(scenario, () => 0f).id);   // 같은 개념의 기출이 우선
            Object.DestroyImmediate(db);
        }

        [Test]
        public void WeightedPick_FavorsHeavierItems()
        {
            var items = new List<string> { "light", "heavy" };
            float W(string s) => s == "heavy" ? 9f : 1f;
            Assert.AreEqual("light", WeightedPick.Pick(items, W, () => 0.05f));
            Assert.AreEqual("heavy", WeightedPick.Pick(items, W, () => 0.5f));
        }
    }

    public class PneumaticLogicTests
    {
        // 정답 배선: 밸브 2번 -> Rod, 4번 -> Cap, 공급 연결, 배기구 개방. 압력은 레벨별로 다르다.
        private static PneumaticWiring Wiring(PneumaticLevel lv, int reg)
        {
            var w = lv.NewWiring();
            w.port2 = CylPort.Rod; w.port4 = CylPort.Cap;
            w.supplyConnected = true; w.exhaust3Open = true; w.exhaust5Open = true;
            w.regulatorIndex = reg;
            return w;
        }

        [TestCase(PneumaticLevelLibrary.Lv1, 1, true)]    // 4bar
        [TestCase(PneumaticLevelLibrary.Lv1, 4, false)]   // 10bar > 호스 8bar
        [TestCase(PneumaticLevelLibrary.Lv1, 0, false)]   // 2bar: 힘 부족
        [TestCase(PneumaticLevelLibrary.Lv2, 2, true)]    // 6bar
        [TestCase(PneumaticLevelLibrary.Lv2, 1, false)]   // 4bar: 800N 하중에 부족
        [TestCase(PneumaticLevelLibrary.Lv3, 3, true)]    // 8bar
        [TestCase(PneumaticLevelLibrary.Lv3, 2, false)]   // 6bar: 1180N 필요, 약 1178N
        public void CorrectWiring_PassesOnlyWithRightPressure(string id, int reg, bool expected)
        {
            var lv = PneumaticLevelLibrary.ById(id);
            Assert.AreEqual(expected, PneumaticTester.Run(lv, Wiring(lv, reg)).passed);
        }

        [Test]
        public void ReversedConnection_IsDiagnosed()
        {
            var lv = PneumaticLevelLibrary.ById(PneumaticLevelLibrary.Lv1);
            var w = Wiring(lv, 1); w.port2 = CylPort.Cap; w.port4 = CylPort.Rod;
            var r = PneumaticTester.Run(lv, w);
            Assert.IsFalse(r.passed);
            Assert.AreEqual(PneumaticFault.ReversedConnection, r.fault);
        }

        [Test]
        public void BothOutletsToSameChamber_IsShortCircuit()
        {
            var lv = PneumaticLevelLibrary.ById(PneumaticLevelLibrary.Lv1);
            var w = Wiring(lv, 1); w.port2 = CylPort.Cap; w.port4 = CylPort.Cap;
            Assert.AreEqual(PneumaticFault.ShortCircuit, PneumaticTester.Run(lv, w).fault);
        }

        [Test]
        public void MissingHose_OnPressurizedOutlet_Leaks()
        {
            var lv = PneumaticLevelLibrary.ById(PneumaticLevelLibrary.Lv1);
            var w = Wiring(lv, 1); w.port2 = CylPort.None;     // 복귀 위치에서 2번이 가압되는데 호스가 없음
            Assert.AreEqual(PneumaticFault.Leak, PneumaticTester.Run(lv, w).fault);
        }

        [Test]
        public void BlockedExhaust_TrapsAir_AndStopsCylinder()
        {
            var lv = PneumaticLevelLibrary.ById(PneumaticLevelLibrary.Lv2);
            var w = Wiring(lv, 2); w.exhaust5Open = false;     // 소음기 막힘 그대로
            var r = PneumaticTester.Run(lv, w);
            Assert.IsFalse(r.passed);
            Assert.AreEqual(PneumaticFault.ExhaustBlocked, r.fault);
        }

        [Test]
        public void SupplyDisconnected_ReportsNoSupply()
        {
            var lv = PneumaticLevelLibrary.ById(PneumaticLevelLibrary.Lv2);
            var w = Wiring(lv, 2); w.supplyConnected = false;
            Assert.AreEqual(PneumaticFault.NoSupply, PneumaticTester.Run(lv, w).fault);
        }

        [Test]
        public void Bistable_ValveRemembersPosition()
        {
            var lv = PneumaticLevelLibrary.ById(PneumaticLevelLibrary.Lv3);
            var sim = new PneumaticSimulator(lv, Wiring(lv, 3));
            sim.Command(PneuAction.Pulse14);
            for (int i = 0; i < 400; i++) sim.Step(0.01f);
            Assert.Greater(sim.X, 0.97f);
            for (int i = 0; i < 200; i++) sim.Step(0.01f);     // 신호 없이도 유지
            Assert.Greater(sim.X, 0.97f);
            sim.Command(PneuAction.Pulse12);
            for (int i = 0; i < 400; i++) sim.Step(0.01f);
            Assert.Less(sim.X, 0.03f);
        }

        [Test]
        public void EveryLevel_HasExactlyTheIntendedWiring()
        {
            foreach (var lv in PneumaticLevelLibrary.All)
            {
                int solutions = 0;
                foreach (CylPort p2 in System.Enum.GetValues(typeof(CylPort)))
                foreach (CylPort p4 in System.Enum.GetValues(typeof(CylPort)))
                foreach (var sup in new[] { true, false })
                foreach (var e3 in new[] { true, false })
                foreach (var e5 in new[] { true, false })
                for (int reg = 0; reg < lv.regulatorBars.Length; reg++)
                {
                    var w = new PneumaticWiring { port2 = p2, port4 = p4, supplyConnected = sup, exhaust3Open = e3, exhaust5Open = e5, regulatorIndex = reg };
                    if (PneumaticTester.Run(lv, w).passed)
                    {
                        solutions++;
                        Assert.AreEqual(CylPort.Rod, p2); Assert.AreEqual(CylPort.Cap, p4);
                    }
                }
                Assert.GreaterOrEqual(solutions, 1, lv.id);
                Assert.LessOrEqual(solutions, 3, lv.id);
            }
        }
    }

    public class UiLogicTests
    {
        [Test]
        public void RhythmJudge_GradesByDistanceToNearestBeat()
        {
            var j = new RhythmJudge(0.5f, 0.12f, 0.2f);   // 박자 0.5초 -> 창이 0.25 이상이면 Miss가 불가능하므로 0.2로 둔다
            Assert.AreEqual(RhythmGrade.Perfect, j.Judge(1.0f).grade);
            Assert.AreEqual(RhythmGrade.Perfect, j.Judge(1.1f).grade);
            Assert.AreEqual(RhythmGrade.Good, j.Judge(1.18f).grade);
            Assert.AreEqual(RhythmGrade.Miss, j.Judge(1.23f).grade);    // 박자 1.0과 1.5의 한가운데 근처
            Assert.IsTrue(j.Judge(0.9f).IsEarly);
            Assert.IsFalse(j.Judge(1.1f).IsEarly);
            Assert.AreEqual(1f, j.Judge(2.0f).Quality);
            Assert.AreEqual(0.5f, j.Judge(2.18f).Quality);
            Assert.AreEqual(0f, j.Judge(2.23f).Quality);
        }

        [Test]
        public void RhythmJudge_RejectsInvalidWindows()
        {
            Assert.Throws<System.ArgumentException>(() => new RhythmJudge(0.5f, 0.3f, 0.2f));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new RhythmJudge(0f, 0.1f, 0.2f));
        }

        [Test]
        public void RhythmJudge_LatencyCompensationShiftsInput()
        {
            var j = new RhythmJudge(0.5f, 0.12f, 0.2f);
            Assert.AreEqual(RhythmGrade.Good, j.Judge(1.18f).grade);
            Assert.AreEqual(RhythmGrade.Perfect, j.Judge(1.18f, 0.1f).grade);   // 지연 0.1초 보정 -> 1.08
        }

        [Test]
        public void RhythmSession_BlocksMashingOnSameBeat()
        {
            var s = new RhythmSession(new RhythmJudge(0.5f, 0.12f, 0.2f));
            Assert.AreEqual(RhythmGrade.Perfect, s.Press(1.0f).grade);
            Assert.AreEqual(RhythmGrade.Miss, s.Press(1.05f).grade);       // 같은 박자 두 번째
            Assert.AreEqual(RhythmGrade.Perfect, s.Press(1.5f).grade);     // 다음 박자는 정상
        }

        [Test]
        public void RhythmJudge_CrossedBeat_AndApproach()
        {
            var j = new RhythmJudge(0.5f, 0.1f, 0.2f);
            Assert.IsTrue(j.CrossedBeat(0.49f, 0.51f));
            Assert.IsFalse(j.CrossedBeat(0.51f, 0.6f));
            Assert.AreEqual(0.5f, j.ApproachProgress(0.25f), 1e-5f);
        }

        [Test]
        public void ChoiceShuffler_KeepsAnswerAndMapping()
        {
            var choices = new[] { "A", "B", "C", "D" };
            for (int seed = 0; seed < 50; seed++)
            {
                var rnd = new System.Random(seed);
                var v = ChoiceShuffler.Shuffle(choices, 2, () => (float)rnd.NextDouble());
                Assert.AreEqual("C", v.choices[v.answerIndex]);
                for (int i = 0; i < 4; i++) Assert.AreEqual(choices[v.ToOriginal(i)], v.choices[i]);
                CollectionAssert.AreEquivalent(choices, v.choices);
            }
        }
    }

    public class SaveSystemTests
    {
        private static SaveData Sample()
        {
            var d = SaveData.NewGame(IslandCatalog.Default());
            d.playTimeSeconds = 123.5f; d.deaths = 2;
            d.player.stats.Add(new StatSave { type = StatType.Hunger, current = 42.5f });
            d.player.inventory.wood = 7; d.player.inventory.food = 3;
            d.concepts.Add(new ConceptSave { concept = ConceptTag.HydraulicPressure, attempts = 4, successes = 3, secondsSinceSeen = 30f });
            d.oopiSeen.Add(new OopiSeenSave { lineId = "idle.1", count = 2 });
            d.GetIsland("island.landing").machines.Add(new MachineSave { id = "m1", kind = MachineKind.WaterPurifier, tier = 2, running = true });
            return d;
        }

        [Test]
        public void RoundTrip_PreservesEverything()
        {
            var sys = new SaveSystem(new MemorySaveStorage());
            Assert.IsTrue(sys.Save(1, Sample(), out _));
            var r = sys.Load(1);
            Assert.IsTrue(r.ok, r.error);
            Assert.AreEqual(123.5f, r.data.playTimeSeconds, 1e-3f);
            Assert.AreEqual(2, r.data.deaths);
            Assert.AreEqual(42.5f, r.data.player.stats[0].current, 1e-3f);
            Assert.AreEqual(7, r.data.player.inventory.wood);
            Assert.AreEqual(3, r.data.concepts[0].successes);
            Assert.AreEqual(2, r.data.oopiSeen[0].count);
            var m = r.data.GetIsland("island.landing").GetMachine("m1");
            Assert.AreEqual(2, m.tier); Assert.IsTrue(m.running); Assert.AreEqual(MachineKind.WaterPurifier, m.kind);
        }

        [Test]
        public void Fnv1a_MatchesKnownVectors()
        {
            Assert.AreEqual(0x811c9dc5u, SaveFormat.Fnv1a32(""));
            Assert.AreEqual(0xe40c292cu, SaveFormat.Fnv1a32("a"));
            Assert.AreEqual(0xbf9cf968u, SaveFormat.Fnv1a32("foobar"));
        }

        [Test]
        public void TamperedFile_IsDetected()
        {
            var store = new MemorySaveStorage();
            var sys = new SaveSystem(store);
            sys.Save(1, Sample(), out _);
            store.files["slot1"] = store.files["slot1"].Replace("\"wood\":7", "\"wood\":9");
            var r = sys.Load(1);
            Assert.IsFalse(r.ok);
            StringAssert.Contains("손상", r.error);
        }

        [Test]
        public void CorruptedPrimary_FallsBackToBackup()
        {
            var store = new MemorySaveStorage();
            var sys = new SaveSystem(store);
            var first = Sample(); first.player.inventory.wood = 1;
            sys.Save(0, first, out _);
            var second = Sample(); second.player.inventory.wood = 2;
            sys.Save(0, second, out _);                         // 첫 저장이 백업이 된다
            store.files["autosave"] = store.files["autosave"].Substring(0, 40);   // 쓰다 끊김

            var r = sys.Load(0);
            Assert.IsTrue(r.ok); Assert.IsTrue(r.usedBackup);
            Assert.AreEqual(1, r.data.player.inventory.wood);
        }

        [Test]
        public void MissingFile_ReportsError_AndNewerVersionIsRejected()
        {
            var store = new MemorySaveStorage();
            var sys = new SaveSystem(store);
            Assert.IsFalse(sys.Load(2).ok);

            var d = Sample(); d.version = SaveData.CurrentVersion + 1;
            store.files["slot3"] = SaveFormat.Encode(JsonUtility.ToJson(d));   // Save()는 버전을 덮어쓰므로 직접 기록
            var r = sys.Load(3);
            Assert.IsFalse(r.ok);
            StringAssert.Contains("새로운 버전", r.error);
        }

        [Test]
        public void Sanitize_FixesHandEditedValues()
        {
            var d = Sample();
            d.player.inventory.wood = -5;
            d.player.stats.Add(new StatSave { type = StatType.Health, current = 0f });
            d.concepts.Add(new ConceptSave { concept = ConceptTag.HydraulicPressure, attempts = 2, successes = 99 });   // 같은 개념 중복 + 성공 > 시도
            var landing = d.GetIsland("island.landing");
            landing.machines.Add(new MachineSave { id = "m1", tier = 99 });                                            // 중복 id
            landing.machines.Add(new MachineSave { id = "m2", tier = 99, running = true });
            d.islands.Add(new IslandSave { id = "island.landing" });                                                    // 중복 섬

            SaveMigration.Sanitize(d);

            Assert.AreEqual(0, d.player.inventory.wood);
            Assert.GreaterOrEqual(d.player.stats.Find(s => s.type == StatType.Health).current, 1f);
            Assert.AreEqual(1, d.concepts.Count);
            Assert.LessOrEqual(d.concepts[0].successes, d.concepts[0].attempts);
            Assert.AreEqual(1, d.islands.FindAll(i => i.id == "island.landing").Count);
            Assert.AreEqual(2, d.GetIsland("island.landing").machines.Count);
            Assert.AreEqual(5, d.GetIsland("island.landing").GetMachine("m2").tier);
        }

        [Test]
        public void ListSlots_ShowsAutoPlusManualSlots()
        {
            var store = new MemorySaveStorage();
            var sys = new SaveSystem(store);
            sys.Save(2, Sample(), out _);
            var slots = sys.ListSlots();
            Assert.AreEqual(4, slots.Count);
            Assert.IsFalse(slots[1].exists);
            Assert.IsTrue(slots[2].exists); Assert.IsTrue(slots[2].valid);
            Assert.AreEqual("island.landing", slots[2].islandId);
        }
    }

    public class GameProgressTests
    {
        private static readonly System.Func<ConceptTag, MasterySample> NoKnowledge = _ => new MasterySample(0, 0);
        private static readonly System.Func<ConceptTag, MasterySample> GoodKnowledge = _ => new MasterySample(5, 5);

        private static GameProgress New() => new GameProgress(SaveData.NewGame(IslandCatalog.Default()), IslandCatalog.Default());

        private static void Run(GameProgress p, string island, MachineKind kind, int tier, string id = null)
        {
            p.Data.GetOrAddIsland(island).UpsertMachine(new MachineSave { id = id ?? $"{island}/{kind}", kind = kind, tier = tier, running = true });
        }

        private static void ClearIsland1(GameProgress p)
        {
            Run(p, "island.landing", MachineKind.WaterPurifier, 1);
            Run(p, "island.landing", MachineKind.PowerGenerator, 1);
            Run(p, "island.landing", MachineKind.AutoGarden, 1);
            Assert.IsTrue(p.RefreshClear(out _));
        }

        [Test]
        public void NewGame_StartsOnFirstIsland_OthersLocked()
        {
            var p = New();
            Assert.AreEqual("island.landing", p.Data.currentIslandId);
            Assert.IsTrue(p.Data.GetIsland("island.landing").unlocked);
            Assert.IsFalse(p.Data.GetIsland("island.foundry").unlocked);
        }

        [Test]
        public void Clear_RequiresEveryMachine_AndUnlocksNextIsland()
        {
            var p = New();
            Run(p, "island.landing", MachineKind.WaterPurifier, 1);
            Run(p, "island.landing", MachineKind.PowerGenerator, 1);
            Assert.IsFalse(p.RefreshClear(out _));
            var report = p.EvaluateClear(p.Current);
            Assert.AreEqual(1, report.missing.Count);
            StringAssert.Contains("자동 재배기", report.missing[0]);

            Run(p, "island.landing", MachineKind.AutoGarden, 1);
            Assert.IsTrue(p.RefreshClear(out var cleared));
            Assert.AreEqual("island.landing", cleared.id);
            Assert.IsTrue(p.Data.GetIsland("island.foundry").unlocked);
            Assert.IsFalse(p.RefreshClear(out _));            // 두 번째 호출은 '처음 클리어'가 아니다
        }

        [Test]
        public void Clear_IsSticky_EvenIfMachineLaterBreaks()
        {
            var p = New();
            ClearIsland1(p);
            p.Data.GetIsland("island.landing").GetMachine("island.landing/WaterPurifier").running = false;
            p.RefreshClear(out _);
            Assert.IsTrue(p.Data.GetIsland("island.landing").cleared);
        }

        [Test]
        public void Machine_BelowRequiredTier_DoesNotCount()
        {
            var p = New();
            Run(p, "island.landing", MachineKind.WaterPurifier, 1);
            Run(p, "island.landing", MachineKind.PowerGenerator, 1);
            Run(p, "island.landing", MachineKind.AutoGarden, 1);
            p.Data.GetIsland("island.landing").GetMachine("island.landing/AutoGarden").running = false;   // 고장
            Assert.IsFalse(p.EvaluateClear(p.Current).cleared);
        }

        [Test]
        public void Travel_Forward_NeedsClear_Gate_AndSupplies()
        {
            var p = New();

            var t = p.CheckTravel("island.foundry", GoodKnowledge, 99, 99);
            Assert.IsFalse(t.ok);                              // 아직 클리어 전
            StringAssert.Contains("복구", t.reason);

            ClearIsland1(p);

            t = p.CheckTravel("island.foundry", NoKnowledge, 99, 99);
            Assert.IsFalse(t.ok);                              // 지식 관문 미통과
            Assert.Greater(t.details.Count, 0);

            t = p.CheckTravel("island.foundry", GoodKnowledge, 2, 99);
            Assert.IsFalse(t.ok);                              // 식량 부족 (필요 3)
            StringAssert.Contains("보급", t.reason);

            t = p.CheckTravel("island.foundry", GoodKnowledge, 3, 3);
            Assert.IsTrue(t.ok);
            Assert.AreEqual(3, t.foodCost); Assert.AreEqual(3, t.waterCost);
        }

        [Test]
        public void Gate_RequiresAverageMastery()
        {
            var p = New();
            ClearIsland1(p);
            System.Func<ConceptTag, MasterySample> weak = _ => new MasterySample(4, 1);    // 시도는 했지만 25%
            var t = p.CheckTravel("island.foundry", weak, 99, 99);
            Assert.IsFalse(t.ok);
            StringAssert.Contains("숙련도", t.details[0]);
        }

        [Test]
        public void Travel_CannotSkipIslands_OrTravelToCurrent()
        {
            var p = New();
            ClearIsland1(p);
            Assert.IsFalse(p.CheckTravel("island.core", GoodKnowledge, 99, 99).ok);
            Assert.IsFalse(p.CheckTravel("island.landing", GoodKnowledge, 99, 99).ok);
            Assert.IsFalse(p.CheckTravel("island.nowhere", GoodKnowledge, 99, 99).ok);
        }

        [Test]
        public void Travel_Back_IsFree_ButOnlyToVisitedIslands()
        {
            var p = New();
            ClearIsland1(p);
            p.ApplyTravel("island.foundry");
            Assert.AreEqual("island.foundry", p.Data.currentIslandId);

            var back = p.CheckTravel("island.landing", NoKnowledge, 0, 0);    // 보급 0, 지식 0이어도 돌아갈 수 있다
            Assert.IsTrue(back.ok);
            Assert.AreEqual(0, back.foodCost);

            var fresh = New();
            Assert.IsFalse(fresh.CheckTravel("island.core", GoodKnowledge, 99, 99).ok);
        }

        [Test]
        public void FinishingLastIsland_CompletesGame()
        {
            var p = New();
            p.Data.currentIslandId = "island.core";
            p.Data.GetIsland("island.core").unlocked = true;
            foreach (var req in p.Find("island.core").clearRequirements) Run(p, "island.core", req.kind, req.minTier);
            Assert.IsTrue(p.RefreshClear(out _));
            Assert.IsTrue(p.IsComplete);
        }

        [Test]
        public void DefaultCatalog_IsOrderedAndGatesUseExistingConcepts()
        {
            var cat = IslandCatalog.Default();
            for (int i = 1; i < cat.Count; i++) Assert.Greater(cat[i].order, cat[i - 1].order);
            Assert.IsTrue(cat[0].entryGate.IsEmpty);
            foreach (var isl in cat) Assert.Greater(isl.clearRequirements.Count, 0);
        }
    }

    public class OopiDialogueTests
    {
        private static OopiDialogueDB Seeded()
        {
            var db = ScriptableObject.CreateInstance<OopiDialogueDB>();
            db.SeedDefaults();
            return db;
        }

        [Test]
        public void DefaultData_ValidatesWithoutIssues()
        {
            var db = Seeded();
            var issues = db.Validate();
            Assert.IsEmpty(issues, string.Join("\n", issues));
            Object.DestroyImmediate(db);
        }

        [Test]
        public void Format_ReplacesKnownVars_AndKeepsUnknown()
        {
            var vars = new System.Collections.Generic.Dictionary<string, string> { { "machine", "크레인" }, { "tier", "3" } };
            Assert.AreEqual("크레인 Tier 3 {nope}", OopiText.Format("{machine} Tier {tier} {nope}", vars));
            Assert.AreEqual("열린 { 괄호", OopiText.Format("열린 { 괄호", vars));
        }

        [Test]
        public void Diagnosis_PrefersExactCause_OverGenericPuzzleLine()
        {
            var db = Seeded();
            var q = new OopiQuery(OopiSituation.Diagnosis).Puzzle(OopiPuzzleFilter.Pneumatic).Cause("pneu.ExhaustBlocked");
            for (int i = 0; i < 10; i++)
            {
                var speech = db.Say(q, () => 0.3f);
                Assert.AreEqual("pneu.ExhaustBlocked", speech.line.cause);
            }
            Object.DestroyImmediate(db);
        }

        [Test]
        public void ConceptSpecificLine_BeatsGenericOne()
        {
            var db = Seeded();
            var q = new OopiQuery(OopiSituation.Diagnosis).Puzzle(OopiPuzzleFilter.Ladder).Cause("ladder.ShouldBeOn").Concept(ConceptTag.LadderTimer);
            Assert.AreEqual("dx.lad.on.3", db.Say(q, () => 0f).line.id);
            Object.DestroyImmediate(db);
        }

        [Test]
        public void SameLine_IsNotRepeatedWithinRecentWindow()
        {
            var db = Seeded();
            var q = new OopiQuery(OopiSituation.Diagnosis).Puzzle(OopiPuzzleFilter.Pneumatic).Cause("pneu.Leak");
            var a = db.Say(q, () => 0f).line.id;
            var b = db.Say(q, () => 0f).line.id;
            Assert.AreNotEqual(a, b);                          // 후보가 2개뿐이므로 번갈아 나온다
            Object.DestroyImmediate(db);
        }

        [Test]
        public void OnceLine_IsSpokenOnlyOnce_AndMemoryRoundTrips()
        {
            var lines = new System.Collections.Generic.List<OopiLine>
            {
                new OopiLine { id = "once", situation = OopiSituation.Idle, text = "한 번만", once = true },
                new OopiLine { id = "many", situation = OopiSituation.Idle, text = "여러 번" },
            };
            var memory = new OopiMemory();
            var q = new OopiQuery(OopiSituation.Idle);
            Assert.AreEqual("once", OopiSelector.Pick(lines, q, memory, () => 0f).id);
            memory.Record("once");
            Assert.AreEqual("many", OopiSelector.Pick(lines, q, memory, () => 0f).id);

            var restored = new OopiMemory();
            restored.Import(memory.Export());                  // 저장 -> 불러오기 후에도 '이미 한 말'
            Assert.AreEqual("many", OopiSelector.Pick(lines, q, restored, () => 0f).id);
        }

        [Test]
        public void MissingDiagnosis_FallsBackToGenericFailure()
        {
            var db = Seeded();
            var q = new OopiQuery(OopiSituation.Diagnosis).Puzzle(OopiPuzzleFilter.Pipe).Cause("pipe.Unknown");
            var speech = db.Say(q, () => 0f);
            Assert.IsNotNull(speech);
            Assert.AreEqual(OopiSituation.Failure, speech.line.situation);
            Object.DestroyImmediate(db);
        }

        [Test]
        public void TierUp_UsesTierSpecificLine_AndFillsVars()
        {
            var db = Seeded();
            var q = new OopiQuery(OopiSituation.TierUp).Tier(5).Var("machine", "크레인");
            var speech = db.Say(q, () => 0f);
            Assert.AreEqual("tier.5", speech.line.id);
            Assert.IsNotEmpty(speech.text);
            Assert.IsFalse(speech.text.Contains("{"));
            Object.DestroyImmediate(db);
        }

        [Test]
        public void CauseKeys_MapFromPuzzleVerdicts()
        {
            Assert.AreEqual("pneu.ExhaustBlocked", OopiCauses.From(PneumaticFault.ExhaustBlocked));
            Assert.IsNull(OopiCauses.From(PneumaticFault.None));
            Assert.AreEqual("hyd.TooSlow", OopiCauses.From(HydraulicCue.Overheat, new HydraulicReport { outcome = HydraulicOutcome.TooSlow }));
            Assert.AreEqual("hyd.Overheat", OopiCauses.From(HydraulicCue.Overheat, new HydraulicReport { outcome = HydraulicOutcome.Success }));
            Assert.AreEqual("ladder.Interlock", OopiCauses.From(new LadderTestReport { passed = false, interlockViolation = true }));
            Assert.AreEqual("ladder.ShouldBeOff", OopiCauses.From(new LadderTestReport { passed = false, expected = false, actual = true }));
        }
    }

    public class DialoguePlayerTests
    {
        private static DialogueEntry E(string id, int pri = 2, string text = "열 글자짜리 대사입니다", string tip = null) =>
            new DialogueEntry { id = id, text = text, tip = tip, priority = pri, situation = OopiSituation.Success };

        private static void RunFor(DialoguePlayer p, float seconds, float dt = 0.05f)
        {
            for (float t = 0; t < seconds; t += dt) p.Tick(dt);
        }

        [Test]
        public void MultipleLines_PlayInArrivalOrder_OneAtATime()
        {
            var p = new DialoguePlayer();
            var started = new List<string>();
            p.LineStarted += e => started.Add(e.id);
            p.Enqueue(E("a")); p.Enqueue(E("b")); p.Enqueue(E("c"));
            Assert.AreEqual("a", p.Current.id);
            Assert.AreEqual(2, p.Pending);
            RunFor(p, 30f);
            CollectionAssert.AreEqual(new[] { "a", "b", "c" }, started);
            Assert.IsNull(p.Current);
        }

        [Test]
        public void Typing_RevealsCharactersGradually_ThenHolds()
        {
            var p = new DialoguePlayer { CharsPerSecond = 10f };
            p.Enqueue(E("a", text: "0123456789"));
            p.Tick(0.5f);
            Assert.AreEqual(5, p.VisibleChars);
            Assert.AreEqual(DialoguePlayer.State.Typing, p.Phase);
            p.Tick(0.6f);
            Assert.AreEqual(10, p.VisibleChars);
            Assert.AreEqual(DialoguePlayer.State.Holding, p.Phase);
        }

        [Test]
        public void Tip_ShowsOnlyWhileHolding()
        {
            var p = new DialoguePlayer();
            p.Enqueue(E("a", tip: "팁"));
            Assert.IsFalse(p.TipVisible);
            p.Skip();                         // 타자 중 -> 즉시 완성 -> 유지 단계
            Assert.IsTrue(p.TipVisible);
        }

        [Test]
        public void Skip_FinishesTypingFirst_ThenAdvances()
        {
            var p = new DialoguePlayer();
            p.Enqueue(E("a")); p.Enqueue(E("b"));
            p.Skip();
            Assert.AreEqual("a", p.Current.id);
            Assert.AreEqual(p.Current.text.Length, p.VisibleChars);
            p.Skip();
            Assert.AreEqual("b", p.Current.id);
        }

        [Test]
        public void ConsecutiveDuplicate_AndBusyIdleChatter_AreDropped()
        {
            var p = new DialoguePlayer();
            Assert.IsTrue(p.Enqueue(E("a")));
            Assert.IsFalse(p.Enqueue(E("a")));
            Assert.IsFalse(p.Enqueue(E("idle", 0)));
            Assert.AreEqual(0, p.Pending);
        }

        [Test]
        public void IdleChatter_PlaysWhenNothingElseIsGoing()
        {
            var p = new DialoguePlayer();
            Assert.IsTrue(p.Enqueue(E("idle", 0)));
            Assert.AreEqual("idle", p.Current.id);
        }

        [Test]
        public void Overflow_DropsLeastImportantOldest()
        {
            var p = new DialoguePlayer { MaxQueue = 3 };
            p.Enqueue(E("cur"));
            p.Enqueue(E("hi1", 3)); p.Enqueue(E("lo1", 1)); p.Enqueue(E("hi2", 3));
            p.Enqueue(E("hi3", 3));          // 넘침 -> 중요도 1짜리가 희생
            Assert.AreEqual(3, p.Pending);
            var order = new List<string>();
            p.LineStarted += e => order.Add(e.id);
            RunFor(p, 60f);
            CollectionAssert.AreEqual(new[] { "hi1", "hi2", "hi3" }, order);
        }

        [Test]
        public void StaleLowPriorityLine_IsSkipped_ImportantOneIsNot()
        {
            var p = new DialoguePlayer { StaleSeconds = 5f, MinHoldSeconds = 20f };
            p.Enqueue(E("cur"));
            p.Enqueue(E("warn", 1)); p.Enqueue(E("result", 2));
            var order = new List<string>();
            p.LineStarted += e => order.Add(e.id);
            RunFor(p, 120f);
            CollectionAssert.AreEqual(new[] { "result" }, order);
        }

        [Test]
        public void LongQueue_CatchesUpFasterThanShortQueue()
        {
            float Duration(int extra)
            {
                var p = new DialoguePlayer { MaxQueue = 10 };
                p.Enqueue(E("x0"));
                for (int i = 1; i <= extra; i++) p.Enqueue(E("x" + i));
                float t = 0;
                while (p.Current != null && t < 300f) { p.Tick(0.05f); t += 0.05f; }
                return t / (extra + 1);
            }
            Assert.Less(Duration(5), Duration(1));
        }

        [Test]
        public void Priority_MapsSituations()
        {
            Assert.AreEqual(3, DialoguePriority.Of(OopiSituation.Diagnosis));
            Assert.AreEqual(3, DialoguePriority.Of(OopiSituation.TimeLow));
            Assert.AreEqual(1, DialoguePriority.Of(OopiSituation.SurvivalWarning));
            Assert.AreEqual(0, DialoguePriority.Of(OopiSituation.Idle));
            Assert.AreEqual(2, DialoguePriority.Of(OopiSituation.Success));
        }
    }

    public class SlotPresenterTests
    {
        private static readonly System.DateTime Now = new System.DateTime(2026, 10, 31, 12, 0, 0, System.DateTimeKind.Utc);
        private static System.DateTime Kst(System.DateTime d) => d.AddHours(9);
        private static string Name(string id) => id == "island.landing" ? "불시착 해안" : id;

        private static SlotSummary Ok(int slot, bool backup = false, bool complete = false) => new SlotSummary
        {
            slot = slot, exists = true, valid = true, usedBackup = backup, gameComplete = complete,
            islandId = "island.landing", playTimeSeconds = 7500f, savedAtUtc = "2026-10-31T11:57:00Z",
        };

        [TestCase(5f, "5초")]
        [TestCase(125f, "2분")]
        [TestCase(7500f, "2시간 05분")]
        [TestCase(-3f, "0초")]
        public void PlayTime_Formats(float sec, string expected) => Assert.AreEqual(expected, SlotPresenter.FormatPlayTime(sec));

        [Test]
        public void SavedAt_ConvertsToLocal_AndRejectsGarbage()
        {
            Assert.AreEqual("2026-10-31 20:57", SlotPresenter.FormatSavedAt("2026-10-31T11:57:00Z", Kst));
            Assert.AreEqual("-", SlotPresenter.FormatSavedAt("not a date", Kst));
            Assert.AreEqual("-", SlotPresenter.FormatSavedAt(null, Kst));
        }

        [Test]
        public void Ago_PicksUnit()
        {
            Assert.AreEqual("방금 전", SlotPresenter.FormatAgo("2026-10-31T11:59:30Z", Now));
            Assert.AreEqual("3분 전", SlotPresenter.FormatAgo("2026-10-31T11:57:00Z", Now));
            Assert.AreEqual("5시간 전", SlotPresenter.FormatAgo("2026-10-31T07:00:00Z", Now));
            Assert.AreEqual("2일 전", SlotPresenter.FormatAgo("2026-10-29T11:00:00Z", Now));
        }

        [Test]
        public void Build_EmptyOkBackupCorrupt()
        {
            var empty = SlotPresenter.Build(new SlotSummary { slot = 1 }, Name, Now, Kst);
            Assert.AreEqual(SlotState.Empty, empty.state); Assert.IsFalse(empty.canLoad); Assert.AreEqual("저장 1", empty.title);

            var ok = SlotPresenter.Build(Ok(0), Name, Now, Kst);
            Assert.AreEqual(SlotState.Ok, ok.state); Assert.IsTrue(ok.canLoad); Assert.IsTrue(ok.isAuto); Assert.IsFalse(ok.canSave);
            Assert.AreEqual("자동 저장", ok.title); Assert.AreEqual("불시착 해안", ok.islandName);
            Assert.AreEqual("2시간 05분", ok.playTime); Assert.AreEqual("3분 전", ok.savedAgo);

            var bak = SlotPresenter.Build(Ok(2, backup: true), Name, Now, Kst);
            Assert.AreEqual(SlotState.RecoveredFromBackup, bak.state); Assert.IsTrue(bak.canLoad);

            var bad = SlotPresenter.Build(new SlotSummary { slot = 3, exists = true, valid = false }, Name, Now, Kst);
            Assert.AreEqual(SlotState.Corrupt, bad.state); Assert.IsFalse(bad.canLoad); Assert.AreEqual("-", bad.islandName);

            var done = SlotPresenter.Build(Ok(1, complete: true), Name, Now, Kst);
            Assert.IsTrue(done.gameComplete); Assert.AreEqual("게임 클리어", done.statusText);
        }

        [Test]
        public void Planner_Load_RequiresConfirm_AndBlocksUnusable()
        {
            var ok = SlotPresenter.Build(Ok(1), Name, Now, Kst);
            Assert.AreEqual(SlotActionKind.ConfirmLoad, SlotActionPlanner.Decide(SlotMode.Load, ok, false, true).kind);
            Assert.AreEqual(SlotActionKind.LoadNow, SlotActionPlanner.Decide(SlotMode.Load, ok, true, true).kind);
            Assert.AreEqual(SlotActionKind.LoadNow, SlotActionPlanner.Decide(SlotMode.Load, ok, false, false).kind);

            var empty = SlotPresenter.Build(new SlotSummary { slot = 1 }, Name, Now, Kst);
            Assert.AreEqual(SlotActionKind.Blocked, SlotActionPlanner.Decide(SlotMode.Load, empty, true, true).kind);
            var bad = SlotPresenter.Build(new SlotSummary { slot = 3, exists = true, valid = false }, Name, Now, Kst);
            Assert.AreEqual(SlotActionKind.Blocked, SlotActionPlanner.Decide(SlotMode.Load, bad, true, true).kind);
        }

        [Test]
        public void Planner_Save_AutoBlocked_EmptyDirect_OccupiedNeedsSecondPress()
        {
            var auto = SlotPresenter.Build(Ok(0), Name, Now, Kst);
            Assert.AreEqual(SlotActionKind.Blocked, SlotActionPlanner.Decide(SlotMode.Save, auto, true, true).kind);

            var empty = SlotPresenter.Build(new SlotSummary { slot = 2 }, Name, Now, Kst);
            Assert.AreEqual(SlotActionKind.SaveNow, SlotActionPlanner.Decide(SlotMode.Save, empty, false, true).kind);

            var used = SlotPresenter.Build(Ok(1), Name, Now, Kst);
            Assert.AreEqual(SlotActionKind.ConfirmOverwrite, SlotActionPlanner.Decide(SlotMode.Save, used, false, true).kind);
            Assert.AreEqual(SlotActionKind.SaveNow, SlotActionPlanner.Decide(SlotMode.Save, used, true, true).kind);

            var bad = SlotPresenter.Build(new SlotSummary { slot = 3, exists = true, valid = false }, Name, Now, Kst);
            Assert.AreEqual(SlotActionKind.ConfirmOverwrite, SlotActionPlanner.Decide(SlotMode.Save, bad, false, true).kind);
        }
    }

    public class TravelConditionTests
    {
        private static readonly System.Func<ConceptTag, MasterySample> None = _ => new MasterySample(0, 0);
        private static readonly System.Func<ConceptTag, MasterySample> Good = _ => new MasterySample(5, 5);
        private static GameProgress New() => new GameProgress(SaveData.NewGame(IslandCatalog.Default()), IslandCatalog.Default());

        private static void Run(GameProgress p, string island, MachineKind kind, int tier) =>
            p.Data.GetOrAddIsland(island).UpsertMachine(new MachineSave { id = $"{island}/{kind}", kind = kind, tier = tier, running = true });

        private static void ClearIsland1(GameProgress p)
        {
            Run(p, "island.landing", MachineKind.WaterPurifier, 1);
            Run(p, "island.landing", MachineKind.PowerGenerator, 1);
            Run(p, "island.landing", MachineKind.AutoGarden, 1);
            p.RefreshClear(out _);
        }

        [Test]
        public void AllThreeFail_ReportsThreeFailedConditions_WithKinds()
        {
            var p = New();
            Run(p, "island.landing", MachineKind.WaterPurifier, 1);          // 일부만 복구 -> 다음 섬은 잠겨 있음
            p.Data.GetIsland("island.foundry").unlocked = true;                // 구조적 실패 대신 조건 평가를 보기 위해 항로를 열어 둠
            var c = p.CheckTravel("island.foundry", None, 0, 0);
            Assert.IsFalse(c.ok);
            Assert.AreEqual(3, c.conditions.Count);
            CollectionAssert.AreEqual(
                new[] { TravelConditionKind.ClearIsland, TravelConditionKind.KnowledgeGate, TravelConditionKind.Supplies },
                c.conditions.Select(x => x.kind).ToArray());
            Assert.AreEqual(3, c.FailedConditions.Count());
            StringAssert.Contains("3가지", c.reason);
        }

        [Test]
        public void OnlySuppliesMissing_ReportsSingleFailure_WithItsMessage()
        {
            var p = New(); ClearIsland1(p);
            var c = p.CheckTravel("island.foundry", Good, 1, 1);
            Assert.IsFalse(c.ok);
            Assert.AreEqual(1, c.FailedConditions.Count());
            Assert.AreEqual(TravelConditionKind.Supplies, c.FailedConditions.First().kind);
            StringAssert.Contains("보급", c.reason);
            Assert.IsTrue(c.conditions[0].passed);
            Assert.IsTrue(c.conditions[1].passed);
        }

        [Test]
        public void OnlyGateMissing_ReportsGate()
        {
            var p = New(); ClearIsland1(p);
            var c = p.CheckTravel("island.foundry", None, 9, 9);
            Assert.AreEqual(1, c.FailedConditions.Count());
            Assert.AreEqual(TravelConditionKind.KnowledgeGate, c.FailedConditions.First().kind);
            Assert.IsTrue(c.details.Count > 0);
        }

        [Test]
        public void AllPass_IsOk_AndCarriesCosts()
        {
            var p = New(); ClearIsland1(p);
            var c = p.CheckTravel("island.foundry", Good, 9, 9);
            Assert.IsTrue(c.ok);
            Assert.AreEqual(0, c.FailedConditions.Count());
            Assert.AreEqual(3, c.foodCost); Assert.AreEqual(3, c.waterCost);
        }

        [Test]
        public void StructuralFailureAndBacktracking_HaveNoConditionList()
        {
            var p = New();
            Assert.AreEqual(0, p.CheckTravel("island.core", Good, 99, 99).conditions.Count);   // 건너뛰기
            Assert.AreEqual(0, p.CheckTravel("island.landing", Good, 99, 99).conditions.Count); // 같은 섬
        }

        [Test]
        public void IslandWithoutGateOrCost_MarksThemNotApplicable()
        {
            var catalog = new List<IslandDef>
            {
                new IslandDef { id = "a", displayName = "A", order = 1 },
                new IslandDef { id = "b", displayName = "B", order = 2 },
            };
            var p = new GameProgress(SaveData.NewGame(catalog), catalog);
            p.RefreshClear(out _);                       // 요구 설비가 없는 섬 -> 즉시 클리어, 다음 섬 열림
            var c = p.CheckTravel("b", None, 0, 0);
            Assert.AreEqual(3, c.conditions.Count);
            Assert.IsFalse(c.conditions[1].applicable);
            Assert.IsFalse(c.conditions[2].applicable);
            Assert.AreEqual(0, c.FailedConditions.Count());
            Assert.IsTrue(c.ok);
        }
    }

    public class TravelPresenterTests
    {
        private static readonly System.Func<ConceptTag, MasterySample> Good = _ => new MasterySample(5, 5);
        private static GameProgress New() => new GameProgress(SaveData.NewGame(IslandCatalog.Default()), IslandCatalog.Default());

        [Test]
        public void Nodes_StatesFollowCurrentIsland()
        {
            var p = New();
            var nodes = TravelPresenter.BuildNodes(p, Good, 0, 0);
            Assert.AreEqual(IslandNodeState.Current, nodes[0].state);
            Assert.AreEqual(IslandNodeState.Next, nodes[1].state);
            Assert.AreEqual(IslandNodeState.Locked, nodes[2].state);
            Assert.IsFalse(nodes[1].canTravel);
            Assert.AreEqual("조건 부족", nodes[1].caption);
        }

        [Test]
        public void Nodes_AfterTravel_FirstIslandIsVisitedAndFree()
        {
            var p = New();
            p.Data.GetOrAddIsland("island.foundry").unlocked = true;
            p.ApplyTravel("island.foundry");
            var nodes = TravelPresenter.BuildNodes(p, Good, 0, 0);
            Assert.AreEqual(IslandNodeState.Visited, nodes[0].state);
            Assert.IsTrue(nodes[0].canTravel);
            Assert.AreEqual("돌아가기 (무료)", nodes[0].caption);
            Assert.AreEqual(IslandNodeState.Current, nodes[1].state);
        }

        [Test]
        public void Lines_ListEveryFailedCondition_WithMarkers()
        {
            var p = New();
            p.Data.GetOrAddIsland("island.foundry").unlocked = true;
            var c = p.CheckTravel("island.foundry", _ => new MasterySample(0, 0), 0, 0);
            var lines = TravelPresenter.BuildLines(c);
            Assert.AreEqual(TravelLineKind.Header, lines[0].kind);
            StringAssert.Contains("3가지", lines[0].text);
            Assert.AreEqual(3, lines.Count(l => l.kind == TravelLineKind.Fail));
            Assert.IsTrue(lines.Any(l => l.text.StartsWith("[미충족] 지식 관문")));
            Assert.IsTrue(lines.Any(l => l.text.StartsWith("[미충족] 항해 보급")));
            Assert.IsTrue(lines.Any(l => l.kind == TravelLineKind.Detail && l.text.Contains("식량")));
        }

        [Test]
        public void Lines_WhenOk_ShowReadyAndCost()
        {
            var p = New();
            p.Data.GetOrAddIsland("island.landing");
            foreach (var k in new[] { MachineKind.WaterPurifier, MachineKind.PowerGenerator, MachineKind.AutoGarden })
                p.Data.GetOrAddIsland("island.landing").UpsertMachine(new MachineSave { id = "m" + k, kind = k, tier = 1, running = true });
            p.RefreshClear(out _);
            var lines = TravelPresenter.BuildLines(p.CheckTravel("island.foundry", Good, 9, 9));
            Assert.AreEqual("이동 준비 완료", lines[0].text);
            Assert.AreEqual(0, lines.Count(l => l.kind == TravelLineKind.Fail));
            Assert.IsTrue(lines.Last().text.Contains("식량 3개"));
        }

        [Test]
        public void Lines_StructuralFailure_IsSingleFailLine()
        {
            var lines = TravelPresenter.BuildLines(New().CheckTravel("island.core", Good, 9, 9));
            Assert.AreEqual(1, lines.Count);
            Assert.AreEqual(TravelLineKind.Fail, lines[0].kind);
        }
    }

    public class IslandLayoutTests
    {
        private static readonly List<IslandDef> Catalog = IslandCatalog.Default();

        [Test]
        public void EveryCatalogIsland_HasExactlyOneLayout()
        {
            foreach (var def in Catalog)
                Assert.AreEqual(1, IslandLayouts.All().Count(l => l.islandId == def.id), def.id);
            Assert.AreEqual(Catalog.Count, IslandLayouts.All().Count);
        }

        [Test]
        public void Layouts_PassAllRules()
        {
            foreach (var l in IslandLayouts.All())
                Assert.IsEmpty(IslandLayouts.Validate(l, Catalog), $"{l.sceneName}: {string.Join(" / ", IslandLayouts.Validate(l, Catalog))}");
        }

        [Test]
        public void Layouts_PlaceEveryClearRequirementMachine()
        {
            foreach (var def in Catalog)
            {
                var l = IslandLayouts.For(def.id);
                foreach (var req in def.clearRequirements)
                    Assert.GreaterOrEqual(l.machines.Count(m => m.kind == req.kind), req.count, $"{def.id} {req.kind}");
            }
        }

        [Test]
        public void MachineIds_AreUniquePerIsland_AndFollowTheNamingRule()
        {
            foreach (var l in IslandLayouts.All())
            {
                var ids = IslandLayouts.MachineIds(l);
                Assert.AreEqual(ids.Count, ids.Distinct().Count(), l.sceneName);
                foreach (var id in ids) StringAssert.StartsWith(l.shortName + ".", id);
            }
            Assert.AreEqual("landing.WaterPurifier.01", IslandLayouts.MachineId("landing", MachineKind.WaterPurifier, 1));
        }

        [Test]
        public void Validate_CatchesBrokenLayouts()
        {
            var l = IslandLayouts.For("island.landing");
            l.machines.RemoveAt(0);
            Assert.IsNotEmpty(IslandLayouts.Validate(l, Catalog));          // 클리어 조건 기계 누락

            l = IslandLayouts.For("island.landing");
            l.machines[1].x = l.machines[0].x + 3; l.machines[1].z = l.machines[0].z;
            Assert.IsTrue(IslandLayouts.Validate(l, Catalog).Any(i => i.Contains("간격")));

            l = IslandLayouts.For("island.foundry");
            l.zones.RemoveAll(z => z.kind == LevelMarkerKind.MetalScrap);
            Assert.IsTrue(IslandLayouts.Validate(l, Catalog).Any(i => i.Contains("금속")));
        }

        [Test]
        public void UpgradeNeed_MatchesHandCalculation()
        {
            // 제련 섬: 제련기 T2, 크레인 T2, 발전기 T3, 재배기 T3 -> 나무 70, 돌 42, 금속 4
            var need = IslandLayouts.UpgradeNeed(Catalog.First(d => d.id == "island.foundry"));
            Assert.AreEqual(70, need.wood); Assert.AreEqual(42, need.stone); Assert.AreEqual(4, need.metalPanel);
            Assert.AreEqual(0, IslandLayouts.UpgradeNeed(Catalog.First(d => d.id == "island.landing")).wood);
        }
    }

    // ───────── 피드백 코어 ─────────

    public class FeedbackCoreTests
    {
        private static float Noise(float ch, float t) => 1f;   // 항상 최댓값 (진폭 검증용: 노이즈 1 -> +1)

        [Test]
        public void ShakeTable_MatchesPolishSpec()
        {
            var l = ShakeTable.Of(ShakeGrade.L);
            Assert.AreEqual(0.18f, l.posAmp, 1e-5f); Assert.AreEqual(2.2f, l.rotAmp, 1e-5f); Assert.AreEqual(0.5f, l.duration, 1e-5f);
            Assert.AreEqual(0f, ShakeTable.Of(ShakeGrade.None).duration);
        }

        [Test]
        public void ShakeState_DecaysToTwoPercentAtDuration_ThenStops()
        {
            var st = new ShakeState();
            st.Add(ShakeGrade.M);
            st.Step(0.0001f, Noise);
            var (p0, _) = st.CurrentAmplitude();
            Assert.AreEqual(0.08f, p0, 0.002f);

            st.Step(0.2995f, Noise);                       // 합계 0.2996초: 끝나기 직전
            var (p1, _) = st.CurrentAmplitude();
            Assert.AreEqual(0.08f * ShakeState.EndFraction, p1, 0.0015f);

            st.Step(0.1f, Noise);
            Assert.AreEqual(0, st.ActiveCount);
            Assert.IsTrue(st.Step(0.016f, Noise).IsZero);
        }

        [Test]
        public void ShakeState_Overlap_UsesMaxNotSum()
        {
            var st = new ShakeState();
            st.Add(ShakeGrade.M); st.Add(ShakeGrade.M); st.Add(ShakeGrade.S);
            var (p, r) = st.CurrentAmplitude();
            Assert.AreEqual(0.08f, p, 1e-5f);
            Assert.AreEqual(1.0f, r, 1e-5f);
        }

        [Test]
        public void ShakeState_RollIsDominantRotation_AndIntensityScales()
        {
            var st = new ShakeState();
            st.Add(ShakeGrade.L);
            var s = st.Step(0.0001f, Noise, 1f);
            Assert.Greater(System.Math.Abs(s.rz), System.Math.Abs(s.rx));
            Assert.Greater(System.Math.Abs(s.rz), System.Math.Abs(s.ry));

            var half = new ShakeState(); half.Add(ShakeGrade.L);
            var h = half.Step(0.0001f, Noise, 0.5f);
            Assert.AreEqual(s.rz * 0.5f, h.rz, 0.01f);
            Assert.IsTrue(new ShakeState().Step(0.1f, Noise).IsZero);
        }

        [Test]
        public void ShakeState_HoldKeepsAmplitudeBeforeDecay()
        {
            var st = new ShakeState();
            st.Add(ShakeGrade.S, 1f, 0.5f);
            st.Step(0.4f, Noise);
            Assert.AreEqual(0.03f, st.CurrentAmplitude().pos, 1e-5f);
            st.Step(0.3f, Noise);                           // hold(0.5) 지나 0.2초 감쇠
            Assert.Less(st.CurrentAmplitude().pos, 0.03f);
        }

        [Test]
        public void PitchLadder_PerfectsClimb_GoodHolds_MissResets_CapsAtFour()
        {
            var l = new PitchLadder();
            Assert.AreEqual(0, l.Semitones);
            l.OnPerfect(); Assert.AreEqual(0, l.Semitones); Assert.AreEqual(1f, l.PitchMultiplier, 1e-5f);
            l.OnPerfect(); Assert.AreEqual(1, l.Semitones);
            l.OnGood();    Assert.AreEqual(1, l.Semitones); Assert.AreEqual(2, l.Combo);
            for (int i = 0; i < 10; i++) l.OnPerfect();
            Assert.AreEqual(4, l.Semitones);
            Assert.AreEqual(1.2599f, l.PitchMultiplier, 0.001f);
            l.OnMiss(); Assert.AreEqual(0, l.Combo); Assert.AreEqual(0, l.Semitones);
        }

        [Test]
        public void HitstopGate_CapsDuration_AndEnforcesCooldown()
        {
            var g = new HitstopGate();
            Assert.AreEqual(0.1f, g.Request(0.5f, 10f), 1e-5f);     // 상한 0.1초
            Assert.AreEqual(0f, g.Request(0.06f, 10.3f));            // 쿨다운 중
            Assert.AreEqual(0.06f, g.Request(0.06f, 11f), 1e-5f);    // 쿨다운 끝
            Assert.AreEqual(0f, g.Request(0f, 20f));
        }

        [Test]
        public void NoRepeatPicker_NeverRepeatsWhenMoreThanOne()
        {
            var p = new NoRepeatPicker();
            var rng = new System.Random(3);
            int last = -1;
            for (int i = 0; i < 200; i++)
            {
                int k = p.Pick(3, () => (float)rng.NextDouble());
                Assert.AreNotEqual(last, k); Assert.That(k, Is.InRange(0, 2)); last = k;
            }
            Assert.AreEqual(-1, p.Pick(0, () => 0.5f));
            Assert.AreEqual(0, new NoRepeatPicker().Pick(1, () => 0.9f));
        }

        [Test]
        public void TimeAlarm_FiresLowOnce_AndDetectsMistakeDrop()
        {
            var a = new TimeAlarm(100f);
            Assert.IsFalse(a.Update(60f, 0.016f).low);
            Assert.IsTrue(a.Update(25f, 0.016f).mistake);            // 35초가 한 번에 깎임
            Assert.IsTrue(a.Update(20f, 0.016f).low == true || a.IsLow);
            Assert.IsFalse(a.Update(19f, 0.016f).low);               // 두 번 울리지 않는다
            var b = new TimeAlarm(40f);
            Assert.IsTrue(b.Update(19.9f, 0.016f).low);              // 20초 이하 절대 기준
        }

        [Test]
        public void StaminaPalette_ColorsAndPulse()
        {
            Assert.AreEqual(StaminaPalette.Good.g, StaminaPalette.Evaluate(1f).g, 1e-5f);
            Assert.AreEqual(StaminaPalette.Warn.r, StaminaPalette.Evaluate(0.38f).r, 1e-5f);
            Assert.AreEqual(StaminaPalette.Danger.r, StaminaPalette.Evaluate(0.1f).r, 1e-5f);
            var mid = StaminaPalette.Evaluate(0.5f);                   // 경계는 두 색의 중간
            Assert.AreEqual((StaminaPalette.Good.r + StaminaPalette.Warn.r) / 2f, mid.r, 1e-4f);
            Assert.AreEqual(0f, StaminaPalette.Pulse(0.6f, 0.25f));
            Assert.AreEqual(1f, StaminaPalette.Pulse(0.2f, 0.25f), 1e-4f);
            Assert.AreEqual(0f, StaminaPalette.Pulse(0.2f, 0.75f), 1e-4f);
        }

        [Test]
        public void Ease_EndpointsAndPunchReturnsToZero()
        {
            foreach (var f in new System.Func<float, float>[] { Ease.OutQuad, Ease.OutCubic, Ease.InOutQuad, t => Ease.OutBack(t) })
            { Assert.AreEqual(0f, f(0f), 1e-5f); Assert.AreEqual(1f, f(1f), 1e-5f); }
            Assert.Greater(Ease.OutBack(0.7f), 1f);                    // 오버슈트
            Assert.AreEqual(0f, Ease.Punch(0f, 0.5f), 1e-5f);
            Assert.AreEqual(0f, Ease.Punch(1f, 0.5f), 1e-5f);
            Assert.AreEqual(0f, Ease.Punch(-3f, 0.5f), 1e-5f);         // 범위 밖은 0~1로 고정
        }

        [Test]
        public void Defaults_CoverEveryHydraulicAndPneumaticCue_AndFixedKeys()
        {
            foreach (var n in System.Enum.GetNames(typeof(HydraulicCue)))
                Assert.IsTrue(FeedbackKeys.TryGetDefault(FeedbackKeys.Hydraulic(n), out _), "유압 큐 누락: " + n);
            foreach (var n in System.Enum.GetNames(typeof(PneumaticCue)))
                Assert.IsTrue(FeedbackKeys.TryGetDefault(FeedbackKeys.Pneumatic(n), out _), "공압 큐 누락: " + n);
            foreach (var k in new[] { FeedbackKeys.LadderPass, FeedbackKeys.LadderFail, FeedbackKeys.LadderInterlock, FeedbackKeys.LadderUnstable,
                     FeedbackKeys.RhythmPerfect, FeedbackKeys.RhythmGood, FeedbackKeys.RhythmMiss, FeedbackKeys.PuzzleSuccess,
                     FeedbackKeys.PuzzleFail, FeedbackKeys.TimeLow, FeedbackKeys.TimeMistake })
                Assert.IsTrue(FeedbackKeys.TryGetDefault(k, out _), "키 누락: " + k);
        }

        [Test]
        public void Defaults_FollowPolishRules()
        {
            foreach (var d in FeedbackKeys.Defaults)
            {
                Assert.LessOrEqual(d.hitstop, 0.06f + 1e-5f, d.key + ": 히트스톱은 0.06초 이하");
                if (d.hitstop > 0f) Assert.AreEqual(ShakeGrade.L, d.shake, d.key + ": 히트스톱은 L 등급에만");
                Assert.LessOrEqual(d.pitchMin, d.pitchMax, d.key);
            }
            FeedbackKeys.TryGetDefault(FeedbackKeys.Hydraulic("HoseBurst"), out var burst);
            Assert.AreEqual(ShakeGrade.L, burst.shake);
            FeedbackKeys.TryGetDefault(FeedbackKeys.Hydraulic("Strain"), out var strain);
            Assert.AreEqual(0.5f, strain.shakeHold, 1e-5f);
        }
    }

    // ───────── 오디오 / 설정 ─────────

    public class AudioCoreTests
    {
        [Test]
        public void PercentToGain_IsSquaredAndClamped()
        {
            Assert.AreEqual(0f, AudioMath.PercentToGain(0));
            Assert.AreEqual(0.25f, AudioMath.PercentToGain(50), 1e-5f);
            Assert.AreEqual(1f, AudioMath.PercentToGain(100));
            Assert.AreEqual(1f, AudioMath.PercentToGain(250));
            Assert.AreEqual(0f, AudioMath.PercentToGain(-5));
        }

        [Test]
        public void MachineLoopPitch_RisesWithTier_AndClamps()
        {
            Assert.AreEqual(1f, AudioMath.MachineLoopPitch(1), 1e-5f);
            Assert.AreEqual(1.16f, AudioMath.MachineLoopPitch(5), 1e-5f);
            Assert.AreEqual(1.16f, AudioMath.MachineLoopPitch(9), 1e-5f);
            Assert.AreEqual(1f, AudioMath.MachineLoopPitch(0), 1e-5f);
        }

        [Test]
        public void Crossfade_IsEqualPower()
        {
            var (o0, i0) = AudioMath.EqualPowerCrossfade(0f);
            Assert.AreEqual(1f, o0, 1e-5f); Assert.AreEqual(0f, i0, 1e-5f);
            var (o1, i1) = AudioMath.EqualPowerCrossfade(1f);
            Assert.AreEqual(0f, o1, 1e-5f); Assert.AreEqual(1f, i1, 1e-5f);
            for (float t = 0f; t <= 1f; t += 0.1f)
            {
                var (o, i) = AudioMath.EqualPowerCrossfade(t);
                Assert.AreEqual(1f, o * o + i * i, 1e-4f);
            }
        }

        [Test]
        public void CrowdGain_ScalesWithInverseSqrt()
        {
            Assert.AreEqual(1f, AudioMath.CrowdGain(0)); Assert.AreEqual(1f, AudioMath.CrowdGain(1));
            Assert.AreEqual(0.5f, AudioMath.CrowdGain(4), 1e-5f);
        }

        [Test]
        public void Ducking_GoesDownFast_ComesBackSlow_DeepestWins()
        {
            var d = new DuckingMixer();
            d.Request("puzzle", 0.35f);
            for (int i = 0; i < 100; i++) d.Step(0.01f);          // 1초
            Assert.AreEqual(0.35f, d.Gain, 0.02f);

            d.Request("dialogue", 0.6f);                           // 덜 깊은 요청은 무시된다
            Assert.AreEqual(0.35f, d.Target, 1e-5f);

            d.Release("puzzle");
            Assert.AreEqual(0.6f, d.Target, 1e-5f);
            d.Release("dialogue");
            float g0 = d.Gain;
            for (int i = 0; i < 30; i++) d.Step(0.01f);            // 0.3초 후에는 아직 다 못 돌아옴
            Assert.Less(d.Gain, 0.9f); Assert.Greater(d.Gain, g0);
            for (int i = 0; i < 1000; i++) d.Step(0.01f);
            Assert.AreEqual(1f, d.Gain, 1e-4f);
        }

        [Test]
        public void Ducking_IsFrameRateIndependent()
        {
            var a = new DuckingMixer(); var b = new DuckingMixer();
            a.Request("p", 0.2f); b.Request("p", 0.2f);
            for (int i = 0; i < 60; i++) a.Step(1f / 60f);
            for (int i = 0; i < 20; i++) b.Step(1f / 20f);
            Assert.AreEqual(a.Gain, b.Gain, 0.001f);
        }

        [Test]
        public void VoiceBudget_StealsOldestWhenFull()
        {
            var v = new VoiceBudget(2);
            Assert.AreEqual(-1, v.Acquire(10));
            Assert.AreEqual(-1, v.Acquire(11));
            Assert.AreEqual(10, v.Acquire(12));
            Assert.AreEqual(11, v.Acquire(13));
        }
    }

    public class SettingsTests
    {
        [Test]
        public void Default_Values()
        {
            var d = SettingsData.Default();
            Assert.AreEqual(100, d.masterPercent); Assert.AreEqual(100, d.shakePercent); Assert.IsTrue(d.hitstopEnabled);
            Assert.AreEqual(1f, d.MasterGain); Assert.AreEqual(1f, d.Shake01);
        }

        [Test]
        public void Sanitize_ClampsOutOfRange()
        {
            var d = new SettingsData { masterPercent = 400, sfxPercent = -20, ambientPercent = 55, shakePercent = 101, version = 99 }.Sanitize();
            Assert.AreEqual(100, d.masterPercent); Assert.AreEqual(0, d.sfxPercent);
            Assert.AreEqual(55, d.ambientPercent); Assert.AreEqual(100, d.shakePercent);
            Assert.AreEqual(SettingsData.CurrentVersion, d.version);
        }

        [Test]
        public void Codec_RoundTrips()
        {
            var d = new SettingsData { masterPercent = 30, sfxPercent = 80, ambientPercent = 10, shakePercent = 0, hitstopEnabled = false };
            var back = SettingsCodec.Parse(SettingsCodec.ToJson(d), out bool usedDefaults);
            Assert.IsFalse(usedDefaults);
            Assert.AreEqual(d, back);
            Assert.AreEqual(0f, back.Shake01);
        }

        [Test]
        public void Codec_CorruptOrEmpty_FallsBackToDefaults()
        {
            foreach (var bad in new[] { null, "", "   ", "{not json" })
            {
                var d = SettingsCodec.Parse(bad, out bool usedDefaults);
                Assert.IsTrue(usedDefaults, "입력: " + bad);
                Assert.AreEqual(SettingsData.Default(), d);
            }
        }

        [Test]
        public void Codec_MissingFields_KeepDefaults_AndClampsManualEdits()
        {
            var d = SettingsCodec.Parse("{\"masterPercent\":50,\"shakePercent\":900}", out _);
            Assert.AreEqual(50, d.masterPercent);
            Assert.AreEqual(100, d.shakePercent);
            Assert.AreEqual(70, d.ambientPercent);     // 없는 항목은 기본값
            Assert.IsTrue(d.hitstopEnabled);
        }

        [Test]
        public void Current_WithoutManager_ReturnsDefaults()
        {
            Assert.IsNotNull(SettingsManager.Current);
            Assert.AreEqual(100, SettingsManager.Current.masterPercent);
        }
    }

    // ───────── 목표 추적 ─────────

    public class ObjectiveTests
    {
        private static WorldSnapshot Snap(string island = "island.landing") => new WorldSnapshot { currentIslandId = island };
        private static MachineSnap Run(MachineKind k, int tier = 1) => new MachineSnap(k, tier, true);

        [Test]
        public void Defaults_HaveUniqueIds_AndEveryHintTitle()
        {
            var all = ObjectiveDefaults.All();
            Assert.AreEqual(all.Count, all.Select(d => d.id).Distinct().Count());
            foreach (var d in all) { Assert.IsFalse(string.IsNullOrWhiteSpace(d.title), d.id); Assert.IsNotNull(d.condition, d.id); }
        }

        [Test]
        public void Defaults_CoverEveryLandingClearRequirement()
        {
            var landing = IslandCatalog.Default().First(i => i.id == "island.landing");
            var defs = ObjectiveDefaults.All().Where(d => d.islandId == "island.landing" && d.condition.kind == ConditionKind.MachineRunning).ToList();
            foreach (var req in landing.clearRequirements)
                Assert.IsTrue(defs.Any(d => d.condition.machine == req.kind && d.condition.minTier >= req.minTier),
                              "클리어 조건에 대응하는 목표 없음: " + req.kind);
        }

        [Test]
        public void Active_StartsAtFirst_AndAdvancesWhenMet()
        {
            var t = new ObjectiveTracker(ObjectiveDefaults.All());
            Assert.AreEqual("landing.wood", t.Active("island.landing").id);

            var w = Snap(); w.wood = 2;
            Assert.AreEqual(0, t.Evaluate(w).Count);
            Assert.AreEqual("2/3", ObjectiveEvaluator.ProgressText(t.Active("island.landing").condition, w));

            w.wood = 3;
            var done = t.Evaluate(w);
            Assert.AreEqual(1, done.Count); Assert.AreEqual("landing.wood", done[0].id);
            Assert.AreEqual("landing.fire", t.Active("island.landing").id);
        }

        [Test]
        public void Completion_Latches_EvenIfResourcesAreSpent()
        {
            var t = new ObjectiveTracker(ObjectiveDefaults.All());
            var w = Snap(); w.wood = 5; t.Evaluate(w);
            w.wood = 0;
            Assert.AreEqual(0, t.Evaluate(w).Count);
            Assert.IsTrue(t.IsComplete("landing.wood"));
        }

        [Test]
        public void LaterObjective_ImpliesEarlierOnes_InOrder()
        {
            var t = new ObjectiveTracker(ObjectiveDefaults.All());
            var w = Snap(); w.machines.Add(Run(MachineKind.WaterPurifier));        // 나무·모닥불 없이 정수기부터 고침
            var done = t.Evaluate(w);
            CollectionAssert.AreEqual(new[] { "landing.wood", "landing.fire", "landing.water" }, done.Select(d => d.id).ToArray());
            Assert.AreEqual("landing.power", t.Active("island.landing").id);
        }

        [Test]
        public void MachineObjective_RequiresRunningAndTier()
        {
            var c = ObjectiveCondition.Running(MachineKind.PowerGenerator, 2);
            var w = Snap();
            w.machines.Add(new MachineSnap(MachineKind.PowerGenerator, 1, true));
            Assert.IsFalse(ObjectiveEvaluator.IsMet(c, w));
            w.machines.Clear(); w.machines.Add(new MachineSnap(MachineKind.PowerGenerator, 3, false));
            Assert.IsFalse(ObjectiveEvaluator.IsMet(c, w));
            w.machines.Clear(); w.machines.Add(new MachineSnap(MachineKind.PowerGenerator, 2, true));
            Assert.IsTrue(ObjectiveEvaluator.IsMet(c, w));
        }

        [Test]
        public void IslandScope_ObjectivesOfOtherIslandsAreNotEvaluated()
        {
            var t = new ObjectiveTracker(ObjectiveDefaults.All());
            var w = Snap("island.landing");                                         // 불시착 해안에서
            w.machines.Add(Run(MachineKind.Smelter));                               // 제련기가 돌아도 (있을 수 없는 상황)
            var done = t.Evaluate(w);
            Assert.IsFalse(done.Any(d => d.id == "foundry.smelter"));
            Assert.IsFalse(t.IsComplete("landing.wood"));
            Assert.AreEqual("landing.sail", t.Active("island.nowhere").id);         // 어느 목록에도 없는 섬: 섬 제한 없는 목표만 해당
        }

        [Test]
        public void ArrivingAtFoundry_CompletesSailGoal_AndShowsFoundryGoals()
        {
            var t = new ObjectiveTracker(ObjectiveDefaults.All());
            var w = Snap("island.foundry");
            var done = t.Evaluate(w);
            Assert.IsTrue(done.Any(d => d.id == "landing.sail"));
            Assert.AreEqual("foundry.smelter", t.Active("island.foundry").id);
        }

        [Test]
        public void FireAndFlagConditions()
        {
            var w = Snap();
            Assert.IsFalse(ObjectiveEvaluator.IsMet(ObjectiveCondition.Fire(), w));
            w.fireLit = true; Assert.IsTrue(ObjectiveEvaluator.IsMet(ObjectiveCondition.Fire(), w));
            Assert.IsFalse(ObjectiveEvaluator.IsMet(ObjectiveCondition.FlagSet("note"), w));
            w.flags.Add("note"); Assert.IsTrue(ObjectiveEvaluator.IsMet(ObjectiveCondition.FlagSet("note"), w));
            w.clearedIslands.Add("island.landing");
            Assert.IsTrue(ObjectiveEvaluator.IsMet(ObjectiveCondition.Cleared("island.landing"), w));
            Assert.IsFalse(ObjectiveEvaluator.IsMet(null, w));
        }

        [Test]
        public void View_ShowsLastDone_Active_AndUpcoming()
        {
            var t = new ObjectiveTracker(ObjectiveDefaults.All());
            var w = Snap(); w.wood = 3; t.Evaluate(w);
            var lines = t.View(w, "island.landing", 4);
            Assert.AreEqual(4, lines.Count);
            Assert.AreEqual(LineState.Done, lines[0].state); Assert.AreEqual("landing.wood", lines[0].def.id);
            Assert.AreEqual(LineState.Active, lines[1].state); Assert.AreEqual("landing.fire", lines[1].def.id);
            Assert.AreEqual(LineState.Upcoming, lines[2].state);

            var first = new ObjectiveTracker(ObjectiveDefaults.All()).View(Snap(), "island.landing", 3);
            Assert.AreEqual(LineState.Active, first[0].state);            // 처음에는 달성한 줄이 없다
            Assert.AreEqual(3, first.Count);
            Assert.AreEqual("0/3", first[0].progress);
        }

        [Test]
        public void View_WhenAllDone_ShowsOnlyLastAsDone()
        {
            var t = new ObjectiveTracker(ObjectiveDefaults.All());
            var w = Snap("island.foundry"); w.machines.Add(Run(MachineKind.CraneLift));
            t.Evaluate(w);
            Assert.IsTrue(t.AllDone);
            var lines = t.View(w, "island.foundry");
            Assert.AreEqual(1, lines.Count); Assert.AreEqual(LineState.Done, lines[0].state);
            Assert.IsNull(t.Active("island.foundry"));
        }

        [Test]
        public void ExportImport_RoundTrips_AndDropsUnknownIds()
        {
            var t = new ObjectiveTracker(ObjectiveDefaults.All());
            var w = Snap(); w.wood = 3; w.fireLit = true; t.Evaluate(w);
            var saved = t.Export();
            CollectionAssert.AreEqual(new[] { "landing.wood", "landing.fire" }, saved);

            var t2 = new ObjectiveTracker(ObjectiveDefaults.All());
            t2.Import(new List<string>(saved) { "removed.in.new.version", null });
            Assert.IsTrue(t2.IsComplete("landing.fire"));
            Assert.AreEqual(2, t2.CompletedCount);
            t2.Import(null); Assert.AreEqual(0, t2.CompletedCount);
            t2.Reset(); Assert.AreEqual(0, t2.CompletedCount);
        }

        [Test]
        public void SaveData_CompletedObjectives_SurviveJson_AndOldFilesHaveEmptyList()
        {
            var data = SaveData.NewGame(IslandCatalog.Default());
            data.completedObjectives = new List<string> { "landing.wood", "landing.fire" };
            var back = JsonUtility.FromJson<SaveData>(JsonUtility.ToJson(data));
            CollectionAssert.AreEqual(data.completedObjectives, back.completedObjectives);

            var old = JsonUtility.FromJson<SaveData>("{\"version\":1}");
            Assert.IsNotNull(old.completedObjectives); Assert.AreEqual(0, old.completedObjectives.Count);
        }
    }

    // ───────── 메인 메뉴 ─────────

    public class MainMenuTests
    {
        private static SlotSummary Slot(int n, bool exists, bool valid, string at, bool backup = false, float play = 0f, string island = "island.landing") =>
            new SlotSummary { slot = n, exists = exists, valid = valid, usedBackup = backup, savedAtUtc = at, playTimeSeconds = play, islandId = island };

        [Test]
        public void ConfirmGate_NeedsSecondPressWithinWindow()
        {
            var g = new ConfirmGate { WindowSeconds = 3f };
            Assert.IsFalse(g.Press("new", 10f));
            Assert.IsTrue(g.IsArmed("new", 12f));
            Assert.IsTrue(g.Press("new", 12f));          // 두 번째 -> 실행
            Assert.IsFalse(g.Press("new", 12.5f));       // 실행 후에는 다시 처음부터
        }

        [Test]
        public void ConfirmGate_ExpiresAndOtherKeyCancels()
        {
            var g = new ConfirmGate { WindowSeconds = 3f };
            g.Press("new", 0f);
            Assert.IsFalse(g.Press("new", 5f));          // 시간 초과 -> 다시 준비만
            Assert.IsFalse(g.Press("quit", 5.5f));       // 다른 키: 이전 준비 취소
            Assert.IsFalse(g.IsArmed("new", 5.6f));
            g.Cancel(); Assert.IsFalse(g.IsArmed("quit", 5.6f));
        }

        [Test]
        public void Usable_AndHasAnySave()
        {
            Assert.IsFalse(MainMenuModel.HasAnyUsableSave(null));
            Assert.IsFalse(MainMenuModel.HasAnyUsableSave(new[] { Slot(0, false, false, null), Slot(1, true, false, "x") }));   // 손상, 복구 불가
            Assert.IsTrue(MainMenuModel.HasAnyUsableSave(new[] { Slot(1, true, false, "x", backup: true) }));                     // 복구본으로 가능
            Assert.IsTrue(MainMenuModel.HasAnyUsableSave(new[] { Slot(2, true, true, "2026-10-31T10:00:00Z") }));
        }

        [Test]
        public void Latest_PicksNewest_IgnoresUnusable_AndBadTimestamps()
        {
            var slots = new[]
            {
                Slot(0, true, true, "2026-10-31T10:00:00Z"),
                Slot(1, true, true, "2026-11-01T09:00:00Z"),
                Slot(2, true, false, "2026-12-01T09:00:00Z"),      // 손상: 제외
                Slot(3, true, true, "garbage"),
            };
            Assert.AreEqual(1, MainMenuModel.Latest(slots).slot);
            Assert.IsNull(MainMenuModel.Latest(new[] { Slot(0, false, false, null) }));
            Assert.AreEqual(3, MainMenuModel.Latest(new[] { Slot(3, true, true, "garbage") }).slot);   // 하나뿐이면 시각이 이상해도 선택
        }

        [Test]
        public void DescribeLatest_ForNoSave_AndForSave()
        {
            Assert.That(MainMenuModel.DescribeLatest(null, null, System.DateTime.UtcNow), Does.Contain("새 게임"));
            var s = Slot(0, true, true, "2026-11-01T09:00:00Z", play: 750f);
            var text = MainMenuModel.DescribeLatest(s, id => "불시착 해안", new System.DateTime(2026, 11, 1, 9, 3, 0, System.DateTimeKind.Utc));
            Assert.That(text, Does.Contain("불시착 해안"));
            Assert.That(text, Does.Contain("12분"));
            Assert.That(text, Does.Contain("3분 전"));
            Assert.That(MainMenuModel.DescribeLatest(Slot(0, true, false, "2026-11-01T09:00:00Z", backup: true), null, System.DateTime.UtcNow), Does.Contain("복구본"));
        }
    }
}
