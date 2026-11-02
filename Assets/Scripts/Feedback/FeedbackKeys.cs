using System;
using System.Collections.Generic;

namespace AutoSurvival.Feedback
{
    /// <summary>기본값 한 줄 (UnityEngine 의존 없음). FeedbackProfile.SeedDefaults()가 이 표로 에셋을 채운다.</summary>
    public readonly struct DefaultFeedback
    {
        public readonly string key; public readonly ShakeGrade shake; public readonly float hitstop;
        public readonly float pitchMin, pitchMax; public readonly bool ladder; public readonly float shakeHold;
        public DefaultFeedback(string key, ShakeGrade shake, float hitstop, float pitchMin, float pitchMax, bool ladder = false, float shakeHold = 0f)
        { this.key = key; this.shake = shake; this.hitstop = hitstop; this.pitchMin = pitchMin; this.pitchMax = pitchMax; this.ladder = ladder; this.shakeHold = shakeHold; }
    }

    /// <summary>큐 이름 규칙: "퍼즐.큐". 열거형 이름을 그대로 쓰므로 큐가 늘어나도 키 규칙은 변하지 않는다.</summary>
    public static class FeedbackKeys
    {
        public static string Hydraulic(string cueName) => "Hydraulic." + cueName;
        public static string Pneumatic(string cueName) => "Pneumatic." + cueName;

        public const string LadderPass = "Ladder.Pass";
        public const string LadderFail = "Ladder.Fail";
        public const string LadderInterlock = "Ladder.Interlock";
        public const string LadderUnstable = "Ladder.Unstable";
        public const string RhythmPerfect = "Rhythm.Perfect";
        public const string RhythmGood = "Rhythm.Good";
        public const string RhythmMiss = "Rhythm.Miss";
        public const string PuzzleSuccess = "Puzzle.Success";
        public const string PuzzleFail = "Puzzle.Fail";
        public const string TimeLow = "Time.Low";
        public const string TimeMistake = "Time.Mistake";
        public const string ObjectiveComplete = "Objective.Complete";

        private static readonly Dictionary<string, DefaultFeedback> _defaults = Build();
        public static IReadOnlyCollection<DefaultFeedback> Defaults => _defaults.Values;
        public static bool TryGetDefault(string key, out DefaultFeedback d) => _defaults.TryGetValue(key, out d);

        private static Dictionary<string, DefaultFeedback> Build()
        {
            var d = new Dictionary<string, DefaultFeedback>();
            void A(string k, ShakeGrade s, float hit, float p0, float p1, bool ladder = false, float hold = 0f) => d[k] = new DefaultFeedback(k, s, hit, p0, p1, ladder, hold);

            // 유압 (폴리싱 기획서 3장)
            A(Hydraulic("PumpGood"), ShakeGrade.S, 0f, 0.97f, 1.03f, true);
            A(Hydraulic("PumpMiss"), ShakeGrade.None, 0f, 0.9f, 1.0f);
            A(Hydraulic("PumpEmpty"), ShakeGrade.S, 0f, 0.85f, 0.95f);
            A(Hydraulic("ReliefVent"), ShakeGrade.M, 0f, 0.95f, 1.05f);
            A(Hydraulic("HoseBurst"), ShakeGrade.L, 0.06f, 0.9f, 1.05f);
            A(Hydraulic("Strain"), ShakeGrade.S, 0f, 0.9f, 1.0f, false, 0.5f);
            A(Hydraulic("Overheat"), ShakeGrade.M, 0f, 0.9f, 1.0f);
            A(Hydraulic("UnsafeRelief"), ShakeGrade.None, 0f, 1.0f, 1.0f);
            A(Hydraulic("LiftStart"), ShakeGrade.M, 0f, 0.98f, 1.02f);
            A(Hydraulic("LiftComplete"), ShakeGrade.M, 0f, 1.0f, 1.0f);

            // 공압
            A(Pneumatic("HoseConnected"), ShakeGrade.None, 0f, 0.97f, 1.03f);
            A(Pneumatic("HoseRemoved"), ShakeGrade.None, 0f, 0.94f, 1.0f);
            A(Pneumatic("ExhaustToggled"), ShakeGrade.None, 0f, 0.97f, 1.03f);
            A(Pneumatic("TestStart"), ShakeGrade.S, 0f, 1.0f, 1.0f);
            A(Pneumatic("StepOk"), ShakeGrade.None, 0f, 1.0f, 1.0f);
            A(Pneumatic("TestPassed"), ShakeGrade.M, 0f, 1.0f, 1.0f);
            A(Pneumatic("Leak"), ShakeGrade.None, 0f, 0.95f, 1.05f);
            A(Pneumatic("ShortCircuit"), ShakeGrade.S, 0f, 0.95f, 1.05f);
            A(Pneumatic("ExhaustBlocked"), ShakeGrade.S, 0f, 0.9f, 1.0f);
            A(Pneumatic("Reversed"), ShakeGrade.S, 0f, 0.95f, 1.0f);
            A(Pneumatic("NotConnected"), ShakeGrade.None, 0f, 1.0f, 1.0f);
            A(Pneumatic("NoSupply"), ShakeGrade.None, 0f, 0.9f, 0.95f);
            A(Pneumatic("Strain"), ShakeGrade.S, 0f, 0.9f, 1.0f, false, 0.5f);
            A(Pneumatic("HoseBurst"), ShakeGrade.L, 0.06f, 0.9f, 1.05f);

            // 래더, 리듬, 퍼즐 공통, 시간
            A(LadderPass, ShakeGrade.M, 0f, 1.0f, 1.0f);
            A(LadderFail, ShakeGrade.S, 0f, 0.95f, 1.0f);
            A(LadderInterlock, ShakeGrade.M, 0f, 0.9f, 1.0f);
            A(LadderUnstable, ShakeGrade.S, 0f, 0.95f, 1.05f);
            A(RhythmPerfect, ShakeGrade.S, 0f, 1.0f, 1.0f, true);
            A(RhythmGood, ShakeGrade.None, 0f, 1.0f, 1.0f);
            A(RhythmMiss, ShakeGrade.None, 0f, 0.9f, 1.0f);
            A(PuzzleSuccess, ShakeGrade.M, 0f, 1.0f, 1.0f);
            A(PuzzleFail, ShakeGrade.M, 0f, 0.9f, 1.0f);
            A(TimeLow, ShakeGrade.None, 0f, 1.0f, 1.0f);
            A(TimeMistake, ShakeGrade.S, 0f, 0.95f, 1.0f);
            A(ObjectiveComplete, ShakeGrade.None, 0f, 1.0f, 1.0f);
            return d;
        }
    }
}
