using UnityEngine;

namespace AutoSurvival.Interaction
{
    /// <summary>바다로 떨어지면 처음 위치로 되돌린다 (가장자리 보이지 않는 벽의 안전망).</summary>
    [RequireComponent(typeof(CharacterController))]
    public class FallGuard : MonoBehaviour
    {
        [SerializeField] private float killY = -4f;
        private Vector3 _home;
        private CharacterController _cc;

        private void Start() { _cc = GetComponent<CharacterController>(); _home = transform.position; }

        private void Update()
        {
            if (transform.position.y > killY) return;
            _cc.enabled = false;
            transform.position = _home;
            _cc.enabled = true;
            PlayerInteractor.Current?.ShowToast("물에 빠졌다! 캠프로 돌아왔다.");
        }
    }
}
