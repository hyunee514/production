using System;
using System.Collections.Generic;
using System.Linq;
using AutoSurvival.Interaction;
using AutoSurvival.Machines;

namespace AutoSurvival.Objectives
{
    public enum ConditionKind
    {
        Resource,        // 인벤토리에 자원이 count개 이상
        FireLit,         // 모닥불이 타고 있음
        MachineRunning,  // 해당 종류의 기계가 minTier 이상으로 가동 중
        IslandCleared,   // 섬 id가 클리어됨
        ArrivedAt,       // 섬 id에 도착함 (현재 섬이 그 섬)
        Flag,            // 코드에서 SetFlag(id)로 켠 신호 (예: 필드노트 읽기)
    }

    /// <summary>목표 달성 조건 한 개. 데이터만 담는다 (함수를 담지 않으므로 저장·편집이 쉽다).</summary>
    [Serializable]
    public class ObjectiveCondition
    {
        public ConditionKind kind;
        public ResourceKind resource;
        public int count = 1;
        public MachineKind machine;
        public int minTier = 1;
        public string id;

        public static ObjectiveCondition Gather(ResourceKind r, int n) => new() { kind = ConditionKind.Resource, resource = r, count = n };
        public static ObjectiveCondition Fire() => new() { kind = ConditionKind.FireLit };
        public static ObjectiveCondition Running(MachineKind m, int tier = 1) => new() { kind = ConditionKind.MachineRunning, machine = m, minTier = tier };
        public static ObjectiveCondition Cleared(string islandId) => new() { kind = ConditionKind.IslandCleared, id = islandId };
        public static ObjectiveCondition Arrived(string islandId) => new() { kind = ConditionKind.ArrivedAt, id = islandId };
        public static ObjectiveCondition FlagSet(string flag) => new() { kind = ConditionKind.Flag, id = flag };
    }

    [Serializable]
    public class ObjectiveDef
    {
        public string id;
        public string title;
        [UnityEngine.TextArea] public string hint;
        [UnityEngine.Tooltip("비우면 어느 섬에서나 평가. 값이 있으면 그 섬에 있을 때만 평가·표시한다.")]
        public string islandId;
        public ObjectiveCondition condition;
    }

    public readonly struct MachineSnap
    {
        public readonly MachineKind kind; public readonly int tier; public readonly bool running;
        public MachineSnap(MachineKind kind, int tier, bool running) { this.kind = kind; this.tier = tier; this.running = running; }
    }

    /// <summary>지금 세계의 상태를 한 장으로 찍은 것. 목표 판정은 이 값만 본다 (엔진 객체를 직접 보지 않는다).</summary>
    public sealed class WorldSnapshot
    {
        public int wood, stone, metalPanel, water, food;
        public bool fireLit;
        public string currentIslandId;
        public readonly List<MachineSnap> machines = new();
        public readonly HashSet<string> clearedIslands = new();
        public readonly HashSet<string> flags = new();

        public int Amount(ResourceKind k) => k switch
        {
            ResourceKind.Wood => wood, ResourceKind.Stone => stone, ResourceKind.MetalPanel => metalPanel,
            ResourceKind.Water => water, _ => food,
        };
    }

    public enum LineState { Done, Active, Upcoming }

    public readonly struct ObjectiveLine
    {
        public readonly ObjectiveDef def; public readonly LineState state; public readonly string progress;
        public ObjectiveLine(ObjectiveDef def, LineState state, string progress) { this.def = def; this.state = state; this.progress = progress; }
    }

    public static class ObjectiveEvaluator
    {
        public static bool IsMet(ObjectiveCondition c, WorldSnapshot w)
        {
            if (c == null) return false;
            switch (c.kind)
            {
                case ConditionKind.Resource: return w.Amount(c.resource) >= c.count;
                case ConditionKind.FireLit: return w.fireLit;
                case ConditionKind.MachineRunning: return w.machines.Any(m => m.kind == c.machine && m.running && m.tier >= c.minTier);
                case ConditionKind.IslandCleared: return w.clearedIslands.Contains(c.id ?? "");
                case ConditionKind.ArrivedAt: return w.currentIslandId == c.id;
                case ConditionKind.Flag: return w.flags.Contains(c.id ?? "");
                default: return false;
            }
        }

