using System;
using System.Collections.Generic;
using System.Linq;
using AutoSurvival.Core;

namespace AutoSurvival.Save
{
    public readonly struct MasterySample
    {
        public readonly int attempts, successes;
        public MasterySample(int attempts, int successes) { this.attempts = attempts; this.successes = successes; }
        public float Mastery => attempts == 0 ? 0f : (float)successes / attempts;
    }

    public sealed class ClearReport
    {
        public bool cleared = true;
        public readonly List<string> missing = new();
    }

    public enum TravelConditionKind { ClearIsland, KnowledgeGate, Supplies }

    /// <summary>다음 섬으로 가는 세 가지 조건 중 하나. 통과 여부와 상세 항목을 함께 담아 UI가 목록으로 보여 준다.</summary>
    public sealed class TravelCondition
    {
        public TravelConditionKind kind;
        public string label;                            // 예: "불시착 해안 설비 복구"
        public bool applicable = true;                  // false면 이 섬에는 해당 조건이 없음 (예: 관문이 없는 섬)
        public bool passed;
        public string failMessage;                      // 실패 시 한 줄 사유
        public readonly List<string> details = new();   // 항목별 안내 (실패 시: 부족한 것 / 통과 시: 현재 상태)
    }

    public sealed class TravelCheck
    {
        public bool ok;
        public string reason;                           // 실패 시 한 줄 사유
        public readonly List<string> details = new();   // 실패한 조건들의 세부 항목을 한데 모은 목록
        /// <summary>앞 섬 -> 다음 섬 이동일 때만 채워진다 (3개: 복구, 관문, 보급). 구조적 실패(같은 섬, 건너뛰기 등)와 되돌아가기는 비어 있다.</summary>
        public readonly List<TravelCondition> conditions = new();
        public int foodCost, waterCost;                 // 성공 시 차감할 보급

        public IEnumerable<TravelCondition> FailedConditions
        {
            get { foreach (var c in conditions) if (c.applicable && !c.passed) yield return c; }
        }
    }

    /// <summary>
    /// 섬 진행도 규칙 (UnityEngine 의존 없음 -> 테스트 가능).
    /// 규칙 요약:
    ///  - 섬 클리어 = 클리어 조건의 기계가 모두 가동 중. 한 번 클리어하면 유지된다.
    ///  - 클리어하면 다음 섬이 열린다 (건너뛰기 불가, 한 번에 한 칸).
    ///  - 다음 섬 이동 = 현재 섬 클리어 + 지식 관문 통과 + 항해 보급(식량·물) 충분.
    ///  - 이미 열린 이전 섬으로 돌아가는 것은 조건 없이 무료 (보급이 바닥나도 베이스로 돌아올 수 있게).
    ///  - 마지막 섬을 클리어하면 게임 클리어.
    /// </summary>
    public sealed class GameProgress
    {
        public SaveData Data { get; }
        public IReadOnlyList<IslandDef> Catalog { get; }

        public GameProgress(SaveData data, IReadOnlyList<IslandDef> catalog)
        {
            Data = data; Catalog = catalog;
            foreach (var def in catalog) Data.GetOrAddIsland(def.id);
            if (Find(Data.currentIslandId) == null)
            {
                var first = SaveData.FirstIsland(catalog);
                if (first != null) { Data.currentIslandId = first.id; Data.GetIsland(first.id).unlocked = true; }
            }
        }

        public IslandDef Find(string id) => string.IsNullOrEmpty(id) ? null : Catalog.FirstOrDefault(d => d.id == id);
        public IslandDef Current => Find(Data.currentIslandId);
        public IslandDef LastIsland => Catalog.OrderBy(d => d.order).LastOrDefault();

        public IslandDef Next(IslandDef def) =>
            def == null ? null : Catalog.Where(d => d.order > def.order).OrderBy(d => d.order).FirstOrDefault();

        // ───────── 클리어 판정 ─────────

        public ClearReport EvaluateClear(IslandDef def)
        {
            var report = new ClearReport();
            var save = Data.GetIsland(def.id);
            if (save == null) { report.cleared = false; return report; }

            foreach (var req in def.clearRequirements)
            {
                int have = save.machines.Count(m => m.kind == req.kind && m.running && m.tier >= req.minTier);
                if (have < req.count)
                {
                    report.cleared = false;
                    report.missing.Add($"{DisplayNames.Machine(req.kind)} Tier {req.minTier} 이상 {req.count}대 가동 (현재 {have}대)");
                }
            }
            return report;
        }

        /// <summary>
        /// 현재 섬의 클리어 상태를 갱신한다. 이번 호출로 처음 클리어되면 true를 돌려주고 다음 섬을 연다.
        /// 클리어 상태는 한 번 켜지면 꺼지지 않는다.
        /// </summary>
        public bool RefreshClear(out IslandDef newlyCleared)
        {
            newlyCleared = null;
            var def = Current;
            if (def == null) return false;
            var save = Data.GetIsland(def.id);
            if (save.cleared) return false;
            if (!EvaluateClear(def).cleared) return false;

            save.cleared = true;
            newlyCleared = def;

            var next = Next(def);
            if (next != null) Data.GetOrAddIsland(next.id).unlocked = true;
            else Data.gameComplete = true;      // 마지막 섬 클리어
            return true;
        }

