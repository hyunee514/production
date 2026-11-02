using System;
using System.Collections.Generic;
using UnityEngine;
using AutoSurvival.Core;
using AutoSurvival.Machines;

namespace AutoSurvival.Save
{
    /// <summary>"이 종류의 기계가 minTier 이상으로 count대 가동 중이어야 한다."</summary>
    [Serializable] public class MachineRequirement
    {
        public MachineKind kind;
        [Range(1, 5)] public int minTier = 1;
        public int count = 1;
    }

    /// <summary>
    /// 다음 섬으로 가기 위한 지식 관문. 시험 범위를 억지로 풀게 하지 않고,
    /// 이 섬에서 퍼즐·현장 노트로 쌓인 숙련도(KnowledgeTracker)가 어느 정도인지만 본다.
    /// 부족하면 '해당 개념의 퍼즐을 더 풀어 보라'는 안내를 준다 (막히지 않도록 퍼즐은 반복 가능).
    /// </summary>
    [Serializable] public class MasteryGate
    {
        public List<ConceptTag> concepts = new();
        [Tooltip("개념마다 최소 이만큼은 시도(퍼즐 또는 현장 노트)해 봐야 한다")] public int minAttemptsPerConcept = 1;
        [Range(0f, 1f)] public float minAverageMastery = 0.5f;

        public bool IsEmpty => concepts == null || concepts.Count == 0;
    }

    [Serializable] public class IslandDef
    {
        public string id;
        public string displayName;
        public string sceneName;                         // 비워 두면 씬 전환 없이 진행도만 갱신 (테스트용)
        public int order;                                // 1, 2, 3 ... 항해 순서
        [TextArea] public string briefing;               // 도착 시 오피 브리핑 / 지도 설명
        public List<MachineRequirement> clearRequirements = new();   // 이 섬을 '클리어'하는 조건
        public MasteryGate entryGate = new();            // 이 섬으로 들어가기 위한 관문 (1번 섬은 비움)
        public int travelFood, travelWater;              // 이 섬으로 '가는' 항해 보급 비용 (되돌아올 때는 무료)
    }

    /// <summary>기본 3섬 구성. Inspector에서 GameManager.islands를 채우면 그쪽이 우선한다.</summary>
    public static class IslandCatalog
    {
        public static List<IslandDef> Default() => new()
        {
            new IslandDef
            {
                id = "island.landing", displayName = "불시착 해안", sceneName = "Island_Landing", order = 1,
                briefing = "추락한 구조선 잔해와 멈춘 정수 시설이 보여. 일단 마실 물과 전기부터 되살리자.",
                clearRequirements = new List<MachineRequirement>
                {
                    new MachineRequirement { kind = MachineKind.WaterPurifier, minTier = 1 },
                    new MachineRequirement { kind = MachineKind.PowerGenerator, minTier = 1 },
                    new MachineRequirement { kind = MachineKind.AutoGarden, minTier = 1 },
                },
            },
            new IslandDef
            {
                id = "island.foundry", displayName = "버려진 제련 섬", sceneName = "Island_Foundry", order = 2,
                briefing = "굴뚝이 늘어선 공업 섬이야. 제련기와 크레인이 살아나면 금속을 마음껏 쓸 수 있어.",
                entryGate = new MasteryGate
                {
                    concepts = new List<ConceptTag> { ConceptTag.LadderSelfHold, ConceptTag.PipeFitting },
                    minAttemptsPerConcept = 1, minAverageMastery = 0.5f,
                },
                travelFood = 3, travelWater = 3,
                clearRequirements = new List<MachineRequirement>
                {
                    new MachineRequirement { kind = MachineKind.Smelter, minTier = 2 },
                    new MachineRequirement { kind = MachineKind.CraneLift, minTier = 2 },
                    new MachineRequirement { kind = MachineKind.PowerGenerator, minTier = 3 },
                    new MachineRequirement { kind = MachineKind.AutoGarden, minTier = 3 },
                },
            },
            new IslandDef
            {
                id = "island.core", displayName = "테라포밍 코어", sceneName = "Island_Core", order = 3,
                briefing = "행성을 멈춰 세운 중앙 설비야. 여기까지 온 네 손이라면 모든 기계를 다시 돌릴 수 있어.",
                entryGate = new MasteryGate
                {
                    concepts = new List<ConceptTag> { ConceptTag.LadderInterlock, ConceptTag.HydraulicPressure, ConceptTag.PneumaticValve },
                    minAttemptsPerConcept = 1, minAverageMastery = 0.6f,
                },
                travelFood = 5, travelWater = 5,
                clearRequirements = new List<MachineRequirement>
                {
                    new MachineRequirement { kind = MachineKind.WaterPurifier, minTier = 3 },
                    new MachineRequirement { kind = MachineKind.Smelter, minTier = 3 },
                    new MachineRequirement { kind = MachineKind.CraneLift, minTier = 3 },
                    new MachineRequirement { kind = MachineKind.PowerGenerator, minTier = 4 },
                    new MachineRequirement { kind = MachineKind.AutoGarden, minTier = 4 },
                },
            },
        };
    }
}
