using System;
using System.Collections.Generic;
using System.Linq;
using AutoSurvival.Core;
using AutoSurvival.Save;

namespace AutoSurvival.UI
{
    public enum IslandNodeState
    {
        Current,   // 지금 있는 섬
        Visited,   // 이미 열린 이전 섬 (무료로 돌아갈 수 있음)
        Next,      // 바로 다음 섬 (조건을 채우면 갈 수 있음)
        Locked     // 그 너머 (한 번에 한 칸만 이동)
    }

    public sealed class IslandNodeView
    {
        public IslandDef def;
        public IslandNodeState state;
        public bool cleared;
        public bool canTravel;
        public TravelCheck check;
        public string caption;      // 노드 아래 짧은 설명 ("현재 위치", "이동 가능", "조건 부족" ...)
    }

    public enum TravelLineKind { Header, Pass, Fail, NotApplicable, Detail }

    public sealed class TravelLine
    {
        public TravelLineKind kind;
        public string text;
        public TravelLine(TravelLineKind kind, string text) { this.kind = kind; this.text = text; }
    }

    /// <summary>
    /// 섬 지도 화면용 표시 데이터 (UnityEngine 의존 없음 -> 테스트 가능).
    /// 판정은 전부 GameProgress.CheckTravel이 하고, 여기서는 그 결과를 노드 상태와 문구 목록으로 바꾸기만 한다.
    /// 글꼴에 없을 수 있는 특수 기호(✔ 등)는 쓰지 않고 [통과]/[미충족] 표기를 쓴다.
    /// </summary>
    public static class TravelPresenter
    {
        public static List<IslandNodeView> BuildNodes(GameProgress progress, Func<ConceptTag, MasterySample> mastery, int food, int water)
        {
            var current = progress.Current;
            var nodes = new List<IslandNodeView>();

            foreach (var def in progress.Catalog.OrderBy(d => d.order))
            {
                var save = progress.Data.GetIsland(def.id);
                var check = progress.CheckTravel(def.id, mastery, food, water);
                var node = new IslandNodeView { def = def, cleared = save != null && save.cleared, check = check, canTravel = check.ok };

                if (current != null && def.id == current.id) node.state = IslandNodeState.Current;
                else if (current != null && def.order < current.order && save != null && save.unlocked) node.state = IslandNodeState.Visited;
                else if (progress.Next(current)?.id == def.id) node.state = IslandNodeState.Next;
                else node.state = IslandNodeState.Locked;

                switch (node.state)
                {
                    case IslandNodeState.Current: node.caption = node.cleared ? "현재 위치 (복구 완료)" : "현재 위치"; break;
                    case IslandNodeState.Visited: node.caption = "돌아가기 (무료)"; break;
                    case IslandNodeState.Next: node.caption = check.ok ? "이동 가능" : "조건 부족"; break;
                    default: node.caption = "아직 갈 수 없음"; break;
                }
                nodes.Add(node);
            }
            return nodes;
        }

        /// <summary>선택한 섬의 이동 가능 여부와 세 조건(설비 복구, 지식 관문, 항해 보급)을 줄 단위 목록으로 만든다.</summary>
        public static List<TravelLine> BuildLines(TravelCheck check)
        {
            var lines = new List<TravelLine>();

            // 구조적 실패(같은 섬, 건너뛰기, 아직 안 가 본 섬 ...): 조건 목록 없이 사유만
            if (check.conditions.Count == 0)
            {
                lines.Add(check.ok
                    ? new TravelLine(TravelLineKind.Pass, "이동할 수 있습니다.")
                    : new TravelLine(TravelLineKind.Fail, check.reason));
                return lines;
            }

            int failed = check.FailedConditions.Count();
            lines.Add(check.ok
                ? new TravelLine(TravelLineKind.Header, "이동 준비 완료")
                : new TravelLine(TravelLineKind.Header, $"이동할 수 없습니다 - 부족한 조건 {failed}가지"));

            foreach (var c in check.conditions)
            {
                if (!c.applicable)
                {
                    lines.Add(new TravelLine(TravelLineKind.NotApplicable, $"[해당 없음] {c.label}"));
                    continue;
                }

                lines.Add(new TravelLine(c.passed ? TravelLineKind.Pass : TravelLineKind.Fail,
                                         c.passed ? $"[통과] {c.label}" : $"[미충족] {c.label}"));
                foreach (var d in c.details) lines.Add(new TravelLine(TravelLineKind.Detail, d));
            }

            if (check.ok && (check.foodCost > 0 || check.waterCost > 0))
                lines.Add(new TravelLine(TravelLineKind.Detail, $"출발하면 식량 {check.foodCost}개, 물 {check.waterCost}개를 소비합니다."));
            return lines;
        }
    }
}
