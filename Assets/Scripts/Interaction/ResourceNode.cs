using UnityEngine;

namespace AutoSurvival.Interaction
{
    public enum ResourceKind { Wood, Stone, MetalPanel, Water, Food }

    /// <summary>
    /// 채집물. [E]로 상호작용하면 인벤토리에 자원이 들어오고, 일정 시간 뒤 다시 생긴다 (모델은 숨기고 충돌만 끈다).
    /// 업그레이드 비용(나무·돌·금속 패널)과 항해 보급(식량·물)을 모으는 생존 루프의 시작점.
    /// </summary>
    public class ResourceNode : MonoBehaviour, IInteractable
    {
        [SerializeField] private ResourceKind kind = ResourceKind.Wood;
        [SerializeField] private int amount = 2;
        [Tooltip("채집한 뒤 다시 생기기까지 (초). 0이면 한 번만")] [SerializeField] private float respawnSeconds = 120f;

        public ResourceKind Kind => kind;
        public int Amount => amount;
        public bool IsDepleted { get; private set; }

        private float _timer;
        private Renderer[] _renderers;
        private Collider[] _colliders;

        public static string LabelOf(ResourceKind k) => k switch
        {
            ResourceKind.Wood => "나무",
            ResourceKind.Stone => "돌",
            ResourceKind.MetalPanel => "금속 패널",
            ResourceKind.Water => "물",
            _ => "식량",
        };

        public string Prompt => IsDepleted ? string.Empty : $"[E] {LabelOf(kind)} 채집 (+{amount})";

        public bool CanInteract(PlayerInteractor who) => !IsDepleted && who != null && who.Inventory != null;

        public void Interact(PlayerInteractor who)
        {
            if (!CanInteract(who)) return;
            var inv = who.Inventory;
            switch (kind)
            {
                case ResourceKind.Wood: inv.wood += amount; break;
                case ResourceKind.Stone: inv.stone += amount; break;
                case ResourceKind.MetalPanel: inv.metalPanel += amount; break;
                case ResourceKind.Water: inv.water += amount; break;
                default: inv.food += amount; break;
            }
            who.ShowToast($"{LabelOf(kind)} +{amount}");
            SetDepleted(true);
            _timer = respawnSeconds;
        }

        private void SetDepleted(bool value)
        {
            _renderers ??= GetComponentsInChildren<Renderer>(true);
            _colliders ??= GetComponentsInChildren<Collider>(true);
            IsDepleted = value;
            foreach (var r in _renderers) if (r != null) r.enabled = !value;
            foreach (var c in _colliders) if (c != null) c.enabled = !value;
        }

        private void Update()
        {
            if (!IsDepleted || respawnSeconds <= 0f) return;
            _timer -= Time.deltaTime;
            if (_timer <= 0f) SetDepleted(false);
        }
    }
}
