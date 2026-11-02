#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using AutoSurvival.Audio;
using AutoSurvival.Core;
using AutoSurvival.Data;
using AutoSurvival.Dialogue;
using AutoSurvival.Feedback;
using AutoSurvival.Interaction;
using AutoSurvival.Level;
using AutoSurvival.Machines;
using AutoSurvival.Objectives;
using AutoSurvival.Puzzles;
using AutoSurvival.Save;
using AutoSurvival.Settings;
using AutoSurvival.Survival;
using AutoSurvival.UI;
using Object = UnityEngine.Object;

namespace AutoSurvival.EditorTools
{
    /// <summary>
    /// 메뉴: AutoSurvival ▸ 프로젝트 기본 세팅 ▸ ...
    ///  - 전체 실행: 데이터 에셋, 퍼즐 프리팹, 섬 씬 3개(마스터 Canvas 포함), Build Settings 등록을 한 번에 한다.
    ///  - 이미 있는 씬은 건너뛴다 (손으로 고친 작업을 지우지 않는다). 다시 만들려면 '섬 씬 다시 만들기'.
    ///  - 배치 숫자는 Level/IslandLayouts.cs 한 곳에 있다 (레벨 디자인 문서와 같은 표).
    /// 이 파일은 Unity 없이 컴파일해 보지 못했다. 처음 실행할 때 콘솔 오류가 있으면 알려 달라.
    /// </summary>
    public static class ProjectSetupTool
    {
        private const string MenuRoot = "AutoSurvival/프로젝트 기본 세팅/";
        private const string ScenesDir = "Assets/Scenes";
        private const string PrefabsDir = "Assets/Prefabs";
        private const string DataDir = "Assets/Data";

        private static readonly List<string> Log = new();
        private static Font _font;
        private static bool _fontIsKorean;

        // ─────────────────────────── 메뉴 ───────────────────────────

        [MenuItem(MenuRoot + "전체 실행 (데이터 + 프리팹 + 씬 3개 + 빌드 등록)", priority = 0)]
        public static void RunAll()
        {
            if (!EditorUtility.DisplayDialog("프로젝트 기본 세팅",
                    "다음을 만듭니다:\n- Assets/Data: PuzzleScenarioDB, OopiDialogueDB (없을 때만)\n- Assets/Prefabs: 퍼즐 프리팹 4개, 메트로놈 음표\n- Assets/Scenes: Island_Landing, Island_Foundry, Island_Core (이미 있으면 건너뜀)\n- Build Settings에 씬 등록\n\n현재 열린 씬에 저장하지 않은 변경이 있으면 먼저 저장하세요.",
                    "실행", "취소")) return;
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            Begin();
            var assets = PrepareAssets();
            foreach (var layout in IslandLayouts.All()) BuildScene(layout, assets, overwrite: false);
            BuildMainMenuScene(assets, overwrite: false);
            RegisterBuildScenes();
            Finish("프로젝트 기본 세팅");
        }

        /// <summary>대화상자 없이 전부 새로 만든다 (CI 서버의 배치 모드용: CiBuild가 호출). 데이터·프리팹은 없을 때만 만들고, 씬 4개는 덮어쓴다.</summary>
        public static void RunAllSilent()
        {
            Begin();
            var assets = PrepareAssets();
            foreach (var layout in IslandLayouts.All()) BuildScene(layout, assets, overwrite: true);
            BuildMainMenuScene(assets, overwrite: true);
            RegisterBuildScenes();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[AutoSurvival] 자동 세팅 완료 (CI)\n" + string.Join("\n", Log));
        }

        [MenuItem(MenuRoot + "섬 씬 3개 다시 만들기 (덮어쓰기)", priority = 20)]
        public static void RebuildScenes()
        {
            if (!EditorUtility.DisplayDialog("씬 다시 만들기",
                    "Island_Landing / Foundry / Core 씬 파일을 새로 만들어 덮어씁니다.\n씬에서 손으로 고친 내용은 사라집니다. 계속할까요?", "덮어쓰기", "취소")) return;
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            Begin();
            var assets = PrepareAssets();
            foreach (var layout in IslandLayouts.All()) BuildScene(layout, assets, overwrite: true);
            BuildMainMenuScene(assets, overwrite: true);
            RegisterBuildScenes();
            Finish("씬 다시 만들기");
        }

        [MenuItem(MenuRoot + "메인 메뉴 씬만 만들기 (MainMenu)", priority = 24)]
        public static void BuildMainMenuOnly()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            string path = $"{ScenesDir}/MainMenu.unity";
            if (File.Exists(path) && !EditorUtility.DisplayDialog("메인 메뉴 씬", "MainMenu 씬이 이미 있습니다. 덮어쓸까요?", "덮어쓰기", "취소")) return;

            Begin();
            var assets = PrepareAssets();
            BuildMainMenuScene(assets, overwrite: true);
            RegisterBuildScenes();
            Finish("메인 메뉴 씬");
        }

        [MenuItem(MenuRoot + "데이터 에셋 + 퍼즐 프리팹만 만들기", priority = 21)]
        public static void AssetsOnly()
        {
            Begin();
            PrepareAssets();
            Finish("데이터·프리팹 준비");
        }

        [MenuItem(MenuRoot + "현재 씬에 마스터 Canvas 추가", priority = 22)]
        public static void AddCanvasToCurrentScene()
        {
            Begin();
            var assets = PrepareAssets();
            var machines = Object.FindObjectsOfType<AutomationMachine>();
            var companion = Object.FindObjectOfType<OopiCompanion>();
            if (companion == null) Warn("현재 씬에 OopiCompanion이 없어 DialogueUI의 companion 슬롯이 비어 있습니다.");
            BuildMasterCanvas(companion, machines);
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            Finish("마스터 Canvas 추가");
        }

        [MenuItem(MenuRoot + "현재 씬 기계 ID 자동 부여/중복 수정", priority = 23)]
        public static void FixMachineIds()
        {
            Begin();
            var scene = SceneManager.GetActiveScene();
            var layout = IslandLayouts.All().FirstOrDefault(l => l.sceneName == scene.name);
            string prefix = layout != null ? layout.shortName : scene.name.ToLowerInvariant().Replace("island_", "");

            var machines = Object.FindObjectsOfType<AutomationMachine>().OrderBy(m => m.transform.GetSiblingIndex()).ThenBy(m => m.name).ToArray();
            var used = new HashSet<string>();
            var perKind = new Dictionary<MachineKind, int>();
            int changed = 0;

            foreach (var m in machines)
            {
                string id = m.MachineId;
                if (string.IsNullOrEmpty(id) || !used.Add(id))
                {
                    string fresh;
                    do
                    {
                        perKind.TryGetValue(m.Kind, out int n);
                        perKind[m.Kind] = ++n;
                        fresh = IslandLayouts.MachineId(prefix, m.Kind, n);
                    } while (used.Contains(fresh));
                    var so = new SerializedObject(m);
                    SetString(so, "machineId", fresh);
                    so.ApplyModifiedProperties();
                    used.Add(fresh);
                    Info($"{m.name}: machineId = {fresh}");
                    changed++;
                }
            }
            if (changed > 0) EditorSceneManager.MarkSceneDirty(scene);
            Info($"기계 {machines.Length}대 검사, {changed}대의 ID를 새로 부여했습니다.");
            Finish("기계 ID 점검");
        }

        [MenuItem(MenuRoot + "배치 규칙 점검 (IslandLayouts)", priority = 40)]
        public static void CheckLayouts()
        {
            Begin();
            var catalog = IslandCatalog.Default();
            int issues = 0;
            foreach (var l in IslandLayouts.All())
            {
                var list = IslandLayouts.Validate(l, catalog);
                issues += list.Count;
                foreach (var i in list) Warn(i);
                Info($"{l.sceneName}: 기계 {l.machines.Count}대, 자원 구역 {l.zones.Count}곳 - 위반 {list.Count}건");
            }
            Finish(issues == 0 ? "배치 규칙 점검: 통과" : $"배치 규칙 점검: {issues}건");
        }

        // ─────────────────────────── 보고 ───────────────────────────

        private static void Begin() { Log.Clear(); _font = null; }
        private static void Info(string m) => Log.Add(m);
        private static void Warn(string m) => Log.Add("[주의] " + m);

        private static void Finish(string title)
        {
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            string all = string.Join("\n", Log);
            Debug.Log($"[AutoSurvival] {title}\n{all}");
            int warns = Log.Count(l => l.StartsWith("[주의]"));
            string preview = string.Join("\n", Log.Take(18));
            if (Log.Count > 18) preview += $"\n... (전체 {Log.Count}줄은 Console 창 참고)";
            EditorUtility.DisplayDialog(title, $"완료. 주의 {warns}건\n\n{preview}", "확인");
        }

        // ─────────────────────────── 에셋 준비 ───────────────────────────

        private sealed class SetupAssets
        {
            public PuzzleScenarioDB scenarioDb;
            public OopiDialogueDB oopiDb;
            public FeedbackProfile feedbackProfile;
            public PuzzleSession pipe, ladder, hydraulic, pneumatic;
            public RectTransform notePrefab;
        }

