using System;
using AutoSurvival.Feedback;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using AutoSurvival.Puzzles;
using AutoSurvival.Survival;

namespace AutoSurvival.UI
{
    /// <summary>
    /// 메트로놈 UI: 유압 퍼즐 펌핑 단계에서 오피의 박자(기본 0.5초)를 눈으로 보여주고
    /// 스페이스바(또는 화면 버튼) 입력을 판정해 HydraulicPuzzle.ApplyStroke(품질)로 넘긴다.
    ///
    /// 표시: 오른쪽에서 가운데 '판정선'으로 흘러오는 음표(박자 하나당 하나). 판정선 주변 색 띠가 완벽/좋음 구간이다.
    ///       박자 순간에는 판정선이 번쩍이고(선택: 틱 소리), 입력하면 "완벽/좋음/빠름/늦음/헛바람" 글자가 뜬다.
    /// 판정: RhythmJudge 와 RhythmSession(연타 방지)을 쓴다. 퍼즐의 박자 값(BeatSeconds, PerfectWindow, OkWindow)을 그대로 읽으므로
    ///       퍼즐 인스펙터에서 박자를 바꾸면 UI도 자동으로 맞는다.
    ///
    /// [Canvas 연결 방법]
    ///  1) Canvas 아래에 패널(root)을 만들고 이 컴포넌트를 붙인다.
    ///  2) track(가로로 긴 RectTransform), hitLine(판정선 Image), perfectZone/goodZone(Image, 선택), notePrefab(작은 Image 프리팹)을 연결한다.
    ///  3) puzzle 슬롯에 HydraulicPuzzle을 넣거나, 기계가 퍼즐을 만들 때 Bind(puzzle)을 호출한다.
    ///     Bind 하면 UseExternalRhythm이 켜져서 퍼즐 내부 박자 처리는 꺼지고 이 UI가 전담한다.
    /// </summary>
    public class MetronomeUI : MonoBehaviour
    {
        [Header("패널")]
        [SerializeField] private GameObject root;

        [Header("트랙")]
        [SerializeField] private RectTransform track;
        [SerializeField] private Graphic hitLine;
        [SerializeField] private RectTransform perfectZone;     // 선택: 가운데 정렬된 띠 (너비는 코드가 설정)
        [SerializeField] private RectTransform goodZone;        // 선택
        [SerializeField] private RectTransform notePrefab;      // 음표 프리팹 (anchor 중앙, 크기는 프리팹 값)
        [Tooltip("트랙 오른쪽 끝에서 판정선까지 몇 박자 앞부터 음표를 보여줄지")] [SerializeField, Range(2, 8)] private int lookaheadBeats = 4;

        [Header("텍스트/게이지")]
        [SerializeField] private Text gradeText;
        [SerializeField] private Text hintText;                 // "스페이스바를 박자에 맞춰!"
        [SerializeField] private Slider staminaBar;             // 선택
        [SerializeField] private Slider progressBar;            // 선택: 리프트 진행도

        [Header("색상")]
        [SerializeField] private Color perfectColor = new Color(0.4f, 0.9f, 0.5f);
        [SerializeField] private Color goodColor = new Color(0.95f, 0.85f, 0.35f);
        [SerializeField] private Color missColor = new Color(0.9f, 0.4f, 0.35f);
        [SerializeField] private Color idleLineColor = new Color(1f, 1f, 1f, 0.6f);
        [SerializeField] private Color beatFlashColor = Color.white;
        [SerializeField] private float flashSeconds = 0.08f;
        [SerializeField] private float gradeShowSeconds = 0.5f;

        [Header("입력/소리")]
        [Tooltip("블루투스·모니터 지연 보정(초). 양수면 입력을 그만큼 일찍 눈금으로 본다.")] [SerializeField] private float inputLatencySeconds = 0f;
        [SerializeField] private AudioSource tickSource;        // 박자 틱 (선택)
        [SerializeField] private AudioClip tickClip;
        [SerializeField] private AudioClip hitClip;
        [SerializeField] private AudioClip missClip;

        [Header("자동 연결 (선택)")]
        [SerializeField] private HydraulicPuzzle puzzle;
        [Tooltip("기계가 유압 퍼즐을 시작하면 자동으로 Bind한다 (puzzle 슬롯을 비워 둬도 된다)")]
        [SerializeField] private bool autoBindToMachines = true;

        /// <summary>판정 결과 알림 (효과음/카메라 연출 등). 스태미나 부족이면 performed=false.</summary>
        public event Action<RhythmJudgement, bool> OnJudged;

        public bool IsActive { get; private set; }
        public float Clock => _clock;

        private RhythmJudge _judge;
        private RhythmSession _session;
        private float _clock, _prevClock;
        private float _flashTimer, _gradeTimer;
        private readonly List<RectTransform> _notes = new();

        private void OnEnable()
        {
            if (autoBindToMachines) AutoSurvival.Machines.AutomationMachine.PuzzleStarted += HandleMachinePuzzle;
            if (puzzle != null) Bind(puzzle);
            else SetVisible(false);
        }

