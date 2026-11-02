using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using AutoSurvival.Core;
using AutoSurvival.Interaction;
using AutoSurvival.Machines;
using AutoSurvival.Survival;

namespace AutoSurvival.Save
{
    /// <summary>
    /// 게임 진행 총괄 (씬 사이에서 살아남는 싱글톤).
    ///  - 저장: 생존 수치, 인벤토리, 개념 숙련도, 섬별 기계 티어·가동 여부, 위치, 오피 대사 기록을 SaveData 하나로 모아 저장
    ///  - 불러오기: 해당 섬의 씬을 열고 위 값을 되돌림
    ///  - 섬 이동·클리어 규칙은 GameProgress(순수 로직)가 판단하고, 이 클래스는 Unity 쪽 연결(씬·컴포넌트·이벤트)만 맡는다.
    /// 자동 저장: 기계 수리/업그레이드 직후, 섬 이동 직후, 일정 간격, 앱이 멈추거나 종료될 때.
    /// 이 클래스는 Unity 안에서만 확인할 수 있다 (씬 전환·컴포넌트). 규칙 자체는 GameProgress 테스트가 검증한다.
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        public static GameManager Instance { get; private set; }

        [Header("섬 구성 (비우면 기본 3섬)")]
        [SerializeField] private List<IslandDef> islands = new();

        [Header("저장")]
        [SerializeField] private float autosaveSeconds = 120f;
        [Tooltip("시작할 때 자동 저장이 있으면 이어서 시작")] [SerializeField] private bool continueOnStart = true;
        [Tooltip("끄면 시작 때 아무것도 하지 않고 메인 메뉴를 기다린다 (MainMenu 씬의 GameManager는 끌 것)")]
        [SerializeField] private bool startGameOnBoot = true;
        [Tooltip("새 게임 시작 시 인벤토리 (물·식량은 항해 보급이 된다)")] [SerializeField] private InventorySave startingInventory = new() { water = 2, food = 2 };

        public GameProgress Progress { get; private set; }
        public SaveSystem Saves { get; private set; }
        public float PlayTime => _playTime;
        public bool IsBusy { get; private set; }     // 씬 전환 중
        /// <summary>메인 메뉴 상태: 시간·자동 저장·목표 판정이 멈춘다. NewGame/Load를 하면 풀린다.</summary>
        public bool IsInMenu { get; private set; }

        /// <summary>외부(오피, UI)가 대사·알림에 쓰도록 노출. 수치는 저장 시점의 것.</summary>
        public event Action<int> OnSaved;
        public event Action OnLoaded;
        public event Action OnNewGame;
        public event Action<string> OnLoadFailed;
        public event Action<IslandDef> OnIslandCleared;
        public event Action OnGameComplete;
        public event Action<TravelCheck> OnTravelDenied;
        public event Action<IslandDef> OnTravelStarted;
        public event Action<IslandDef> OnArrived;
        public event Action OnPlayerDied;

        /// <summary>오피 대사 DB 같은 외부 시스템이 세이브에 기록을 싣고 내릴 수 있는 훅.</summary>
        public event Action<SaveData> BeforeSave;
        public event Action<SaveData> AfterLoad;

        private PlayerInteractor _player;
        private float _playTime, _autosaveTimer;
        private bool _applyingLoad;

        // ───────── 수명 ─────────

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            if (islands == null || islands.Count == 0) islands = IslandCatalog.Default();
            Saves = new SaveSystem(new FileSaveStorage());
            Progress = new GameProgress(SaveData.NewGame(islands), islands);

