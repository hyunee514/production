using System;
using System.Collections.Generic;
using UnityEngine;
using AutoSurvival.Puzzles;
using AutoSurvival.Survival;

namespace AutoSurvival.Interaction
{
    public interface IInteractable
    {
        string Prompt { get; }
        bool CanInteract(PlayerInteractor who);
        void Interact(PlayerInteractor who);
    }

    /// <summary>티어 업그레이드 비용. 에셋 매핑: resource-wood, resource-stone, metal-panel.</summary>
    [Serializable]
    public struct ItemCost
    {
        public int wood, stone, metalPanel;
    }

    /// <summary>최소 인벤토리. 실제 프로젝트에서는 ScriptableObject 아이템 DB로 확장.</summary>
    public class Inventory : MonoBehaviour
    {
        public int wood, stone, metalPanel, water, food;

        public bool Has(ItemCost c) => wood >= c.wood && stone >= c.stone && metalPanel >= c.metalPanel;

        public bool TryConsume(ItemCost c)
        {
            if (!Has(c)) return false;
            wood -= c.wood; stone -= c.stone; metalPanel -= c.metalPanel;
            return true;
        }
    }

    /// <summary>주변(근접 반경) 상호작용 대상 중 바라보는 쪽에 가까운 것을 고르고, 퍼즐 진입/종료 시 조작 모드를 전환한다.</summary>
    [RequireComponent(typeof(SurvivalStats), typeof(Inventory))]
    public class PlayerInteractor : MonoBehaviour
    {
        [SerializeField] private Camera playerCamera;            // (3인칭에서는 선택 사항)
        [SerializeField] private float reach = 2.8f;             // 근접 반경(m)
        [SerializeField] private KeyCode interactKey = KeyCode.E;
        [SerializeField] private MonoBehaviour movementController;   // 퍼즐 중 이동 잠금

        /// <summary>씬에 있는 플레이어 (씬 전환 후 GameManager가 다시 찾을 때 사용).</summary>
        public static PlayerInteractor Current { get; private set; }

        public SurvivalStats Stats { get; private set; }
        public Inventory Inventory { get; private set; }

        public event Action<string> OnPromptChanged;
        public event Action<string> OnToast;

        private IInteractable _target;
        private PuzzleSession _currentPuzzle;

        private void Awake()
        {
            Current = this;
            Stats = GetComponent<SurvivalStats>();
            Inventory = GetComponent<Inventory>();
        }

        private void OnDestroy() { if (Current == this) Current = null; }

        private void Update()
        {
            if (_currentPuzzle != null)
            {
                // 퍼즐 중에도 월드는 흐른다. ESC로 중단 가능 (패널티 없이 포기).
                if (GameInput.KeyDown(KeyCode.Escape)) _currentPuzzle.Abort();
                return;
            }

            FindTarget();
            if (_target != null && GameInput.KeyDown(interactKey) && !GameInput.MovementBlocked && _target.CanInteract(this))
            {
                (movementController as ThirdPersonController)?.PlayInteract();
                _target.Interact(this);
            }
        }

        private readonly Collider[] _hits = new Collider[48];
        private readonly List<IInteractable> _cands = new();
        private readonly List<Vector3> _candPos = new();

        private void FindTarget()
        {
            _cands.Clear(); _candPos.Clear();
            var origin = transform.position;
            int n = Physics.OverlapSphereNonAlloc(origin + Vector3.up * 0.9f, reach, _hits, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var it = _hits[i].GetComponentInParent<IInteractable>();
                if (it == null || _cands.Contains(it)) continue;
                _cands.Add(it);
                _candPos.Add(_hits[i].bounds.ClosestPoint(origin));
            }

            Vector3 facing = movementController is ThirdPersonController tp ? tp.Facing : transform.forward;
            int idx = ThirdPersonMath.PickTarget(origin, facing, _candPos, reach);
            var found = idx >= 0 ? _cands[idx] : null;

            if (!ReferenceEquals(found, _target))
            {
                _target = found;
                OnPromptChanged?.Invoke(_target != null ? _target.Prompt : string.Empty);
            }
        }

        public void EnterPuzzleMode(PuzzleSession puzzle)
        {
            _currentPuzzle = puzzle;
            if (movementController != null) movementController.enabled = false;
        }

        public void ExitPuzzleMode()
        {
            _currentPuzzle = null;
            if (movementController != null) movementController.enabled = true;
        }

        public void ShowToast(string message) => OnToast?.Invoke(message);
    }
}
