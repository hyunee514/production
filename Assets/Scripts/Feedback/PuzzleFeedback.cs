using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using AutoSurvival.Machines;
using AutoSurvival.Puzzles;
using AutoSurvival.UI;
using AutoSurvival.Data;
using AutoSurvival.Audio;
using AutoSurvival.Settings;

namespace AutoSurvival.Feedback
{
    /// <summary>
    /// 퍼즐 이벤트 -> 연출 연결 컨트롤러.
    ///  - AutomationMachine.PuzzleStarted로 퍼즐이 시작될 때 그 퍼즐의 OnCue/OnTestRan/OnFinished를 구독하고, 끝나면 해제한다.
    ///  - 큐가 오면 FeedbackProfile에서 항목을 찾아: 파티클 생성, 효과음(피치 랜덤 x 콤보 사다리), 카메라 흔들림, 히트스톱을 실행한다.
    ///  - 리듬 펌핑: MetronomeUI.OnJudged로 콤보와 피치 사다리를 갱신한다.
    ///    메트로놈이 붙으면 유압 퍼즐은 UseExternalRhythm = true 이므로, 같은 한 번의 펌핑이
    ///    (1) 퍼즐의 PumpGood/PumpMiss 와 (2) 메트로놈의 Rhythm.* 판정 두 번 울린다. 중복을 막기 위해
    ///    외부 리듬 모드에서는 PumpMiss를 무시하고(Rhythm.Miss가 담당), PumpGood은 사다리 없이 연출만 재생한다.
    ///    메트로놈이 없는 내장 리듬 모드에서는 PumpGood/PumpMiss가 직접 사다리를 움직인다.
    ///  - 효과음 파일이 없는 큐는 소리 없이 지나간다 (에러 없음). 프로필에 없는 큐도 마찬가지.
    /// </summary>
    public class PuzzleFeedback : MonoBehaviour
    {
        public static PuzzleFeedback Instance { get; private set; }

        [SerializeField] private FeedbackProfile profile;
        [SerializeField] private CameraShaker cameraShaker;
        [SerializeField] private MetronomeUI metronome;
        [Tooltip("효과음을 동시에 몇 개까지 재생할지")]
        [SerializeField, Range(2, 16)] private int audioVoices = 6;
        [Range(0f, 1f)] [SerializeField] private float sfxVolume = 1f;
        [SerializeField] private bool hitstopEnabled = true;
        [Tooltip("파티클을 퍼즐 위치 위로 얼마나 띄울지(m)")]
        [SerializeField] private float particleHeight = 1f;

        /// <summary>콤보가 바뀔 때 (콤보 수, 올라간 반음 수). UI가 "x3" 같은 표시에 사용.</summary>
        public event Action<int, int> OnComboChanged;
        /// <summary>시간이 25% 이하로 처음 내려갔을 때 (화면 가장자리 붉은 비네트 시작 신호)</summary>
        public event Action OnTimeLow;

        public PitchLadder Ladder { get; } = new();
        public int Combo => Ladder.Combo;

        private readonly HitstopGate _hitstop = new();
        private readonly NoRepeatPicker _picker = new();
        private readonly List<AudioSource> _voices = new();
        private int _nextVoice;

        private PuzzleSession _session;
        private HydraulicPuzzle _hydraulic;
        private PneumaticPuzzle _pneumatic;
        private LadderPuzzle _ladder;
        private TimeAlarm _alarm;
        private bool _inHitstop;

        private void Awake()
        {
            Instance = this;
            for (int i = 0; i < audioVoices; i++)
            {
                var go = new GameObject("FeedbackVoice" + i);
                go.transform.SetParent(transform, false);
                var src = go.AddComponent<AudioSource>();
                src.playOnAwake = false; src.spatialBlend = 0f;
                _voices.Add(src);
            }
            if (cameraShaker == null) cameraShaker = FindFirstObjectByType<CameraShaker>();
            if (metronome == null) metronome = FindFirstObjectByType<MetronomeUI>();
        }

        private void OnEnable()
        {
            AutomationMachine.PuzzleStarted += HandlePuzzleStarted;
            EnsureMetronome();
        }