        // ───────── 섬 이동 ─────────

        /// <param name="mastery">개념 -> (시도, 성공). GameManager가 KnowledgeTracker에서 읽어 넘긴다.</param>
        public TravelCheck CheckTravel(string targetId, Func<ConceptTag, MasterySample> mastery, int food, int water)
        {
            var check = new TravelCheck();
            var target = Find(targetId);
            var current = Current;

            if (target == null) return Fail(check, "알 수 없는 섬입니다.");
            if (current != null && target.id == current.id) return Fail(check, "이미 이 섬에 있습니다.");

            var targetSave = Data.GetIsland(target.id);

            // 이전 섬으로 돌아가기: 한 번이라도 열린 섬이면 무료
            if (current != null && target.order < current.order)
            {
                if (!targetSave.unlocked) return Fail(check, "아직 가 본 적 없는 섬입니다.");
                check.ok = true;
                return check;
            }

            // 앞으로 가기: 한 칸씩만
            var next = Next(current);
            if (next == null || next.id != target.id) return Fail(check, "한 번에 바로 다음 섬까지만 갈 수 있습니다.");

            // 세 조건을 각각 독립적으로 평가한다 (하나가 실패해도 나머지 상태를 함께 보여 주기 위해).
            var clear = EvaluateClear(current);
            bool cleared = Data.GetIsland(current.id).cleared || clear.cleared;
            var cClear = new TravelCondition
            {
                kind = TravelConditionKind.ClearIsland,
                label = $"{current.displayName} 설비 복구",
                passed = cleared,
                failMessage = $"{current.displayName}의 설비를 모두 복구해야 떠날 수 있습니다.",
            };
            if (!cleared) cClear.details.AddRange(clear.missing);
            else cClear.details.Add("모든 설비가 가동 중입니다.");

            if (cleared && !targetSave.unlocked) return Fail(check, "아직 항로가 열리지 않았습니다.");

            var gate = target.entryGate;
            var cGate = new TravelCondition
            {
                kind = TravelConditionKind.KnowledgeGate,
                label = "지식 관문",
                applicable = gate != null && !gate.IsEmpty,
                failMessage = "항해 전에 더 익혀 둘 개념이 있습니다.",
            };
            if (!cGate.applicable) { cGate.passed = true; cGate.details.Add("이 섬에는 관문이 없습니다."); }
            else
            {
                var gateIssues = new List<string>();
                float sum = 0f;
                foreach (var tag in gate.concepts)
                {
                    var s = mastery(tag);
                    sum += s.Mastery;
                    if (s.attempts < gate.minAttemptsPerConcept)
                        gateIssues.Add($"{DisplayNames.Concept(tag)}: 퍼즐·현장 노트를 {gate.minAttemptsPerConcept}회 이상 풀어 보세요 (현재 {s.attempts}회)");
                }
                float avg = sum / gate.concepts.Count;
                if (gateIssues.Count == 0 && avg + 1e-6f < gate.minAverageMastery)
                    gateIssues.Add($"숙련도 평균 {avg * 100f:0}% (필요 {gate.minAverageMastery * 100f:0}%) — 약한 개념의 퍼즐을 더 풀어 보세요");

                cGate.passed = gateIssues.Count == 0;
                if (cGate.passed) cGate.details.Add($"숙련도 평균 {avg * 100f:0}% (필요 {gate.minAverageMastery * 100f:0}%)");
                else cGate.details.AddRange(gateIssues);
            }

            bool suppliesOk = food >= target.travelFood && water >= target.travelWater;
            var cSupply = new TravelCondition
            {
                kind = TravelConditionKind.Supplies,
                label = "항해 보급",
                applicable = target.travelFood > 0 || target.travelWater > 0,
                passed = suppliesOk || (target.travelFood <= 0 && target.travelWater <= 0),
                failMessage = "항해 보급이 부족합니다.",
            };
            if (!cSupply.applicable) cSupply.details.Add("보급이 필요하지 않습니다.");
            else cSupply.details.Add($"식량 {target.travelFood}개 (보유 {food}), 물 {target.travelWater}개 (보유 {water}) 필요");

            check.conditions.Add(cClear); check.conditions.Add(cGate); check.conditions.Add(cSupply);

            var failed = new List<TravelCondition>(check.FailedConditions);
            if (failed.Count > 0)
            {
                check.ok = false;
                check.reason = failed.Count == 1 ? failed[0].failMessage : $"이동 조건 {failed.Count}가지를 아직 채우지 못했습니다.";
                foreach (var f in failed) check.details.AddRange(f.details);
                return check;
            }

            check.ok = true;
            check.foodCost = target.travelFood;
            check.waterCost = target.travelWater;
            return check;
        }

        private static TravelCheck Fail(TravelCheck c, string reason) { c.ok = false; c.reason = reason; return c; }

        /// <summary>이동 확정 (검증은 CheckTravel이 한다). 보급 차감과 씬 전환은 호출자 몫.</summary>
        public void ApplyTravel(string targetId)
        {
            var target = Find(targetId);
            if (target == null) return;
            Data.currentIslandId = target.id;
            Data.GetOrAddIsland(target.id).unlocked = true;
            Data.GetIsland(target.id).hasPosition = false;      // 도착 지점은 씬의 기본 스폰
        }

        public bool IsComplete => Data.gameComplete;
    }
}
