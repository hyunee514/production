using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using AutoSurvival.Save;

namespace AutoSurvival.UI
{
    [Serializable]
    public class SlotWidget
    {
        public GameObject root;
        public Button button;
        public Text titleText;          // "자동 저장" / "저장 1"
        public Text islandText;
        public Text playTimeText;
        public Text savedAtText;        // "2026-10-31 18:50 (3분 전)"
        public Text statusText;         // 정상 / 손상 안내 / 확인 요청
        public Graphic stateTint;       // 슬롯 배경 색
        public GameObject completeBadge;
    }

    /// <summary>
    /// 저장 슬롯 UI: 자동 저장 1칸 + 수동 저장 3칸을 보여 주고, 클릭하면 GameManager로 저장/불러오기를 실행한다.
    /// 칸마다 섬 이름, 플레이 시간, 저장 시각, 파일 상태(정상 / 직전 저장본으로 복구 가능 / 손상 / 비어 있음)를 표시한다.
    /// 덮어쓰기와 불러오기는 같은 칸을 한 번 더 눌러야 실행된다 (실수 방지, confirmSeconds 안에).
    ///
    /// [Canvas 연결 방법]
    ///  1) slots 배열은 인덱스 = 슬롯 번호 (0 = 자동 저장, 1~3 = 수동 저장) 순서로 4칸을 연결한다.
    ///  2) saveModeButton / loadModeButton으로 모드를 바꾸고, messageText에 안내 문구가 나온다.
    ///  3) Open(SlotMode.Save) 또는 Open(SlotMode.Load)를 일시정지 메뉴 / 타이틀 버튼에서 호출한다.
    /// </summary>
    public class SaveSlotUI : MonoBehaviour
    {
        [SerializeField] private GameObject root;
        [SerializeField] private SlotWidget[] slots = new SlotWidget[4];
        [SerializeField] private Button saveModeButton, loadModeButton, closeButton;
        [SerializeField] private Text modeTitleText;
        [SerializeField] private Text messageText;

        [Header("동작")]
        [SerializeField] private bool confirmLoad = true;
        [SerializeField] private float confirmSeconds = 4f;
        [SerializeField] private bool closeAfterLoad = true;

        [Header("색상")]
        [SerializeField] private Color okColor = new Color(0.85f, 0.95f, 0.85f);
        [SerializeField] private Color emptyColor = new Color(0.85f, 0.85f, 0.85f);
        [SerializeField] private Color backupColor = new Color(0.98f, 0.92f, 0.7f);
        [SerializeField] private Color corruptColor = new Color(0.95f, 0.75f, 0.72f);
        [SerializeField] private Color armedColor = new Color(0.75f, 0.85f, 1f);

        public SlotMode Mode { get; private set; } = SlotMode.Load;
        public bool IsOpen { get; private set; }

        private readonly List<SlotViewModel> _views = new();
        private int _armedSlot = -1;
        private float _armedUntil;
        private GameManager _gm;

        private void Awake()
        {
            for (int i = 0; i < slots.Length; i++)
            {
                int captured = i;
                if (slots[i] != null && slots[i].button != null) slots[i].button.onClick.AddListener(() => OnSlotClicked(captured));
            }
            if (saveModeButton != null) saveModeButton.onClick.AddListener(() => SetMode(SlotMode.Save));
            if (loadModeButton != null) loadModeButton.onClick.AddListener(() => SetMode(SlotMode.Load));
            if (closeButton != null) closeButton.onClick.AddListener(Close);
            if (root != null && root != gameObject) root.SetActive(false);
        }

        private void OnDisable() => Unbind();

        // ───────── 열기/닫기 ─────────

        public void Open(SlotMode mode)
        {
            Bind(GameManager.Instance);
            IsOpen = true;
            GameInput.BlockMovement(this, true);      // 메뉴가 열려 있는 동안 이동·시점을 멈춘다
            if (root != null) root.SetActive(true);
            Mode = mode;
            Disarm();
            SetMessage("");
            Refresh();
        }

        public void Close()
        {
            IsOpen = false;
            Disarm();
            if (root != null) root.SetActive(false);
            GameInput.BlockMovement(this, false);
        }

        public void SetMode(SlotMode mode)
        {
            Mode = mode;
            Disarm();
            SetMessage("");
            Refresh();
        }

        private void Bind(GameManager gm)
        {
            if (_gm == gm) return;
            Unbind();
            _gm = gm;
            if (_gm == null) return;
            _gm.OnSaved += HandleSaved;
            _gm.OnLoadFailed += HandleLoadFailed;
        }

