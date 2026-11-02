using System;
using UnityEngine;

namespace AutoSurvival.Settings
{
    /// <summary>
    /// 설정 관리자: 마스터/효과음/환경음 볼륨, 카메라 흔들림 세기(0~100%), 히트스톱 on/off.
    ///  - 값을 바꾸면 Changed 이벤트가 나가고 (AudioManager, CameraShaker, PuzzleFeedback이 바로 반영),
    ///    슬라이더를 끄는 동안 매 프레임 디스크에 쓰지 않도록 마지막 변경 0.5초 뒤에 PlayerPrefs에 저장한다.
    ///  - 게임이 꺼지거나 백그라운드로 가면 즉시 저장한다.
    ///  - 저장 파일이 깨졌으면 기본값으로 시작한다.
    ///  - 설정은 세이브 슬롯과 별개다: 새 게임이나 불러오기로 바뀌지 않는다.
    /// 설정 값이 필요한 쪽은 SettingsManager.Current를 읽으면 된다 (매니저가 없어도 기본값이 돌아온다).
    /// </summary>
    public class SettingsManager : MonoBehaviour
    {
        public const string PrefsKey = "autosurvival.settings";

        public static SettingsManager Instance { get; private set; }

        private static readonly SettingsData _fallback = SettingsData.Default();

        /// <summary>현재 설정 (읽기용). 이 객체를 직접 고치지 말고 Set* 메서드를 쓸 것.</summary>
        public static SettingsData Current => Instance != null ? Instance.Data : _fallback;

        [SerializeField] private float saveDelaySeconds = 0.5f;

        public SettingsData Data { get; private set; } = SettingsData.Default();

        /// <summary>설정이 바뀔 때마다 (슬라이더를 끄는 동안 연속으로 나간다)</summary>
        public event Action<SettingsData> Changed;

        private bool _dirty;
        private float _saveAt;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
            if (transform.parent == null) DontDestroyOnLoad(gameObject);
            Load();
        }

        private void OnDestroy() { if (Instance == this) { Flush(); Instance = null; } }

        private void Update()
        {
            if (_dirty && Time.unscaledTime >= _saveAt) Flush();
        }

        private void OnApplicationPause(bool paused) { if (paused) Flush(); }
        private void OnApplicationQuit() => Flush();

        // ───────── 불러오기 / 저장 ─────────

        public void Load()
        {
            string json = null;
            try { json = PlayerPrefs.GetString(PrefsKey, ""); } catch (Exception) { /* 접근 불가 환경 */ }
            Data = SettingsCodec.Parse(json, out bool usedDefaults);
            if (usedDefaults && !string.IsNullOrWhiteSpace(json)) Debug.LogWarning("[SettingsManager] 설정 파일이 손상되어 기본값으로 시작합니다.");
            Changed?.Invoke(Data);
        }

        /// <summary>지금 바로 저장 (대기 중인 변경이 없으면 아무것도 하지 않는다).</summary>
        public void Flush()
        {
            if (!_dirty) return;
            _dirty = false;
            try
            {
                PlayerPrefs.SetString(PrefsKey, SettingsCodec.ToJson(Data));
                PlayerPrefs.Save();
            }
            catch (Exception e) { Debug.LogWarning($"[SettingsManager] 설정 저장 실패: {e.Message}"); }
        }

        // ───────── 값 바꾸기 ─────────

        public void SetMaster(int percent) => Apply(d => d.masterPercent = percent);
        public void SetSfx(int percent) => Apply(d => d.sfxPercent = percent);
        public void SetAmbient(int percent) => Apply(d => d.ambientPercent = percent);
        public void SetShake(int percent) => Apply(d => d.shakePercent = percent);
        public void SetHitstop(bool enabled) => Apply(d => d.hitstopEnabled = enabled);

        public void ResetToDefaults() { Data = SettingsData.Default(); MarkChanged(); }

        private void Apply(Action<SettingsData> change)
        {
            var before = Data.Clone();
            change(Data);
            Data.Sanitize();
            if (!Data.Equals(before)) MarkChanged();
        }

        private void MarkChanged()
        {
            _dirty = true;
            _saveAt = Time.unscaledTime + saveDelaySeconds;
            Changed?.Invoke(Data);
        }
    }
}