        // 메인 메뉴 씬에서 시작해 이 오브젝트가 씬을 넘어 살아남는 경우, 섬 씬의 메트로놈은 나중에 생긴다. 퍼즐이 시작될 때마다 다시 찾는다.
        private MetronomeUI _hookedMetronome;
        private void EnsureMetronome()
        {
            if (metronome == null) metronome = FindFirstObjectByType<MetronomeUI>();
            if (_hookedMetronome == metronome) return;
            if (_hookedMetronome != null) _hookedMetronome.OnJudged -= HandleJudged;
            _hookedMetronome = metronome;
            if (_hookedMetronome != null) _hookedMetronome.OnJudged += HandleJudged;
        }

        private void OnDisable()
        {
            AutomationMachine.PuzzleStarted -= HandlePuzzleStarted;
            if (_hookedMetronome != null) _hookedMetronome.OnJudged -= HandleJudged;
            _hookedMetronome = null;
            Detach();
            RestoreTime();
        }

        private void OnDestroy() { if (Instance == this) Instance = null; }

        // ───────── 퍼즐 연결 ─────────

        private void HandlePuzzleStarted(AutomationMachine machine, PuzzleSession session, PuzzleScenarioEntry scenario)
        {
            Attach(session);
        }

        /// <summary>퍼즐 하나에 연결한다 (기계 없이 직접 띄운 퍼즐에도 쓸 수 있다).</summary>
        public void Attach(PuzzleSession session)
        {
            Detach();
            if (session == null) return;
            EnsureMetronome();
            _session = session;
            Ladder.Reset();
            RaiseCombo();
            _alarm = new TimeAlarm(Mathf.Max(1f, session.TimeLeft));

            _session.OnFinished += HandleFinished;
            switch (session)
            {
                case HydraulicPuzzle h: _hydraulic = h; h.OnCue += HandleHydraulic; break;
                case PneumaticPuzzle p: _pneumatic = p; p.OnCue += HandlePneumatic; break;
                case LadderPuzzle l: _ladder = l; l.OnTestRan += HandleLadder; break;
            }
        }

        public void Detach()
        {
            if (_session != null) _session.OnFinished -= HandleFinished;
            if (_hydraulic != null) _hydraulic.OnCue -= HandleHydraulic;
            if (_pneumatic != null) _pneumatic.OnCue -= HandlePneumatic;
            if (_ladder != null) _ladder.OnTestRan -= HandleLadder;
            _session = null; _hydraulic = null; _pneumatic = null; _ladder = null; _alarm = null;
        }

        private void Update()
        {
            if (_session == null || !_session.IsRunning || _alarm == null) return;
            var (low, mistake) = _alarm.Update(_session.TimeLeft, Time.deltaTime);
            if (low) { Play(FeedbackKeys.TimeLow); OnTimeLow?.Invoke(); }
            if (mistake) Play(FeedbackKeys.TimeMistake);
        }

        // ───────── 이벤트 처리 ─────────

        private void HandleHydraulic(HydraulicCue cue, HydraulicReport report)
        {
            bool external = _hydraulic != null && _hydraulic.UseExternalRhythm;

            if (cue == HydraulicCue.PumpMiss && external) return;      // Rhythm.Miss가 담당 (중복 방지)

            if (!external)                                              // 내장 리듬: 펌핑 결과가 곧 콤보
            {
                if (cue == HydraulicCue.PumpGood) { Ladder.OnPerfect(); RaiseCombo(); }
                else if (cue == HydraulicCue.PumpMiss) { Ladder.OnMiss(); RaiseCombo(); }
            }

            // 외부 리듬: PumpGood은 증기/피스톤 연출만. 피치 사다리는 Rhythm.Perfect가 한 번만 올린다.
            Play(FeedbackKeys.Hydraulic(cue.ToString()), !external);
        }

        private void HandlePneumatic(PneumaticCue cue, PneumaticReport report) => Play(FeedbackKeys.Pneumatic(cue.ToString()));

        private void HandleLadder(LadderTestReport report, string message)
        {
            string key = report.passed ? FeedbackKeys.LadderPass
                       : report.interlockViolation ? FeedbackKeys.LadderInterlock
                       : report.unstable ? FeedbackKeys.LadderUnstable
                       : FeedbackKeys.LadderFail;
            Play(key);
        }

        private void HandleJudged(RhythmJudgement j, bool performed)
        {
            if (!performed) return;     // 숨 고르는 중: 콤보 유지 (PumpEmpty 큐가 따로 소리를 냄)
            switch (j.grade)
            {
                case RhythmGrade.Perfect: Ladder.OnPerfect(); RaiseCombo(); Play(FeedbackKeys.RhythmPerfect); break;
                case RhythmGrade.Good: Ladder.OnGood(); Play(FeedbackKeys.RhythmGood); break;
                default: Ladder.OnMiss(); RaiseCombo(); Play(FeedbackKeys.RhythmMiss); break;
            }
        }

