using System;
using System.Collections.Generic;
using UnityEngine;
using AutoSurvival.Data;
using AutoSurvival.Interaction;
using AutoSurvival.Puzzles;
using AutoSurvival.Survival;

namespace AutoSurvival.Machines
{
    public enum MachineState { Broken, Repairing, Running }

    public enum MachineKind
    {
        WaterPurifier,   // 정수기 (공압/배관)  -> Thirst 해결
        Smelter,         // 제련기 (PLC)        -> 금속 패널/도구 업그레이드
        PowerGenerator,  // 발전기 (PLC+인터록)  -> 베이스 전력
        AutoGarden,      // 자동 재배기 (타이머)  -> Hunger 해결
        CraneLift        // 크레인 (유압)        -> 거대 자원 운반
    }

    /// <summary>
    /// 월드 위의 수리 가능한 자동화 기계. 상호작용하면 해당 기계 종류에 맞는 퍼즐을 연다.
    /// Tier는 퍼즐 난이도와 기계 성능을 함께 올리는 '업그레이드 축'이다.
    /// </summary>
    public class AutomationMachine : MonoBehaviour, IInteractable
    {
        [Tooltip("저장/불러오기용 고유 ID. 한 섬(씬) 안에서 겹치지 않아야 한다. 비워 두면 에디터가 자동 생성한다.")]
        [SerializeField] private string machineId;
        [SerializeField] private MachineKind kind;
        [SerializeField, Range(1, 5)] private int tier = 1;
        [SerializeField] private MachineState state = MachineState.Broken;
        [SerializeField] private Transform puzzleAnchor;

        [Header("퍼즐 (DB가 있으면 DB가 종류·파라미터를 고른다)")]
        [SerializeField] private PuzzleScenarioDB scenarioDb;
        [SerializeField] private PuzzleSession pipePuzzlePrefab;
        [SerializeField] private PuzzleSession ladderPuzzlePrefab;
        [SerializeField] private PuzzleSession hydraulicPuzzlePrefab;
        [SerializeField] private PuzzleSession pneumaticPuzzlePrefab;
        [SerializeField] private PuzzleSession puzzlePrefab;   // DB 없이 쓰는 기본 퍼즐(구버전 호환)

        [Header("산출")]
        [SerializeField] private float outputPerTier = 1f;
        [SerializeField] private float outputInterval = 30f;

        public string MachineId => machineId;
        public MachineKind Kind => kind;
        public int Tier => tier;
        public MachineState State => state;

        public event Action<AutomationMachine> OnRepaired;
        public event Action<AutomationMachine, int> OnTierUp;

        /// <summary>퍼즐 성공 직후 '현장 노트'(기출 연계 문제)를 UI에 띄우기 위한 이벤트. 답은 scenarioDb.RecordAnswer로 보고한다.</summary>
        public event Action<AutomationMachine, ExamQuestion, PuzzleScenarioEntry> OnFieldNote;

        public PuzzleScenarioDB ScenarioDb => scenarioDb;

        // ── 전역 알림/등록 (GameManager·오피가 개별 기계를 일일이 연결하지 않아도 되도록) ──
        private static readonly List<AutomationMachine> _all = new();
        /// <summary>지금 로드된 씬의 모든 기계.</summary>
        public static IReadOnlyList<AutomationMachine> All => _all;
        /// <summary>수리·업그레이드로 상태가 바뀐 직후.</summary>
        public static event Action<AutomationMachine> Changed;
        /// <summary>퍼즐이 시작된 직후 (오피 대사 등). 제한시간은 session.TimeLeft로 읽는다.</summary>
        public static event Action<AutomationMachine, PuzzleSession, PuzzleScenarioEntry> PuzzleStarted;

        private void OnEnable() { if (!_all.Contains(this)) _all.Add(this); }
        private void OnDisable() => _all.Remove(this);

        private void OnValidate()
        {
            if (string.IsNullOrEmpty(machineId)) machineId = $"{kind}_{Guid.NewGuid().ToString("N").Substring(0, 6)}";
        }

        /// <summary>불러오기용: 티어와 가동 여부를 복원한다 (수리 중이던 퍼즐은 복원하지 않는다).</summary>
        public void Restore(int savedTier, bool running)
        {
            tier = Mathf.Clamp(savedTier, 1, 5);
            state = running ? MachineState.Running : MachineState.Broken;
            _timer = 0f;
        }

        /// <summary>기계 종류 -> 기본 퍼즐 종류.</summary>
        public static PuzzleKind DefaultPuzzleKind(MachineKind k) => k switch
        {
            MachineKind.WaterPurifier => PuzzleKind.Pipe,
            MachineKind.CraneLift => PuzzleKind.Hydraulic,
            MachineKind.AutoGarden => PuzzleKind.Pneumatic,
            _ => PuzzleKind.Ladder      // 제련기, 발전기, 자동 재배기
        };

