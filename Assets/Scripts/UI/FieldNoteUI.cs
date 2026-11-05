using System;
using UnityEngine;
using UnityEngine.UI;
using AutoSurvival.Data;
using AutoSurvival.Machines;

namespace AutoSurvival.UI
{
    /// <summary>
    /// 현장 노트 UI: 퍼즐이 끝난 직후 오피의 대사와 4지선다 문제를 보여주고,
    /// 답을 고르면 정오답을 PuzzleScenarioDB.RecordAnswer -> KnowledgeTracker에 기록한다.
    ///
    /// [Canvas 연결 방법]
    ///  1) Canvas 아래에 패널(root)을 만들고 이 컴포넌트를 붙인다.
    ///  2) Text(오피 대사, 출처, 문제, 결과, 해설) + Button 4개(각 버튼 자식에 Text) + 닫기 Button을 Inspector에 드래그한다.
    ///  3) 기계의 OnFieldNote를 받으려면 BindMachine(machine)을 호출하거나 machines 배열에 넣는다.
    ///     직접 열려면 Show(db, question, opieLine).
    /// 패널은 처음엔 꺼져 있어도 된다 (root를 켜고 끄는 방식).
    /// </summary>
    public class FieldNoteUI : MonoBehaviour
    {
        [Header("패널")]
        [SerializeField] private GameObject root;

        [Header("텍스트")]
        [SerializeField] private Text opieLineText;          // 오피의 대사 (fieldNote)
        [SerializeField] private Text sourceText;            // "2024년 1회 기출" / "자체 문제"
        [SerializeField] private Text questionText;
        [SerializeField] private Text resultText;            // 정답/오답 + 해설
        [SerializeField] private Text progressText;          // 선택 사항: "현장 노트"

        [Header("버튼")]
        [SerializeField] private Button[] choiceButtons = new Button[4];
        [SerializeField] private Button closeButton;

        [Header("색상")]
        [SerializeField] private Color correctColor = new Color(0.55f, 0.85f, 0.5f);
        [SerializeField] private Color wrongColor = new Color(0.95f, 0.5f, 0.45f);
        [SerializeField] private Color normalColor = Color.white;

        [Header("옵션")]
        [Tooltip("보기 순서를 섞는다 (위치 암기 방지)")] [SerializeField] private bool shuffleChoices = true;
        [Tooltip("글자가 한 글자씩 나오는 속도 (초/글자). 0이면 즉시.")] [SerializeField] private float typewriterSeconds = 0.02f;
        [SerializeField] private bool allowNumberKeys = true;     // 1~4 키로 선택
        [Tooltip("자동으로 구독할 기계들 (선택)")] [SerializeField] private AutomationMachine[] machines;

        /// <summary>(문제, 정답 여부). 보상 지급, 효과음, 통계 등에 사용.</summary>
        public event Action<ExamQuestion, bool> OnAnswered;
        public event Action OnClosed;

        public bool IsOpen { get; private set; }
        public bool Answered { get; private set; }
        public ExamQuestion Current { get; private set; }

        private PuzzleScenarioDB _db;
        private ShuffledChoices _view;
        private string _fullLine;
        private float _typeTimer;
        private int _typedChars;

        private void Awake()
        {
            for (int i = 0; i < choiceButtons.Length; i++)
            {
                int captured = i;
                if (choiceButtons[i] != null) choiceButtons[i].onClick.AddListener(() => Choose(captured));
            }
            if (closeButton != null) closeButton.onClick.AddListener(Close);
            if (root != null && root != gameObject) root.SetActive(false);
        }

        private void OnEnable()
        {
            if (machines == null) return;
            foreach (var m in machines) BindMachine(m);
        }

        private void OnDisable()
        {
            if (machines == null) return;
            foreach (var m in machines) UnbindMachine(m);
        }

        public void BindMachine(AutomationMachine m) { if (m != null) { m.OnFieldNote -= HandleFieldNote; m.OnFieldNote += HandleFieldNote; } }
        public void UnbindMachine(AutomationMachine m) { if (m != null) m.OnFieldNote -= HandleFieldNote; }

        private void HandleFieldNote(AutomationMachine machine, ExamQuestion question, PuzzleScenarioEntry scenario)
        {
            Show(machine.ScenarioDb, question, null);
        }

        // ───────── 표시 ─────────

