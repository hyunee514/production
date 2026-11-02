using System;
using System.Collections.Generic;
using System.Linq;

namespace AutoSurvival.Puzzles
{
    // ───────────────────────────────────────────────────────────────
    // 순수 C# 유압 로직 (파스칼의 원리). 단위: 힘 N, 면적 cm², 압력 bar, 길이 cm.
    //   P[bar] = F[N] / (A[cm²] x 10)       (1 bar = 10 N/cm²)
    //   파스칼: P_입력 = P_출력  ->  F_출력 = F_입력 x (A_출력 / A_입력)
    //   일 보존: 출력 피스톤이 h만큼 오르려면 입력 피스톤은 h x (A_출력/A_입력)만큼 움직여야 한다.
    // ───────────────────────────────────────────────────────────────

    public enum HydraulicOutcome
    {
        Success,
        UnsafeRelief,       // (엄격 모드) 릴리프 설정이 호스 허용압을 넘음 -> 안전 점검 불합격
        ReliefOpens,        // 필요 압력 > 릴리프 설정 -> 릴리프가 열려 기름이 빠짐, 하중 못 듦
        HoseBurst,          // 필요 압력 > 호스 허용압 (릴리프가 보호해 주지 못함) -> 호스 파열
        InsufficientForce,  // 펌프가 낼 수 있는 압력 < 필요 압력 -> 하중이 꿈쩍 않음
        TooSlow             // 힘은 충분하지만 필요한 펌핑 횟수가 한도 초과 -> 펌프 과열
    }

    public class HydraulicLevel
    {
        public string id, title, story;
        public float loadN;                 // 들어 올릴 하중
        public float liftCm;                // 목표 상승 높이
        public float inputForceN;           // 플레이어가 펌프 손잡이에 낼 수 있는 힘
        public float strokeCm;              // 펌프 1회 스트로크 길이 (입력 피스톤 이동 거리)
        public int maxStrokes;              // 허용 펌핑 횟수 (초과하면 과열)
        public float hoseLimitBar;          // 호스·배관 허용 압력
        public float friction = 0.10f;      // 마찰 손실: 실제 필요 힘 = 하중 x (1 + friction)
        public bool strictRelief;           // true면 릴리프 설정이 호스 허용압을 넘는 것 자체가 불합격
        public float[] inputAreasCm2;       // 선택 가능한 입력(펌프) 피스톤 면적
        public float[] outputAreasCm2;      // 선택 가능한 출력(리프트) 피스톤 면적
        public float[] reliefBars;          // 선택 가능한 릴리프 설정 압력
    }

    public readonly struct HydraulicDesign
    {
        public readonly int inputIndex, outputIndex, reliefIndex;
        public HydraulicDesign(int input, int output, int relief) { inputIndex = input; outputIndex = output; reliefIndex = relief; }
    }

    public class HydraulicReport
    {
        public HydraulicOutcome outcome;
        public float areaRatio;         // A_출력 / A_입력 = 힘 배율
        public float pumpPressureBar;   // 플레이어가 펌프로 만들 수 있는 최대 압력
        public float loadPressureBar;   // 하중을 들어 올리는 데 필요한 압력(마찰 포함)
        public float reliefBar;
        public float hoseLimitBar;
        public int strokesNeeded;
        public int maxStrokes;
        public float liftForceN;        // 이 설계로 낼 수 있는 최대 출력 힘 (= min(펌프압, 릴리프)·A_출력·10)
        public bool Succeeded => outcome == HydraulicOutcome.Success;
    }

    public static class HydraulicLogic
    {
        public static float PressureBar(float forceN, float areaCm2) => forceN / (areaCm2 * 10f);

        public static int StrokesNeeded(HydraulicLevel lv, float inputArea, float outputArea) =>
            (int)Math.Ceiling(lv.liftCm * outputArea / (lv.strokeCm * inputArea) - 1e-6);