        /// <summary>진행 표시 문구. 자원은 "2/3"처럼, 나머지는 빈 문자열.</summary>
        public static string ProgressText(ObjectiveCondition c, WorldSnapshot w)
        {
            if (c == null || w == null) return "";
            if (c.kind == ConditionKind.Resource) return $"{Math.Min(w.Amount(c.resource), c.count)}/{c.count}";
            return "";
        }
    }

    /// <summary>
    /// 목표 추적기 (엔진 비의존).
    ///  - 한 번 달성한 목표는 계속 달성 상태로 남는다 (재료를 써서 줄어들어도 되돌아가지 않는다).
    ///  - 뒤쪽 목표가 달성되면 앞쪽 목표도 함께 달성 처리한다 (순서를 건너뛰고 진행한 플레이어, 오래된 세이브 대응).
    ///  - islandId가 있는 목표는 그 섬에 있을 때만 평가한다 (다른 섬의 같은 종류 기계로 잘못 달성되지 않도록).
    /// </summary>
    public sealed class ObjectiveTracker
    {
        private readonly List<ObjectiveDef> _defs;
        private readonly HashSet<string> _done = new();

        public IReadOnlyList<ObjectiveDef> Defs => _defs;
        public int CompletedCount => _done.Count;

        public ObjectiveTracker(IEnumerable<ObjectiveDef> defs) { _defs = defs.Where(d => d != null && !string.IsNullOrEmpty(d.id)).ToList(); }

        public bool IsComplete(string id) => _done.Contains(id);

        private static bool Applies(ObjectiveDef d, string currentIsland) => string.IsNullOrEmpty(d.islandId) || d.islandId == currentIsland;

        /// <summary>지금 해야 할 목표 (현재 섬에 해당하는 것 중 첫 미달성). 없으면 null.</summary>
        public ObjectiveDef Active(string currentIsland) => _defs.FirstOrDefault(d => !_done.Contains(d.id) && Applies(d, currentIsland));

        public bool AllDone => _defs.All(d => _done.Contains(d.id));

        /// <summary>새로 달성된 목표들을 (목록 순서대로) 돌려준다.</summary>
        public List<ObjectiveDef> Evaluate(WorldSnapshot w)
        {
            var newly = new List<ObjectiveDef>();
            for (int i = 0; i < _defs.Count; i++)
            {
                var d = _defs[i];
                if (_done.Contains(d.id) || !Applies(d, w.currentIslandId)) continue;
                if (!ObjectiveEvaluator.IsMet(d.condition, w)) continue;

                // 앞쪽의 미달성 목표도 함께 달성 (순서대로 newly에 담긴다)
                for (int j = 0; j <= i; j++)
                    if (_done.Add(_defs[j].id)) newly.Add(_defs[j]);
            }
            return newly;
        }

        /// <summary>화면에 보일 줄: 직전에 달성한 것 1개 + 현재 목표 + 다음 목표들 (최대 maxLines줄).</summary>
        public List<ObjectiveLine> View(WorldSnapshot w, string currentIsland, int maxLines = 4)
        {
            var applicable = _defs.Where(d => Applies(d, currentIsland)).ToList();
            var lines = new List<ObjectiveLine>();
            int activeIdx = applicable.FindIndex(d => !_done.Contains(d.id));

            if (activeIdx < 0)
            {
                // 모두 달성: 마지막 것만 달성 상태로 보여 준다
                if (applicable.Count > 0) lines.Add(new ObjectiveLine(applicable[^1], LineState.Done, ""));
                return lines;
            }

            if (activeIdx > 0 && maxLines > 1) lines.Add(new ObjectiveLine(applicable[activeIdx - 1], LineState.Done, ""));
            lines.Add(new ObjectiveLine(applicable[activeIdx], LineState.Active, ObjectiveEvaluator.ProgressText(applicable[activeIdx].condition, w)));
            for (int k = activeIdx + 1; k < applicable.Count && lines.Count < maxLines; k++)
                lines.Add(new ObjectiveLine(applicable[k], LineState.Upcoming, ""));
            return lines;
        }

