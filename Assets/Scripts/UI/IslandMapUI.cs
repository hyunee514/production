using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using AutoSurvival.Save;

namespace AutoSurvival.UI
{
    [Serializable]
    public class IslandNodeWidget
    {
        public string islandId;             // IslandDef.id 와 같은 값 (예: island.landing)
        public Button button;
        public Text nameText;
        public Text captionText;            // "현재 위치" / "이동 가능" / "조건 부족" ...
        public Graphic tint;
        public GameObject currentMarker;
        public GameObject clearedMark;
        public GameObject lockIcon;
    }

    /// <summary>
    /// 섬 지도 UI: 섬을 고르면 GameManager.CheckTravel(= GameProgress.CheckTravel)로 이동 가능 여부를 판정하고,
    /// 실패하면 세 조건(설비 복구, 지식 관문, 항해 보급) 중 무엇이 부족한지를 목록으로 보여 준다.
    /// 이동 버튼을 누르면 GameManager.TravelTo가 다시 판정하고, 통과하면 보급을 차감하고 섬을 옮긴다.
    ///
    /// [Canvas 연결 방법]
    ///  1) nodes에 섬마다 버튼·이름·캡션을 연결하고 islandId를 IslandDef.id와 똑같이 적는다.
    ///  2) 오른쪽 상세 패널: titleText, briefingText, lineContainer(세로 레이아웃), lineTemplate(Text 한 개, 비활성), travelButton.
    ///  3) Open() / Toggle()을 호출하거나 toggleKey(기본 M)를 쓴다. 열려 있는 동안 0.5초마다 보급·숙련도 변화를 다시 판정한다.
    /// </summary>
    public class IslandMapUI : MonoBehaviour
    {
        [SerializeField] private GameObject root;
        [SerializeField] private IslandNodeWidget[] nodes;
        [SerializeField] private KeyCode toggleKey = KeyCode.M;

        [Header("상세 패널")]
        [SerializeField] private Text titleText;
        [SerializeField] private Text briefingText;
        [SerializeField] private RectTransform lineContainer;
        [SerializeField] private Text lineTemplate;
        [SerializeField] private Button travelButton;
        [SerializeField] private Text travelButtonLabel;
        [SerializeField] private Text messageText;
        [SerializeField] private Button closeButton;

        [Header("색상")]
        [SerializeField] private Color currentColor = new Color(0.7f, 0.85f, 1f);
        [SerializeField] private Color visitedColor = new Color(0.85f, 0.9f, 0.85f);
        [SerializeField] private Color nextReadyColor = new Color(0.8f, 0.95f, 0.7f);
        [SerializeField] private Color nextBlockedColor = new Color(0.98f, 0.9f, 0.7f);
        [SerializeField] private Color lockedColor = new Color(0.7f, 0.7f, 0.7f);
        [SerializeField] private Color passColor = new Color(0.25f, 0.6f, 0.3f);
        [SerializeField] private Color failColor = new Color(0.8f, 0.3f, 0.25f);
        [SerializeField] private Color detailColor = new Color(0.35f, 0.35f, 0.35f);
        [SerializeField] private Color neutralColor = new Color(0.5f, 0.5f, 0.5f);

        public bool IsOpen { get; private set; }
        public string SelectedId { get; private set; }

        private GameManager _gm;
        private List<IslandNodeView> _views = new();
        private readonly List<Text> _linePool = new();
        private float _nextPoll;

        private void Awake()
        {
            if (nodes != null)
                foreach (var n in nodes)
                {
                    var captured = n;
                    if (captured != null && captured.button != null) captured.button.onClick.AddListener(() => Select(captured.islandId));
                }
            if (travelButton != null) travelButton.onClick.AddListener(OnTravelClicked);
            if (closeButton != null) closeButton.onClick.AddListener(Close);
            if (lineTemplate != null) lineTemplate.gameObject.SetActive(false);
            if (root != null && root != gameObject) root.SetActive(false);
        }

        private void OnDisable() => Unbind();

        // ───────── 열기/닫기 ─────────

        public void Open()
        {
            Bind(GameManager.Instance);
            IsOpen = true;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            if (root != null) root.SetActive(true);
            SetMessage("");

            // 처음에는 현재 섬 다음 칸(없으면 현재 섬)을 보여 준다
            var progress = _gm != null ? _gm.Progress : null;
            if (progress != null)
            {
                var next = progress.Next(progress.Current);
                SelectedId = (next ?? progress.Current)?.id;
            }
            Refresh();
        }

