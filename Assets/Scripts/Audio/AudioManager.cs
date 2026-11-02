using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using AutoSurvival.Machines;
using AutoSurvival.Puzzles;
using AutoSurvival.Settings;
using AutoSurvival.Data;

namespace AutoSurvival.Audio
{
    [Serializable] public class MachineLoopClip { public MachineKind kind; public AudioClip clip; [Range(0f, 1f)] public float volume = 0.6f; }
    [Serializable] public class AmbientClip { [Tooltip("씬 이름. 예: Island_Landing, MainMenu")] public string sceneName; public AudioClip clip; [Range(0f, 1f)] public float volume = 0.7f; }

    /// <summary>
    /// 오디오 관리자. 폴리싱 기획서의 레이어 구조:
    ///   ① 환경음  : 씬마다 하나의 루프 (섬이 바뀌면 크로스페이드)
    ///   ② 기계 루프음: 가동 중(Running)인 기계에 붙는 3D 루프. 티어가 높을수록 피치가 올라간다.
    ///   ③ 퍼즐 효과음: PuzzleFeedback이 재생한다. 이 매니저는 크기(SfxGain)만 알려 준다.
    /// 퍼즐이 시작되면 ①②를 더킹(낮춤)하고, 끝나면 천천히 되돌린다. ③은 영향을 받지 않는다.
    /// 크기 = 마스터 x 버스(설정 화면 슬라이더) x 더킹 x 개별 클립 크기.
    ///
    /// 클립이 비어 있어도 에러 없이 조용히 넘어간다. 먼저 코드를 붙이고 소리는 나중에 채워 넣으면 된다.
    /// </summary>
    public class AudioManager : MonoBehaviour
    {
        public static AudioManager Instance { get; private set; }

        [Header("환경음")]
        [SerializeField] private AmbientClip[] ambientByScene = Array.Empty<AmbientClip>();
        [SerializeField] private AudioClip defaultAmbient;
        [SerializeField] private float ambientFadeSeconds = 2f;

        [Header("기계 루프음")]
        [SerializeField] private MachineLoopClip[] machineLoops = Array.Empty<MachineLoopClip>();
        [SerializeField] private float loopMinDistance = 2f;
        [SerializeField] private float loopMaxDistance = 25f;

        [Header("더킹 (퍼즐 중 배경음 낮추기)")]
        [Tooltip("퍼즐 중 환경음·기계음의 크기 (0.35 = 약 -9dB)")]
        [SerializeField, Range(0f, 1f)] private float puzzleDuckGain = 0.35f;
        [SerializeField] private float duckAttackSeconds = 0.25f;
        [SerializeField] private float duckReleaseSeconds = 0.9f;

        private readonly DuckingMixer _duck = new();
        private readonly Dictionary<AutomationMachine, AudioSource> _loops = new();
        private AudioSource _ambientA, _ambientB;       // A = 지금 소리, B = 새로 들어오는 소리
        private float _ambientVolumeA, _ambientVolumeB;
        private float _fade = 1f;                       // 0~1 (1이면 크로스페이드 끝)
        private PuzzleSession _puzzle;
        private bool _puzzleDuck;
        private float _resyncTimer;

        // ───────── 다른 스크립트가 쓰는 창구 ─────────

        /// <summary>효과음 크기 (마스터 x 효과음 슬라이더). 매니저가 없으면 1.</summary>
        public static float SfxGain => Instance != null ? Instance.CurrentSfxGain : 1f;
        public float CurrentSfxGain => SettingsManager.Current.MasterGain * SettingsManager.Current.SfxGain;
        public float CurrentAmbientGain => SettingsManager.Current.MasterGain * SettingsManager.Current.AmbientGain;
        public float DuckGain => _duck.Gain;

        /// <summary>임시로 배경음을 낮춘다 (id가 같으면 같은 요청). 예: 중요한 대사 중에 RequestDuck("dialogue", 0.6f)</summary>
        public void RequestDuck(string id, float gain) => _duck.Request(id, gain);
        public void ReleaseDuck(string id) => _duck.Release(id);

        // ───────── 수명 ─────────

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
            if (transform.parent == null) DontDestroyOnLoad(gameObject);

            _ambientA = MakeSource("AmbientA"); _ambientB = MakeSource("AmbientB");
            _duck.AttackSeconds = duckAttackSeconds; _duck.ReleaseSeconds = duckReleaseSeconds;
        }

        private AudioSource MakeSource(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var s = go.AddComponent<AudioSource>();
            s.loop = true; s.playOnAwake = false; s.spatialBlend = 0f; s.volume = 0f;
            return s;
        }

        private void OnEnable()
        {
            AutomationMachine.PuzzleStarted += HandlePuzzleStarted;
            AutomationMachine.Changed += HandleMachineChanged;
            SceneManager.sceneLoaded += HandleSceneLoaded;
            PlayAmbientForScene(SceneManager.GetActiveScene().name);
        }

        private void OnDisable()
        {
            AutomationMachine.PuzzleStarted -= HandlePuzzleStarted;
            AutomationMachine.Changed -= HandleMachineChanged;
            SceneManager.sceneLoaded -= HandleSceneLoaded;
        }

        private void OnDestroy() { if (Instance == this) Instance = null; }

        // ───────── 이벤트 ─────────