        private PuzzleSession _active;
        private PuzzleScenarioEntry _activeScenario;
        private float _timer;
        private bool _upgrading;   // 이번 퍼즐이 수리(false)인지 업그레이드(true)인지

        public string Prompt => state switch
        {
            MachineState.Broken  => $"[E] {kind} 수리 (Tier {tier})",
            MachineState.Running => tier < 5 ? $"[E] {kind} 업그레이드 (Tier {tier} → {tier + 1})" : $"{kind} 최대 티어",
            _ => "수리 중..."
        };

        public bool CanInteract(PlayerInteractor who) => state != MachineState.Repairing && !(state == MachineState.Running && tier >= 5);

        public void Interact(PlayerInteractor who)
        {
            if (!CanInteract(who)) return;

            // 업그레이드는 자원을 소비해야 퍼즐에 도전할 수 있다 (생존 <-> 퍼즐 연결 고리)
            if (state == MachineState.Running && !who.Inventory.Has(UpgradeCost(tier + 1)))
            {
                who.ShowToast("자원이 부족합니다. 채집하러 가자!");
                return;
            }

            _upgrading = state == MachineState.Running;
            int puzzleTier = _upgrading ? tier + 1 : tier;   // 업그레이드 도전 시 한 단계 높은 난이도

            // DB가 있으면 시나리오를 뽑고(약한 개념 우선), 없으면 기존 방식(고정 프리팹 + 티어 난이도)
            _activeScenario = scenarioDb != null ? scenarioDb.PickScenario(DefaultPuzzleKind(kind), puzzleTier) : null;
            var prefab = PrefabFor(_activeScenario != null ? _activeScenario.kind : DefaultPuzzleKind(kind));
            if (prefab == null)
            {
                who.ShowToast("이 기계의 퍼즐 설정이 비어 있습니다.");
                _upgrading = false;
                return;
            }

            if (_upgrading) who.Inventory.TryConsume(UpgradeCost(tier + 1));   // 퍼즐이 확정된 뒤에 자원 소비

            state = MachineState.Repairing;
            _active = Instantiate(prefab, puzzleAnchor != null ? puzzleAnchor : transform);
            _active.OnFinished += r => HandleResult(r, who);
            who.EnterPuzzleMode(_active);
            if (_activeScenario != null && !string.IsNullOrEmpty(_activeScenario.introLine))
                who.ShowToast(_activeScenario.introLine);   // 오피의 도입 대사
            _active.Begin(who.Stats, puzzleTier, _activeScenario);
            PuzzleStarted?.Invoke(this, _active, _activeScenario);
        }

        private PuzzleSession PrefabFor(PuzzleKind k)
        {
            PuzzleSession p = k switch
            {
                PuzzleKind.Pipe => pipePuzzlePrefab,
                PuzzleKind.Ladder => ladderPuzzlePrefab,
                PuzzleKind.Pneumatic => pneumaticPuzzlePrefab,
                _ => hydraulicPuzzlePrefab
            };
            return p != null ? p : puzzlePrefab;
        }

        private void HandleResult(PuzzleResult result, PlayerInteractor who)
        {
            bool wasRunning = _upgrading;
            who.ExitPuzzleMode();
            if (_active != null) Destroy(_active.gameObject);

            if (result == PuzzleResult.Success)
            {
                if (!wasRunning) { state = MachineState.Running; OnRepaired?.Invoke(this); }
                else { tier++; state = MachineState.Running; OnTierUp?.Invoke(this, tier); }
                Changed?.Invoke(this);

                // 방금 한 일이 시험에서 어떻게 나오는지 보여 주는 현장 노트 (선택 응답, 퍼즐은 이미 끝남)
                var note = scenarioDb != null ? scenarioDb.PickReviewQuestion(_activeScenario) : null;
                if (note != null) OnFieldNote?.Invoke(this, note, _activeScenario);
            }
            else
            {
                state = wasRunning ? MachineState.Running : MachineState.Broken;
                if (result == PuzzleResult.Fail) who.ShowToast("합선! 다시 정비해야 한다.");
            }
        }

        /// <summary>티어별 업그레이드 비용. 에셋: resource-wood / resource-stone / metal-panel 등.</summary>
        public static ItemCost UpgradeCost(int targetTier) => new ItemCost
        {
            wood = 5 * targetTier,
            stone = 3 * targetTier,
            metalPanel = targetTier >= 3 ? targetTier - 1 : 0
        };

        private void Update()
        {
            if (state != MachineState.Running) return;
            _timer += Time.deltaTime;
            if (_timer < outputInterval) return;
            _timer = 0f;
            Produce();
        }

        private void Produce()
        {
            // 기계 종류별 산출물 (정수 -> 물 병, 재배기 -> 작물, 제련기 -> 금속 패널...)
            // 실제 구현에서는 'ProductionBus' 이벤트로 베이스 창고(chest)에 적재.
            Debug.Log($"{kind} (Tier {tier}) 가동: +{outputPerTier * tier}");
        }
    }
}
