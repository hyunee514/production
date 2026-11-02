using UnityEngine;

namespace AutoSurvival.Interaction
{
    /// <summary>
    /// 테스트용 1인칭 이동 (WASD, 마우스, Shift 달리기). PlayerInteractor가 퍼즐 중 이 컴포넌트를 끄고 켠다.
    /// 나중에 제대로 된 캐릭터 컨트롤러로 바꿔도 PlayerInteractor의 movementController 슬롯만 바꾸면 된다.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class SimpleFirstPersonController : MonoBehaviour
    {
        [SerializeField] private Transform cameraPivot;
        [SerializeField] private float walkSpeed = 4.5f;
        [SerializeField] private float runSpeed = 7f;
        [SerializeField] private float mouseSensitivity = 2f;
        [SerializeField] private float gravity = -20f;

        private CharacterController _cc;
        private float _pitch, _vy;

        private void Awake() => _cc = GetComponent<CharacterController>();

        private void OnEnable()
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        private void Update()
        {
            // 메뉴(저장 슬롯, 섬 지도 등)가 마우스를 풀어 둔 동안에는 시점·이동을 멈춘다
            if (Cursor.lockState != CursorLockMode.Locked)
            {
                _vy = _cc.isGrounded ? -1f : _vy + gravity * Time.deltaTime;
                _cc.Move(Vector3.up * _vy * Time.deltaTime);
                return;
            }

            float mx = Input.GetAxis("Mouse X") * mouseSensitivity;
            float my = Input.GetAxis("Mouse Y") * mouseSensitivity;
            transform.Rotate(0f, mx, 0f);
            _pitch = Mathf.Clamp(_pitch - my, -80f, 80f);
            if (cameraPivot != null) cameraPivot.localRotation = Quaternion.Euler(_pitch, 0f, 0f);

            var move = transform.right * Input.GetAxisRaw("Horizontal") + transform.forward * Input.GetAxisRaw("Vertical");
            if (move.sqrMagnitude > 1f) move.Normalize();
            float speed = Input.GetKey(KeyCode.LeftShift) ? runSpeed : walkSpeed;

            _vy = _cc.isGrounded ? -1f : _vy + gravity * Time.deltaTime;
            _cc.Move((move * speed + Vector3.up * _vy) * Time.deltaTime);
        }
    }
}
