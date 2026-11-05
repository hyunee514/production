#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using AutoSurvival.Feedback;
using AutoSurvival.Interaction;
using AutoSurvival.Level;
using AutoSurvival.Survival;
using Object = UnityEngine.Object;

namespace AutoSurvival.EditorTools
{
    /// <summary>3인칭 플레이어 + 아기자기한 디오라마 섬(파스텔 하늘/안개, 바다, 케니 나무·바위·풀 배치).</summary>
    public static partial class ProjectSetupTool
    {
        // ─────────────── 테마 ───────────────

        private struct Theme
        {
            public Color grass, sand, rock, sea, sky, fog, ambSky, ambEq, ambGround, sun;
            public float sunIntensity;
        }

        private static Theme ThemeOf(IslandLayout l)
        {
            if (l.shortName == "foundry") return new Theme
            {
                grass = new Color(0.72f, 0.68f, 0.42f), sand = new Color(0.90f, 0.72f, 0.52f), rock = new Color(0.45f, 0.36f, 0.34f),
                sea = new Color(0.42f, 0.68f, 0.78f), sky = new Color(1f, 0.72f, 0.55f), fog = new Color(1f, 0.82f, 0.68f),
                ambSky = new Color(1f, 0.85f, 0.75f), ambEq = new Color(0.95f, 0.78f, 0.65f), ambGround = new Color(0.55f, 0.42f, 0.38f),
                sun = new Color(1f, 0.82f, 0.62f), sunIntensity = 1.05f
            };
            if (l.shortName == "core") return new Theme
            {
                grass = new Color(0.42f, 0.62f, 0.62f), sand = new Color(0.72f, 0.72f, 0.85f), rock = new Color(0.30f, 0.30f, 0.42f),
                sea = new Color(0.30f, 0.38f, 0.65f), sky = new Color(0.50f, 0.45f, 0.78f), fog = new Color(0.60f, 0.56f, 0.85f),
                ambSky = new Color(0.65f, 0.65f, 0.95f), ambEq = new Color(0.60f, 0.58f, 0.85f), ambGround = new Color(0.30f, 0.28f, 0.42f),
                sun = new Color(0.80f, 0.82f, 1f), sunIntensity = 0.95f
            };
            return new Theme
            {
                grass = new Color(0.55f, 0.82f, 0.42f), sand = new Color(0.97f, 0.89f, 0.68f), rock = new Color(0.62f, 0.55f, 0.50f),
                sea = new Color(0.36f, 0.80f, 0.86f), sky = new Color(0.55f, 0.80f, 1f), fog = new Color(0.82f, 0.94f, 1f),
                ambSky = new Color(0.78f, 0.88f, 1f), ambEq = new Color(0.88f, 0.90f, 0.82f), ambGround = new Color(0.55f, 0.50f, 0.42f),
                sun = new Color(1f, 0.95f, 0.82f), sunIntensity = 1.1f
            };
        }

        // ─────────────── 바닥(섬 + 바다) ───────────────

