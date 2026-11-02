using System;
using System.Collections.Generic;
using UnityEngine;
using AutoSurvival.Core;
using AutoSurvival.Data;
using AutoSurvival.Interaction;
using AutoSurvival.Machines;
using AutoSurvival.Puzzles;
using AutoSurvival.Save;
using AutoSurvival.Survival;

namespace AutoSurvival.Dialogue
{
    /// <summary>
    /// 오피가 '언제' 말하는지를 정하는 연결 컴포넌트. 대사 내용은 OopiDialogueDB가 고른다.
    /// 구독하는 신호: 기계의 퍼즐 시작·수리·업그레이드, 퍼즐별 실패 원인(공압/유압/래더), 퍼즐 종료,
    ///               생존 수치 경고, GameManager의 섬 도착·클리어. 말한 기록은 세이브에 함께 저장된다.
    /// UI는 OnSpeak를 구독해 말풍선·자막을 띄우면 된다 (player가 연결돼 있으면 토스트로도 보낸다).
    /// </summary>
    public class OopiCompanion : MonoBehaviour
    {
        /// <summary>살아 있는 오피 (씬이 바뀌어도 처음 만들어진 것이 유지된다 — GameManager와 같은 _Systems에 있기 때문).</summary>
        public static OopiCompanion Active { get; private set; }
        private void Awake() { if (Active == null) Active = this; }
        private void OnDestroy() { if (Active == this) Active = null; }

        [SerializeField] private OopiDialogueDB db;
        [SerializeField] private bool alsoShowAsToast = true;

        [Header("말 간격")]
        [SerializeField] private float minSecondsBetweenLines = 2.5f;
        [Tooltip("남은 시간이 이 비율 이하이고")] [SerializeField, Range(0.05f, 0.6f)] private float timeLowFraction = 0.25f;
        [Tooltip("남은 시간이 이 초 이하일 때 한 번 경고")] [SerializeField] private float timeLowMaxSeconds = 20f;
        [Tooltip("같은 수치 경고 사이 최소 간격(초)")] [SerializeField] private float survivalWarnCooldown = 90f;
        [SerializeField] private float idleMinSeconds = 60f, idleMaxSeconds = 150f;
        [SerializeField, Range(0.05f, 0.5f)] private float survivalWarnLevel = 0.25f;

        /// <summary>말풍선 UI(DialogueUI)가 대사를 보여 줄 때는 토스트와 겹치지 않도록 끈다.</summary>
        public bool AlsoShowAsToast { get => alsoShowAsToast; set => alsoShowAsToast = value; }

        public event Action<OopiSpeech> OnSpeak;

        private PuzzleSession _puzzle;
        private OopiPuzzleFilter _puzzleKind;
        private AutomationMachine _machine;
        private int _puzzleTier, _machineTierAtStart;
        private AutomationMachine _pendingMachine;     // 퍼즐 종료 순간 기계 상태가 먼저 바뀌므로, 성공 대사 뒤에 이어 말하려고 보관
        private float _puzzleStartTime, _initialTime;
        private bool _timeLowSpoken, _diagnosisSpoken;

        private float _lastSpokeAt = -999f, _nextIdleAt;
        private readonly Dictionary<string, float> _warnedAt = new();
        private PlayerInteractor _player;

        // ───────── 수명 ─────────

        private void OnEnable()
        {
            AutomationMachine.PuzzleStarted += HandlePuzzleStarted;
            AutomationMachine.Changed += HandleMachineChanged;

            var gm = GameManager.Instance;
            if (gm != null)
            {
                gm.OnArrived += HandleArrived;
                gm.OnIslandCleared += HandleIslandCleared;
                gm.BeforeSave += HandleBeforeSave;
                gm.AfterLoad += HandleAfterLoad;
            }
            ScheduleIdle();
        }

        private void OnDisable()
        {
            AutomationMachine.PuzzleStarted -= HandlePuzzleStarted;
            AutomationMachine.Changed -= HandleMachineChanged;
            var gm = GameManager.Instance;
            if (gm != null)
            {
                gm.OnArrived -= HandleArrived;
                gm.OnIslandCleared -= HandleIslandCleared;
                gm.BeforeSave -= HandleBeforeSave;
                gm.AfterLoad -= HandleAfterLoad;
            }
            DetachPuzzle();
        }

        // ───────── 말하기 ─────────

