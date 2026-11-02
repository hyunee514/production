using System;
using System.Collections.Generic;
using UnityEngine;
using AutoSurvival.Feedback;
using AutoSurvival.Interaction;
using AutoSurvival.Machines;
using AutoSurvival.Save;
using AutoSurvival.Survival;

namespace AutoSurvival.Objectives
{
    /// <summary>
    /// 목표 관리자. 0.25초마다 세계의 상태를 한 장(WorldSnapshot)으로 찍어 ObjectiveTracker에 넘기고,
    /// 새로 달성된 목표가 있으면 알린다 (달성음은 PuzzleFeedback이 낸다).
    ///  - 저장: GameManager.BeforeSave에서 SaveData.completedObjectives에 기록, AfterLoad에서 복원, 새 게임이면 초기화.
    ///  - 씬 전환 중(GameManager.IsBusy)이나 메인 메뉴에서는 판정하지 않는다 (이전 씬의 값으로 잘못 달성되는 것 방지).
    ///  - 목록은 기본값(ObjectiveDefaults) 또는 인스펙터의 customObjectives로 바꿀 수 있다.
    /// </summary>
    public class ObjectiveManager : MonoBehaviour
    {
        public static ObjectiveManager Instance { get; private set; }

        [Tooltip("비워 두면 기본 목표(불시착 해안 -> 제련 섬)를 쓴다")]
        [SerializeField] private List<ObjectiveDef> customObjectives = new();
        [SerializeField] private float pollSeconds = 0.25f;
        [SerializeField] private bool playCompleteSound = true;

        public ObjectiveTracker Tracker { get; private set; }
        public WorldSnapshot LastSnapshot { get; private set; } = new WorldSnapshot();

        /// <summary>목표가 새로 달성됨 (여러 개가 한꺼번에 달성되면 하나씩 차례로)</summary>
        public event Action<ObjectiveDef> OnCompleted;
        /// <summary>목록이 바뀜 (달성, 불러오기, 새 게임, 섬 이동) — UI가 다시 그린다</summary>
        public event Action OnChanged;
        /// <summary>현재 목표의 진행 문구가 바뀜 (예: 나무 2/3)</summary>
        public event Action<string> OnProgressText;

        private readonly HashSet<string> _flags = new();
        private float _timer;
        private string _lastProgress = "";
        private string _lastIsland;
        private GameManager _gm;

        public string CurrentIslandId => GameManager.Instance?.Progress?.Data.currentIslandId ?? "";

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
            if (transform.parent == null) DontDestroyOnLoad(gameObject);

            Tracker = new ObjectiveTracker(customObjectives != null && customObjectives.Count > 0 ? customObjectives : ObjectiveDefaults.All());
        }

        private void OnEnable() { TryBind(); }
        private void OnDisable() { Unbind(); }
        private void OnDestroy() { if (Instance == this) Instance = null; }

        private void TryBind()
        {
            var gm = GameManager.Instance;
            if (gm == null || gm == _gm) return;
            Unbind();
            _gm = gm;
            _gm.BeforeSave += HandleBeforeSave;
            _gm.AfterLoad += HandleAfterLoad;
            _gm.OnNewGame += HandleNewGame;
        }

        private void Unbind()
        {
            if (_gm == null) return;
            _gm.BeforeSave -= HandleBeforeSave;
            _gm.AfterLoad -= HandleAfterLoad;
            _gm.OnNewGame -= HandleNewGame;
            _gm = null;
        }

        // ───────── 저장 연동 ─────────

        private void HandleBeforeSave(SaveData data) => data.completedObjectives = Tracker.Export();

        private void HandleAfterLoad(SaveData data)
        {
            Tracker.Import(data.completedObjectives);
            _flags.Clear();
            _lastProgress = "";
            OnChanged?.Invoke();
        }

        private void HandleNewGame()
        {
            Tracker.Reset();
            _flags.Clear();
            _lastProgress = "";
            OnChanged?.Invoke();
        }

        // ───────── 코드에서 달성시키는 신호 ─────────

        /// <summary>Flag 조건용 신호를 켠다. 예: 필드노트를 처음 읽었을 때 SetFlag("readFieldNote")</summary>
        public void SetFlag(string flag) { if (!string.IsNullOrEmpty(flag)) _flags.Add(flag); _timer = 0f; }

        // ───────── 판정 ─────────

        private void Update()
        {
            if (_gm == null) TryBind();
            if (_gm == null || _gm.IsBusy || _gm.IsInMenu) return;

            string island = CurrentIslandId;
            if (island != _lastIsland) { _lastIsland = island; _lastProgress = ""; OnChanged?.Invoke(); }

            _timer -= Time.unscaledDeltaTime;
            if (_timer > 0f) return;
            _timer = pollSeconds;

            var snap = TakeSnapshot();
            LastSnapshot = snap;

            var newly = Tracker.Evaluate(snap);
            if (newly.Count > 0)
            {
                foreach (var d in newly) OnCompleted?.Invoke(d);
                if (playCompleteSound) PuzzleFeedback.Instance?.Play(FeedbackKeys.ObjectiveComplete);
                OnChanged?.Invoke();
                _lastProgress = "";
            }

            var active = Tracker.Active(island);
            string progress = active != null ? ObjectiveEvaluator.ProgressText(active.condition, snap) : "";
            if (progress != _lastProgress) { _lastProgress = progress; OnProgressText?.Invoke(progress); }
        }

        private WorldSnapshot TakeSnapshot()
        {
            var w = new WorldSnapshot { currentIslandId = CurrentIslandId };

            var player = PlayerInteractor.Current;
            if (player != null && player.Inventory != null)
            {
                var inv = player.Inventory;
                w.wood = inv.wood; w.stone = inv.stone; w.metalPanel = inv.metalPanel; w.water = inv.water; w.food = inv.food;
            }

            w.fireLit = Campfire.AnyLit;

            foreach (var m in AutomationMachine.All)
                if (m != null) w.machines.Add(new MachineSnap(m.Kind, m.Tier, m.State == MachineState.Running));

            var progress = GameManager.Instance?.Progress;
            if (progress != null)
                foreach (var isl in progress.Data.islands) if (isl.cleared) w.clearedIslands.Add(isl.id);

            foreach (var f in _flags) w.flags.Add(f);
            return w;
        }

        /// <summary>UI가 지금 그릴 줄들.</summary>
        public List<ObjectiveLine> CurrentLines(int maxLines = 4) => Tracker.View(LastSnapshot, CurrentIslandId, maxLines);
    }
}