        private void OnDisable()
        {
            AutoSurvival.Machines.AutomationMachine.PuzzleStarted -= HandleMachinePuzzle;
            Unbind();
        }

        private void HandleMachinePuzzle(AutoSurvival.Machines.AutomationMachine machine, PuzzleSession session, AutoSurvival.Data.PuzzleScenarioEntry scenario)
        {
            if (session is HydraulicPuzzle hydraulic) Bind(hydraulic);
        }

        // ───────── 연결 ─────────

        public void Bind(HydraulicPuzzle p)
        {
            Unbind();
            puzzle = p;
            if (puzzle == null) return;

            puzzle.UseExternalRhythm = true;                // 퍼즐의 내부 박자·키 처리를 끈다
            _combo.Reset();
            puzzle.OnPhaseChanged += HandlePhase;
            puzzle.OnProgress += HandleProgress;
            puzzle.OnCue += HandleCue;

            _judge = new RhythmJudge(puzzle.BeatSeconds, puzzle.PerfectWindow, puzzle.OkWindow);
            _session = new RhythmSession(_judge);
            LayoutZones();
            SetVisible(puzzle.IsRunning && puzzle.CurrentPhase == HydraulicPuzzle.Phase.Pumping);
            if (IsActive) StartClock();
        }

        public void Unbind()
        {
            if (puzzle == null) return;
            puzzle.OnPhaseChanged -= HandlePhase;
            puzzle.OnProgress -= HandleProgress;
            puzzle.OnCue -= HandleCue;
            puzzle.UseExternalRhythm = false;
            puzzle = null;
            SetVisible(false);
        }

        private void HandlePhase(HydraulicPuzzle.Phase phase)
        {
            SetVisible(phase == HydraulicPuzzle.Phase.Pumping);
            if (phase == HydraulicPuzzle.Phase.Pumping) StartClock();
        }

        private void HandleProgress(float p) { if (progressBar != null) progressBar.value = p; }

        private void HandleCue(HydraulicCue cue, HydraulicReport report)
        {
            if (cue == HydraulicCue.LiftComplete || cue == HydraulicCue.Overheat) SetVisible(false);
        }

        private void StartClock()
        {
            _clock = 0f; _prevClock = 0f;
            _session?.Reset();
            if (hintText != null) hintText.text = $"{KeyName(puzzle.PumpKey)} 를 오피의 박자에 맞춰 눌러!";
            if (progressBar != null) progressBar.value = 0f;
        }

        private static string KeyName(KeyCode k) => k == KeyCode.Space ? "스페이스바" : k.ToString();

        private void SetVisible(bool on)
        {
            IsActive = on;
            if (root != null) root.SetActive(on);
            if (!on) { ClearNotes(); }
        }

        // ───────── 레이아웃 ─────────

        private float HalfWidth => track != null ? track.rect.width * 0.5f : 300f;

        /// <summary>초당 몇 픽셀로 음표가 흐르는가.</summary>
        private float PxPerSecond => HalfWidth / (lookaheadBeats * (_judge?.BeatSeconds ?? 0.5f));

        private void LayoutZones()
        {
            if (_judge == null) return;
            SetZoneWidth(perfectZone, _judge.PerfectWindow);
            SetZoneWidth(goodZone, _judge.GoodWindow);
        }

        private void SetZoneWidth(RectTransform z, float windowSeconds)
        {
            if (z == null) return;
            z.anchorMin = new Vector2(0.5f, 0f); z.anchorMax = new Vector2(0.5f, 1f);
            z.pivot = new Vector2(0.5f, 0.5f);
            z.anchoredPosition = Vector2.zero;
            z.sizeDelta = new Vector2(2f * windowSeconds * PxPerSecond, 0f);
        }

        // ───────── 매 프레임 ─────────

        private void Update()
        {
            if (!IsActive || puzzle == null || !puzzle.IsRunning || _judge == null) return;

            _prevClock = _clock;
            _clock += Time.deltaTime;

            if (_judge.CrossedBeat(_prevClock, _clock))
            {
                _flashTimer = flashSeconds;
                if (tickSource != null && tickClip != null) tickSource.PlayOneShot(tickClip);
            }

            if (Input.GetKeyDown(puzzle.PumpKey)) Press();

            UpdateNotes();
            UpdateFeedback();
            UpdateStamina();
        }