        /// <summary>force=false면 직전 발언과의 간격이 짧을 때 말을 삼킨다 (연속 대사 폭주 방지).</summary>
        public OopiSpeech Speak(OopiQuery query, bool force = false)
        {
            if (db == null) return null;
            if (!force && Time.unscaledTime - _lastSpokeAt < minSecondsBetweenLines) return null;

            var speech = db.Say(query);
            if (speech == null) return null;

            _lastSpokeAt = Time.unscaledTime;
            OnSpeak?.Invoke(speech);
            if (alsoShowAsToast && _player != null) _player.ShowToast(speech.text);
            ScheduleIdle();
            return speech;
        }

        // ───────── 퍼즐 ─────────

        private void HandlePuzzleStarted(AutomationMachine machine, PuzzleSession puzzle, PuzzleScenarioEntry scenario)
        {
            DetachPuzzle();
            _machine = machine; _puzzle = puzzle;
            _puzzleKind = KindOf(puzzle);
            _puzzleTier = machine.Tier;
            _machineTierAtStart = machine.Tier;
            _pendingMachine = null;
            _initialTime = puzzle.TimeLeft;
            _timeLowSpoken = false; _diagnosisSpoken = false;
            _player = PlayerInteractor.Current;

            switch (puzzle)
            {
                case PneumaticPuzzle pn: pn.OnCue += HandlePneumaticCue; break;
                case HydraulicPuzzle hy: hy.OnCue += HandleHydraulicCue; break;
                case LadderPuzzle la: la.OnTestRan += HandleLadderTest; break;
            }
            puzzle.OnFinished += HandlePuzzleFinished;

            var q = Query(OopiSituation.PuzzleEnter);
            if (scenario != null) q.Concept(scenario.concept);
            Speak(q, force: true);
        }

        private void DetachPuzzle()
        {
            if (_puzzle == null) return;
            switch (_puzzle)
            {
                case PneumaticPuzzle pn: pn.OnCue -= HandlePneumaticCue; break;
                case HydraulicPuzzle hy: hy.OnCue -= HandleHydraulicCue; break;
                case LadderPuzzle la: la.OnTestRan -= HandleLadderTest; break;
            }
            _puzzle.OnFinished -= HandlePuzzleFinished;
            _puzzle = null;
        }

        private static OopiPuzzleFilter KindOf(PuzzleSession p) => p switch
        {
            PneumaticPuzzle => OopiPuzzleFilter.Pneumatic,
            HydraulicPuzzle => OopiPuzzleFilter.Hydraulic,
            LadderPuzzle => OopiPuzzleFilter.Ladder,
            PipePuzzle => OopiPuzzleFilter.Pipe,
            _ => OopiPuzzleFilter.Any
        };

        private OopiQuery Query(OopiSituation s)
        {
            var q = new OopiQuery(s).Puzzle(_puzzleKind).Tier(Mathf.Max(1, _puzzleTier));
            if (_machine != null) q.Var("machine", DisplayNames.Machine(_machine.Kind));
            return q;
        }

        // 실패 원인 신호 -> 진단 대사
        private void HandlePneumaticCue(PneumaticCue cue, PneumaticReport report)
        {
            if (report == null || report.passed) return;
            Diagnose(OopiCauses.From(report.fault), null);
        }

        private void HandleHydraulicCue(HydraulicCue cue, HydraulicReport report)
        {
            string cause = OopiCauses.From(cue, report);
            if (cause != null) Diagnose(cause, null);
        }

        private void HandleLadderTest(LadderTestReport report, string message)
        {
            if (report == null || report.passed) return;
            ConceptTag? concept = _puzzle != null && _puzzle.Scenario != null ? _puzzle.Scenario.concept : (ConceptTag?)null;
            Diagnose(OopiCauses.From(report), concept);
        }

        private void Diagnose(string cause, ConceptTag? concept)
        {
            if (cause == null) return;
            var q = Query(OopiSituation.Diagnosis).Cause(cause);
            if (concept.HasValue) q.Concept(concept.Value);
            else if (_puzzle != null && _puzzle.Scenario != null) q.Concept(_puzzle.Scenario.concept);
            if (Speak(q, force: true) != null) _diagnosisSpoken = true;
        }

