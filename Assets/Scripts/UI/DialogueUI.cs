using UnityEngine;
using UnityEngine.UI;
using AutoSurvival.Dialogue;

namespace AutoSurvival.UI
{
    /// <summary>
    /// 오피 말풍선 UI. OopiCompanion.OnSpeak를 구독해 대사를 큐(DialoguePlayer)에 쌓고,
    /// 여러 대사가 한꺼번에 들어와도 순서대로 한 줄씩 타자 효과로 보여 준다.
    ///
    /// [Canvas 연결 방법]
    ///  1) Canvas 아래에 말풍선 패널(root)을 만들고 이 컴포넌트를 붙인다 (root는 꺼 둬도 된다. 대사가 있을 때만 켜진다).
    ///  2) lineText(본문), tipText(기능사 지식 한 줄, 선택), portrait(오피 얼굴, 선택)를 연결한다.
    ///     moodSprites는 OopiMood 순서(Neutral, Cheer, Worry, Joke, Serious)로 넣는다.
    ///  3) companion 슬롯에 OopiCompanion을 넣는다 (씬 전환 후 생기는 경우 Bind를 호출).
    /// 건너뛰기: 말풍선 클릭(skipButton) 또는 skipKey(기본 Tab). Space는 유압 펌핑 키라 쓰지 않는다.
    /// </summary>
    public class DialogueUI : MonoBehaviour
    {
        [Header("연결")]
        [SerializeField] private OopiCompanion companion;
        [SerializeField] private GameObject root;
        [SerializeField] private CanvasGroup group;               // 선택: 페이드 인/아웃
        [SerializeField] private Text lineText;
        [SerializeField] private Text tipText;
        [SerializeField] private Image portrait;
        [SerializeField] private Sprite[] moodSprites;            // OopiMood 순서
        [SerializeField] private Button skipButton;
        [SerializeField] private KeyCode skipKey = KeyCode.Tab;

        [Header("재생 속도")]
        [SerializeField] private float charsPerSecond = 30f;
        [SerializeField] private float minHoldSeconds = 1.8f;
        [SerializeField] private float holdPerChar = 0.05f;
        [SerializeField, Range(1, 12)] private int maxQueue = 6;
        [SerializeField] private float fadeSeconds = 0.15f;

        [Header("소리 (선택)")]
        [SerializeField] private AudioSource blipSource;
        [SerializeField] private AudioClip blipClip;
        [SerializeField, Range(1, 8)] private int blipEveryChars = 3;

        [Tooltip("말풍선이 대사를 보여 주므로 오피의 토스트 알림은 끈다")]
        [SerializeField] private bool disableCompanionToast = true;

        public DialoguePlayer Player { get; private set; }

        private OopiCompanion _bound;
        private float _alpha;
        private int _lastBlipChars;

        private void Awake()
        {
            Player = new DialoguePlayer();
            ApplySettings();
            if (skipButton != null) skipButton.onClick.AddListener(Skip);
            SetVisible(0f);
        }

        private void OnEnable()
        {
            // 두 번째 섬부터는 씬의 _Systems 복사본이 사라지고 처음 씬의 오피만 남는다. 끊어진 참조는 살아 있는 오피로 바꾼다.
            if ((companion == null || (OopiCompanion.Active != null && companion != OopiCompanion.Active)) && OopiCompanion.Active != null)
                companion = OopiCompanion.Active;
            if (companion != null) Bind(companion);
        }
        private void OnDisable() => Unbind();

        public void Bind(OopiCompanion c)
        {
            Unbind();
            _bound = c;
            if (_bound == null) return;
            _bound.OnSpeak += HandleSpeak;
            if (disableCompanionToast) _bound.AlsoShowAsToast = false;
        }

        private void Unbind()
        {
            if (_bound != null) _bound.OnSpeak -= HandleSpeak;
            _bound = null;
        }

        private void ApplySettings()
        {
            Player.CharsPerSecond = charsPerSecond;
            Player.MinHoldSeconds = minHoldSeconds;
            Player.HoldPerChar = holdPerChar;
            Player.MaxQueue = maxQueue;
        }

        private void HandleSpeak(OopiSpeech speech)
        {
            Player.Enqueue(new DialogueEntry
            {
                id = speech.line.id,
                text = speech.text,
                tip = speech.tip,
                mood = speech.line.mood,
                situation = speech.line.situation,
                priority = DialoguePriority.Of(speech.line.situation),
            });
        }

        /// <summary>DB와 무관한 임의의 안내 문구(튜토리얼 등)를 오피 말투로 보여 줄 때.</summary>
        public void Say(string text, OopiMood mood = OopiMood.Neutral, string tip = null)
        {
            Player.Enqueue(new DialogueEntry { id = "ui:" + text, text = text, tip = tip, mood = mood, situation = OopiSituation.Success, priority = 2 });
        }

        public void Skip() => Player.Skip();

        private void Update()
        {
            ApplySettings();
            if (GameInput.KeyDown(skipKey)) Skip();

            var before = Player.Current;
            Player.Tick(Time.unscaledDeltaTime);
            var cur = Player.Current;

            if (cur != before) OnLineChanged(cur);

            if (cur != null)
            {
                if (lineText != null) lineText.text = cur.text.Substring(0, Mathf.Min(Player.VisibleChars, cur.text.Length));
                if (tipText != null)
                {
                    tipText.gameObject.SetActive(Player.TipVisible);
                    if (Player.TipVisible) tipText.text = cur.tip;
                }
                TickBlip();
            }

            // 대사가 있으면 서서히 나타나고, 없으면 서서히 사라진다
            float target = cur != null ? 1f : 0f;
            _alpha = fadeSeconds <= 0f ? target : Mathf.MoveTowards(_alpha, target, Time.unscaledDeltaTime / fadeSeconds);
            SetVisible(_alpha);
        }

        private void OnLineChanged(DialogueEntry cur)
        {
            _lastBlipChars = 0;
            if (cur == null) return;

            // 새 대사가 시작될 때 말풍선이 막 나타나는 중이면 팝인, 이미 떠 있으면 초상화만 살짝 튄다
            if (root != null && _alpha < 0.05f && root.transform is RectTransform bubble) UiTween.PopIn(bubble, 0.92f, 0.14f);
            else if (portrait != null) UiTween.PunchScale(portrait.transform, 0.1f, 0.12f);
            if (lineText != null) lineText.text = "";
            if (tipText != null) tipText.gameObject.SetActive(false);
            if (portrait != null && moodSprites != null)
            {
                int i = (int)cur.mood;
                if (i >= 0 && i < moodSprites.Length && moodSprites[i] != null) portrait.sprite = moodSprites[i];
            }
        }

        private void TickBlip()
        {
            if (blipSource == null || blipClip == null) return;
            if (Player.VisibleChars - _lastBlipChars >= blipEveryChars)
            {
                _lastBlipChars = Player.VisibleChars;
                blipSource.PlayOneShot(blipClip);
            }
        }

        private void SetVisible(float alpha)
        {
            if (group != null) group.alpha = alpha;
            if (root != null) root.SetActive(alpha > 0.001f || Player.Current != null);
        }
    }
}