        private void Unbind()
        {
            if (_gm == null) return;
            _gm.OnSaved -= HandleSaved;
            _gm.OnLoadFailed -= HandleLoadFailed;
            _gm = null;
        }

        private void HandleSaved(int slot) { if (IsOpen) Refresh(); }
        private void HandleLoadFailed(string error) { if (IsOpen) { SetMessage($"불러오기에 실패했습니다: {error}"); Refresh(); } }

        // ───────── 표시 ─────────

        public void Refresh()
        {
            _views.Clear();
            if (_gm == null) { SetMessage("게임 매니저를 찾을 수 없습니다."); return; }

            var summaries = _gm.ListSlots();
            var now = DateTime.UtcNow;
            foreach (var s in summaries) _views.Add(SlotPresenter.Build(s, _gm.IslandName, now));

            if (modeTitleText != null) modeTitleText.text = Mode == SlotMode.Save ? "저장하기" : "불러오기";
            if (saveModeButton != null) saveModeButton.interactable = Mode != SlotMode.Save;
            if (loadModeButton != null) loadModeButton.interactable = Mode != SlotMode.Load;

            for (int i = 0; i < slots.Length && i < _views.Count; i++) Draw(slots[i], _views[i]);
        }

        private void Draw(SlotWidget w, SlotViewModel vm)
        {
            if (w == null) return;
            bool armed = IsArmed(vm.slot);

            SetText(w.titleText, vm.title);
            SetText(w.islandText, vm.islandName);
            SetText(w.playTimeText, vm.playTime);
            SetText(w.savedAtText, string.IsNullOrEmpty(vm.savedAgo) ? vm.savedAt : $"{vm.savedAt} ({vm.savedAgo})");
            SetText(w.statusText, armed ? (Mode == SlotMode.Save ? "한 번 더 누르면 저장합니다" : "한 번 더 누르면 불러옵니다") : vm.statusText);
            if (w.completeBadge != null) w.completeBadge.SetActive(vm.gameComplete);

            if (w.stateTint != null)
                w.stateTint.color = armed ? armedColor
                    : vm.state == SlotState.Ok ? okColor
                    : vm.state == SlotState.RecoveredFromBackup ? backupColor
                    : vm.state == SlotState.Corrupt ? corruptColor : emptyColor;

            // 불가능한 동작이어도 눌러서 이유를 볼 수 있게 버튼은 켜 둔다 (게임이 씬을 바꾸는 중일 때만 잠금)
            if (w.button != null) w.button.interactable = _gm != null && !_gm.IsBusy;
        }

        // ───────── 클릭 ─────────

        private void OnSlotClicked(int slot)
        {
            if (!IsOpen || _gm == null || slot < 0 || slot >= _views.Count) return;
            var vm = _views[slot];

            var action = SlotActionPlanner.Decide(Mode, vm, IsArmed(slot), confirmLoad);
            switch (action.kind)
            {
                case SlotActionKind.Blocked:
                    Disarm();
                    SetMessage(action.message);
                    break;

                case SlotActionKind.ConfirmOverwrite:
                case SlotActionKind.ConfirmLoad:
                    _armedSlot = slot;
                    _armedUntil = Time.unscaledTime + confirmSeconds;
                    SetMessage(action.message);
                    break;

                case SlotActionKind.SaveNow:
                    Disarm();
                    SetMessage(_gm.Save(slot) ? $"{vm.title}에 저장했습니다." : "저장에 실패했습니다. 저장 공간을 확인해 주세요.");
                    break;

                case SlotActionKind.LoadNow:
                    Disarm();
                    if (_gm.Load(slot))
                    {
                        SetMessage($"{vm.title}을(를) 불러왔습니다.");
                        if (closeAfterLoad) { Close(); return; }
                    }
                    break;     // 실패 메시지는 OnLoadFailed가 표시
            }
            Refresh();
        }

        private bool IsArmed(int slot) => _armedSlot == slot && Time.unscaledTime < _armedUntil;
        private void Disarm() { _armedSlot = -1; }

        private void Update()
        {
            // 확인 대기 시간이 지나면 원래 표시로 되돌린다
            if (IsOpen && _armedSlot >= 0 && Time.unscaledTime >= _armedUntil) { Disarm(); Refresh(); }
        }

        private void SetMessage(string text) { if (messageText != null) messageText.text = text ?? ""; }
        private static void SetText(Text t, string s) { if (t != null) t.text = s ?? ""; }
    }
}