        /// <summary>opieLine이 null이면 문제의 fieldNote를 쓴다.</summary>
        public void Show(PuzzleScenarioDB db, ExamQuestion question, string opieLine = null)
        {
            if (question == null || question.choices == null) return;

            _db = db;
            Current = question;
            Answered = false;
            IsOpen = true;

            _view = shuffleChoices
                ? ChoiceShuffler.Shuffle(question.choices, question.answerIndex, () => UnityEngine.Random.value)
                : IdentityOrder(question);

            if (root != null) root.SetActive(true);

            SetText(sourceText, SourceLabel(question));
            SetText(questionText, question.question);
            SetText(resultText, "");
            SetText(progressText, "현장 노트");

            for (int i = 0; i < choiceButtons.Length; i++)
            {
                var b = choiceButtons[i];
                if (b == null) continue;
                bool used = i < _view.choices.Length;
                b.gameObject.SetActive(used);
                if (!used) continue;
                b.interactable = true;
                SetButtonColor(b, normalColor);
                var label = b.GetComponentInChildren<Text>();
                if (label != null) label.text = $"{i + 1}. {_view.choices[i]}";
            }
            if (closeButton != null) closeButton.gameObject.SetActive(false);   // 답을 고른 뒤에 나타난다

            _fullLine = string.IsNullOrEmpty(opieLine) ? question.fieldNote : opieLine;
            _typedChars = typewriterSeconds <= 0f ? (_fullLine?.Length ?? 0) : 0;
            _typeTimer = 0f;
            SetText(opieLineText, _fullLine?.Substring(0, _typedChars) ?? "");
        }

        private static ShuffledChoices IdentityOrder(ExamQuestion q)
        {
            var order = new int[q.choices.Length];
            for (int i = 0; i < order.Length; i++) order[i] = i;
            return new ShuffledChoices { choices = (string[])q.choices.Clone(), originalIndex = order, answerIndex = q.answerIndex };
        }

        private static string SourceLabel(ExamQuestion q) =>
            q.origin == QuestionOrigin.PastExam
                ? (string.IsNullOrEmpty(q.examLabel) ? "기출문제" : $"{q.examLabel} 기출")
                : "연습 문제";

        // ───────── 답 ─────────

        /// <summary>화면 기준 번호(0~3)로 답한다. 한 번만 인정된다.</summary>
        public void Choose(int shuffledIndex)
        {
            if (!IsOpen || Answered || Current == null || _view == null) return;
            if (shuffledIndex < 0 || shuffledIndex >= _view.choices.Length) return;

            Answered = true;
            bool correct = shuffledIndex == _view.answerIndex;

            // 기록은 원래 번호로: DB가 answerIndex(원래 번호)와 비교하고 KnowledgeTracker에 보고한다.
            if (_db != null) _db.RecordAnswer(Current, _view.ToOriginal(shuffledIndex));
            else AutoSurvival.Core.KnowledgeTracker.Instance?.Report(Current.concept, correct);

            // 시각 피드백
            for (int i = 0; i < choiceButtons.Length && i < _view.choices.Length; i++)
            {
                var b = choiceButtons[i];
                if (b == null) continue;
                b.interactable = false;
                if (i == _view.answerIndex) SetButtonColor(b, correctColor);
                else if (i == shuffledIndex) SetButtonColor(b, wrongColor);
            }

            string head = correct ? "정답!" : $"아쉬워. 정답은 {_view.answerIndex + 1}번이야.";
            SetText(resultText, string.IsNullOrEmpty(Current.explanation) ? head : $"{head}\n{Current.explanation}");
            if (closeButton != null) closeButton.gameObject.SetActive(true);

            // 대사를 즉시 끝까지 보여준다
            _typedChars = _fullLine?.Length ?? 0;
            SetText(opieLineText, _fullLine ?? "");

            OnAnswered?.Invoke(Current, correct);
        }

        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            if (root != null) root.SetActive(false);
            OnClosed?.Invoke();
        }

        // ───────── 매 프레임 ─────────

        private void Update()
        {
            if (!IsOpen) return;

            // 타이프라이터
            if (_fullLine != null && _typedChars < _fullLine.Length && typewriterSeconds > 0f)
            {
                _typeTimer += Time.unscaledDeltaTime;
                int add = Mathf.FloorToInt(_typeTimer / typewriterSeconds);
                if (add > 0)
                {
                    _typeTimer -= add * typewriterSeconds;
                    _typedChars = Mathf.Min(_fullLine.Length, _typedChars + add);
                    SetText(opieLineText, _fullLine.Substring(0, _typedChars));
                }
            }

            if (allowNumberKeys && !Answered)
            {
                for (int i = 0; i < 4; i++)
                    if (GameInput.KeyDown(KeyCode.Alpha1 + i)) { Choose(i); break; }
            }
            if (Answered && (GameInput.KeyDown(KeyCode.Return) || GameInput.KeyDown(KeyCode.KeypadEnter))) Close();
        }

        // ───────── 도우미 ─────────

        private static void SetText(Text t, string s) { if (t != null) t.text = s ?? ""; }

        private static void SetButtonColor(Button b, Color c)
        {
            var colors = b.colors;
            colors.disabledColor = c;     // 답 이후엔 interactable=false 이므로 disabledColor로 결과 색을 표시
            colors.normalColor = c;
            b.colors = colors;
            if (b.targetGraphic != null) b.targetGraphic.color = c;
        }
    }
}
