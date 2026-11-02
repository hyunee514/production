using System;
using System.Collections.Generic;
using AutoSurvival.Core;
using AutoSurvival.Machines;
using AutoSurvival.Survival;

namespace AutoSurvival.Save
{
    // JsonUtility는 Dictionary를 저장하지 못하므로 모든 컬렉션을 List<직렬화 클래스>로 둔다.
    // enum은 정수로 저장된다 -> enum 값의 '순서'를 바꾸지 말고 새 값은 맨 뒤에 추가할 것.

    [Serializable] public class StatSave { public StatType type; public float current; }

    [Serializable] public class InventorySave { public int wood, stone, metalPanel, water, food; }

    [Serializable] public class ConceptSave
    {
        public ConceptTag concept;
        public int attempts, successes;
        public float secondsSinceSeen;      // Time.time은 세션마다 0에서 시작하므로 '얼마나 오래 안 봤는가'로 저장
    }

    [Serializable] public class MachineSave
    {
        public string id;
        public MachineKind kind;
        public int tier = 1;
        public bool running;                // false = 고장(Broken). 수리 중(Repairing)은 저장하지 않고 Broken/Running으로 되돌린다.
    }

    [Serializable] public class IslandSave
    {
        public string id;
        public bool unlocked;
        public bool cleared;                // 한 번 클리어하면 유지된다 (기계를 나중에 건드려도 되돌아가지 않음)
        public bool hasPosition;
        public float px, py, pz;            // 마지막 플레이어 위치
        public List<MachineSave> machines = new();

        public MachineSave GetMachine(string machineId) => machines.Find(m => m.id == machineId);

        public void UpsertMachine(MachineSave m)
        {
            int i = machines.FindIndex(x => x.id == m.id);
            if (i >= 0) machines[i] = m; else machines.Add(m);
        }
    }

    [Serializable] public class OopiSeenSave { public string lineId; public int count; }

    [Serializable] public class PlayerSave
    {
        public List<StatSave> stats = new();
        public InventorySave inventory = new();
    }

    /// <summary>세이브 파일 한 개의 전체 내용.</summary>
    [Serializable] public class SaveData
    {
        public const int CurrentVersion = 1;

        public int version = CurrentVersion;
        public string savedAtUtc;
        public float playTimeSeconds;
        public int deaths;

        public string currentIslandId;
        public bool gameComplete;

        public PlayerSave player = new();
        public List<IslandSave> islands = new();
        public List<ConceptSave> concepts = new();
        public List<string> completedObjectives = new();   // 달성한 목표 id (ObjectiveManager가 기록·복원)
        public List<OopiSeenSave> oopiSeen = new();     // 오피가 한 말 횟수 (반복 줄이기 + 한 번만 하는 대사 기억)

        public IslandSave GetIsland(string id) => islands.Find(i => i.id == id);

        public IslandSave GetOrAddIsland(string id)
        {
            var s = GetIsland(id);
            if (s == null) { s = new IslandSave { id = id }; islands.Add(s); }
            return s;
        }

        /// <summary>새 게임: 첫 번째 섬만 열려 있고 생존 수치는 가득 찬 상태 (수치는 GameManager가 채운다).</summary>
        public static SaveData NewGame(IReadOnlyList<IslandDef> catalog)
        {
            var d = new SaveData();
            foreach (var def in catalog) d.GetOrAddIsland(def.id);
            var first = FirstIsland(catalog);
            if (first != null)
            {
                d.currentIslandId = first.id;
                d.GetIsland(first.id).unlocked = true;
            }
            return d;
        }

        public static IslandDef FirstIsland(IReadOnlyList<IslandDef> catalog)
        {
            IslandDef best = null;
            foreach (var def in catalog) if (best == null || def.order < best.order) best = def;
            return best;
        }
    }
}
