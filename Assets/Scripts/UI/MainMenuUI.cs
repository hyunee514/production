using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using AutoSurvival.Save;

namespace AutoSurvival.UI
{
    /// <summary>
    /// 타이틀(메인 메뉴) 컨트롤러. MainMenu 씬의 Canvas에 붙인다.
    ///  [새 게임]  저장이 있으면 "한 번 더 누르면 시작" 확인 후 GameManager.NewGame (자동 저장을 덮어쓰게 된다).
    ///  [이어하기] 저장 슬롯 창(SaveSlotUI, 불러오기 모드)을 연다. 사용할 수 있는 저장이 없으면 눌러지지 않는다.
    ///  [설정]    SettingsPanelUI를 연다.
    ///  [종료]    한 번 더 누르면 종료 (에디터에서는 플레이 중지, WebGL에서는 버튼을 숨김).
    ///
    /// 메뉴 씬의 GameManager는 startGameOnBoot를 꺼 둔다 (켜 두면 시작하자마자 이어하기/새 게임이 자동으로 진행된다).
    /// 이 씬에서는 마우스가 항상 풀려 있다 (SaveSlotUI가 닫힐 때 커서를 잠그는 것을 다시 풀어 준다).
    /// </summary>
    public class MainMenuUI : MonoBehaviour
    {
        [Header("화면")]
        [SerializeField] private GameObject root;
        [SerializeField] private CanvasGroup group;
        [SerializeField] private Text titleText;
        [SerializeField] private Text subtitleText;
        [SerializeField] private Text infoText;           // "마지막 저장: ..."
        [SerializeField] private Text messageText;        // 확인 안내, 오류
        [SerializeField] private Text versionText;

        [Header("버튼")]
        [SerializeField] private Button newGameButton;
        [SerializeField] private Button continueButton;
        [SerializeField] private Button settingsButton;
        [SerializeField] private Button quitButton;

        [Header("연결")]
        [SerializeField] private SaveSlotUI saveSlots;
        [SerializeField] private SettingsPanelUI settingsPanel;

        [Header("문구")]
        [SerializeField] private string title = "에이전";
        [SerializeField] private string subtitle = "멈춰버린 행성의 엔지니어";
        [SerializeField] private float fadeInSeconds = 0.6f;
        [SerializeField] private float confirmSeconds = 3.5f;

        private readonly ConfirmGate _gate = new();
        private GameManager _gm;
        private bool _saveWasOpen;
        private bool _leaving;

        private void Awake()
        {
            _gate.WindowSeconds = confirmSeconds;
            if (newGameButton != null) newGameButton.onClick.AddListener(OnNewGame);
            if (continueButton != null) continueButton.onClick.AddListener(OnContinue);
            if (settingsButton != null) settingsButton.onClick.AddListener(OnSettings);
            if (quitButton != null) quitButton.onClick.AddListener(OnQuit);
#if UNITY_WEBGL
            if (quitButton != null) quitButton.gameObject.SetActive(false);    // 브라우저에서는 종료할 수 없다
#endif
            if (titleText != null) titleText.text = title;
            if (subtitleText != null) subtitleText.text = subtitle;
            if (versionText != null) versionText.text = "v" + Application.version;
            if (group != null) group.alpha = 0f;
        }

        private void OnEnable()
        {
            _leaving = false;
            Time.timeScale = 1f;
            Bind(GameManager.Instance);
            Refresh();
        }

        private void OnDisable() { Unbind(); }

        private void Start()
        {
            if (group != null) UiTween.Fade(group, 1f, fadeInSeconds);
            Bind(GameManager.Instance);
            Refresh();
            FocusFirstButton();
        }

        private void Bind(GameManager gm)
        {
            if (gm == null || gm == _gm) return;
            Unbind();
            _gm = gm;
            _gm.OnLoaded += HandleStarted;
            _gm.OnNewGame += HandleStarted;
            _gm.OnLoadFailed += HandleLoadFailed;
        }

