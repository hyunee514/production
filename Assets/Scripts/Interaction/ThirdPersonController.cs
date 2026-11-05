using UnityEngine;

namespace AutoSurvival.Interaction
{
    /// <summary>
    /// 3인칭 이동. 카메라가 보는 방향 기준으로 걷고(WASD/조이스틱), 이동 방향으로 캐릭터가 몸을 돌린다.
    /// Animator 파라미터: Speed(0~1 float), Grounded(bool), Jump/Interact(trigger). Animator가 없어도 동작한다.
    /// PlayerInteractor가 퍼즐 중 이 컴포넌트를 끄고 켠다 (movementController 슬롯).
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class ThirdPersonController : MonoBehaviour
    {
        [SerializeField] private Transform cameraRig;        // yaw를 읽는다 (ThirdPersonCamera)
        [SerializeField] private Transform visual;           // 회전시킬 모델 (없으면 자기 자신)
        [SerializeField] private Animator animator;
        [SerializeField] private float walkSpeed = 3.6f;
        [SerializeField] private float runSpeed = 6.2f;
        [SerializeField] private float turnSpeed = 720f;     // 도/초
        [SerializeField] private float jumpHeight = 1.1f;
        [SerializeField] private float gravity = -22f;
        [SerializeField] private bool allowRunByDefault = false;

        private CharacterController _cc;
        private float _vy, _yaw, _speed01;
        private static readonly int SpeedHash = Animator.StringToHash("Speed");
        private static readonly int GroundedHash = Animator.StringToHash("Grounded");
        private static readonly int JumpHash = Animator.StringToHash("Jump");
        private static readonly int InteractHash = Animator.StringToHash("Interact");

        public Vector3 Facing => (visual != null ? visual : transform).forward;

        private void Awake()
        {
            _cc = GetComponent<CharacterController>();
            if (visual == null) visual = transform;
            _yaw = visual.eulerAngles.y;
        }

        private void OnDisable()
        {
            if (animator != null) { animator.SetFloat(SpeedHash, 0f); }
            _speed01 = 0f;
        }

        public void PlayInteract()
        {
            if (animator != null) animator.SetTrigger(InteractHash);
        }

        private void Update()
        {
            bool blocked = GameInput.MovementBlocked;
            Vector2 input = blocked ? Vector2.zero : GameInput.Move;
            float camYaw = cameraRig != null ? cameraRig.eulerAngles.y : 0f;
            Vector3 dir = ThirdPersonMath.CameraRelative(camYaw, input);

            bool wantsRun = GameInput.Key(KeyCode.LeftShift) || allowRunByDefault;
            float speed = wantsRun ? runSpeed : walkSpeed;
            float mag = dir.magnitude;

            if (mag > 0.05f)
            {
                float target = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
                _yaw = ThirdPersonMath.TurnToward(_yaw, target, turnSpeed * Time.deltaTime);
                visual.rotation = Quaternion.Euler(0f, _yaw, 0f);
            }

            bool grounded = _cc.isGrounded;
            if (grounded && _vy < 0f) _vy = -2f;
            if (grounded && !blocked && GameInput.KeyDown(KeyCode.Space))
            {
                _vy = Mathf.Sqrt(jumpHeight * -2f * gravity);
                if (animator != null) animator.SetTrigger(JumpHash);
            }
            _vy += gravity * Time.deltaTime;

            _cc.Move((dir * speed + Vector3.up * _vy) * Time.deltaTime);

            _speed01 = Mathf.MoveTowards(_speed01, mag > 0.05f ? (wantsRun ? 1f : 0.5f) : 0f, 6f * Time.deltaTime);
            if (animator != null)
            {
                animator.SetFloat(SpeedHash, _speed01);
                animator.SetBool(GroundedHash, grounded || _cc.isGrounded);
            }
        }
    }
}
