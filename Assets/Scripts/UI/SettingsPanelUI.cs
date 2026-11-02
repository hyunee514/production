using UnityEngine;
using UnityEngine.UI;
using AutoSurvival.Feedback;
using AutoSurvival.Settings;

namespace AutoSurvival.UI
{
    /// <summary>
    /// 설정 창: 마스터/효과음/환경음 볼륨, 카메라 흔들림 세기(0~100%), 히트스톱 on/off.
    /// 슬라이더를 움직이면 바로 반영되고(SettingsManager가 잠시 뒤 저장), "흔들림 시험" 버튼으로 현재 세기를 느껴 볼 수 있다.
    /// 메인 메뉴와 게임 중 일시정지 메뉴에서 같은 창을 쓴다.
    /// </summary>
    public class SettingsPanelUI : MonoBehaviour
    {
        [SerializeField] private GameObject root;
        [SerializeField] private Slider masterSlider, sfxSlider, ambientSlider, shakeSlider;
        [SerializeField] private Text masterValue, sfxValue, ambientValue, shakeValue;
        [SerializeField] private Toggle hitstopToggle;
        [SerializeField] private Button resetButton, closeButton, testShakeButton;
        [Tooltip("게임 중에 열고 닫을 때 마우스를 잠그고 푼다 (메인 메뉴에서는 끈다)")]
        [SerializeField] private bool controlCursor = false;

        [Tooltip("게임 중 설정 창을 열고 닫는 키 (None이면 사용 안 함). 에디터 도구가 F10으로 설정한다")]
        [SerializeField] private KeyCode toggleKey = KeyCode.None;

        public bool IsOpen { get; private set; }

        private bool _syncing;

        private void Awake()
        {
            Hook(masterSlider, v => Manager?.SetMaster(v));
            Hook(sfxSlider, v => Manager?.SetSfx(v));
            Hook(ambientSlider, v => Manager?.SetAmbient(v));
            Hook(shakeSlider, v => Manager?.SetShake(v));
            if (hitstopToggle != null) hitstopToggle.onValueChanged.AddListener(on => { if (!_syncing) Manager?.SetHitstop(on); });
            if (resetButton != null) resetButton.onClick.AddListener(() => { Manager?.ResetToDefaults(); });
            if (closeButton != null) closeButton.onClick.AddListener(Close);
            if (testShakeButton != null) testShakeButton.onClick.AddListener(TestShake);
            if (root != null && root != gameObject) root.SetActive(false);
        }

        private static SettingsManager Manager => SettingsManager.Instance;

        private void Hook(Slider s, System.Action<int> set)
        {
            if (s == null) return;
            s.minValue = 0; s.maxValue = 100; s.wholeNumbers = true;
            s.onValueChanged.AddListener(v => { if (!_syncing) set(Mathf.RoundToInt(v)); });
        }

        private void OnEnable() { if (Manager != null) Manager.Changed += HandleChanged; }
        private void OnDisable() { if (Manager != null) Manager.Changed -= HandleChanged; }

        private void HandleChanged(SettingsData d) { if (IsOpen) Show(d); }

        public void Open()
        {
            IsOpen = true;
            if (root != null) root.SetActive(true);
            if (Manager != null) { Manager.Changed -= HandleChanged; Manager.Changed += HandleChanged; }
            Show(SettingsManager.Current);
            if (controlCursor) { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
        }

        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            Manager?.Flush();
            if (root != null) root.SetActive(false);
            if (controlCursor) { Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; }
        }

        private void Update()
        {
            if (IsOpen && Input.GetKeyDown(KeyCode.Escape)) { Close(); return; }
            if (toggleKey != KeyCode.None && Input.GetKeyDown(toggleKey)) { if (IsOpen) Close(); else Open(); }
        }

        private void Show(SettingsData d)
        {
            _syncing = true;
            Set(masterSlider, masterValue, d.masterPercent);
            Set(sfxSlider, sfxValue, d.sfxPercent);
            Set(ambientSlider, ambientValue, d.ambientPercent);
            Set(shakeSlider, shakeValue, d.shakePercent);
            if (hitstopToggle != null) hitstopToggle.isOn = d.hitstopEnabled;
            _syncing = false;
        }

        private static void Set(Slider s, Text label, int value)
        {
            if (s != null) s.value = value;
            if (label != null) label.text = value + "%";
        }

        private void TestShake()
        {
            // 세기 0%면 아무 일도 안 일어나는 것이 맞다 (흔들림 끄기)
            if (CameraShaker.Instance != null) CameraShaker.Instance.Shake(ShakeGrade.M);
        }
    }
}