        private void Unbind()
        {
            if (_gm == null) return;
            _gm.OnLoaded -= HandleStarted;
            _gm.OnNewGame -= HandleStarted;
            _gm.OnLoadFailed -= HandleLoadFailed;
            _gm = null;
        }

        // ───────── 버튼 ─────────

        private void OnNewGame()
        {
            if (!EnsureGameManager()) return;

            bool hasSave = MainMenuModel.HasAnyUsableSave(_gm.ListSlots());
            if (hasSave && !_gate.Press("new", Time.unscaledTime))
            {
                SetMessage("저장된 게임이 있어요. 새로 시작하면 자동 저장이 덮어써져요. 정말이면 한 번 더 눌러 주세요.");
                return;
            }
            SetMessage("");
            _leaving = true;
            _gm.NewGame();
        }

        private void OnContinue()
        {
            if (!EnsureGameManager() || saveSlots == null) return;
            _gate.Cancel(); SetMessage("");
            saveSlots.Open(SlotMode.Load);
        }

        private void OnSettings()
        {
            _gate.Cancel(); SetMessage("");
            if (settingsPanel != null) settingsPanel.Open();
        }

        private void OnQuit()
        {
            if (!_gate.Press("quit", Time.unscaledTime)) { SetMessage("종료하려면 한 번 더 눌러 주세요."); return; }
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private bool EnsureGameManager()
        {
            _gm ??= GameManager.Instance;
            if (_gm == null) { SetMessage("GameManager가 씬에 없어요. _Systems 오브젝트를 확인해 주세요."); return false; }
            Bind(_gm);
            return true;
        }

        // ───────── 이벤트 ─────────

        private void HandleStarted()
        {
            // 게임이 시작되면 메뉴를 숨긴다 (씬이 바뀌면 어차피 사라지지만, 전환 중 한두 프레임 동안 보이지 않게)
            _leaving = true;
            if (root != null) root.SetActive(false);
        }

        private void HandleLoadFailed(string error)
        {
            _leaving = false;
            SetMessage($"불러오지 못했어요: {error}");
            Refresh();
        }

        // ───────── 표시 ─────────

        public void Refresh()
        {
            var gm = _gm ?? GameManager.Instance;
            var slots = gm != null ? gm.ListSlots() : null;
            bool hasSave = MainMenuModel.HasAnyUsableSave(slots);

            if (continueButton != null) continueButton.interactable = hasSave;
            if (infoText != null)
                infoText.text = MainMenuModel.DescribeLatest(MainMenuModel.Latest(slots),
                    id => gm != null ? gm.IslandName(id) : id, DateTime.UtcNow);
        }

        private void SetMessage(string text) { if (messageText != null) messageText.text = text ?? ""; }

        private void FocusFirstButton()
        {
            if (EventSystem.current == null) return;
            var first = continueButton != null && continueButton.interactable ? continueButton : newGameButton;
            if (first != null) EventSystem.current.SetSelectedGameObject(first.gameObject);
        }

        private void Update()
        {
            // 저장 창이 닫힌 직후: 목록 갱신 + 방향키가 다시 메뉴로 돌아오게 선택 복구
            bool open = saveSlots != null && saveSlots.IsOpen;
            if (_saveWasOpen && !open) { Refresh(); FocusFirstButton(); }
            _saveWasOpen = open;

            // 확인 안내 문구는 시간이 지나면 사라진다
            if (messageText != null && !string.IsNullOrEmpty(messageText.text)
                && !_gate.IsArmed("new", Time.unscaledTime) && !_gate.IsArmed("quit", Time.unscaledTime)
                && messageText.text.EndsWith("눌러 주세요.")) messageText.text = "";

            if (GameInput.KeyDown(KeyCode.Escape) && settingsPanel == null) _gate.Cancel();
        }

        private void LateUpdate()
        {
            if (_leaving) return;
            // SaveSlotUI.Close()가 커서를 잠그므로, 메뉴 씬에서는 매 프레임 푼다
            if (Cursor.lockState != CursorLockMode.None) Cursor.lockState = CursorLockMode.None;
            if (!Cursor.visible) Cursor.visible = true;
        }
    }
}