        // ───────── 저장 ─────────

        public List<string> Export() => _defs.Where(d => _done.Contains(d.id)).Select(d => d.id).ToList();

        /// <summary>저장된 id 목록을 복원한다. 지금 목록에 없는 id(옛 버전)는 버린다.</summary>
        public void Import(IEnumerable<string> ids)
        {
            _done.Clear();
            if (ids == null) return;
            var known = new HashSet<string>(_defs.Select(d => d.id));
            foreach (var id in ids) if (id != null && known.Contains(id)) _done.Add(id);
        }

        public void Reset() => _done.Clear();
    }

    /// <summary>첫 번째 섬(불시착 해안)부터 제련 섬 입구까지의 기본 목표 목록.</summary>
    public static class ObjectiveDefaults
    {
        public const string Landing = "island.landing";
        public const string Foundry = "island.foundry";

        public static List<ObjectiveDef> All() => new()
        {
            new ObjectiveDef { id = "landing.wood", islandId = Landing, title = "나무 3개 모으기",
                hint = "해안 야자숲에서 [E]로 채집해 보자. 모닥불과 수리에 쓸 거야.", condition = ObjectiveCondition.Gather(ResourceKind.Wood, 3) },
            new ObjectiveDef { id = "landing.fire", islandId = Landing, title = "모닥불 피우기",
                hint = "베이스캠프 모닥불에서 [E]로 나무를 넣어 불을 붙이자. 불 가까이에서는 체온이 올라가.", condition = ObjectiveCondition.Fire() },
            new ObjectiveDef { id = "landing.water", islandId = Landing, title = "정수 플랜트 티어 1 가동하기",
                hint = "정수기에 다가가 [E]. 수리 퍼즐을 풀면 마실 물이 생겨.", condition = ObjectiveCondition.Running(MachineKind.WaterPurifier, 1) },
            new ObjectiveDef { id = "landing.power", islandId = Landing, title = "발전기 티어 1 가동하기",
                hint = "발전기는 래더 회로 퍼즐이야. 두 출력이 동시에 켜지지 않게 인터록을 잊지 말자.", condition = ObjectiveCondition.Running(MachineKind.PowerGenerator, 1) },
            new ObjectiveDef { id = "landing.garden", islandId = Landing, title = "자동 재배기 티어 1 가동하기",
                hint = "재배기는 타이머 회로야. 켜짐 지연(TON)이 언제 동작하는지 생각해 보자.", condition = ObjectiveCondition.Running(MachineKind.AutoGarden, 1) },
            new ObjectiveDef { id = "landing.sail", title = "제련 섬으로 항해하기",
                hint = "M 키로 섬 지도를 열자. 물과 식량 보급을 챙기는 것도 잊지 말고!", condition = ObjectiveCondition.Arrived(Foundry) },
            new ObjectiveDef { id = "foundry.smelter", islandId = Foundry, title = "제련기 가동하기",
                hint = "제련기는 PLC 회로 퍼즐이야. 가동하면 금속 패널을 만들 수 있어.", condition = ObjectiveCondition.Running(MachineKind.Smelter, 1) },
            new ObjectiveDef { id = "foundry.crane", islandId = Foundry, title = "크레인 가동하기",
                hint = "크레인은 유압 퍼즐이야. 파스칼의 원리로 작은 힘을 큰 힘으로!", condition = ObjectiveCondition.Running(MachineKind.CraneLift, 1) },
        };
    }
}
