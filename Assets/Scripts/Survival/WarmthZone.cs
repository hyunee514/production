using UnityEngine;

namespace AutoSurvival.Survival
{
    /// <summary>
    /// 모닥불/텐트 주변 따뜻함 구역. 에셋: campfire-pit, campfire-stand, tent, tent-canvas.
    /// 구역 안에서는 Warmth 감소가 멈추고 회복된다. 퍼즐은 이 구역 안에서만 '차분히' 풀 수 있다.
    /// </summary>
    [RequireComponent(typeof(SphereCollider))]
    public class WarmthZone : MonoBehaviour
    {
        [SerializeField] private float warmthPerSecond = 3f;
        [SerializeField] private bool requiresFuel = true;
        [SerializeField] private float fuelSeconds = 0f;               // 처음에는 꺼져 있다. Campfire가 나무를 받아 불을 붙인다.
        [SerializeField] private float maxFuelSeconds = 300f;
        [Tooltip("불이 타는 동안만 켜지는 불꽃/조명 오브젝트 (선택)")]
        [SerializeField] private GameObject flameVisual;

        public bool IsLit => !requiresFuel || fuelSeconds > 0f;
        public float FuelSeconds => fuelSeconds;
        public bool IsFull => requiresFuel && fuelSeconds >= maxFuelSeconds - 0.01f;

        private SurvivalStats _inside;

        private void Reset()
        {
            var c = GetComponent<SphereCollider>();
            c.isTrigger = true;
            c.radius = 4f;
        }

        public void AddFuel(float seconds) => fuelSeconds = Mathf.Min(maxFuelSeconds, fuelSeconds + seconds); // resource-wood 투입

        private void Update()
        {
            if (requiresFuel && fuelSeconds > 0f) fuelSeconds -= Time.deltaTime;
            if (_inside != null && IsLit) _inside.Modify(StatType.Warmth, warmthPerSecond * Time.deltaTime);
            if (flameVisual != null && flameVisual.activeSelf != IsLit) flameVisual.SetActive(IsLit);
        }

        private void OnTriggerEnter(Collider other)
        {
            if (other.TryGetComponent(out SurvivalStats s)) _inside = s;
        }

        private void OnTriggerExit(Collider other)
        {
            if (other.TryGetComponent(out SurvivalStats s) && s == _inside) _inside = null;
        }
    }
}