        private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            _loops.Clear();                                   // 옛 씬의 기계는 사라졌다
            PlayAmbientForScene(scene.name);
            _resyncTimer = 0.2f;                              // 새 씬의 기계가 등록된 뒤 동기화
        }

        private void HandlePuzzleStarted(AutomationMachine machine, PuzzleSession session, PuzzleScenarioEntry scenario)
        {
            if (_puzzle != null) _puzzle.OnFinished -= HandlePuzzleFinished;
            _puzzle = session;
            if (session == null) return;
            session.OnFinished += HandlePuzzleFinished;
            _puzzleDuck = true;
            _duck.Request("puzzle", puzzleDuckGain);
        }

        private void HandlePuzzleFinished(PuzzleResult result)
        {
            if (_puzzle != null) _puzzle.OnFinished -= HandlePuzzleFinished;
            _puzzle = null;
            _puzzleDuck = false;
            _duck.Release("puzzle");
        }

        private void HandleMachineChanged(AutomationMachine machine) => SyncMachine(machine);

        // ───────── 환경음 ─────────

        public void PlayAmbientForScene(string sceneName)
        {
            AudioClip clip = defaultAmbient; float volume = 0.7f;
            foreach (var a in ambientByScene)
                if (a != null && a.sceneName == sceneName && a.clip != null) { clip = a.clip; volume = a.volume; break; }
            PlayAmbient(clip, volume);
        }

        /// <summary>환경음을 바꾼다. 같은 클립이면 그대로 두고, 다르면 fade 시간 동안 크로스페이드.</summary>
        public void PlayAmbient(AudioClip clip, float volume = 0.7f)
        {
            if (_ambientA.clip == clip && _fade >= 1f) { _ambientVolumeA = volume; return; }
            if (_ambientB.clip == clip && _fade < 1f) { _ambientVolumeB = volume; return; }

            // 크로스페이드 도중에 또 바뀌면 지금 들리는 쪽(A)을 B로 대체하고 새로 시작
            if (_fade < 1f) { SwapToFinished(); }
            _ambientB.clip = clip; _ambientVolumeB = volume;
            if (clip != null) { _ambientB.volume = 0f; _ambientB.Play(); } else _ambientB.Stop();
            _fade = ambientFadeSeconds <= 0f ? 1f : 0f;
            if (_fade >= 1f) SwapToFinished();
        }

        private void SwapToFinished()
        {
            (_ambientA, _ambientB) = (_ambientB, _ambientA);
            (_ambientVolumeA, _ambientVolumeB) = (_ambientVolumeB, _ambientVolumeA);
            _ambientB.Stop(); _ambientB.clip = null; _ambientB.volume = 0f;
            _fade = 1f;
        }

        // ───────── 기계 루프음 ─────────

        private MachineLoopClip LoopFor(MachineKind kind)
        {
            foreach (var l in machineLoops) if (l != null && l.kind == kind && l.clip != null) return l;
            return null;
        }

        private void SyncMachine(AutomationMachine m)
        {
            if (m == null) return;
            var def = LoopFor(m.Kind);
            bool shouldPlay = def != null && m.State == MachineState.Running;

            _loops.TryGetValue(m, out var src);
            if (!shouldPlay)
            {
                if (src != null) { src.Stop(); }
                return;
            }

            if (src == null)
            {
                var go = new GameObject("LoopAudio");
                go.transform.SetParent(m.transform, false);
                src = go.AddComponent<AudioSource>();
                src.loop = true; src.playOnAwake = false; src.spatialBlend = 1f;
                src.rolloffMode = AudioRolloffMode.Linear;
                src.minDistance = loopMinDistance; src.maxDistance = loopMaxDistance;
                _loops[m] = src;
            }
            if (src.clip != def.clip) src.clip = def.clip;
            src.pitch = AudioMath.MachineLoopPitch(m.Tier);
            if (!src.isPlaying) src.Play();
        }

        private void ResyncAllMachines()
        {
            foreach (var m in AutomationMachine.All) SyncMachine(m);
            // 사라진 기계 정리
            List<AutomationMachine> dead = null;
            foreach (var kv in _loops) if (kv.Key == null) (dead ??= new()).Add(kv.Key);
            if (dead != null) foreach (var k in dead) _loops.Remove(k);
        }

        // ───────── 매 프레임 ─────────

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;

            // 퍼즐이 비정상 종료되어(오브젝트 파괴 등) 이벤트가 안 왔어도 더킹이 풀리게 한다
            if (_puzzleDuck && (_puzzle == null || !_puzzle.IsRunning)) HandlePuzzleFinished(PuzzleResult.Aborted);
            _duck.Step(dt);

            _resyncTimer -= dt;
            if (_resyncTimer <= 0f) { ResyncAllMachines(); _resyncTimer = 1f; }

            float bus = CurrentAmbientGain * _duck.Gain;

            // 환경음 크로스페이드
            if (_fade < 1f)
            {
                _fade = Mathf.Min(1f, _fade + dt / Mathf.Max(0.01f, ambientFadeSeconds));
                var (o, i) = AudioMath.EqualPowerCrossfade(_fade);
                _ambientA.volume = _ambientVolumeA * o * bus;
                _ambientB.volume = _ambientVolumeB * i * bus;
                if (_fade >= 1f) SwapToFinished();
            }
            else _ambientA.volume = _ambientVolumeA * bus;

            // 기계 루프음: 같은 종류가 여러 대면 합이 커지지 않게 보정
            int running = 0;
            foreach (var kv in _loops) if (kv.Value != null && kv.Value.isPlaying) running++;
            float crowd = AudioMath.CrowdGain(running);
            foreach (var kv in _loops)
            {
                if (kv.Value == null || kv.Key == null) continue;
                var def = LoopFor(kv.Key.Kind);
                kv.Value.volume = (def != null ? def.volume : 0f) * crowd * bus;
            }
        }
    }
}
