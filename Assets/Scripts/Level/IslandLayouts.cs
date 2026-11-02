using System;
using System.Collections.Generic;
using System.Linq;
using AutoSurvival.Interaction;
using AutoSurvival.Machines;
using AutoSurvival.Save;

namespace AutoSurvival.Level
{
    /// <summary>기계 한 대의 위치. 방향은 베이스캠프를 향하도록 도구가 계산한다.</summary>
    public sealed class MachineSpot
    {
        public MachineKind kind; public float x, z; public int tier = 1;
        public MachineSpot(MachineKind kind, float x, float z) { this.kind = kind; this.x = x; this.z = z; }
    }

    /// <summary>자원 구역. 채집물 count개를 반지름 radius 원 안에 흩뿌리고, 각 채집물은 amount만큼 준다.</summary>
    public sealed class ZoneSpec
    {
        public LevelMarkerKind kind; public string label;
        public float x, z, radius; public int count, amount;
        public ZoneSpec(LevelMarkerKind kind, string label, float x, float z, float radius, int count, int amount)
        { this.kind = kind; this.label = label; this.x = x; this.z = z; this.radius = radius; this.count = count; this.amount = amount; }
        public int Supply => count * amount;
    }

    public sealed class LandmarkSpec
    {
        public string label; public float x, z;
        public LandmarkSpec(string label, float x, float z) { this.label = label; this.x = x; this.z = z; }
    }

    /// <summary>한 섬(씬)의 배치. 좌표는 미터, x는 동쪽, z는 북쪽. 바닥은 (-halfX..halfX, -halfZ..halfZ).</summary>
    public sealed class IslandLayout
    {
        public string islandId, sceneName, shortName, theme;
        public float halfX, halfZ;
        public float campX, campZ, spawnX, spawnZ;
        public List<MachineSpot> machines = new();
        public List<ZoneSpec> zones = new();
        public List<LandmarkSpec> landmarks = new();
    }

    /// <summary>
    /// 세 섬의 배치 데이터와 규칙 검사 (UnityEngine 의존 없음 -> 테스트 가능). 레벨 디자인 문서의 숫자가 곧 이 표다.
    /// 에디터 도구(ProjectSetupTool)가 이 표대로 씬을 만든다. 숫자를 바꾸면 Validate가 규칙 위반을 알려 준다.
    /// </summary>
    public static class IslandLayouts
    {
        public const float MinMachineGap = 12f;       // 기계끼리 최소 간격
        public const float MinCampToMachine = 15f;    // 베이스캠프에서 가장 가까운 기계까지 최소
        public const float MaxCampToMachine = 75f;    // 베이스캠프에서 가장 먼 기계까지 최대
        public const float BoundsMargin = 5f;         // 바닥 가장자리 여유
        public const float ResourceNeedRatio = 0.5f;  // 처음부터 놓인 자원이 전체 업그레이드 비용의 이만큼은 되어야 한다 (나머지는 재생)