        /// <summary>화면의 '펌프' 버튼에서도 호출할 수 있다 (모바일/마우스).</summary>
        public void Press()
        {
            if (!IsActive || puzzle == null || !puzzle.IsRunning || _session == null) return;

            var judgement = _session.Press(_clock, inputLatencySeconds);
            bool performed = puzzle.ApplyStroke(judgement.Quality);

            if (!performed)
            {
                ShowGrade("숨 고르는 중...", missColor, GradePunch.None);
            }
            else
            {
                switch (judgement.grade)
                {
                    case RhythmGrade.Perfect:
                        _combo.OnPerfect();
                        ShowGrade(_combo.Combo >= 2 ? $"완벽! x{_combo.Combo}" : "완벽!", perfectColor, GradePunch.Perfect);
                        Play(hitClip); break;
                    case RhythmGrade.Good:
                        _combo.OnGood();
                        ShowGrade(judgement.IsEarly ? "좋음 (빨랐어)" : "좋음 (늦었어)", goodColor, GradePunch.Good);
                        Play(hitClip); break;
                    default:
                        _combo.OnMiss();
                        ShowGrade(judgement.IsEarly ? "너무 빨라!" : "너무 늦어!", missColor, GradePunch.Miss);
                        Play(missClip); break;
                }
            }
            OnJudged?.Invoke(judgement, performed);
        }

        // PuzzleFeedback이 있으면 판정음은 그쪽(FeedbackProfile)이 낸다. 둘이 겹쳐 두 번 울리지 않도록 이쪽은 양보한다.
        private void Play(AudioClip c)
        {
            if (PuzzleFeedback.Instance != null) return;
            if (tickSource != null && c != null) tickSource.PlayOneShot(c);
        }

        private enum GradePunch { None, Perfect, Good, Miss }

        // 연속 완벽 콤보 (표시용). 규칙은 PuzzleFeedback의 피치 사다리와 같다: 완벽 +1, 좋음 유지, 헛바람 0.
        private readonly PitchLadder _combo = new();

        private void ShowGrade(string text, Color color, GradePunch punch = GradePunch.None)
        {
            if (gradeText == null) return;
            gradeText.text = text; gradeText.color = color;
            _gradeTimer = gradeShowSeconds;

            // 폴리싱 기획서 4장: 완벽 1.5 -> 1.0 (0.12초), 좋음 살짝, 헛바람은 이 팝업만 떨고 가라앉는다 (화면 전체 UI는 고정)
            var rt = gradeText.rectTransform;
            switch (punch)
            {
                case GradePunch.Perfect: UiTween.PunchScale(rt, 0.5f, 0.12f); break;
                case GradePunch.Good: UiTween.PunchScale(rt, 0.2f, 0.10f); break;
                case GradePunch.Miss: UiTween.WiggleX(rt, 8f, 0.2f, 3); break;
            }
        }

        private void UpdateFeedback()
        {
            if (hitLine != null)
            {
                _flashTimer = Mathf.Max(0f, _flashTimer - Time.deltaTime);
                hitLine.color = _flashTimer > 0f ? beatFlashColor : idleLineColor;
            }
            if (gradeText != null && _gradeTimer > 0f)
            {
                _gradeTimer -= Time.deltaTime;
                if (_gradeTimer <= 0f) gradeText.text = "";
            }
        }

        private void UpdateStamina()
        {
            var stats = puzzle != null ? puzzle.PlayerStats : null;
            if (staminaBar == null || stats == null) return;

            float n = stats.Get(StatType.Stamina);       // 정규화 0~1
            staminaBar.value = n;

            // 색: 청록 -> 노랑 -> 빨강, 25% 이하는 1Hz로 맥동. 색 자체는 경계에서 부드럽게 섞이므로 매 프레임 직접 지정한다.
            if (staminaBar.fillRect != null)
            {
                var fill = staminaBar.fillRect.GetComponent<Image>();
                if (fill != null)
                {
                    var c = StaminaPalette.Evaluate(n);
                    float pulse = StaminaPalette.Pulse(n, Time.unscaledTime);
                    fill.color = Color.Lerp(new Color(c.r, c.g, c.b, 1f), Color.white, pulse * 0.3f);
                }
            }
        }

        // ───────── 음표 ─────────

        private void UpdateNotes()
        {
            if (track == null || notePrefab == null) return;

            float beat = _judge.BeatSeconds;
            float px = PxPerSecond;
            float lookahead = lookaheadBeats * beat;

            // 화면에 있어야 할 박자 범위: 판정창 지난 직후 ~ lookahead 앞
            int first = Mathf.Max(0, Mathf.CeilToInt((_clock - _judge.GoodWindow) / beat));
            int last = Mathf.FloorToInt((_clock + lookahead) / beat);
            int count = Mathf.Max(0, last - first + 1);

            while (_notes.Count < count)
            {
                var n = Instantiate(notePrefab, track);
                n.gameObject.SetActive(true);
                _notes.Add(n);
            }
            for (int i = 0; i < _notes.Count; i++)
            {
                bool used = i < count;
                _notes[i].gameObject.SetActive(used);
                if (!used) continue;
                float dt = (first + i) * beat - _clock;          // 박자까지 남은 시간 (+ 앞으로 다가옴)
                _notes[i].anchoredPosition = new Vector2(dt * px, 0f);
            }
        }

        private void ClearNotes()
        {
            foreach (var n in _notes) if (n != null) n.gameObject.SetActive(false);
        }
    }
}