        private static SetupAssets PrepareAssets()
        {
            EnsureFolder(DataDir); EnsureFolder(PrefabsDir + "/Puzzles"); EnsureFolder(PrefabsDir + "/UI"); EnsureFolder(ScenesDir);
            var a = new SetupAssets();

            // 데이터 에셋: 새로 만들었거나 비어 있을 때만 기본 데이터를 채운다 (가져온 기출을 지우지 않는다)
            a.scenarioDb = LoadOrCreate<PuzzleScenarioDB>(DataDir + "/PuzzleScenarioDB.asset", out bool newScenario);
            if (newScenario || a.scenarioDb.Questions.Count == 0) { a.scenarioDb.SeedDefaults(); EditorUtility.SetDirty(a.scenarioDb); Info("PuzzleScenarioDB: 기본 문제·시나리오를 채웠습니다."); }
            a.oopiDb = LoadOrCreate<OopiDialogueDB>(DataDir + "/OopiDialogueDB.asset", out bool newOopi);
            if (newOopi || a.oopiDb.Lines.Count == 0) { a.oopiDb.SeedDefaults(); EditorUtility.SetDirty(a.oopiDb); Info($"OopiDialogueDB: 기본 대사 {a.oopiDb.Lines.Count}개를 채웠습니다."); }

            // 퍼즐 프리팹: 컴포넌트만 붙은 뼈대 (배관은 Factory Kit 조각을 이름으로 찾아 연결 시도)
            a.pipe = EnsurePuzzlePrefab<PipePuzzle>("PipePuzzle", go =>
            {
                var so = new SerializedObject(go.GetComponent<PipePuzzle>());
                SetModelRef(so, "straightPrefab", "pipe-large");
                SetModelRef(so, "elbowPrefab", "pipe-large-bend");
                SetModelRef(so, "teePrefab", "pipe-large-junction");
                SetModelRef(so, "crossPrefab", "pipe-large-cross");
                SetModelRef(so, "sourcePrefab", "pipe-large-valve", "machine-connection-pipe");
                SetModelRef(so, "sinkPrefab", "piston-round");
                so.ApplyModifiedPropertiesWithoutUndo();
            });
            a.ladder = EnsurePuzzlePrefab<LadderPuzzle>("LadderPuzzle", null);
            a.hydraulic = EnsurePuzzlePrefab<HydraulicPuzzle>("HydraulicPuzzle", null);
            a.pneumatic = EnsurePuzzlePrefab<PneumaticPuzzle>("PneumaticPuzzle", null);

            // 연출 데이터: 새로 만들었거나 비어 있을 때만 기본 항목(큐 40여 개)을 채운다. 사용자가 넣은 파티클·소리는 건드리지 않는다.
            a.feedbackProfile = LoadOrCreate<FeedbackProfile>(DataDir + "/FeedbackProfile.asset", out bool newFeedback);
            int seeded = a.feedbackProfile.SeedDefaults();
            if (newFeedback || seeded > 0) { EditorUtility.SetDirty(a.feedbackProfile); Info($"FeedbackProfile: 기본 큐 {seeded}개를 채웠습니다 (파티클·효과음은 직접 끼워 넣으세요)."); }

            a.notePrefab = EnsureNotePrefab();
            return a;
        }