        public static List<IslandLayout> All() => new()
        {
            new IslandLayout
            {
                islandId = "island.landing", sceneName = "Island_Landing", shortName = "landing", theme = "해안 (Pirate Kit + Survival Kit)",
                halfX = 50, halfZ = 50, campX = 0, campZ = 0, spawnX = 0, spawnZ = -5,
                machines =
                {
                    new MachineSpot(MachineKind.WaterPurifier, -20, 14),
                    new MachineSpot(MachineKind.PowerGenerator, 2, 28),
                    new MachineSpot(MachineKind.AutoGarden, 22, 12),
                },
                zones =
                {
                    new ZoneSpec(LevelMarkerKind.Wood, "야자숲", -32, -10, 8, 8, 3),
                    new ZoneSpec(LevelMarkerKind.Stone, "암초 해안", 34, -18, 7, 6, 2),
                    new ZoneSpec(LevelMarkerKind.Food, "낚시 바위", 16, -34, 6, 6, 2),
                    new ZoneSpec(LevelMarkerKind.Water, "민물 웅덩이", -14, -34, 5, 6, 2),
                },
                landmarks = { new LandmarkSpec("난파선 잔해", -38, -30), new LandmarkSpec("등대 터", 38, 32) },
            },
            new IslandLayout
            {
                islandId = "island.foundry", sceneName = "Island_Foundry", shortName = "foundry", theme = "폐공업지대 (City Kit Industrial + Factory Kit)",
                halfX = 70, halfZ = 60, campX = -24, campZ = -28, spawnX = -24, spawnZ = -33,
                machines =
                {
                    new MachineSpot(MachineKind.Smelter, -36, 2),
                    new MachineSpot(MachineKind.CraneLift, 14, -12),
                    new MachineSpot(MachineKind.PowerGenerator, -6, 30),
                    new MachineSpot(MachineKind.AutoGarden, -52, 28),
                },
                zones =
                {
                    new ZoneSpec(LevelMarkerKind.Wood, "목재 야적장", -6, -46, 8, 12, 3),
                    new ZoneSpec(LevelMarkerKind.Stone, "폐석 더미", 44, 12, 8, 8, 3),
                    new ZoneSpec(LevelMarkerKind.MetalScrap, "고철 컨테이너", 40, -34, 7, 6, 1),
                    new ZoneSpec(LevelMarkerKind.Food, "온실 잔해", -58, 6, 6, 6, 2),
                    new ZoneSpec(LevelMarkerKind.Water, "빗물 탱크", 20, 40, 6, 6, 2),
                },
                landmarks = { new LandmarkSpec("중앙 부두", 14, -26), new LandmarkSpec("굴뚝 지대", -20, 44) },
            },
            new IslandLayout
            {
                islandId = "island.core", sceneName = "Island_Core", shortName = "core", theme = "지하 코어 시설 (Mini Dungeon + Factory Kit)",
                halfX = 60, halfZ = 70, campX = 0, campZ = -30, spawnX = 0, spawnZ = -35,
                machines =
                {
                    new MachineSpot(MachineKind.WaterPurifier, -30, -22),
                    new MachineSpot(MachineKind.Smelter, 30, -22),
                    new MachineSpot(MachineKind.CraneLift, -26, 18),
                    new MachineSpot(MachineKind.PowerGenerator, 26, 18),
                    new MachineSpot(MachineKind.AutoGarden, 0, 44),
                },
                zones =
                {
                    new ZoneSpec(LevelMarkerKind.Wood, "무너진 목재 창고", -44, -52, 8, 14, 6),
                    new ZoneSpec(LevelMarkerKind.Stone, "붕괴 갱도", 44, -52, 8, 10, 5),
                    new ZoneSpec(LevelMarkerKind.MetalScrap, "폐기 조립라인", 0, 2, 8, 8, 2),
                    new ZoneSpec(LevelMarkerKind.Food, "지하 수경 재배실", -46, 44, 6, 6, 2),
                    new ZoneSpec(LevelMarkerKind.Water, "응축수 배관실", 46, 44, 6, 6, 2),
                },
                landmarks = { new LandmarkSpec("중앙 코어 반응로", 0, 62), new LandmarkSpec("엘리베이터 입구", 0, -52) },
            },
        };

        public static IslandLayout For(string islandId) => All().FirstOrDefault(l => l.islandId == islandId);