            AutomationMachine.Changed += HandleMachineChanged;
            SceneManager.sceneLoaded += HandleSceneLoaded;
            Application.quitting += HandleQuitting;
        }

        private void OnDestroy()
        {
            if (Instance != this) return;
            AutomationMachine.Changed -= HandleMachineChanged;
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            Application.quitting -= HandleQuitting;
            Instance = null;
        }

        private void Start()
        {
            BindPlayer();
            if (!startGameOnBoot) { IsInMenu = true; return; }
            if (continueOnStart && Saves.HasSave(SaveSystem.AutoSlot)) Load(SaveSystem.AutoSlot);
            else NewGame();
        }

        private void Update()
        {
            if (IsBusy || IsInMenu) return;
            _playTime += Time.unscaledDeltaTime;

            if (autosaveSeconds > 0f)
            {
                _autosaveTimer += Time.unscaledDeltaTime;
                if (_autosaveTimer >= autosaveSeconds) Save(SaveSystem.AutoSlot);
            }
        }

        private void OnApplicationPause(bool paused) { if (paused) SilentAutosave(); }
        private void HandleQuitting() => SilentAutosave();

        private void SilentAutosave()
        {
            if (Progress == null || IsBusy || IsInMenu || _applyingLoad || (_player != null && _player.Stats != null && _player.Stats.IsDead)) return;
            Save(SaveSystem.AutoSlot);
        }

        // ───────── 플레이어 연결 ─────────

        private void BindPlayer()
        {
            if (_player != null && _player.Stats != null) _player.Stats.OnDeath -= HandleDeath;
            _player = PlayerInteractor.Current;
            if (_player != null && _player.Stats != null) _player.Stats.OnDeath += HandleDeath;
        }

        private void HandleDeath()
        {
            Progress.Data.deaths++;
            OnPlayerDied?.Invoke();
        }

        // ───────── 새 게임 / 저장 / 불러오기 ─────────

        public void NewGame()
        {
            IsInMenu = false;
            Progress = new GameProgress(SaveData.NewGame(islands), islands);
            _playTime = 0f;
            KnowledgeTracker.Instance?.ClearAll();

            var inv = Progress.Data.player.inventory;
            inv.wood = startingInventory.wood; inv.stone = startingInventory.stone; inv.metalPanel = startingInventory.metalPanel;
            inv.water = startingInventory.water; inv.food = startingInventory.food;

            if (_player != null && _player.Stats != null)
            {
                foreach (StatType t in Enum.GetValues(typeof(StatType)))
                    _player.Stats.SetCurrent(t, 9999f);   // 최대치로 자름
                _player.Stats.Revive();
            }
            ApplyInventory(inv);
            ReloadSceneFor(Progress.Current, applyAfter: true);   // 씬이 바뀌면 새 씬의 플레이어에게 시작 인벤토리를 넣어야 한다
            OnNewGame?.Invoke();
        }

        public bool Save(int slot)
        {
            if (Progress == null || IsInMenu) return false;
            // 사망한 상태(체력 0)를 자동 저장으로 덮어쓰면 리스폰할 지점이 사라진다.
            if (slot == SaveSystem.AutoSlot && _player != null && _player.Stats != null && _player.Stats.IsDead) return false;

            CaptureWorld();
            return WriteSave(slot);
        }

        /// <summary>이미 기록된 Progress.Data를 그대로 파일에 쓴다 (섬 이동처럼 스냅샷 시점을 직접 정할 때).</summary>
        private bool WriteSave(int slot)
        {
            BeforeSave?.Invoke(Progress.Data);

            if (!Saves.Save(slot, Progress.Data, out string error))
            {
                Debug.LogWarning($"[GameManager] {error}");
                return false;
            }
            if (slot == SaveSystem.AutoSlot) _autosaveTimer = 0f;
            OnSaved?.Invoke(slot);
            return true;
        }

        public bool Load(int slot)
        {
            var result = Saves.Load(slot);
            if (!result.ok)
            {
                OnLoadFailed?.Invoke(result.error);
                return false;
            }
            if (result.usedBackup) Debug.LogWarning("[GameManager] 저장 파일이 손상되어 직전 저장본으로 복구했습니다.");

            IsInMenu = false;
            Progress = new GameProgress(result.data, islands);
            _playTime = result.data.playTimeSeconds;
            _autosaveTimer = 0f;

            ApplyPlayerAndKnowledge();
            ReloadSceneFor(Progress.Current, applyAfter: true);
            AfterLoad?.Invoke(result.data);
            OnLoaded?.Invoke();
            return true;
        }

        /// <summary>타이틀로 돌아간다: 지금 상태를 자동 저장하고 메뉴 씬을 연다. 이어하기로 다시 들어올 수 있다.</summary>
        public void ReturnToTitle(string menuSceneName = "MainMenu")
        {
            if (IsBusy) return;
            Save(SaveSystem.AutoSlot);
            IsInMenu = true;
            Time.timeScale = 1f;
            SceneManager.LoadScene(menuSceneName);
        }

        /// <summary>사망 후 호출: 마지막 자동 저장으로 돌아간다 (사망 횟수는 유지). 저장이 없으면 새 게임.</summary>
        public void Respawn()
        {
            int deaths = Progress != null ? Progress.Data.deaths : 0;
            if (Saves.HasSave(SaveSystem.AutoSlot) && Load(SaveSystem.AutoSlot)) Progress.Data.deaths = deaths;
            else { NewGame(); Progress.Data.deaths = deaths; }
        }

        // ───────── 스냅샷 ─────────

        /// <summary>지금 월드의 값을 Progress.Data에 기록한다.</summary>
        private void CaptureWorld()
        {
            CapturePlayerAndKnowledge(includePosition: true);
            CaptureMachines();
        }

        private void CapturePlayerAndKnowledge(bool includePosition)
        {
            var d = Progress.Data;
            d.playTimeSeconds = _playTime;

            if (_player == null) BindPlayer();
            if (_player != null)
            {
                d.player.stats.Clear();
                if (_player.Stats != null)
                    foreach (StatType t in Enum.GetValues(typeof(StatType)))
                        d.player.stats.Add(new StatSave { type = t, current = _player.Stats.GetCurrent(t) });

                if (_player.Inventory != null)
                {
                    var inv = _player.Inventory;
                    d.player.inventory = new InventorySave { wood = inv.wood, stone = inv.stone, metalPanel = inv.metalPanel, water = inv.water, food = inv.food };
                }

                if (includePosition)
                {
                    var island = d.GetOrAddIsland(d.currentIslandId);
                    var p = _player.transform.position;
                    island.hasPosition = true; island.px = p.x; island.py = p.y; island.pz = p.z;
                }
            }

            d.concepts.Clear();
            var tracker = KnowledgeTracker.Instance;
            if (tracker != null)
                foreach (var kv in tracker.All())
                    if (kv.Value.attempts > 0)
                        d.concepts.Add(new ConceptSave
                        {
                            concept = kv.Key, attempts = kv.Value.attempts, successes = kv.Value.successes,
                            secondsSinceSeen = tracker.SecondsSinceSeen(kv.Key)
                        });
        }

        private void CaptureMachines()
        {
            var island = Progress.Data.GetOrAddIsland(Progress.Data.currentIslandId);
            var seen = new HashSet<string>();
            foreach (var m in AutomationMachine.All)
            {
                if (m == null) continue;
                if (string.IsNullOrEmpty(m.MachineId)) { Debug.LogWarning($"[GameManager] '{m.name}'의 machineId가 비어 있어 저장되지 않습니다.", m); continue; }
                if (!seen.Add(m.MachineId)) { Debug.LogWarning($"[GameManager] machineId '{m.MachineId}'가 중복됩니다. 먼저 등록된 기계만 저장됩니다.", m); continue; }

                island.UpsertMachine(new MachineSave
                {
                    id = m.MachineId, kind = m.Kind, tier = m.Tier,
                    running = m.State == MachineState.Running
                });
            }
        }

        private void ApplyPlayerAndKnowledge()
        {
            var d = Progress.Data;
            if (_player == null) BindPlayer();

            if (_player != null && _player.Stats != null)
            {
                foreach (var s in d.player.stats) _player.Stats.SetCurrent(s.type, s.current);
                _player.Stats.Revive();
            }
            ApplyInventory(d.player.inventory);

            var tracker = KnowledgeTracker.Instance;
            if (tracker != null)
            {
                tracker.ClearAll();
                foreach (var c in d.concepts) tracker.Restore(c.concept, c.attempts, c.successes, c.secondsSinceSeen);
            }
        }

        private void ApplyInventory(InventorySave inv)
        {
            if (_player == null || _player.Inventory == null) return;
            var target = _player.Inventory;
            target.wood = inv.wood; target.stone = inv.stone; target.metalPanel = inv.metalPanel;
            target.water = inv.water; target.food = inv.food;
        }

        /// <summary>현재 씬의 기계들과 플레이어 위치를 섬 저장값으로 되돌린다.</summary>
        private void ApplyIslandState()
        {
            var island = Progress.Data.GetIsland(Progress.Data.currentIslandId);
            if (island == null) return;

            foreach (var m in AutomationMachine.All)
            {
                if (m == null || string.IsNullOrEmpty(m.MachineId)) continue;
                var saved = island.GetMachine(m.MachineId);
                if (saved != null) m.Restore(saved.tier, saved.running);
            }

            if (island.hasPosition && _player != null)
            {
                var cc = _player.GetComponent<CharacterController>();
                if (cc != null) cc.enabled = false;          // CharacterController가 켜져 있으면 위치 지정이 무시된다
                _player.transform.position = new Vector3(island.px, island.py, island.pz);
                if (cc != null) cc.enabled = true;
            }
        }

        // ───────── 씬 ─────────

        private void ReloadSceneFor(IslandDef island, bool applyAfter)
        {
            if (island == null) return;

            bool needsScene = !string.IsNullOrEmpty(island.sceneName) && SceneManager.GetActiveScene().name != island.sceneName;
            if (needsScene) StartCoroutine(LoadSceneRoutine(island, applyAfter));
            else if (applyAfter) ApplyIslandState();
        }

        private IEnumerator LoadSceneRoutine(IslandDef island, bool applyAfter)
        {
            IsBusy = true; _applyingLoad = true;
            var op = SceneManager.LoadSceneAsync(island.sceneName);
            while (op != null && !op.isDone) yield return null;
            yield return null;                                   // 새 씬의 OnEnable/Awake가 끝난 뒤 한 프레임 더

            BindPlayer();
            if (applyAfter) { ApplyPlayerAndKnowledge(); ApplyIslandState(); }
            _applyingLoad = false; IsBusy = false;
        }

        private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            // 씬을 우리가 직접 열지 않은 경우(에디터에서 바로 플레이 등)에도 플레이어를 다시 연결한다.
            if (!IsBusy) BindPlayer();
        }

        // ───────── 섬 이동 ─────────

        /// <summary>섬 id -> 표시 이름 (모르는 id는 그대로).</summary>
        public string IslandName(string islandId) => Progress?.Find(islandId)?.displayName ?? islandId;

        /// <summary>슬롯 선택 화면용 요약.</summary>
        public List<SlotSummary> ListSlots() => Saves.ListSlots();

        /// <summary>항해 보급으로 쓰이는 현재 보유량.</summary>
        public (int food, int water) Supplies() =>
            _player != null && _player.Inventory != null ? (_player.Inventory.food, _player.Inventory.water) : (0, 0);

        public TravelCheck CheckTravel(string islandId)
        {
            int food = _player != null && _player.Inventory != null ? _player.Inventory.food : 0;
            int water = _player != null && _player.Inventory != null ? _player.Inventory.water : 0;
            return Progress.CheckTravel(islandId, MasteryOf, food, water);
        }

        public static MasterySample MasteryOf(ConceptTag tag)
        {
            var t = KnowledgeTracker.Instance;
            if (t == null) return new MasterySample(0, 0);
            var r = t.Get(tag);
            return new MasterySample(r.attempts, r.successes);
        }

        /// <summary>섬으로 이동. 조건을 못 채우면 OnTravelDenied를 내고 아무 것도 바꾸지 않는다.</summary>
        public bool TravelTo(string islandId)
        {
            if (IsBusy) return false;

            var check = CheckTravel(islandId);
            if (!check.ok) { OnTravelDenied?.Invoke(check); return false; }

            var target = Progress.Find(islandId);
            OnTravelStarted?.Invoke(target);

            CaptureWorld();                                      // 떠나는 섬의 상태를 먼저 기록 (기계, 위치)
            Progress.ApplyTravel(islandId);

            if (_player != null && _player.Inventory != null)
            {
                _player.Inventory.food -= check.foodCost;
                _player.Inventory.water -= check.waterCost;
            }
            CapturePlayerAndKnowledge(includePosition: false);   // 보급 차감 반영. 기계·위치는 옛 섬 것이므로 새 섬에 기록하지 않는다
            WriteSave(SaveSystem.AutoSlot);

            ReloadSceneFor(target, applyAfter: true);
            OnArrived?.Invoke(target);
            return true;
        }

        // ───────── 클리어 판정 ─────────

        private void HandleMachineChanged(AutomationMachine machine)
        {
            if (Progress == null || _applyingLoad) return;
            CaptureMachines();

            if (Progress.RefreshClear(out var cleared))
            {
                OnIslandCleared?.Invoke(cleared);
                if (Progress.IsComplete) OnGameComplete?.Invoke();
            }
            Save(SaveSystem.AutoSlot);
        }
    }
}
