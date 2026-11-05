using UnityEngine;

namespace AutoSurvival.Interaction
{
    /// <summary>
    /// 타깃을 따라다니는 궤도 카메라 (아기자기한 디오라마 시점: 좁은 화각, 비스듬히 내려다봄).
    /// 계층: CameraRig(이 컴포넌트) > ShakePivot(CameraShaker) > Camera. 흔들림은 ShakePivot만 쓰므로 충돌하지 않는다.
    /// </summary>
    public class ThirdPersonCamera : MonoBehaviour
    {
        [SerializeField] private Transform target;
        [SerializeField] private float height = 1.3f;          // 타깃 기준 바라보는 높이
        [SerializeField] private float distance = 8f;
        [SerializeField] private float minDistance = 4f;
        [SerializeField] private float maxDistance = 14f;
        [SerializeField] private float pitch = 38f;
        [SerializeField] private float minPitch = 12f;
        [SerializeField] private float maxPitch = 70f;
        [SerializeField] private float yawSpeed = 0.22f;        // 픽셀 → 도
        [SerializeField] private float pitchSpeed = 0.16f;
        [SerializeField] private float followSmooth = 10f;
        [SerializeField] private float collisionRadius = 0.3f;
        [SerializeField] private LayerMask collisionMask = ~0;

        private float _yaw, _dist;
        private Vector3 _focus;

        public float Yaw => _yaw;

        public void SetTarget(Transform t, float startYaw)
        {
            target = t; _yaw = startYaw;
            if (t != null) _focus = t.position + Vector3.up * height;
        }

        private void OnEnable()
        {
            _dist = distance;
            if (target != null) { _focus = target.position + Vector3.up * height; _yaw = transform.eulerAngles.y; }
        }

        private void LateUpdate()
        {
            if (target == null) return;

            if (!GameInput.MovementBlocked)
            {
                var look = GameInput.Look;
                _yaw += look.x * yawSpeed;
                pitch = ThirdPersonMath.ClampPitch(pitch - look.y * pitchSpeed, minPitch, maxPitch);
                float z = GameInput.Zoom;
                if (Mathf.Abs(z) > 0.01f) distance = Mathf.Clamp(distance - z * 0.8f, minDistance, maxDistance);
            }
            _dist = Mathf.Lerp(_dist, distance, 1f - Mathf.Exp(-8f * Time.deltaTime));

            var want = target.position + Vector3.up * height;
            _focus = Vector3.Lerp(_focus, want, 1f - Mathf.Exp(-followSmooth * Time.deltaTime));

            var dir = ThirdPersonMath.OrbitOffset(_yaw, pitch, 1f);
            float d = _dist;
            if (Physics.SphereCast(_focus, collisionRadius, dir, out var hit, d, collisionMask, QueryTriggerInteraction.Ignore)
                && hit.collider.transform.root != target.root)
                d = Mathf.Max(1.2f, hit.distance);

            transform.position = _focus + dir * d;
            transform.rotation = Quaternion.LookRotation(_focus - transform.position, Vector3.up);
        }
    }
}