        public static float Dist(float x1, float z1, float x2, float z2)
        {
            float dx = x1 - x2, dz = z1 - z2;
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>기계 종류와 같은 이름의 채집 종류: 구역 종류 -> 인벤토리 자원.</summary>
        public static bool IsResource(LevelMarkerKind k) =>
            k == LevelMarkerKind.Wood || k == LevelMarkerKind.Stone || k == LevelMarkerKind.MetalScrap || k == LevelMarkerKind.Food || k == LevelMarkerKind.Water;

        /// <summary>섬의 클리어 조건을 모두 티어 1에서 끌어올리는 데 드는 총 업그레이드 비용 (나무, 돌, 금속 패널).</summary>
        public static ItemCost UpgradeNeed(IslandDef def)
        {
            var total = new ItemCost();
            foreach (var req in def.clearRequirements)
                for (int n = 0; n < req.count; n++)
                    for (int t = 2; t <= req.minTier; t++)
                    {
                        var c = AutomationMachine.UpgradeCost(t);
                        total.wood += c.wood; total.stone += c.stone; total.metalPanel += c.metalPanel;
                    }
            return total;
        }

        public static int Supply(IslandLayout l, LevelMarkerKind kind) => l.zones.Where(z => z.kind == kind).Sum(z => z.Supply);

        /// <summary>규칙 위반 목록 (비어 있으면 통과).</summary>
        public static List<string> Validate(IslandLayout l, IReadOnlyList<IslandDef> catalog)
        {
            var issues = new List<string>();
            var def = catalog.FirstOrDefault(d => d.id == l.islandId);
            if (def == null) { issues.Add($"{l.islandId}: 섬 카탈로그에 없는 id"); return issues; }
            if (def.sceneName != l.sceneName) issues.Add($"{l.islandId}: 씬 이름이 카탈로그({def.sceneName})와 다름 ({l.sceneName})");

            // 클리어 조건의 기계가 모두 놓였는가
            foreach (var req in def.clearRequirements)
            {
                int have = l.machines.Count(m => m.kind == req.kind);
                if (have < req.count) issues.Add($"{l.shortName}: {req.kind} {req.count}대가 필요한데 {have}대만 배치됨");
            }

            // 범위
            bool Inside(float x, float z, float pad) => Math.Abs(x) <= l.halfX - pad && Math.Abs(z) <= l.halfZ - pad;
            foreach (var m in l.machines) if (!Inside(m.x, m.z, BoundsMargin)) issues.Add($"{l.shortName}: {m.kind}이(가) 바닥 가장자리에 너무 가까움");
            if (!Inside(l.campX, l.campZ, BoundsMargin)) issues.Add($"{l.shortName}: 베이스캠프가 바닥 밖");
            if (!Inside(l.spawnX, l.spawnZ, BoundsMargin)) issues.Add($"{l.shortName}: 스폰이 바닥 밖");
            foreach (var z in l.zones)
                if (Math.Abs(z.x) + z.radius > l.halfX - 3 || Math.Abs(z.z) + z.radius > l.halfZ - 3) issues.Add($"{l.shortName}: 구역 '{z.label}'이(가) 바닥을 벗어남");

            // 간격
            for (int i = 0; i < l.machines.Count; i++)
                for (int j = i + 1; j < l.machines.Count; j++)
                {
                    float d = Dist(l.machines[i].x, l.machines[i].z, l.machines[j].x, l.machines[j].z);
                    if (d < MinMachineGap) issues.Add($"{l.shortName}: {l.machines[i].kind}-{l.machines[j].kind} 간격 {d:0.#}m (최소 {MinMachineGap}m)");
                }
            foreach (var m in l.machines)
            {
                float d = Dist(m.x, m.z, l.campX, l.campZ);
                if (d < MinCampToMachine || d > MaxCampToMachine) issues.Add($"{l.shortName}: 베이스캠프-{m.kind} 거리 {d:0.#}m (허용 {MinCampToMachine}~{MaxCampToMachine}m)");
            }
            if (Dist(l.spawnX, l.spawnZ, l.campX, l.campZ) > 8f) issues.Add($"{l.shortName}: 스폰이 베이스캠프에서 8m보다 멂");

            // 구역이 기계·캠프를 덮지 않는가
            foreach (var z in l.zones)
            {
                foreach (var m in l.machines)
                    if (Dist(z.x, z.z, m.x, m.z) < z.radius + 4f) issues.Add($"{l.shortName}: 구역 '{z.label}'이(가) {m.kind}과(와) 겹침");
                if (Dist(z.x, z.z, l.campX, l.campZ) < z.radius + 6f) issues.Add($"{l.shortName}: 구역 '{z.label}'이(가) 베이스캠프와 겹침");
                if (z.count <= 0 || z.amount <= 0) issues.Add($"{l.shortName}: 구역 '{z.label}'의 수량이 0");
            }

            // 필수 자원
            foreach (var k in new[] { LevelMarkerKind.Wood, LevelMarkerKind.Stone, LevelMarkerKind.Food, LevelMarkerKind.Water })
                if (Supply(l, k) <= 0) issues.Add($"{l.shortName}: {k} 구역이 없음");

            // 업그레이드 비용을 처음 놓인 자원으로 일부는 감당할 수 있는가
            var need = UpgradeNeed(def);
            void Budget(string name, int needAmount, int supply)
            {
                if (needAmount > 0 && supply < Math.Ceiling(needAmount * ResourceNeedRatio))
                    issues.Add($"{l.shortName}: {name} 공급 {supply} < 필요 {needAmount}의 {ResourceNeedRatio * 100:0}%");
            }
            Budget("나무", need.wood, Supply(l, LevelMarkerKind.Wood));
            Budget("돌", need.stone, Supply(l, LevelMarkerKind.Stone));
            Budget("금속 패널", need.metalPanel, Supply(l, LevelMarkerKind.MetalScrap));

            // 다음 섬 항해 보급(식량·물)을 넉넉히 모을 수 있는가
            var next = catalog.Where(d => d.order > def.order).OrderBy(d => d.order).FirstOrDefault();
            if (next != null)
            {
                if (Supply(l, LevelMarkerKind.Food) < next.travelFood * 2) issues.Add($"{l.shortName}: 식량 공급이 다음 항해({next.travelFood})의 2배 미만");
                if (Supply(l, LevelMarkerKind.Water) < next.travelWater * 2) issues.Add($"{l.shortName}: 물 공급이 다음 항해({next.travelWater})의 2배 미만");
            }
            return issues;
        }

        /// <summary>machineId 규칙: "{섬}.{기계}.{번호}" (같은 섬 안에서 유일). 예: landing.WaterPurifier.01</summary>
        public static string MachineId(string shortName, MachineKind kind, int index) => $"{shortName}.{kind}.{index:00}";

        /// <summary>같은 종류가 여러 대일 때 번호를 매긴 id 목록 (배치 순서대로).</summary>
        public static List<string> MachineIds(IslandLayout l)
        {
            var counts = new Dictionary<MachineKind, int>();
            var ids = new List<string>();
            foreach (var m in l.machines)
            {
                counts.TryGetValue(m.kind, out int n);
                counts[m.kind] = ++n;
                ids.Add(MachineId(l.shortName, m.kind, n));
            }
            return ids;
        }
    }
}