        private void HandleFinished(PuzzleResult result)
        {
            if (result == PuzzleResult.Success) Play(FeedbackKeys.PuzzleSuccess);
            else if (result == PuzzleResult.Fail) Play(FeedbackKeys.PuzzleFail);
            Ladder.Reset();
            RaiseCombo();
            Detach();
        }

        private void RaiseCombo() => OnComboChanged?.Invoke(Ladder.Combo, Ladder.Semitones);

        // ───────── 실행 ─────────

        /// <summary>큐 이름으로 연출 재생. 퍼즐 외의 곳(예: 자원 채집 성공)에서도 직접 불러 쓸 수 있다.</summary>
        public void Play(string key, bool allowLadder = true)
        {
            var e = profile != null ? profile.Find(key) : null;
            if (e == null) return;

            SpawnParticle(e);
            PlaySfx(e, allowLadder);

            if (e.shake != ShakeGrade.None)
            {
                if (cameraShaker != null) cameraShaker.Shake(e.shake, 1f, e.shakeHold);
                else CameraShaker.ShakeStatic(e.shake);
            }
            if (hitstopEnabled && SettingsManager.Current.hitstopEnabled && e.hitstopSeconds > 0f) RequestHitstop(e.hitstopSeconds);
        }

        private void SpawnParticle(FeedbackEntry e)
        {
            if (e.particlePrefab == null) return;

            Vector3 pos; Quaternion rot = Quaternion.identity;
            var cam = Camera.main;
            if (e.particleAtCamera && cam != null)
            {
                pos = cam.transform.position + cam.transform.forward * 1.2f; rot = cam.transform.rotation;
            }
            else if (_session != null) pos = _session.transform.position + Vector3.up * particleHeight;
            else pos = cam != null ? cam.transform.position + cam.transform.forward * 2f : Vector3.zero;

            var go = Instantiate(e.particlePrefab, pos, rot);
            go.transform.localScale *= Mathf.Max(0.01f, e.particleScale);
            Destroy(go, e.particleLifetime > 0f ? e.particleLifetime : 3f);
        }

        private void PlaySfx(FeedbackEntry e, bool allowLadder)
        {
            if (e.sfx == null || e.sfx.Length == 0) return;
            int i = _picker.Pick(e.sfx.Length, () => UnityEngine.Random.value);
            var clip = i >= 0 ? e.sfx[i] : null;
            if (clip == null) return;

            float pitch = UnityEngine.Random.Range(Mathf.Min(e.pitchRange.x, e.pitchRange.y), Mathf.Max(e.pitchRange.x, e.pitchRange.y));
            if (e.usePitchLadder && allowLadder) pitch *= Ladder.PitchMultiplier;

            var src = _voices[_nextVoice];
            _nextVoice = (_nextVoice + 1) % _voices.Count;
            src.pitch = Mathf.Clamp(pitch, 0.5f, 2f);
            src.volume = e.volume * sfxVolume * AudioManager.SfxGain;
            src.clip = clip;
            src.Play();
        }

        // ───────── 히트스톱 ─────────

        private void RequestHitstop(float seconds)
        {
            if (_inHitstop || Time.timeScale <= 0f) return;    // 이미 멈췄거나 일시정지 중
            float granted = _hitstop.Request(seconds, Time.unscaledTime);
            if (granted > 0f) StartCoroutine(HitstopRoutine(granted));
        }

        private IEnumerator HitstopRoutine(float seconds)
        {
            _inHitstop = true;
            float previous = Time.timeScale;
            Time.timeScale = 0f;
            yield return new WaitForSecondsRealtime(seconds);
            // 멈춘 사이에 일시정지 메뉴가 열려 timeScale이 바뀌었다면 건드리지 않는다.
            if (Time.timeScale == 0f) Time.timeScale = previous;
            _inHitstop = false;
        }

        private void RestoreTime()
        {
            if (_inHitstop) { Time.timeScale = 1f; _inHitstop = false; }
        }

        // 설정 화면용
        public float SfxVolume { get => sfxVolume; set => sfxVolume = Mathf.Clamp01(value); }
        public bool HitstopEnabled { get => hitstopEnabled; set => hitstopEnabled = value; }
    }
}