        public void Close()
        {
            IsOpen = false;
            if (root != null) root.SetActive(false);
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        public void Toggle() { if (IsOpen) Close(); else Open(); }

        private void Bind(GameManager gm)
        {
            if (_gm == gm) return;
            Unbind();
            _gm = gm;
            if (_gm == null) return;
            _gm.OnTravelDenied += HandleDenied;
            _gm.OnArrived += HandleArrived;
        }

        private void Unbind()
        {
            if (_gm == null) return;
            _gm.OnTravelDenied -= HandleDenied;
            _gm.OnArrived -= HandleArrived;
            _gm = null;
        }

        private void HandleDenied(TravelCheck check)
        {
            if (!IsOpen) return;
            SetMessage(check.reason);
            Refresh();
        }

        private void HandleArrived(IslandDef island) => Close();

        // ───────── 선택 / 이동 ─────────

        public void Select(string islandId)
        {
            SelectedId = islandId;
            SetMessage("");
            Refresh();
        }

        private void OnTravelClicked()
        {
            if (_gm == null || string.IsNullOrEmpty(SelectedId)) return;
            // 판정은 GameManager -> GameProgress.CheckTravel. 실패하면 OnTravelDenied로 사유가 돌아온다.
            _gm.TravelTo(SelectedId);
        }

        // ───────── 표시 ─────────

        public void Refresh()
        {
            if (_gm == null || _gm.Progress == null) return;

            var (food, water) = _gm.Supplies();
            _views = TravelPresenter.BuildNodes(_gm.Progress, GameManager.MasteryOf, food, water);

            foreach (var w in nodes ?? Array.Empty<IslandNodeWidget>())
            {
                var v = _views.Find(x => x.def.id == w.islandId);
                if (v == null) { if (w.button != null) w.button.gameObject.SetActive(false); continue; }
                DrawNode(w, v);
            }
            DrawDetail(_views.Find(x => x.def.id == SelectedId));
            _nextPoll = Time.unscaledTime + 0.5f;
        }

        private void DrawNode(IslandNodeWidget w, IslandNodeView v)
        {
            if (w.button != null) { w.button.gameObject.SetActive(true); w.button.interactable = true; }
            if (w.nameText != null) w.nameText.text = v.def.displayName;
            if (w.captionText != null) w.captionText.text = v.caption;
            if (w.currentMarker != null) w.currentMarker.SetActive(v.state == IslandNodeState.Current);
            if (w.clearedMark != null) w.clearedMark.SetActive(v.cleared);
            if (w.lockIcon != null) w.lockIcon.SetActive(v.state == IslandNodeState.Locked);
            if (w.tint != null)
                w.tint.color = v.state == IslandNodeState.Current ? currentColor
                    : v.state == IslandNodeState.Visited ? visitedColor
                    : v.state == IslandNodeState.Next ? (v.canTravel ? nextReadyColor : nextBlockedColor) : lockedColor;
        }

        private void DrawDetail(IslandNodeView v)
        {
            if (v == null)
            {
                if (titleText != null) titleText.text = "";
                if (briefingText != null) briefingText.text = "";
                ShowLines(new List<TravelLine>());
                if (travelButton != null) travelButton.interactable = false;
                return;
            }

            if (titleText != null) titleText.text = v.def.displayName;
            if (briefingText != null) briefingText.text = v.def.briefing;
            ShowLines(TravelPresenter.BuildLines(v.check));

            if (travelButton != null)
            {
                // 현재 섬만 막고, 못 가는 섬도 눌러서 사유를 확인할 수 있게 한다 (건너뛰기 불가 섬은 목록이 사유를 안내)
                travelButton.interactable = v.state != IslandNodeState.Current && !_gm.IsBusy;
            }
            if (travelButtonLabel != null)
                travelButtonLabel.text = v.state == IslandNodeState.Current ? "현재 위치"
                    : v.canTravel ? (v.state == IslandNodeState.Visited ? "돌아가기" : "출항") : "이동 조건 확인";
        }

        private void ShowLines(List<TravelLine> lines)
        {
            if (lineContainer == null || lineTemplate == null) return;

            while (_linePool.Count < lines.Count)
            {
                var t = Instantiate(lineTemplate, lineContainer);
                t.gameObject.SetActive(true);
                _linePool.Add(t);
            }
            for (int i = 0; i < _linePool.Count; i++)
            {
                bool used = i < lines.Count;
                _linePool[i].gameObject.SetActive(used);
                if (!used) continue;

                var line = lines[i];
                _linePool[i].text = line.kind == TravelLineKind.Detail ? "    " + line.text : line.text;
                _linePool[i].color = ColorOf(line.kind);
                _linePool[i].fontStyle = line.kind == TravelLineKind.Header ? FontStyle.Bold : FontStyle.Normal;
            }
        }

        private Color ColorOf(TravelLineKind k)
        {
            switch (k)
            {
                case TravelLineKind.Pass: return passColor;
                case TravelLineKind.Fail: return failColor;
                case TravelLineKind.Detail: return detailColor;
                case TravelLineKind.NotApplicable: return neutralColor;
                default: return detailColor;
            }
        }

        private void SetMessage(string text) { if (messageText != null) messageText.text = text ?? ""; }

        private void Update()
        {
            if (Input.GetKeyDown(toggleKey)) Toggle();
            if (IsOpen && Time.unscaledTime >= _nextPoll) Refresh();   // 식량·물·숙련도가 바뀌면 판정도 바뀐다
        }
    }
}