        public static HydraulicReport Evaluate(HydraulicLevel lv, HydraulicDesign d)
        {
            float a1 = lv.inputAreasCm2[d.inputIndex];
            float a2 = lv.outputAreasCm2[d.outputIndex];
            float relief = lv.reliefBars[d.reliefIndex];

            var r = new HydraulicReport
            {
                areaRatio = a2 / a1,
                pumpPressureBar = PressureBar(lv.inputForceN, a1),
                loadPressureBar = PressureBar(lv.loadN * (1f + lv.friction), a2),
                reliefBar = relief,
                hoseLimitBar = lv.hoseLimitBar,
                strokesNeeded = StrokesNeeded(lv, a1, a2),
                maxStrokes = lv.maxStrokes,
            };
            r.liftForceN = Math.Min(r.pumpPressureBar, relief) * a2 * 10f;

            // 판정 순서는 '현장에서 먼저 드러나는 문제' 순서다.
            if (lv.strictRelief && relief > lv.hoseLimitBar) r.outcome = HydraulicOutcome.UnsafeRelief;
            else if (r.loadPressureBar > relief) r.outcome = HydraulicOutcome.ReliefOpens;
            else if (r.loadPressureBar > lv.hoseLimitBar) r.outcome = HydraulicOutcome.HoseBurst;
            else if (r.pumpPressureBar < r.loadPressureBar) r.outcome = HydraulicOutcome.InsufficientForce;
            else if (r.strokesNeeded > lv.maxStrokes) r.outcome = HydraulicOutcome.TooSlow;
            else r.outcome = HydraulicOutcome.Success;
            return r;
        }

        /// <summary>레벨에서 성공하는 설계가 몇 개인지 센다 (레벨 디자인 검증용: 최소 1개, 너무 많지 않게).</summary>
        public static List<HydraulicDesign> SolvableDesigns(HydraulicLevel lv)
        {
            var list = new List<HydraulicDesign>();
            for (int i = 0; i < lv.inputAreasCm2.Length; i++)
                for (int o = 0; o < lv.outputAreasCm2.Length; o++)
                    for (int r = 0; r < lv.reliefBars.Length; r++)
                    {
                        var d = new HydraulicDesign(i, o, r);
                        if (Evaluate(lv, d).Succeeded) list.Add(d);
                    }
            return list;
        }
    }

    /// <summary>티어별 유압 크레인 레벨. 새 레벨은 여기 추가하고 PuzzleScenarioDB에서 id로 참조한다.</summary>
    public static class HydraulicLevelLibrary
    {
        public const string Lv1 = "hyd.lv1";
        public const string Lv2 = "hyd.lv2";
        public const string Lv3 = "hyd.lv3";

        private static List<HydraulicLevel> _all;
        public static IReadOnlyList<HydraulicLevel> All => _all ??= Build();
        public static HydraulicLevel ById(string id) => All.FirstOrDefault(l => l.id == id);

        public static HydraulicLevel ForTier(int tier) =>
            tier <= 2 ? All[0] : tier <= 4 ? All[1] : All[2];

        private static List<HydraulicLevel> Build() => new()
        {
            new HydraulicLevel
            {
                id = Lv1, title = "소형 크레인: 보급 상자 들어올리기",
                story = "상자가 크레인 아래에 끼었어. 펌프는 내 힘으로 누르니까, 피스톤 크기와 릴리프 압력을 골라 줘.",
                loadN = 3000, liftCm = 20, inputForceN = 200, strokeCm = 10, maxStrokes = 60, hoseLimitBar = 8,
                strictRelief = false,
                inputAreasCm2 = new[] { 2f, 5f, 10f, 20f },
                outputAreasCm2 = new[] { 20f, 50f, 100f, 200f, 400f },
                reliefBars = new[] { 2f, 5f, 10f, 25f },
            },
            new HydraulicLevel
            {
                id = Lv2, title = "중형 크레인: 바위 운반",
                story = "이번엔 바위야. 호스가 견딜 수 있는 압력을 넘기면 터지고, 릴리프는 호스 한계 아래로 맞춰야 해.",
                loadN = 12000, liftCm = 30, inputForceN = 400, strokeCm = 15, maxStrokes = 120, hoseLimitBar = 20,
                strictRelief = true,
                inputAreasCm2 = new[] { 2f, 5f, 10f, 20f },
                outputAreasCm2 = new[] { 50f, 100f, 200f, 400f, 800f },
                reliefBars = new[] { 5f, 10f, 15f, 20f, 30f },
            },
            new HydraulicLevel
            {
                id = Lv3, title = "대형 크레인: 침몰 컨테이너 인양",
                story = "힘만 키우면 펌프가 먼저 과열돼. 힘과 속도, 안전 한계까지 전부 만족하는 조합을 찾아 줘!",
                loadN = 30000, liftCm = 40, inputForceN = 500, strokeCm = 15, maxStrokes = 220, hoseLimitBar = 25,
                strictRelief = true,
                inputAreasCm2 = new[] { 1f, 2f, 5f, 10f },
                outputAreasCm2 = new[] { 100f, 200f, 400f, 800f },
                reliefBars = new[] { 10f, 20f, 30f, 40f },
            },
        };
    }
}
