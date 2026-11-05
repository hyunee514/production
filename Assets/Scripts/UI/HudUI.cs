using UnityEngine;
using UnityEngine.UI;
using AutoSurvival.Interaction;
using AutoSurvival.Save;
using AutoSurvival.Survival;

namespace AutoSurvival.UI
{
    /// <summary>
    /// 최소 HUD: 상호작용 안내([E] ...), 토스트 메시지, 생존 수치·자원 한 줄, 단축키(M 지도는 IslandMapUI 자체, F5 저장, F9 불러오기).
    /// PlayerInteractor가 씬마다 새로 생기므로 Update에서 PlayerInteractor.Current를 다시 찾아 연결한다.
    /// </summary>
    public class HudUI : MonoBehaviour
    {
        [SerializeField] private Text promptText;
        [SerializeField] private Text toastText;
        [SerializeField] private Text statsText;
        [SerializeField] private SaveSlotUI saveSlots;
        [SerializeField] private KeyCode saveKey = KeyCode.F5;
        [SerializeField] private KeyCode loadKey = KeyCode.F9;
        [SerializeField] private float toastSeconds = 2.5f;

        private PlayerInteractor _bound;
        private float _toastTimer;

        private void OnDisable() => Unbind();

        private void Unbind()
        {
            if (_bound == null) return;
            _bound.OnPromptChanged -= HandlePrompt;
            _bound.OnToast -= HandleToast;
            _bound = null;
        }

        private void HandlePrompt(string text) { if (promptText != null) promptText.text = text ?? ""; }

        private void HandleToast(string text)
        {
            if (toastText == null) return;
            toastText.text = text ?? "";
            _toastTimer = toastSeconds;
        }

        private void Update()
        {
            var current = PlayerInteractor.Current;
            if (current != _bound)
            {
                Unbind();
                _bound = current;
                if (_bound != null) { _bound.OnPromptChanged += HandlePrompt; _bound.OnToast += HandleToast; }
            }

            if (_toastTimer > 0f)
            {
                _toastTimer -= Time.unscaledDeltaTime;
                if (_toastTimer <= 0f && toastText != null) toastText.text = "";
            }

            if (statsText != null && _bound != null && _bound.Stats != null)
            {
                var s = _bound.Stats; var inv = _bound.Inventory;
                statsText.text = $"체력 {Pct(s.Get(StatType.Health))}  허기 {Pct(s.Get(StatType.Hunger))}  갈증 {Pct(s.Get(StatType.Thirst))}  체온 {Pct(s.Get(StatType.Warmth))}\n"
                               + $"나무 {inv.wood}  돌 {inv.stone}  금속 {inv.metalPanel}  물 {inv.water}  식량 {inv.food}";
            }

            if (saveSlots != null)
            {
                if (GameInput.KeyDown(saveKey)) saveSlots.Open(SlotMode.Save);
                else if (GameInput.KeyDown(loadKey)) saveSlots.Open(SlotMode.Load);
            }
        }

        private static string Pct(float v) => $"{Mathf.RoundToInt(v * 100f)}%";
    }
}
