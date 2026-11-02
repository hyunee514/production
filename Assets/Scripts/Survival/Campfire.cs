using System.Collections.Generic;
using UnityEngine;
using AutoSurvival.Interaction;

namespace AutoSurvival.Survival
{
    /// <summary>
    /// 모닥불: [E]로 나무를 넣어 불을 붙이고 이어 간다. 불이 타는 동안 WarmthZone이 체온을 올린다.
    /// 처음에는 꺼져 있다 (WarmthZone의 fuelSeconds가 0). 나무 1개 = 기본 90초.
    /// 콜라이더가 필요하다 (모델에 없으면 BoxCollider를 붙일 것. 에디터 도구가 자동으로 붙인다).
    /// </summary>
    [RequireComponent(typeof(WarmthZone))]
    public class Campfire : MonoBehaviour, IInteractable
    {
        [SerializeField] private WarmthZone zone;
        [SerializeField] private int woodCost = 1;
        [SerializeField] private float secondsPerWood = 90f;

        private static readonly List<Campfire> _all = new();

        /// <summary>장면 안의 모닥불 중 하나라도 타고 있는가 (목표 추적기가 사용)</summary>
        public static bool AnyLit
        {
            get { foreach (var c in _all) if (c != null && c.zone != null && c.zone.IsLit) return true; return false; }
        }

        private void Awake() { if (zone == null) zone = GetComponent<WarmthZone>(); }
        private void OnEnable() { _all.Add(this); }
        private void OnDisable() { _all.Remove(this); }

        public string Prompt => zone == null ? "" :
            zone.IsLit ? $"[E] 장작 넣기 (나무 {woodCost})  남은 불 {Mathf.CeilToInt(zone.FuelSeconds)}초"
                       : $"[E] 모닥불 피우기 (나무 {woodCost})";

        public bool CanInteract(PlayerInteractor who) => zone != null && who != null && who.Inventory != null && !zone.IsFull;

        public void Interact(PlayerInteractor who)
        {
            if (!CanInteract(who)) return;
            if (who.Inventory.wood < woodCost) { who.ShowToast($"나무가 {woodCost}개 필요해. 해안 숲에서 모아 오자."); return; }

            bool wasLit = zone.IsLit;
            who.Inventory.wood -= woodCost;
            zone.AddFuel(secondsPerWood);
            who.ShowToast(wasLit ? "장작을 더 넣었다." : "불이 붙었다! 몸이 따뜻해진다.");
        }
    }
}