        private static GameObject Slab(string name, Transform parent, float cx, float cz, float sx, float sz, float topY, float thickness, Color color, bool collider)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(cx, topY - thickness * 0.5f, cz);
            go.transform.localScale = new Vector3(sx, thickness, sz);
            Tint(go, color);
            if (!collider) Object.DestroyImmediate(go.GetComponent<Collider>());
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.NavigationStatic);
            return go;
        }

        private static void BuildGround(IslandLayout l)
        {
            var t = ThemeOf(l);
            var root = new GameObject("Island").transform;

            // 바다: 아주 넓은 평면 (낙사 방지 벽은 아래에서)
            var sea = GameObject.CreatePrimitive(PrimitiveType.Plane);
            sea.name = "Sea"; sea.transform.SetParent(root, false);
            sea.transform.position = new Vector3(0f, -0.75f, 0f);
            sea.transform.localScale = new Vector3(60f, 1f, 60f);
            Tint(sea, t.sea);
            Object.DestroyImmediate(sea.GetComponent<Collider>());

            const float beach = 3f;
            float sx = l.halfX * 2f, sz = l.halfZ * 2f;
            Slab("Rock", root, 0, 0, sx + beach * 2f + 2f, sz + beach * 2f + 2f, -0.6f, 6f, t.rock, false);
            Slab("Sand", root, 0, 0, sx + beach * 2f, sz + beach * 2f, -0.12f, 2.0f, t.sand, true);
            Slab("Grass", root, 0, 0, sx, sz, 0f, 1.5f, t.grass, true);

            // 가장자리 보이지 않는 벽 (모래 끝에서 0.4m 안쪽)
            float wx = l.halfX + beach - 0.4f, wz = l.halfZ + beach - 0.4f;
            Wall(root, new Vector3(wx, 2f, 0f), new Vector3(1f, 6f, sz + beach * 2f));
            Wall(root, new Vector3(-wx, 2f, 0f), new Vector3(1f, 6f, sz + beach * 2f));
            Wall(root, new Vector3(0f, 2f, wz), new Vector3(sx + beach * 2f, 6f, 1f));
            Wall(root, new Vector3(0f, 2f, -wz), new Vector3(sx + beach * 2f, 6f, 1f));
        }

        private static void Wall(Transform parent, Vector3 pos, Vector3 size)
        {
            var go = new GameObject("EdgeWall", typeof(BoxCollider));
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            go.GetComponent<BoxCollider>().size = size;
        }

        // ─────────────── 하늘/안개/빛 ───────────────

        private static void ApplyAtmosphere(IslandLayout l)
        {
            var t = ThemeOf(l);

            EnsureFolder("Assets/Materials/Generated");
            string skyPath = $"Assets/Materials/Generated/Sky_{l.shortName}.mat";
            var sky = AssetDatabase.LoadAssetAtPath<Material>(skyPath);
            var shader = Shader.Find("Skybox/Procedural");
            if (sky == null && shader != null)
            {
                sky = new Material(shader);
                AssetDatabase.CreateAsset(sky, skyPath);
            }
            if (sky != null)
            {
                sky.SetColor("_SkyTint", t.sky);
                sky.SetColor("_GroundColor", t.fog);
                sky.SetFloat("_AtmosphereThickness", 0.55f);
                sky.SetFloat("_SunSize", 0.06f);
                sky.SetFloat("_Exposure", 1.15f);
                EditorUtility.SetDirty(sky);
                RenderSettings.skybox = sky;
            }

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = t.fog;
            RenderSettings.fogDensity = 0.010f;

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = t.ambSky;
            RenderSettings.ambientEquatorColor = t.ambEq;
            RenderSettings.ambientGroundColor = t.ambGround;

            var light = Object.FindObjectsOfType<Light>().FirstOrDefault(x => x.type == LightType.Directional);
            if (light != null)
            {
                light.color = t.sun; light.intensity = t.sunIntensity;
                light.transform.rotation = Quaternion.Euler(48f, -35f, 0f);
                light.shadows = LightShadows.Soft;
                light.shadowStrength = 0.55f;
                light.shadowBias = 0.05f;
                RenderSettings.sun = light;
            }
        }

        // ─────────────── 플레이어(3인칭) ───────────────

        private static readonly string[] PlayerModelNames = { "character-female-a", "character-male-a", "character-human" };

        private static void BuildPlayer(IslandLayout l)
        {
            var player = new GameObject("Player") { tag = "Player" };
            var cc = player.AddComponent<CharacterController>();
            cc.height = 1.6f; cc.radius = 0.35f; cc.center = new Vector3(0f, 0.8f, 0f); cc.stepOffset = 0.35f;

            player.AddComponent<SurvivalStats>();
            player.AddComponent<Inventory>();
            var interactor = player.AddComponent<PlayerInteractor>();
            var controller = player.AddComponent<ThirdPersonController>();
            player.AddComponent<FallGuard>();

            // 모습: 케니 미니 캐릭터 (없으면 알록달록 캡슐)
            var visual = new GameObject("Visual").transform;
            visual.SetParent(player.transform, false);
            Animator animator = null;
            var model = InstantiateModel(PlayerModelNames, visual);
            if (model != null)
            {
                model.name = "Model";
                model.transform.localScale = Vector3.one * 2.0f;                 // 키 약 1.6m: 귀엽고 통통하게 보이는 비율
                foreach (var c in model.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
                animator = model.GetComponentInChildren<Animator>();
                if (animator == null) animator = model.AddComponent<Animator>();
                var ctrl = EnsurePlayerAnimator(FindModel(PlayerModelNames));
                if (ctrl != null) animator.runtimeAnimatorController = ctrl;
                animator.applyRootMotion = false;
            }
            else
            {
                Warn("캐릭터 모델(kenney_mini-characters)을 찾지 못해 캡슐로 대체했습니다.");
                var cap = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                cap.name = "Model"; cap.transform.SetParent(visual, false);
                cap.transform.localPosition = new Vector3(0f, 0.8f, 0f); cap.transform.localScale = new Vector3(0.7f, 0.8f, 0.7f);
                Object.DestroyImmediate(cap.GetComponent<Collider>());
                Tint(cap, new Color(1f, 0.7f, 0.4f));
            }

            // 카메라: CameraRig(궤도) > ShakePivot(흔들림) > Camera
            var cam = Camera.main != null ? Camera.main : Object.FindObjectOfType<Camera>();
            if (cam == null) { var camGo = new GameObject("Main Camera", typeof(Camera)); camGo.tag = "MainCamera"; cam = camGo.GetComponent<Camera>(); }
            if (cam.GetComponent<AudioListener>() == null) cam.gameObject.AddComponent<AudioListener>();
            cam.fieldOfView = 38f;                                               // 좁은 화각 = 미니어처/디오라마 느낌
            cam.nearClipPlane = 0.3f; cam.farClipPlane = 220f;
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.allowMSAA = false;

            float yaw = 0f;
            var look = l.machines.Count > 0 ? new Vector3(l.machines[0].x, 0f, l.machines[0].z) : new Vector3(l.campX, 0f, l.campZ + 10f);
            var dir = new Vector3(look.x - l.spawnX, 0f, look.z - l.spawnZ);
            if (dir.sqrMagnitude > 0.01f) yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;

            var rig = new GameObject("CameraRig");
            rig.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            var rigCam = rig.AddComponent<ThirdPersonCamera>();
            var shakePivot = new GameObject("ShakePivot");
            shakePivot.transform.SetParent(rig.transform, false);
            shakePivot.AddComponent<CameraShaker>();
            cam.transform.SetParent(shakePivot.transform, false);
            cam.transform.localPosition = Vector3.zero;
            cam.transform.localRotation = Quaternion.identity;

            var rigSo = new SerializedObject(rigCam);
            SetRef(rigSo, "target", player.transform);
            rigSo.ApplyModifiedPropertiesWithoutUndo();

            var cSo = new SerializedObject(controller);
            SetRef(cSo, "cameraRig", rig.transform);
            SetRef(cSo, "visual", visual);
            if (animator != null) SetRef(cSo, "animator", animator);
            cSo.ApplyModifiedPropertiesWithoutUndo();

            var iSo = new SerializedObject(interactor);
            SetRef(iSo, "playerCamera", cam);
            SetRef(iSo, "movementController", controller);
            iSo.ApplyModifiedPropertiesWithoutUndo();

            player.transform.position = new Vector3(l.spawnX, 0.05f, l.spawnZ);
            visual.rotation = Quaternion.Euler(0f, yaw, 0f);
        }

        // ─────────────── 애니메이터 ───────────────

        private static AnimationClip FindClip(AnimationClip[] clips, params string[] names)
        {
            foreach (var n in names)
            {
                var c = clips.FirstOrDefault(x => x.name.ToLowerInvariant() == n);
                if (c != null) return c;
            }
            foreach (var n in names)
            {
                var c = clips.FirstOrDefault(x => x.name.ToLowerInvariant().Contains(n));
                if (c != null) return c;
            }
            return null;
        }

        private static RuntimeAnimatorController EnsurePlayerAnimator(string fbxPath)
        {
            if (string.IsNullOrEmpty(fbxPath)) return null;
            EnsureFolder("Assets/Animation");
            const string path = "Assets/Animation/Player.controller";
            AssetDatabase.DeleteAsset(path);                                    // 매번 새로 만든다 (클립 참조 갱신)

            var clips = AssetDatabase.LoadAllAssetsAtPath(fbxPath).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview")).ToArray();
            if (clips.Length == 0) { Warn("캐릭터 FBX에서 애니메이션 클립을 찾지 못했습니다."); return null; }

            var idle = FindClip(clips, "idle");
            var walk = FindClip(clips, "walk");
            var sprint = FindClip(clips, "sprint", "run");
            var jump = FindClip(clips, "jump");
            var fall = FindClip(clips, "fall");
            var interact = FindClip(clips, "interact-right", "pick-up", "interact");
            if (idle == null) return null;

            var ctrl = AnimatorController.CreateAnimatorControllerAtPath(path);
            ctrl.AddParameter("Speed", AnimatorControllerParameterType.Float);
            ctrl.AddParameter("Grounded", AnimatorControllerParameterType.Bool);
            ctrl.AddParameter("Jump", AnimatorControllerParameterType.Trigger);
            ctrl.AddParameter("Interact", AnimatorControllerParameterType.Trigger);
            var sm = ctrl.layers[0].stateMachine;

            var sIdle = sm.AddState("Idle"); sIdle.motion = idle;
            var sWalk = sm.AddState("Walk"); sWalk.motion = walk != null ? walk : idle;
            var sRun = sm.AddState("Run"); sRun.motion = sprint != null ? sprint : sWalk.motion;
            var sFall = sm.AddState("Fall"); sFall.motion = fall != null ? fall : idle;
            var sJump = sm.AddState("Jump"); sJump.motion = jump != null ? jump : sFall.motion;
            var sAct = sm.AddState("Interact"); sAct.motion = interact != null ? interact : idle;
            sm.defaultState = sIdle;

            AnimatorStateTransition T(AnimatorState from, AnimatorState to, bool exit = false, float exitTime = 0f)
            {
                var tr = from.AddTransition(to);
                tr.hasExitTime = exit; tr.exitTime = exitTime; tr.duration = 0.1f;
                return tr;
            }

            T(sIdle, sWalk).AddCondition(AnimatorConditionMode.Greater, 0.1f, "Speed");
            T(sWalk, sIdle).AddCondition(AnimatorConditionMode.Less, 0.1f, "Speed");
            T(sWalk, sRun).AddCondition(AnimatorConditionMode.Greater, 0.75f, "Speed");
            T(sRun, sWalk).AddCondition(AnimatorConditionMode.Less, 0.75f, "Speed");
            T(sRun, sIdle).AddCondition(AnimatorConditionMode.Less, 0.1f, "Speed");

            foreach (var s in new[] { sIdle, sWalk, sRun })
            {
                T(s, sJump).AddCondition(AnimatorConditionMode.If, 0f, "Jump");
                T(s, sAct).AddCondition(AnimatorConditionMode.If, 0f, "Interact");
                T(s, sFall).AddCondition(AnimatorConditionMode.IfNot, 0f, "Grounded");
            }
            T(sJump, sFall, true, 0.55f);
            T(sFall, sIdle).AddCondition(AnimatorConditionMode.If, 0f, "Grounded");
            T(sAct, sIdle, true, 0.9f);

            EditorUtility.SetDirty(ctrl);
            return ctrl;
        }

        // ─────────────── 장식 배치 (케니 에셋) ───────────────

        private static readonly string[][] TreeSets =
        {
            new[] { "tree" }, new[] { "tree-high" }, new[] { "tree-tall" }, new[] { "tree-large" }, new[] { "tree-small" },
        };
        private static readonly string[][] GrassSets = { new[] { "grass" }, new[] { "grass-large" }, new[] { "patch-grass" }, new[] { "grass-plant" }, new[] { "plant" } };
        private static readonly string[][] FlowerSets = { new[] { "flowers" }, new[] { "flowers-tall" } };
        private static readonly string[][] RockSets = { new[] { "rock-a" }, new[] { "rock-b" }, new[] { "rock-c" }, new[] { "rocks-small" }, new[] { "rocks-medium" } };
        private static readonly string[][] PalmSets = { new[] { "palm-straight" }, new[] { "palm-bend" }, new[] { "palm-detailed-straight" } };

        private static void ScatterDecor(IslandLayout l)
        {
            var root = new GameObject("Decor");
            var rng = new System.Random(l.shortName.GetHashCode() * 7919 + 13);

            var avoid = new List<(Vector2 p, float r)>
            {
                (new Vector2(l.campX, l.campZ), 7f), (new Vector2(l.spawnX, l.spawnZ), 4f)
            };
            foreach (var m in l.machines) avoid.Add((new Vector2(m.x, m.z), 6f));
            foreach (var z in l.zones) avoid.Add((new Vector2(z.x, z.z), z.radius + 1.5f));
            foreach (var lm in l.landmarks) avoid.Add((new Vector2(lm.x, lm.z), 5f));

            var placed = new List<Vector2>();
            bool Free(Vector2 p, float spacing)
            {
                foreach (var a in avoid) if ((a.p - p).sqrMagnitude < a.r * a.r) return false;
                foreach (var q in placed) if ((q - p).sqrMagnitude < spacing * spacing) return false;
                return true;
            }

            int Count(int basis) => Mathf.RoundToInt(basis * (l.halfX * l.halfZ) / 2500f);   // 100x100 섬 기준 개수

            // 가장자리 띠에 모이는 점 (섬 가장자리에서 edgeMin..edgeMax m 안쪽)
            Vector2 EdgePoint(float edgeMin, float edgeMax)
            {
                float d = Mathf.Lerp(edgeMin, edgeMax, (float)rng.NextDouble());
                float ux = l.halfX - d, uz = l.halfZ - d;
                float u = (float)rng.NextDouble() * 2f - 1f;
                int side = rng.Next(4);
                float x = side == 0 ? -ux : side == 1 ? ux : u * ux;
                float z = side == 2 ? -uz : side == 3 ? uz : u * uz;
                return new Vector2(x, z);
            }
            Vector2 AnyPoint() => new Vector2(((float)rng.NextDouble() * 2f - 1f) * (l.halfX - 2f), ((float)rng.NextDouble() * 2f - 1f) * (l.halfZ - 2f));

            void Put(string[][] sets, int count, System.Func<Vector2> pick, float spacing, float smin, float smax, bool collide, float y = 0f)
            {
                int made = 0;
                for (int tries = 0; tries < count * 12 && made < count; tries++)
                {
                    var p = pick();
                    if (!Free(p, spacing)) continue;
                    var set = sets[rng.Next(sets.Length)];
                    if (FindModel(set) == null) continue;
                    var go = InstantiateModel(set, root.transform);
                    if (go == null) continue;
                    foreach (var c in go.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
                    float s = Mathf.Lerp(smin, smax, (float)rng.NextDouble());
                    go.transform.position = new Vector3(p.x, y, p.y);
                    go.transform.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
                    go.transform.localScale = Vector3.one * s;
                    if (collide) { var sc = go.AddComponent<SphereCollider>(); sc.radius = 0.35f; sc.center = new Vector3(0f, 0.5f, 0f); }
                    foreach (var tr in go.GetComponentsInChildren<Transform>()) GameObjectUtility.SetStaticEditorFlags(tr.gameObject, StaticEditorFlags.BatchingStatic);
                    placed.Add(p); made++;
                }
            }

            Put(TreeSets, Count(46), () => EdgePoint(3f, 16f), 3.2f, 1.7f, 2.6f, true);
            Put(TreeSets, Count(10), AnyPoint, 6f, 1.6f, 2.2f, true);                      // 군데군데 서 있는 나무
            Put(RockSets, Count(18), () => EdgePoint(2f, 10f), 3f, 1.4f, 2.2f, true);
            Put(FlowerSets, Count(45), AnyPoint, 1.6f, 1.6f, 2.4f, false);
            Put(GrassSets, Count(110), AnyPoint, 1.3f, 1.6f, 2.6f, false);

            if (l.shortName == "landing")                                                    // 해변 야자수
                Put(PalmSets, Count(16), () =>
                {
                    float side = (float)rng.NextDouble();
                    float t = (float)rng.NextDouble() * 2f - 1f;
                    float ex = l.halfX + 1.2f, ez = l.halfZ + 1.2f;
                    return side < 0.25f ? new Vector2(-ex, t * ez) : side < 0.5f ? new Vector2(ex, t * ez) : side < 0.75f ? new Vector2(t * ex, -ez) : new Vector2(t * ex, ez);
                }, 6f, 2f, 2.8f, true, -0.12f);

            var stat = root.GetComponentsInChildren<Transform>().Length;
            Info($"{l.sceneName}: 장식 {stat - 1}개 배치");
        }
    }

    /// <summary>케니 미니 캐릭터의 걷기/달리기/대기 애니메이션이 반복되도록 임포트 설정을 맞춘다.</summary>
    public class KennyCharacterImportFix : AssetPostprocessor
    {
        private static readonly string[] Loops = { "idle", "walk", "sprint", "run", "fall", "sit", "crouch" };

        private void OnPreprocessModel()
        {
            if (!assetPath.Contains("kenney_mini-characters")) return;
            var mi = (ModelImporter)assetImporter;
            mi.animationType = ModelImporterAnimationType.Generic;
            mi.importAnimation = true;
            var clips = mi.defaultClipAnimations;
            if (clips == null || clips.Length == 0) return;
            foreach (var c in clips)
                c.loopTime = Loops.Any(k => c.name.ToLowerInvariant().Contains(k));
            mi.clipAnimations = clips;
        }
    }
}
#endif