        private void HandlePuzzleFinished(PuzzleResult result)
        {
            if (result == PuzzleResult.Success)
            {
                var q = Query(OopiSituation.Success);
                if (_puzzle != null && _puzzle.Scenario != null) q.Concept(_puzzle.Scenario.concept);
                Speak(q, force: true);
            }
            else if (result == PuzzleResult.Fail && !_diagnosisSpoken)
            {
                // 진단 없이 끝난 실패 = 시간 초과. 이미 원인을 설명했다면 같은 말을 또 하지 않는다.
                Speak(Query(OopiSituation.Failure).Cause(OopiCauses.Timeout), force: true);
            }
            DetachPuzzle();
            FlushPendingMachine();
        }

        // ───────── 기계 / 섬 ─────────

        private void HandleMachineChanged(AutomationMachine machine)
        {
            // 퍼즐이 끝나는 순간 기계 상태가 먼저 바뀐다. 성공 대사를 먼저 하고 수리·업그레이드 대사를 잇기 위해 보관한다.
            if (_puzzle != null) { _pendingMachine = machine; return; }
            SpeakMachine(machine);
        }

        private void FlushPendingMachine()
        {
            if (_pendingMachine == null) return;
            var m = _pendingMachine;
            _pendingMachine = null;
            SpeakMachine(m);
        }

        private void SpeakMachine(AutomationMachine machine)
        {
            bool upgraded = machine.Tier > _machineTierAtStart;
            var q = new OopiQuery(upgraded ? OopiSituation.TierUp : OopiSituation.Repaired)
                .Tier(machine.Tier).Var("machine", DisplayNames.Machine(machine.Kind));
            Speak(q, force: true);
        }

        private void HandleArrived(Save.IslandDef island)
        {
            Speak(new OopiQuery(OopiSituation.IslandArrive).Var("island", island.displayName), force: true);
        }

        private void HandleIslandCleared(Save.IslandDef island)
        {
            Speak(new OopiQuery(OopiSituation.IslandCleared).Var("island", island.displayName), force: true);
        }

        // ───────── 매 프레임: 시간 촉박 / 생존 경고 / 혼잣말 ─────────

        private void Update()
        {
            if (db == null) return;

            if (_puzzle != null && _puzzle.IsRunning)
            {
                CheckTimeLow();
                return;       // 퍼즐 중에는 생존 경고·혼잣말을 하지 않는다 (시간 촉박 경고가 우선)
            }

            if (_player == null) _player = PlayerInteractor.Current;
            CheckSurvival();

            if (Time.unscaledTime >= _nextIdleAt)
            {
                Speak(new OopiQuery(OopiSituation.Idle));
                ScheduleIdle();
            }
        }

        private void CheckTimeLow()
        {
            if (_timeLowSpoken) return;
            float left = _puzzle.TimeLeft;
            if (left > timeLowMaxSeconds || _initialTime <= 0f || left / _initialTime > timeLowFraction) return;

            _timeLowSpoken = true;
            Speak(Query(OopiSituation.TimeLow).Var("seconds", Mathf.CeilToInt(Mathf.Max(0f, left)).ToString()), force: true);
        }

        private void CheckSurvival()
        {
            var stats = _player != null ? _player.Stats : null;
            if (stats == null || stats.IsDead) return;

            TryWarn(stats, StatType.Health, "health");
            TryWarn(stats, StatType.Hunger, "hunger");
            TryWarn(stats, StatType.Thirst, "thirst");
            TryWarn(stats, StatType.Warmth, "warmth");
        }

        private void TryWarn(SurvivalStats stats, StatType type, string cause)
        {
            if (stats.Get(type) > survivalWarnLevel) return;
            if (_warnedAt.TryGetValue(cause, out var at) && Time.unscaledTime - at < survivalWarnCooldown) return;

            if (Speak(new OopiQuery(OopiSituation.SurvivalWarning).Cause(cause)) != null)
                _warnedAt[cause] = Time.unscaledTime;
        }

        private void ScheduleIdle() => _nextIdleAt = Time.unscaledTime + UnityEngine.Random.Range(idleMinSeconds, idleMaxSeconds);

        // ───────── 세이브 연동 ─────────

        private void HandleBeforeSave(SaveData data)
        {
            if (db == null) return;
            data.oopiSeen.Clear();
            foreach (var kv in db.ExportMemory()) data.oopiSeen.Add(new OopiSeenSave { lineId = kv.Key, count = kv.Value });
        }

        private void HandleAfterLoad(SaveData data)
        {
            if (db == null) return;
            var counts = new List<KeyValuePair<string, int>>();
            foreach (var o in data.oopiSeen) counts.Add(new KeyValuePair<string, int>(o.lineId, o.count));
            db.ImportMemory(counts);
        }
    }
}