        private static T LoadOrCreate<T>(string path, out bool created) where T : ScriptableObject
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing != null) { created = false; return existing; }
            var inst = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(inst, path);
            created = true;
            return inst;
        }

        private static PuzzleSession EnsurePuzzlePrefab<T>(string name, Action<GameObject> configure) where T : PuzzleSession
        {
            string path = $"{PrefabsDir}/Puzzles/{name}.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null) { var c = existing.GetComponent<T>(); if (c != null) return c; }

            var go = new GameObject(name);
            go.AddComponent<T>();
            configure?.Invoke(go);
            var saved = PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
            Info($"퍼즐 프리팹 생성: {path} (컴포넌트만 붙은 뼈대)");
            return saved.GetComponent<T>();
        }

        private static RectTransform EnsureNotePrefab()
        {
            string path = PrefabsDir + "/UI/MetronomeNote.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null) return existing.GetComponent<RectTransform>();

            var go = new GameObject("MetronomeNote", typeof(RectTransform), typeof(Image));
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(18f, 44f);
            var img = go.GetComponent<Image>();
            img.color = new Color(1f, 0.85f, 0.3f);
            img.raycastTarget = false;
            var saved = PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
            return saved.GetComponent<RectTransform>();
        }

        // ─────────────────────────── 씬 만들기 ───────────────────────────

        private static void BuildScene(IslandLayout layout, SetupAssets assets, bool overwrite)
        {
            string path = $"{ScenesDir}/{layout.sceneName}.unity";
            if (!overwrite && File.Exists(path)) { Info($"{layout.sceneName}: 이미 있어 건너뜀"); return; }

            var catalog = IslandCatalog.Default();
            foreach (var issue in IslandLayouts.Validate(layout, catalog)) Warn(issue);
            var def = catalog.First(d => d.id == layout.islandId);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            // 바닥과 분위기
            BuildGround(layout);
            ApplyAtmosphere(layout);

            // 시스템 오브젝트 (GameManager는 첫 씬 것이 살아남고, 다른 씬의 것은 스스로 사라진다)
            var systems = new GameObject("_Systems");
            systems.AddComponent<GameManager>();
            systems.AddComponent<KnowledgeTracker>();
            var companion = systems.AddComponent<OopiCompanion>();
            var companionSo = new SerializedObject(companion);
            SetRef(companionSo, "db", assets.oopiDb);
            companionSo.ApplyModifiedPropertiesWithoutUndo();

            AddCoreManagers(systems, assets);

            BuildPlayer(layout);
            BuildBasecamp(layout);
            var machines = BuildMachines(layout, assets);
            BuildResourceZones(layout);
            BuildLandmarks(layout);
            if (layout.shortName == "core") AddMachineLights(machines);

            BuildMasterCanvas(companion, machines);

            EditorSceneManager.SaveScene(scene, path);
            Info($"{layout.sceneName}: 생성 완료 (기계 {machines.Length}대: {string.Join(", ", machines.Select(m => m.MachineId))})");
        }

        private static void BuildGround(IslandLayout l)
        {
            Color groundColor = l.shortName == "landing" ? new Color(0.86f, 0.80f, 0.60f)
                              : l.shortName == "foundry" ? new Color(0.32f, 0.30f, 0.30f)
                              : new Color(0.20f, 0.20f, 0.24f);

            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.localScale = new Vector3(l.halfX * 2f / 10f, 1f, l.halfZ * 2f / 10f);
            Tint(ground, groundColor);

            if (l.shortName == "landing")
            {
                var sea = GameObject.CreatePrimitive(PrimitiveType.Plane);
                sea.name = "Sea";
                sea.transform.position = new Vector3(0f, -0.35f, 0f);
                sea.transform.localScale = new Vector3(40f, 1f, 40f);
                Tint(sea, new Color(0.15f, 0.45f, 0.75f));
                Object.DestroyImmediate(sea.GetComponent<Collider>());      // 바다는 보이기만 한다 (낙사 방지 벽은 나중에)
            }
        }

        private static void ApplyAtmosphere(IslandLayout l)
        {
            var light = Object.FindObjectOfType<Light>();
            if (l.shortName == "foundry")
            {
                RenderSettings.ambientLight = new Color(0.30f, 0.27f, 0.25f);
                if (light != null) { light.color = new Color(1f, 0.78f, 0.55f); light.intensity = 0.9f; }
            }
            else if (l.shortName == "core")
            {
                RenderSettings.ambientLight = new Color(0.12f, 0.12f, 0.16f);
                if (light != null) { light.color = new Color(0.6f, 0.7f, 1f); light.intensity = 0.25f; }
            }
        }

        private static void BuildPlayer(IslandLayout l)
        {
            var player = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            player.name = "Player";
            player.tag = "Player";
            Object.DestroyImmediate(player.GetComponent<CapsuleCollider>());
            player.GetComponent<MeshRenderer>().enabled = false;           // 1인칭이라 몸은 보이지 않게

            var cc = player.AddComponent<CharacterController>();
            cc.height = 2f; cc.radius = 0.4f; cc.center = new Vector3(0f, 1f, 0f);

            player.AddComponent<SurvivalStats>();
            player.AddComponent<Inventory>();
            var interactor = player.AddComponent<PlayerInteractor>();
            var controller = player.AddComponent<SimpleFirstPersonController>();

            // 기본 카메라를 플레이어 머리 높이로 옮긴다
            var cam = Camera.main != null ? Camera.main : Object.FindObjectOfType<Camera>();
            if (cam == null) { var camGo = new GameObject("Main Camera", typeof(Camera)); camGo.tag = "MainCamera"; cam = camGo.GetComponent<Camera>(); }
            // 계층: Player > Head(마우스 상하 회전) > ShakePivot(CameraShaker, 흔들림 전용) > Camera
            // 흔들림과 시점 회전이 서로 다른 Transform을 쓰므로 충돌하지 않는다.
            var head = new GameObject("Head").transform;
            head.SetParent(player.transform, false);
            head.localPosition = new Vector3(0f, 1.6f, 0f);
            var shakePivot = new GameObject("ShakePivot");
            shakePivot.transform.SetParent(head, false);
            shakePivot.AddComponent<CameraShaker>();
            cam.transform.SetParent(shakePivot.transform, false);
            cam.transform.localPosition = Vector3.zero;
            cam.transform.localRotation = Quaternion.identity;

            var controllerSo = new SerializedObject(controller);
            SetRef(controllerSo, "cameraPivot", head);
            controllerSo.ApplyModifiedPropertiesWithoutUndo();

            var interactorSo = new SerializedObject(interactor);
            SetRef(interactorSo, "playerCamera", cam);
            SetRef(interactorSo, "movementController", controller);
            interactorSo.ApplyModifiedPropertiesWithoutUndo();

            // 스폰: 베이스캠프를 등지고 첫 기계 쪽을 본다
            player.transform.position = new Vector3(l.spawnX, 0.05f, l.spawnZ);
            var look = l.machines.Count > 0 ? new Vector3(l.machines[0].x, 0f, l.machines[0].z) : new Vector3(l.campX, 0f, l.campZ + 10f);
            player.transform.rotation = Quaternion.LookRotation(new Vector3(look.x - l.spawnX, 0f, look.z - l.spawnZ), Vector3.up);
        }

        private static void BuildBasecamp(IslandLayout l)
        {
            var root = new GameObject("Basecamp");
            root.transform.position = new Vector3(l.campX, 0f, l.campZ);
            var marker = root.AddComponent<LevelMarker>();
            marker.kind = LevelMarkerKind.Basecamp; marker.label = "베이스캠프"; marker.radius = 6f;

            // 모닥불: 따뜻함 구역 (트리거 콜라이더는 직접 붙인다: Reset은 스크립트로 AddComponent할 때 호출되지 않는다)
            var fire = FindModel(new[] { "campfire-pit" }) != null ? InstantiateModel(new[] { "campfire-pit" }, root.transform) : GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            fire.name = "Campfire";
            fire.transform.SetParent(root.transform, false);
            fire.transform.localPosition = Vector3.zero;
            if (fire.GetComponent<MeshRenderer>() != null) { fire.transform.localScale = new Vector3(1.2f, 0.2f, 1.2f); Tint(fire, new Color(0.9f, 0.4f, 0.1f)); }
            foreach (var c in fire.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
            var sphere = fire.AddComponent<SphereCollider>();
            sphere.isTrigger = true; sphere.radius = 4f;
            fire.AddComponent<WarmthZone>();
            fire.AddComponent<Campfire>();                       // [E]로 나무를 넣어 불 붙이기 (처음에는 꺼져 있다)

            // 상호작용용 콜라이더: 불의 온기 영역(트리거, 반경 4m) 안에서도 레이가 맞도록 작은 실체 콜라이더를 따로 둔다
            var pit = new GameObject("InteractCollider");
            pit.transform.SetParent(fire.transform, false);
            var pitBox = pit.AddComponent<BoxCollider>();
            pitBox.size = new Vector3(1.4f, 1.0f, 1.4f); pitBox.center = new Vector3(0f, 0.4f, 0f);

            // 텐트(휴식 자리표시)와 보관함 자리
            var tent = FindModel(new[] { "tent" }) != null ? InstantiateModel(new[] { "tent" }, root.transform) : GameObject.CreatePrimitive(PrimitiveType.Cube);
            tent.name = "Tent";
            tent.transform.SetParent(root.transform, false);
            tent.transform.localPosition = new Vector3(3f, 1f, 1.5f);
            if (tent.GetComponent<MeshRenderer>() != null) { tent.transform.localScale = new Vector3(3f, 2f, 3f); Tint(tent, new Color(0.55f, 0.65f, 0.4f)); }

            var spawn = new GameObject("PlayerSpawn");
            spawn.transform.position = new Vector3(l.spawnX, 0f, l.spawnZ);
            var sm = spawn.AddComponent<LevelMarker>();
            sm.kind = LevelMarkerKind.PlayerSpawn; sm.label = "스폰"; sm.radius = 1.5f;
        }

        private static AutomationMachine[] BuildMachines(IslandLayout l, SetupAssets assets)
        {
            var root = new GameObject("Machines");
            var ids = IslandLayouts.MachineIds(l);
            var result = new List<AutomationMachine>();

            for (int i = 0; i < l.machines.Count; i++)
            {
                var spot = l.machines[i];
                var go = BuildMachineVisual(spot.kind);
                go.name = $"{spot.kind}_{i + 1:00}";
                go.transform.SetParent(root.transform, false);
                go.transform.position = new Vector3(spot.x, 0f, spot.z);
                go.transform.rotation = Quaternion.LookRotation(new Vector3(l.campX - spot.x, 0f, l.campZ - spot.z), Vector3.up);

                var anchor = new GameObject("PuzzleAnchor");
                anchor.transform.SetParent(go.transform, false);
                anchor.transform.localPosition = new Vector3(0f, 2.5f, 0f);

                var machine = go.AddComponent<AutomationMachine>();
                var so = new SerializedObject(machine);
                SetString(so, "machineId", ids[i]);
                SetEnum(so, "kind", (int)spot.kind);
                SetInt(so, "tier", spot.tier);
                SetEnum(so, "state", (int)MachineState.Broken);
                SetRef(so, "puzzleAnchor", anchor.transform);
                SetRef(so, "scenarioDb", assets.scenarioDb);
                SetRef(so, "pipePuzzlePrefab", assets.pipe);
                SetRef(so, "ladderPuzzlePrefab", assets.ladder);
                SetRef(so, "hydraulicPuzzlePrefab", assets.hydraulic);
                SetRef(so, "pneumaticPuzzlePrefab", assets.pneumatic);
                so.ApplyModifiedPropertiesWithoutUndo();
                result.Add(machine);
            }
            return result.ToArray();
        }

        private static readonly Dictionary<MachineKind, string[]> MachineModelNames = new()
        {
            { MachineKind.WaterPurifier, new[] { "machine-tank", "tank", "pipe-glass-large" } },
            { MachineKind.Smelter, new[] { "machine-furnace", "furnace", "machine-large" } },
            { MachineKind.PowerGenerator, new[] { "machine-generator", "generator", "machine-medium" } },
            { MachineKind.AutoGarden, new[] { "machine-window", "machine-small" } },
            { MachineKind.CraneLift, new[] { "crane-lift", "crane" } },
        };

        private static GameObject BuildMachineVisual(MachineKind kind)
        {
            var root = new GameObject("Machine");

            // 1) Kenney 모델을 이름으로 찾아 보면 그걸 쓰고, 2) 없으면 색 상자 + 영문 이름표 (상호작용은 둘 다 같다)
            var model = InstantiateModel(MachineModelNames[kind], root.transform);
            if (model != null)
            {
                model.name = "Model";
                model.transform.localScale = Vector3.one * 2.5f;
                FitBoxCollider(root);
            }
            else
            {
                var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
                body.name = "Placeholder";
                body.transform.SetParent(root.transform, false);
                body.transform.localPosition = new Vector3(0f, 1.5f, 0f);
                body.transform.localScale = new Vector3(3f, 3f, 3f);
                Tint(body, MachineColor(kind));
                var bodyCollider = body.GetComponent<Collider>();
                if (bodyCollider != null) Object.DestroyImmediate(bodyCollider);
                var box = root.AddComponent<BoxCollider>();
                box.center = new Vector3(0f, 1.5f, 0f);
                box.size = new Vector3(3f, 3f, 3f);

                var label = new GameObject("Label", typeof(TextMesh));
                label.transform.SetParent(root.transform, false);
                label.transform.localPosition = new Vector3(0f, 3.6f, 0f);
                var tm = label.GetComponent<TextMesh>();
                tm.text = kind.ToString();
                tm.characterSize = 0.25f; tm.fontSize = 48; tm.anchor = TextAnchor.MiddleCenter; tm.color = Color.white;
            }
            return root;
        }

        private static Color MachineColor(MachineKind k) => k switch
        {
            MachineKind.WaterPurifier => new Color(0.30f, 0.60f, 0.90f),
            MachineKind.Smelter => new Color(0.90f, 0.45f, 0.20f),
            MachineKind.PowerGenerator => new Color(0.95f, 0.85f, 0.25f),
            MachineKind.AutoGarden => new Color(0.40f, 0.80f, 0.40f),
            _ => new Color(0.70f, 0.70f, 0.75f),
        };

        private static void FitBoxCollider(GameObject root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) { root.AddComponent<BoxCollider>(); return; }
            var b = renderers[0].bounds;
            foreach (var r in renderers) b.Encapsulate(r.bounds);
            var box = root.AddComponent<BoxCollider>();
            box.center = root.transform.InverseTransformPoint(b.center);
            box.size = b.size;
        }

        private static void AddMachineLights(AutomationMachine[] machines)
        {
            foreach (var m in machines)
            {
                var go = new GameObject("MachineLight");
                go.transform.SetParent(m.transform, false);
                go.transform.localPosition = new Vector3(0f, 5f, 0f);
                var light = go.AddComponent<Light>();
                light.type = LightType.Point; light.range = 18f; light.intensity = 2f; light.color = new Color(1f, 0.8f, 0.55f);
            }
        }

        private static void BuildResourceZones(IslandLayout l)
        {
            var root = new GameObject("Resources");
            foreach (var z in l.zones)
            {
                var zoneGo = new GameObject($"Zone_{z.kind}_{z.label}");
                zoneGo.transform.SetParent(root.transform, false);
                zoneGo.transform.position = new Vector3(z.x, 0f, z.z);
                var marker = zoneGo.AddComponent<LevelMarker>();
                marker.kind = z.kind; marker.label = $"{z.label} ({z.kind} ×{z.count})"; marker.radius = z.radius;
                marker.nodeCount = z.count; marker.amountPerNode = z.amount;

                var rng = new System.Random(z.label.GetHashCode() ^ (int)(z.x * 31f + z.z * 17f));
                var placed = new List<Vector2>();
                for (int n = 0; n < z.count; n++)
                {
                    Vector2 p = default;
                    for (int tries = 0; tries < 40; tries++)       // 서로 2m 이상 떨어지도록 뽑는다
                    {
                        double ang = rng.NextDouble() * Math.PI * 2.0;
                        double rad = Math.Sqrt(rng.NextDouble()) * (z.radius - 0.8f);
                        p = new Vector2((float)(Math.Cos(ang) * rad), (float)(Math.Sin(ang) * rad));
                        if (placed.All(q => (q - p).sqrMagnitude >= 4f)) break;
                    }
                    placed.Add(p);

                    var node = BuildResourceVisual(z.kind);
                    node.name = $"{z.kind}_{n + 1:00}";
                    node.transform.SetParent(zoneGo.transform, false);
                    node.transform.localPosition = new Vector3(p.x, 0f, p.y);

                    var rn = node.AddComponent<ResourceNode>();
                    var so = new SerializedObject(rn);
                    SetEnum(so, "kind", (int)ToResourceKind(z.kind));
                    SetInt(so, "amount", z.amount);
                    so.ApplyModifiedPropertiesWithoutUndo();
                }
            }
        }

        private static ResourceKind ToResourceKind(LevelMarkerKind k) => k switch
        {
            LevelMarkerKind.Wood => ResourceKind.Wood,
            LevelMarkerKind.Stone => ResourceKind.Stone,
            LevelMarkerKind.MetalScrap => ResourceKind.MetalPanel,
            LevelMarkerKind.Water => ResourceKind.Water,
            _ => ResourceKind.Food,
        };

        private static readonly Dictionary<LevelMarkerKind, string[]> ResourceModelNames = new()
        {
            { LevelMarkerKind.Wood, new[] { "resource-wood", "tree", "log" } },
            { LevelMarkerKind.Stone, new[] { "resource-stone", "rock-a", "rock" } },
            { LevelMarkerKind.MetalScrap, new[] { "metal-panel", "box-large" } },
            { LevelMarkerKind.Food, new[] { "fish", "carrot", "box-small" } },
            { LevelMarkerKind.Water, new[] { "barrel", "bucket" } },
        };

        private static GameObject BuildResourceVisual(LevelMarkerKind kind)
        {
            var root = new GameObject("Node");
            var model = InstantiateModel(ResourceModelNames[kind], root.transform);
            if (model != null)
            {
                model.name = "Model";
                FitBoxCollider(root);
                return root;
            }

            PrimitiveType shape; Vector3 scale; Color color; float y;
            switch (kind)
            {
                case LevelMarkerKind.Wood: shape = PrimitiveType.Cylinder; scale = new Vector3(0.5f, 1.0f, 0.5f); color = new Color(0.50f, 0.32f, 0.15f); y = 1.0f; break;
                case LevelMarkerKind.Stone: shape = PrimitiveType.Sphere; scale = new Vector3(1.0f, 0.8f, 1.0f); color = new Color(0.60f, 0.60f, 0.64f); y = 0.4f; break;
                case LevelMarkerKind.MetalScrap: shape = PrimitiveType.Cube; scale = new Vector3(1.0f, 0.3f, 0.7f); color = new Color(0.55f, 0.75f, 0.90f); y = 0.15f; break;
                case LevelMarkerKind.Water: shape = PrimitiveType.Cylinder; scale = new Vector3(0.6f, 0.6f, 0.6f); color = new Color(0.25f, 0.55f, 1.00f); y = 0.6f; break;
                default: shape = PrimitiveType.Sphere; scale = new Vector3(0.6f, 0.6f, 0.6f); color = new Color(0.95f, 0.50f, 0.25f); y = 0.3f; break;
            }
            var prim = GameObject.CreatePrimitive(shape);
            prim.name = "Placeholder";
            prim.transform.SetParent(root.transform, false);
            prim.transform.localPosition = new Vector3(0f, y, 0f);
            prim.transform.localScale = scale;
            Tint(prim, color);
            return root;
        }

        private static void BuildLandmarks(IslandLayout l)
        {
            var root = new GameObject("Landmarks");
            foreach (var lm in l.landmarks)
            {
                var go = new GameObject($"Landmark_{lm.label}");
                go.transform.SetParent(root.transform, false);
                go.transform.position = new Vector3(lm.x, 0f, lm.z);
                var m = go.AddComponent<LevelMarker>();
                m.kind = LevelMarkerKind.Landmark; m.label = lm.label; m.radius = 4f;
            }
        }

        // ─────────────────────────── Build Settings ───────────────────────────

        private static void RegisterBuildScenes()
        {
            var list = EditorBuildSettings.scenes.ToList();
            int added = 0;
            foreach (var layout in IslandLayouts.All())
            {
                string path = $"{ScenesDir}/{layout.sceneName}.unity";
                if (!File.Exists(path)) continue;
                int idx = list.FindIndex(s => s.path == path);
                if (idx >= 0) { list[idx] = new EditorBuildSettingsScene(path, true); continue; }
                list.Add(new EditorBuildSettingsScene(path, true));
                added++;
            }
            // MainMenu는 항상 첫 씬 (게임을 켜면 타이틀부터). 메뉴 씬이 없으면 섬 씬이 그대로 첫 씬.
            string menuPath = $"{ScenesDir}/MainMenu.unity";
            if (File.Exists(menuPath))
            {
                list.RemoveAll(sc => sc.path == menuPath);
                list.Insert(0, new EditorBuildSettingsScene(menuPath, true));
            }
            EditorBuildSettings.scenes = list.ToArray();
            Info($"Build Settings: 씬 {added}개 추가 (MainMenu가 0번이면 정상, 없으면 Island_Landing이 첫 씬이 되도록 순서를 확인하세요)");
        }

        // ─────────────────────────── 마스터 Canvas ───────────────────────────

        private static GameObject BuildMasterCanvas(OopiCompanion companion, AutomationMachine[] machines)
        {
            foreach (var old in Object.FindObjectsOfType<Canvas>().Where(c => c.name == "MasterCanvas")) Object.DestroyImmediate(old.gameObject);

            var canvasGo = new GameObject("MasterCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            var t = canvasGo.transform;
            // 아래에 있는 것이 먼저 그려진다: HUD -> 말풍선 -> 퍼즐 UI -> 큰 창 순서
            var saveSlots = BuildSaveSlotUIObject(t);                    // HUD가 참조하므로 먼저 만들되 형제 순서는 아래에서 정리
            BuildHud(t, saveSlots);
            BuildDialogueUI(t, companion);
            BuildMetronomeUI(t);
            BuildFieldNoteUI(t, machines);
            BuildIslandMapUI(t);
            BuildObjectiveUI(t);
            var settingsPanel = BuildSettingsPanel(t, inMenu: false);
            saveSlots.transform.SetAsLastSibling();                       // 저장 창은 맨 위
            settingsPanel.transform.SetAsLastSibling();

            if (Object.FindObjectOfType<EventSystem>() == null)
                new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));

            if (!_fontIsKorean)
                Warn("한글을 표시할 글꼴(Noto Sans KR, Nanum 등)을 프로젝트에서 찾지 못해 기본 글꼴을 썼습니다. 한글이 □로 보이면 MasterCanvas의 모든 Text에 한글 글꼴을 지정하세요.");
            return canvasGo;
        }

        // ── HUD ──

        private static void BuildHud(Transform canvas, SaveSlotUI saveSlots)
        {
            var root = UIRect("HudUI", canvas); Stretch(root);
            var hud = root.gameObject.AddComponent<HudUI>();

            var prompt = Label("PromptText", root, "", 30, TextAnchor.MiddleCenter, Color.white);
            Place(prompt.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -120f), new Vector2(900f, 50f));
            var toast = Label("ToastText", root, "", 30, TextAnchor.MiddleCenter, new Color(1f, 0.92f, 0.5f));
            Place(toast.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -90f), new Vector2(1000f, 50f));
            var stats = Label("StatsText", root, "", 24, TextAnchor.UpperLeft, Color.white);
            Place(stats.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(30f, -30f), new Vector2(900f, 80f));
            var hint = Label("KeyHintText", root, "M 섬 지도   J 목표   F10 설정   F5 저장   F9 불러오기   E 상호작용", 22, TextAnchor.LowerRight, new Color(1f, 1f, 1f, 0.6f));
            Place(hint.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-30f, 20f), new Vector2(900f, 40f));

            var so = new SerializedObject(hud);
            SetRef(so, "promptText", prompt); SetRef(so, "toastText", toast); SetRef(so, "statsText", stats); SetRef(so, "saveSlots", saveSlots);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ── 오피 말풍선 ──

        private static void BuildDialogueUI(Transform canvas, OopiCompanion companion)
        {
            var container = UIRect("DialogueUI", canvas); Stretch(container);
            var ui = container.gameObject.AddComponent<DialogueUI>();

            var panel = Panel("Panel", container, new Color(0.08f, 0.10f, 0.14f, 0.88f));
            Place(panel.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 60f), new Vector2(1100f, 190f));
            var group = panel.gameObject.AddComponent<CanvasGroup>();
            var skip = panel.gameObject.AddComponent<Button>();
            skip.targetGraphic = panel;

            var portrait = Panel("Portrait", panel.rectTransform, new Color(0.3f, 0.6f, 0.9f));
            Place(portrait.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(25f, 0f), new Vector2(140f, 140f));
            portrait.raycastTarget = false;
            var line = Label("LineText", panel.rectTransform, "", 30, TextAnchor.UpperLeft, Color.white);
            Place(line.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(190f, -20f), new Vector2(880f, 100f));
            var tip = Label("TipText", panel.rectTransform, "", 22, TextAnchor.UpperLeft, new Color(0.65f, 0.9f, 1f));
            Place(tip.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(190f, 15f), new Vector2(880f, 60f));
            var skipHint = Label("SkipHint", panel.rectTransform, "Tab / 클릭: 넘기기", 18, TextAnchor.LowerRight, new Color(1f, 1f, 1f, 0.45f));
            Place(skipHint.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-15f, 8f), new Vector2(300f, 26f));

            var so = new SerializedObject(ui);
            SetRef(so, "companion", companion);
            SetRef(so, "root", panel.gameObject);
            SetRef(so, "group", group);
            SetRef(so, "lineText", line);
            SetRef(so, "tipText", tip);
            SetRef(so, "portrait", portrait);
            SetRef(so, "skipButton", skip);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ── 메트로놈 ──

        private static void BuildMetronomeUI(Transform canvas)
        {
            var container = UIRect("MetronomeUI", canvas); Stretch(container);
            var ui = container.gameObject.AddComponent<MetronomeUI>();

            var panel = Panel("Panel", container, new Color(0.05f, 0.07f, 0.10f, 0.80f));
            Place(panel.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 290f), new Vector2(1100f, 260f));
            panel.raycastTarget = false;

            var hint = Label("HintText", panel.rectTransform, "", 26, TextAnchor.MiddleCenter, Color.white);
            Place(hint.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -20f), new Vector2(1000f, 40f));

            var track = Panel("Track", panel.rectTransform, new Color(0.15f, 0.18f, 0.24f, 1f));
            Place(track.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 10f), new Vector2(1000f, 70f));
            track.raycastTarget = false;
            var good = Panel("GoodZone", track.rectTransform, new Color(0.95f, 0.85f, 0.35f, 0.30f));
            Place(good.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(120f, 70f)); good.raycastTarget = false;
            var perfect = Panel("PerfectZone", track.rectTransform, new Color(0.4f, 0.9f, 0.5f, 0.35f));
            Place(perfect.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(60f, 70f)); perfect.raycastTarget = false;
            var hitLine = Panel("HitLine", track.rectTransform, new Color(1f, 1f, 1f, 0.6f));
            Place(hitLine.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(4f, 90f)); hitLine.raycastTarget = false;

            var grade = Label("GradeText", panel.rectTransform, "", 40, TextAnchor.MiddleCenter, Color.white);
            Place(grade.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -55f), new Vector2(600f, 50f));
            var stamina = MakeSlider("StaminaBar", panel.rectTransform, new Vector2(480f, 18f), new Color(0.4f, 0.8f, 0.9f));
            Place((RectTransform)stamina.transform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(40f, 28f), new Vector2(480f, 18f));
            var progress = MakeSlider("ProgressBar", panel.rectTransform, new Vector2(480f, 18f), new Color(0.95f, 0.75f, 0.25f));
            Place((RectTransform)progress.transform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-40f, 28f), new Vector2(480f, 18f));

            var so = new SerializedObject(ui);
            SetRef(so, "root", panel.gameObject);
            SetRef(so, "track", track.rectTransform);
            SetRef(so, "hitLine", hitLine);
            SetRef(so, "perfectZone", perfect.rectTransform);
            SetRef(so, "goodZone", good.rectTransform);
            SetRef(so, "notePrefab", AssetDatabase.LoadAssetAtPath<GameObject>(PrefabsDir + "/UI/MetronomeNote.prefab")?.GetComponent<RectTransform>());
            SetRef(so, "gradeText", grade);
            SetRef(so, "hintText", hint);
            SetRef(so, "staminaBar", stamina);
            SetRef(so, "progressBar", progress);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ── 현장 노트 ──

        private static void BuildFieldNoteUI(Transform canvas, AutomationMachine[] machines)
        {
            var container = UIRect("FieldNoteUI", canvas); Stretch(container);
            var ui = container.gameObject.AddComponent<FieldNoteUI>();

            var panel = Panel("Panel", container, new Color(0.07f, 0.09f, 0.13f, 0.95f));
            Place(panel.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1200f, 760f));

            var progress = Label("ProgressText", panel.rectTransform, "현장 노트", 24, TextAnchor.UpperLeft, new Color(0.65f, 0.9f, 1f));
            Place(progress.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(30f, -20f), new Vector2(500f, 34f));
            var source = Label("SourceText", panel.rectTransform, "", 22, TextAnchor.UpperRight, new Color(1f, 1f, 1f, 0.6f));
            Place(source.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-30f, -20f), new Vector2(500f, 34f));
            var opie = Label("OpieLineText", panel.rectTransform, "", 28, TextAnchor.UpperLeft, new Color(1f, 0.92f, 0.5f));
            Place(opie.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -65f), new Vector2(1140f, 90f));
            var question = Label("QuestionText", panel.rectTransform, "", 30, TextAnchor.UpperLeft, Color.white);
            Place(question.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -170f), new Vector2(1140f, 130f));

            var choices = new Button[4];
            for (int i = 0; i < 4; i++)
            {
                choices[i] = MakeButton($"Choice{i + 1}", panel.rectTransform, $"{i + 1}", new Vector2(1140f, 64f), new Color(0.20f, 0.25f, 0.34f));
                Place((RectTransform)choices[i].transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -330f - i * 74f), new Vector2(1140f, 64f));
                choices[i].GetComponentInChildren<Text>().alignment = TextAnchor.MiddleLeft;
            }
            var result = Label("ResultText", panel.rectTransform, "", 24, TextAnchor.UpperLeft, Color.white);
            Place(result.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 75f), new Vector2(1140f, 110f));
            var close = MakeButton("CloseButton", panel.rectTransform, "닫기", new Vector2(160f, 50f), new Color(0.35f, 0.35f, 0.40f));
            Place((RectTransform)close.transform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-30f, 18f), new Vector2(160f, 50f));

            var so = new SerializedObject(ui);
            SetRef(so, "root", panel.gameObject);
            SetRef(so, "opieLineText", opie); SetRef(so, "sourceText", source); SetRef(so, "questionText", question);
            SetRef(so, "resultText", result); SetRef(so, "progressText", progress);
            SetRefArray(so, "choiceButtons", choices.Cast<Object>().ToArray());
            SetRef(so, "closeButton", close);
            SetRefArray(so, "machines", machines.Cast<Object>().ToArray());
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ── 저장 슬롯 ──

        private static SaveSlotUI BuildSaveSlotUIObject(Transform canvas)
        {
            var container = UIRect("SaveSlotUI", canvas); Stretch(container);
            var ui = container.gameObject.AddComponent<SaveSlotUI>();

            var panel = Panel("Panel", container, new Color(0.05f, 0.06f, 0.09f, 0.97f));
            Place(panel.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1100f, 800f));

            var title = Label("ModeTitle", panel.rectTransform, "저장하기", 40, TextAnchor.MiddleLeft, Color.white);
            Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(40f, -25f), new Vector2(400f, 60f));
            var saveMode = MakeButton("SaveModeButton", panel.rectTransform, "저장", new Vector2(150f, 52f), new Color(0.25f, 0.45f, 0.30f));
            Place((RectTransform)saveMode.transform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-380f, -30f), new Vector2(150f, 52f));
            var loadMode = MakeButton("LoadModeButton", panel.rectTransform, "불러오기", new Vector2(150f, 52f), new Color(0.25f, 0.35f, 0.55f));
            Place((RectTransform)loadMode.transform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-215f, -30f), new Vector2(150f, 52f));
            var close = MakeButton("CloseButton", panel.rectTransform, "닫기", new Vector2(120f, 52f), new Color(0.35f, 0.35f, 0.40f));
            Place((RectTransform)close.transform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-40f, -30f), new Vector2(120f, 52f));

            var slotNames = new[] { "AutoSlot", "Slot1", "Slot2", "Slot3" };
            var widgets = new SlotWidgetRefs[4];
            for (int i = 0; i < 4; i++)
            {
                var row = MakeButton(slotNames[i], panel.rectTransform, "", new Vector2(1020f, 140f), new Color(0.85f, 0.85f, 0.85f));
                var rt = (RectTransform)row.transform;
                Place(rt, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -120f - i * 150f), new Vector2(1020f, 140f));
                row.GetComponentInChildren<Text>().text = "";
                Object.DestroyImmediate(row.GetComponentInChildren<Text>().gameObject);   // 기본 라벨 제거

                var dark = new Color(0.1f, 0.1f, 0.12f);
                var wTitle = Label("Title", rt, i == 0 ? "자동 저장" : $"저장 {i}", 30, TextAnchor.UpperLeft, dark);
                Place(wTitle.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(25f, -14f), new Vector2(300f, 40f));
                var wIsland = Label("Island", rt, "-", 26, TextAnchor.UpperLeft, dark);
                Place(wIsland.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(25f, -60f), new Vector2(380f, 36f));
                var wPlay = Label("PlayTime", rt, "-", 24, TextAnchor.UpperLeft, dark);
                Place(wPlay.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(25f, -98f), new Vector2(300f, 34f));
                var wSaved = Label("SavedAt", rt, "-", 24, TextAnchor.UpperRight, dark);
                Place(wSaved.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-25f, -14f), new Vector2(520f, 34f));
                var wStatus = Label("Status", rt, "", 24, TextAnchor.LowerRight, dark);
                Place(wStatus.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-25f, 12f), new Vector2(620f, 34f));
                var badge = Label("CompleteBadge", rt, "CLEAR", 22, TextAnchor.MiddleCenter, new Color(0.7f, 0.45f, 0f));
                Place(badge.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(330f, -20f), new Vector2(100f, 30f));
                badge.gameObject.SetActive(false);

                widgets[i] = new SlotWidgetRefs { root = rt.gameObject, button = row, title = wTitle, island = wIsland, playTime = wPlay, savedAt = wSaved, status = wStatus, tint = row.targetGraphic, badge = badge.gameObject };
            }

            var message = Label("MessageText", panel.rectTransform, "", 26, TextAnchor.MiddleCenter, new Color(1f, 0.92f, 0.5f));
            Place(message.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 25f), new Vector2(1000f, 50f));

            var so = new SerializedObject(ui);
            SetRef(so, "root", panel.gameObject);
            for (int i = 0; i < 4; i++)
            {
                string p = $"slots.Array.data[{i}].";
                SetRef(so, p + "root", widgets[i].root); SetRef(so, p + "button", widgets[i].button);
                SetRef(so, p + "titleText", widgets[i].title); SetRef(so, p + "islandText", widgets[i].island);
                SetRef(so, p + "playTimeText", widgets[i].playTime); SetRef(so, p + "savedAtText", widgets[i].savedAt);
                SetRef(so, p + "statusText", widgets[i].status); SetRef(so, p + "stateTint", widgets[i].tint);
                SetRef(so, p + "completeBadge", widgets[i].badge);
            }
            SetRef(so, "saveModeButton", saveMode); SetRef(so, "loadModeButton", loadMode); SetRef(so, "closeButton", close);
            SetRef(so, "modeTitleText", title); SetRef(so, "messageText", message);
            so.ApplyModifiedPropertiesWithoutUndo();
            return ui;
        }

        private sealed class SlotWidgetRefs
        {
            public GameObject root, badge; public Button button; public Text title, island, playTime, savedAt, status; public Graphic tint;
        }

        // ── 섬 지도 ──

        private static void BuildIslandMapUI(Transform canvas)
        {
            var container = UIRect("IslandMapUI", canvas); Stretch(container);
            var ui = container.gameObject.AddComponent<IslandMapUI>();

            var panel = Panel("Panel", container, new Color(0.05f, 0.08f, 0.12f, 0.97f));
            Place(panel.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1500f, 820f));

            var mapTitle = Label("MapTitle", panel.rectTransform, "섬 지도", 40, TextAnchor.MiddleLeft, Color.white);
            Place(mapTitle.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(40f, -25f), new Vector2(400f, 60f));
            var close = MakeButton("CloseButton", panel.rectTransform, "닫기 (M)", new Vector2(160f, 52f), new Color(0.35f, 0.35f, 0.40f));
            Place((RectTransform)close.transform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-40f, -30f), new Vector2(160f, 52f));

            // 왼쪽: 섬 노드 (카탈로그 순서대로 세로 배치, 선으로 연결)
            var catalog = IslandCatalog.Default().OrderBy(d => d.order).ToList();
            var nodeRefs = new List<NodeRefs>();
            for (int i = 0; i < catalog.Count; i++)
            {
                float y = -170f - i * 200f;
                if (i > 0)
                {
                    var link = Panel($"Link{i}", panel.rectTransform, new Color(1f, 1f, 1f, 0.25f));
                    Place(link.rectTransform, new Vector2(0f, 1f), new Vector2(0.5f, 1f), new Vector2(260f, y + 90f), new Vector2(6f, 110f));
                    link.raycastTarget = false;
                }
                var btn = MakeButton($"Node_{catalog[i].id}", panel.rectTransform, "", new Vector2(420f, 130f), new Color(0.85f, 0.85f, 0.85f));
                var rt = (RectTransform)btn.transform;
                Place(rt, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(50f, y), new Vector2(420f, 130f));
                Object.DestroyImmediate(btn.GetComponentInChildren<Text>().gameObject);

                var dark = new Color(0.1f, 0.1f, 0.12f);
                var nm = Label("Name", rt, catalog[i].displayName, 30, TextAnchor.UpperLeft, dark);
                Place(nm.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(20f, -14f), new Vector2(380f, 40f));
                var cap = Label("Caption", rt, "", 24, TextAnchor.UpperLeft, dark);
                Place(cap.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(20f, 14f), new Vector2(300f, 34f));
                var cur = Label("CurrentMarker", rt, "▶", 30, TextAnchor.MiddleCenter, new Color(0.1f, 0.4f, 0.9f));
                cur.text = "NOW";
                Place(cur.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-15f, -14f), new Vector2(90f, 34f));
                var cleared = Label("ClearedMark", rt, "CLEAR", 24, TextAnchor.MiddleCenter, new Color(0.15f, 0.55f, 0.25f));
                Place(cleared.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-15f, 14f), new Vector2(110f, 34f));
                var lockIcon = Label("LockIcon", rt, "LOCK", 22, TextAnchor.MiddleCenter, new Color(0.4f, 0.4f, 0.4f));
                Place(lockIcon.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-15f, -50f), new Vector2(90f, 30f));

                nodeRefs.Add(new NodeRefs { id = catalog[i].id, button = btn, name = nm, caption = cap, tint = btn.targetGraphic, current = cur.gameObject, cleared = cleared.gameObject, lockIcon = lockIcon.gameObject });
            }

            // 오른쪽: 상세 패널
            var detail = Panel("Detail", panel.rectTransform, new Color(0.10f, 0.13f, 0.19f, 1f));
            Place(detail.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-40f, -30f), new Vector2(920f, 680f));
            detail.raycastTarget = false;
            var title = Label("DetailTitle", detail.rectTransform, "", 36, TextAnchor.UpperLeft, Color.white);
            Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(30f, -20f), new Vector2(860f, 50f));
            var brief = Label("Briefing", detail.rectTransform, "", 24, TextAnchor.UpperLeft, new Color(1f, 1f, 1f, 0.75f));
            Place(brief.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(30f, -75f), new Vector2(860f, 90f));

            var listRt = UIRect("Lines", detail.rectTransform);
            Place(listRt, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(30f, -180f), new Vector2(860f, 350f));
            var vlg = listRt.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.childControlWidth = true; vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;
            vlg.spacing = 4f;
            var template = Label("LineTemplate", listRt, "", 26, TextAnchor.UpperLeft, Color.white);
            template.horizontalOverflow = HorizontalWrapMode.Wrap;
            template.gameObject.SetActive(false);

            var message = Label("MessageText", detail.rectTransform, "", 24, TextAnchor.MiddleLeft, new Color(1f, 0.92f, 0.5f));
            Place(message.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(30f, 100f), new Vector2(860f, 40f));
            var travel = MakeButton("TravelButton", detail.rectTransform, "이동", new Vector2(360f, 70f), new Color(0.25f, 0.55f, 0.35f));
            Place((RectTransform)travel.transform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-30f, 20f), new Vector2(360f, 70f));
            var travelLabel = travel.GetComponentInChildren<Text>();
            travelLabel.fontSize = 30;

            var so = new SerializedObject(ui);
            SetRef(so, "root", panel.gameObject);
            var nodesProp = so.FindProperty("nodes");
            if (nodesProp != null)
            {
                nodesProp.arraySize = nodeRefs.Count;
                for (int i = 0; i < nodeRefs.Count; i++)
                {
                    string p = $"nodes.Array.data[{i}].";
                    SetString(so, p + "islandId", nodeRefs[i].id);
                    SetRef(so, p + "button", nodeRefs[i].button); SetRef(so, p + "nameText", nodeRefs[i].name);
                    SetRef(so, p + "captionText", nodeRefs[i].caption); SetRef(so, p + "tint", nodeRefs[i].tint);
                    SetRef(so, p + "currentMarker", nodeRefs[i].current); SetRef(so, p + "clearedMark", nodeRefs[i].cleared);
                    SetRef(so, p + "lockIcon", nodeRefs[i].lockIcon);
                }
            }
            else Warn("IslandMapUI.nodes 필드를 찾지 못했습니다 (필드 이름이 바뀌었는지 확인).");
            SetRef(so, "titleText", title); SetRef(so, "briefingText", brief);
            SetRef(so, "lineContainer", listRt); SetRef(so, "lineTemplate", template);
            SetRef(so, "travelButton", travel); SetRef(so, "travelButtonLabel", travelLabel);
            SetRef(so, "messageText", message); SetRef(so, "closeButton", close);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private sealed class NodeRefs
        {
            public string id; public Button button; public Text name, caption; public Graphic tint; public GameObject current, cleared, lockIcon;
        }

        // ─────────────────────────── UI 부품 ───────────────────────────


        // ─────────────────────────── 공통 매니저 / 목표 UI / 설정 창 / 메인 메뉴 ───────────────────────────

        /// <summary>_Systems에 붙이는 공통 매니저들. GameManager와 같은 오브젝트에 있으면 씬을 넘어 함께 살아남는다.</summary>
        private static void AddCoreManagers(GameObject systems, SetupAssets assets)
        {
            systems.AddComponent<SettingsManager>();
            systems.AddComponent<AudioManager>();
            systems.AddComponent<ObjectiveManager>();
            var feedback = systems.AddComponent<PuzzleFeedback>();
            var so = new SerializedObject(feedback);
            SetRef(so, "profile", assets.feedbackProfile);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void BuildObjectiveUI(Transform canvas)
        {
            var host = UIRect("ObjectiveUI", canvas); Stretch(host);
            var ui = host.gameObject.AddComponent<ObjectiveUI>();

            // 오른쪽 위 패널 (host와 다른 오브젝트여야 한다: 패널을 숨겨도 ObjectiveUI는 계속 동작해야 함)
            var panel = Panel("Panel", host, new Color(0.04f, 0.06f, 0.09f, 0.72f));
            Place(panel.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-30f, -30f), new Vector2(560f, 100f));
            panel.raycastTarget = false;
            var group = panel.gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false; group.interactable = false;
            var vlg = panel.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(18, 18, 14, 14); vlg.spacing = 6f;
            vlg.childControlWidth = true; vlg.childControlHeight = true; vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;
            panel.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var header = Label("Header", panel.rectTransform, "목표", 24, TextAnchor.MiddleLeft, new Color(1f, 0.85f, 0.4f));
            header.fontStyle = FontStyle.Bold;

            var content = UIRect("Content", panel.rectTransform);
            var clg = content.gameObject.AddComponent<VerticalLayoutGroup>();
            clg.spacing = 4f; clg.childControlWidth = true; clg.childControlHeight = true; clg.childForceExpandWidth = true; clg.childForceExpandHeight = false;

            // 줄 템플릿: [Box][Check(겹침)] + 제목 글자
            var line = UIRect("LineTemplate", content);
            var hlg = line.gameObject.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 10f; hlg.childAlignment = TextAnchor.MiddleLeft;
            hlg.childControlWidth = true; hlg.childControlHeight = true; hlg.childForceExpandWidth = false; hlg.childForceExpandHeight = false;
            var box = Panel("Box", line, new Color(1f, 1f, 1f, 0.25f)); box.raycastTarget = false;
            var boxLe = box.gameObject.AddComponent<LayoutElement>(); boxLe.minWidth = 28f; boxLe.minHeight = 28f; boxLe.preferredWidth = 28f; boxLe.preferredHeight = 28f;
            var check = Panel("Check", line, new Color(0.35f, 0.85f, 0.5f)); check.raycastTarget = false;
            var checkLe = check.gameObject.AddComponent<LayoutElement>(); checkLe.ignoreLayout = true;
            Place(check.rectTransform, new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(14f, 0f), new Vector2(28f, 28f));
            var mark = Label("Mark", check.rectTransform, "v", 22, TextAnchor.MiddleCenter, new Color(0.04f, 0.1f, 0.06f));
            mark.fontStyle = FontStyle.Bold; Stretch(mark.rectTransform);
            var label = Label("Title", line, "목표", 24, TextAnchor.MiddleLeft, Color.white);
            var labelLe = label.gameObject.AddComponent<LayoutElement>(); labelLe.flexibleWidth = 1f; labelLe.minHeight = 30f;
            line.gameObject.SetActive(false);

            var hint = Label("Hint", panel.rectTransform, "", 20, TextAnchor.UpperLeft, new Color(1f, 1f, 1f, 0.7f));

            var so = new SerializedObject(ui);
            SetRef(so, "root", panel.gameObject); SetRef(so, "group", group); SetRef(so, "headerText", header);
            SetRef(so, "content", content); SetRef(so, "lineTemplate", line.gameObject); SetRef(so, "hintText", hint);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static Slider MakeControlSlider(string name, Transform parent, Vector2 size)
        {
            var bg = Panel(name, parent, new Color(0f, 0f, 0f, 0.55f));
            bg.rectTransform.sizeDelta = size;

            var fillArea = UIRect("Fill Area", bg.transform); Stretch(fillArea);
            var fill = Panel("Fill", fillArea, new Color(0.3f, 0.8f, 0.75f)); fill.raycastTarget = false;
            fill.rectTransform.anchorMin = Vector2.zero; fill.rectTransform.anchorMax = Vector2.one;
            fill.rectTransform.offsetMin = Vector2.zero; fill.rectTransform.offsetMax = Vector2.zero;

            var handleArea = UIRect("Handle Slide Area", bg.transform); Stretch(handleArea);
            var handle = Panel("Handle", handleArea, Color.white);
            handle.rectTransform.anchorMin = new Vector2(0f, 0f); handle.rectTransform.anchorMax = new Vector2(0f, 1f);
            handle.rectTransform.sizeDelta = new Vector2(22f, 0f);

            var slider = bg.gameObject.AddComponent<Slider>();
            slider.fillRect = fill.rectTransform; slider.handleRect = handle.rectTransform; slider.targetGraphic = handle;
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = 0f; slider.maxValue = 100f; slider.wholeNumbers = true; slider.value = 100f;
            return slider;
        }

        private static SettingsPanelUI BuildSettingsPanel(Transform canvas, bool inMenu)
        {
            var container = UIRect("SettingsPanelUI", canvas); Stretch(container);
            var ui = container.gameObject.AddComponent<SettingsPanelUI>();

            var panel = Panel("Panel", container, new Color(0.05f, 0.06f, 0.09f, 0.97f));
            Place(panel.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(900f, 720f));
            var title = Label("Title", panel.rectTransform, "설정", 42, TextAnchor.MiddleLeft, Color.white);
            Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(40f, -25f), new Vector2(500f, 60f));

            string[] names = { "마스터 볼륨", "효과음", "환경음·기계음", "카메라 흔들림" };
            var sliders = new Slider[4]; var values = new Text[4];
            for (int i = 0; i < 4; i++)
            {
                float y = -130f - i * 100f;
                var l = Label("Name" + i, panel.rectTransform, names[i], 30, TextAnchor.MiddleLeft, Color.white);
                Place(l.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(50f, y), new Vector2(300f, 50f));
                sliders[i] = MakeControlSlider("Slider" + i, panel.rectTransform, new Vector2(380f, 36f));
                Place((RectTransform)sliders[i].transform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(370f, y - 7f), new Vector2(380f, 36f));
                values[i] = Label("Value" + i, panel.rectTransform, "100%", 30, TextAnchor.MiddleRight, Color.white);
                Place(values[i].rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(770f, y), new Vector2(100f, 50f));
            }

            // 히트스톱 토글
            var toggleBg = Panel("HitstopToggle", panel.rectTransform, new Color(0f, 0f, 0f, 0.55f));
            Place(toggleBg.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(50f, -540f), new Vector2(44f, 44f));
            var tick = Panel("Checkmark", toggleBg.rectTransform, new Color(0.3f, 0.8f, 0.75f)); tick.raycastTarget = false;
            Stretch(tick.rectTransform); tick.rectTransform.offsetMin = new Vector2(7f, 7f); tick.rectTransform.offsetMax = new Vector2(-7f, -7f);
            var toggle = toggleBg.gameObject.AddComponent<Toggle>();
            toggle.targetGraphic = toggleBg; toggle.graphic = tick; toggle.isOn = true;
            var tl = Label("HitstopLabel", panel.rectTransform, "큰 충격 때 화면 순간 정지 (히트스톱)", 28, TextAnchor.MiddleLeft, Color.white);
            Place(tl.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(110f, -537f), new Vector2(700f, 50f));

            var test = MakeButton("TestShakeButton", panel.rectTransform, "흔들림 시험", new Vector2(200f, 56f), new Color(0.25f, 0.4f, 0.5f));
            Place((RectTransform)test.transform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(50f, 40f), new Vector2(200f, 56f));
            var reset = MakeButton("ResetButton", panel.rectTransform, "기본값", new Vector2(160f, 56f), new Color(0.35f, 0.35f, 0.40f));
            Place((RectTransform)reset.transform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(280f, 40f), new Vector2(160f, 56f));
            var close = MakeButton("CloseButton", panel.rectTransform, "닫기", new Vector2(160f, 56f), new Color(0.35f, 0.35f, 0.40f));
            Place((RectTransform)close.transform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-50f, 40f), new Vector2(160f, 56f));

            var so = new SerializedObject(ui);
            SetRef(so, "root", panel.gameObject);
            SetRef(so, "masterSlider", sliders[0]); SetRef(so, "sfxSlider", sliders[1]); SetRef(so, "ambientSlider", sliders[2]); SetRef(so, "shakeSlider", sliders[3]);
            SetRef(so, "masterValue", values[0]); SetRef(so, "sfxValue", values[1]); SetRef(so, "ambientValue", values[2]); SetRef(so, "shakeValue", values[3]);
            SetRef(so, "hitstopToggle", toggle); SetRef(so, "resetButton", reset); SetRef(so, "closeButton", close); SetRef(so, "testShakeButton", test);
            SetBool(so, "controlCursor", !inMenu);
            if (!inMenu) SetInt(so, "toggleKey", (int)KeyCode.F10);
            so.ApplyModifiedPropertiesWithoutUndo();
            return ui;
        }

        private static void BuildMainMenuScene(SetupAssets assets, bool overwrite)
        {
            string path = $"{ScenesDir}/MainMenu.unity";
            if (!overwrite && File.Exists(path)) { Info("MainMenu: 이미 있어 건너뜀"); return; }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var camGo = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
            camGo.tag = "MainCamera";
            var cam = camGo.GetComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.04f, 0.06f, 0.09f);

            // 시스템: 이 오브젝트가 게임이 끝날 때까지 씬을 넘어 살아남는다 (섬 씬의 _Systems 복사본은 스스로 사라진다)
            var systems = new GameObject("_Systems");
            var gm = systems.AddComponent<GameManager>();
            var gmSo = new SerializedObject(gm);
            SetBool(gmSo, "startGameOnBoot", false);          // 메뉴에서 고를 때까지 기다린다
            gmSo.ApplyModifiedPropertiesWithoutUndo();
            systems.AddComponent<KnowledgeTracker>();
            var companion = systems.AddComponent<OopiCompanion>();
            var companionSo = new SerializedObject(companion);
            SetRef(companionSo, "db", assets.oopiDb);
            companionSo.ApplyModifiedPropertiesWithoutUndo();
            AddCoreManagers(systems, assets);

            var canvasGo = new GameObject("MenuCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f); scaler.matchWidthOrHeight = 0.5f;
            var t = canvasGo.transform;

            var host = UIRect("MainMenuUI", t); Stretch(host);
            var menu = host.gameObject.AddComponent<MainMenuUI>();
            var menuRoot = UIRect("MenuRoot", host); Stretch(menuRoot);
            var group = menuRoot.gameObject.AddComponent<CanvasGroup>();

            var bg = Panel("Background", menuRoot, new Color(0.06f, 0.09f, 0.13f)); Stretch(bg.rectTransform); bg.raycastTarget = false;
            var title = Label("Title", menuRoot, "에이전", 110, TextAnchor.MiddleCenter, new Color(1f, 0.85f, 0.4f));
            title.fontStyle = FontStyle.Bold;
            Place(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -170f), new Vector2(1400f, 140f));
            var subtitle = Label("Subtitle", menuRoot, "멈춰버린 행성의 엔지니어", 40, TextAnchor.MiddleCenter, new Color(1f, 1f, 1f, 0.8f));
            Place(subtitle.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -300f), new Vector2(1400f, 60f));

            string[] labels = { "새 게임", "이어하기", "설정", "종료" };
            var buttons = new Button[4];
            for (int i = 0; i < 4; i++)
            {
                buttons[i] = MakeButton("Button" + i, menuRoot, labels[i], new Vector2(480f, 76f), new Color(0.2f, 0.32f, 0.42f));
                Place((RectTransform)buttons[i].transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 60f - i * 100f), new Vector2(480f, 76f));
            }

            var info = Label("InfoText", menuRoot, "", 26, TextAnchor.MiddleCenter, new Color(1f, 1f, 1f, 0.75f));
            Place(info.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 150f), new Vector2(1500f, 44f));
            var message = Label("MessageText", menuRoot, "", 26, TextAnchor.MiddleCenter, new Color(1f, 0.92f, 0.5f));
            Place(message.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 100f), new Vector2(1500f, 44f));
            var version = Label("VersionText", menuRoot, "", 22, TextAnchor.LowerRight, new Color(1f, 1f, 1f, 0.4f));
            Place(version.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-30f, 20f), new Vector2(300f, 36f));

            var saveSlots = BuildSaveSlotUIObject(t);
            var settings = BuildSettingsPanel(t, inMenu: true);

            var so = new SerializedObject(menu);
            SetRef(so, "root", menuRoot.gameObject); SetRef(so, "group", group);
            SetRef(so, "titleText", title); SetRef(so, "subtitleText", subtitle); SetRef(so, "infoText", info);
            SetRef(so, "messageText", message); SetRef(so, "versionText", version);
            SetRef(so, "newGameButton", buttons[0]); SetRef(so, "continueButton", buttons[1]);
            SetRef(so, "settingsButton", buttons[2]); SetRef(so, "quitButton", buttons[3]);
            SetRef(so, "saveSlots", saveSlots); SetRef(so, "settingsPanel", settings);
            so.ApplyModifiedPropertiesWithoutUndo();

            // 메뉴에서는 저장 창을 "불러오기"로만 쓴다 (저장 모드 전환 버튼 숨김)
            var saveSo = new SerializedObject(saveSlots);
            var saveBtn = saveSo.FindProperty("saveModeButton")?.objectReferenceValue as Button;
            if (saveBtn != null) saveBtn.gameObject.SetActive(false);

            saveSlots.transform.SetAsLastSibling();
            settings.transform.SetAsLastSibling();

            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            if (!_fontIsKorean) Warn("한글 글꼴을 찾지 못해 기본 글꼴을 썼습니다. 메뉴 글자가 □로 보이면 MenuCanvas의 모든 Text에 한글 글꼴을 지정하세요.");

            EditorSceneManager.SaveScene(scene, path);
            Info("MainMenu: 생성 완료 (Build Settings 0번으로 등록)");
        }

        private static Font GetFont()
        {
            if (_font != null) return _font;

            // 프로젝트에 한글 글꼴이 있으면 우선 사용
            foreach (var guid in AssetDatabase.FindAssets("t:Font"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string n = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
                if (n.Contains("noto") || n.Contains("nanum") || n.Contains("malgun") || n.Contains("pretendard") || n.Contains("gothic") || n.Contains("kr"))
                {
                    var f = AssetDatabase.LoadAssetAtPath<Font>(path);
                    if (f != null) { _font = f; _fontIsKorean = true; return _font; }
                }
            }

            _fontIsKorean = false;
            try { _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch { _font = null; }
            if (_font == null) { try { _font = Resources.GetBuiltinResource<Font>("Arial.ttf"); } catch { _font = null; } }
            return _font;
        }

        private static RectTransform UIRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go.GetComponent<RectTransform>();
        }

        private static void Stretch(RectTransform r)
        {
            r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one;
            r.offsetMin = Vector2.zero; r.offsetMax = Vector2.zero;
        }

        private static void Place(RectTransform r, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            r.anchorMin = anchor; r.anchorMax = anchor; r.pivot = pivot;
            r.anchoredPosition = pos; r.sizeDelta = size;
        }

        private static Image Panel(string name, Transform parent, Color color)
        {
            var rt = UIRect(name, parent);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            return img;
        }

        private static Image Panel(string name, RectTransform parent, Color color) => Panel(name, (Transform)parent, color);

        private static Text Label(string name, Transform parent, string text, int size, TextAnchor align, Color color)
        {
            var rt = UIRect(name, parent);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = GetFont();
            t.text = text; t.fontSize = size; t.alignment = align; t.color = color;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            return t;
        }

        private static Text Label(string name, RectTransform parent, string text, int size, TextAnchor align, Color color) =>
            Label(name, (Transform)parent, text, size, align, color);

        private static Button MakeButton(string name, Transform parent, string label, Vector2 size, Color bg)
        {
            var img = Panel(name, parent, bg);
            ((RectTransform)img.transform).sizeDelta = size;
            var btn = img.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            var text = Label("Label", img.transform, label, 26, TextAnchor.MiddleCenter, Color.white);
            Stretch(text.rectTransform);
            return btn;
        }

        private static Slider MakeSlider(string name, Transform parent, Vector2 size, Color fill)
        {
            var bg = Panel(name, parent, new Color(0f, 0f, 0f, 0.5f));
            ((RectTransform)bg.transform).sizeDelta = size;
            bg.raycastTarget = false;

            var fillArea = UIRect("Fill Area", bg.transform); Stretch(fillArea);
            var fillImg = Panel("Fill", fillArea, fill);
            fillImg.raycastTarget = false;
            var fr = fillImg.rectTransform;
            fr.anchorMin = Vector2.zero; fr.anchorMax = Vector2.one; fr.offsetMin = Vector2.zero; fr.offsetMax = Vector2.zero;

            var slider = bg.gameObject.AddComponent<Slider>();
            slider.fillRect = fr;
            slider.targetGraphic = null;
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = 0f; slider.maxValue = 1f; slider.value = 0f;
            slider.interactable = false;
            slider.transition = Selectable.Transition.None;
            return slider;
        }

        // ─────────────────────────── 모델 찾기 (Kenney 에셋) ───────────────────────────

        private static Dictionary<string, string> _modelIndex;

        /// <summary>프로젝트의 프리팹·모델 파일을 이름(소문자, 확장자 없음)으로 찾아 둔다. 같은 이름이 여러 개면 처음 것.</summary>
        private static string FindModel(string[] names)
        {
            if (_modelIndex == null)
            {
                _modelIndex = new Dictionary<string, string>();
                foreach (var guid in AssetDatabase.FindAssets("t:Prefab t:Model"))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (!path.StartsWith("Assets/")) continue;
                    string key = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
                    if (!_modelIndex.ContainsKey(key)) _modelIndex[key] = path;
                }
            }
            foreach (var n in names)
                if (_modelIndex.TryGetValue(n.ToLowerInvariant(), out var path)) return path;
            return null;
        }

        private static GameObject InstantiateModel(string[] names, Transform parent)
        {
            string path = FindModel(names);
            if (path == null) return null;
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (asset == null) return null;
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(asset);
            inst.transform.SetParent(parent, false);
            return inst;
        }

        private static void SetModelRef(SerializedObject so, string field, params string[] names)
        {
            string path = FindModel(names);
            if (path == null) { Warn($"{so.targetObject.GetType().Name}.{field}: 모델 '{names[0]}'을(를) 프로젝트에서 찾지 못함 (직접 연결 필요)"); return; }
            SetRef(so, field, AssetDatabase.LoadAssetAtPath<GameObject>(path));
        }

        // ─────────────────────────── SerializedObject 도우미 ───────────────────────────

        private static void SetRef(SerializedObject so, string path, Object value)
        {
            var p = so.FindProperty(path);
            if (p == null) { Warn($"{so.targetObject.GetType().Name}.{path}: 필드를 찾지 못함"); return; }
            p.objectReferenceValue = value;
        }

        private static void SetRefArray(SerializedObject so, string path, Object[] values)
        {
            var p = so.FindProperty(path);
            if (p == null) { Warn($"{so.targetObject.GetType().Name}.{path}: 필드를 찾지 못함"); return; }
            p.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++) p.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        }

        private static void SetString(SerializedObject so, string path, string value)
        {
            var p = so.FindProperty(path);
            if (p == null) { Warn($"{so.targetObject.GetType().Name}.{path}: 필드를 찾지 못함"); return; }
            p.stringValue = value;
        }

        private static void SetInt(SerializedObject so, string path, int value)
        {
            var p = so.FindProperty(path);
            if (p == null) { Warn($"{so.targetObject.GetType().Name}.{path}: 필드를 찾지 못함"); return; }
            p.intValue = value;
        }

        private static void SetBool(SerializedObject so, string path, bool value)
        {
            var p = so.FindProperty(path);
            if (p == null) { Warn($"{so.targetObject.GetType().Name}.{path}: 필드를 찾지 못함"); return; }
            p.boolValue = value;
        }

        private static void SetEnum(SerializedObject so, string path, int index)
        {
            var p = so.FindProperty(path);
            if (p == null) { Warn($"{so.targetObject.GetType().Name}.{path}: 필드를 찾지 못함"); return; }
            p.enumValueIndex = index;
        }

        // ─────────────────────────── 기타 ───────────────────────────

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        private static readonly Dictionary<Color, Material> MaterialCache = new();

        /// <summary>색 재질을 만들어 입힌다. 파이프라인(Built-in/URP)에 맞는 기본 셰이더를 고른다.</summary>
        private static void Tint(GameObject go, Color color)
        {
            var r = go.GetComponent<Renderer>();
            if (r == null) return;
            r.sharedMaterial = ColorMaterial(color);
        }

        private static Material ColorMaterial(Color color)
        {
            if (MaterialCache.TryGetValue(color, out var cached) && cached != null) return cached;

            EnsureFolder("Assets/Materials/Generated");
            string name = $"Mat_{ColorUtility.ToHtmlStringRGB(color)}";
            string path = $"Assets/Materials/Generated/{name}.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) { MaterialCache[color] = existing; return existing; }

            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard") ?? Shader.Find("Sprites/Default");
            var mat = new Material(shader) { name = name };
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
            AssetDatabase.CreateAsset(mat, path);
            MaterialCache[color] = mat;
            return mat;
        }
    }
}
#endif
