using UnityEngine;
using AutoSurvival.Settings;

namespace AutoSurvival.Feedback
{
    /// <summary>
    /// 카메라 흔들림 (펄린 노이즈 + 지수 감쇠). 계산은 FeedbackCore의 ShakeState가 하고, 이 스크립트는 값을 Transform에 적용만 한다.
    ///
    /// 설치: Player > Head(마우스 상하 회전) > ShakePivot(이 스크립트) > Main Camera
    ///  - ShakePivot에는 다른 스크립트가 회전을 쓰지 않는다. 그래서 localPosition/localRotation을 "절대값"으로 덮어써도
    ///    ThirdPersonCamera의 궤도 회전과 충돌하지 않는다 (흔들림이 끝나면 정확히 0으로 돌아온다).
    ///  - UI는 Canvas라서 이 흔들림의 영향을 받지 않는다 (의도된 동작: UI는 고정, 3D 카메라만 흔든다).
    ///  - 일시정지(Time.timeScale = 0)나 히트스톱 중에도 흔들림이 부드럽게 끝나도록 unscaledDeltaTime을 쓴다.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public class CameraShaker : MonoBehaviour
    {
        public static CameraShaker Instance { get; private set; }

        [Tooltip("흔들릴 대상. 비워두면 이 오브젝트")]
        [SerializeField] private Transform target;
        [Range(0f, 1f)] [SerializeField] private float intensity = 1f;
        [SerializeField] private float frequency = 22f;
        [Tooltip("Z축 회전(롤)의 세기 배율. 기본 1")]
        [SerializeField] private float rollBias = 1f;
        [Tooltip("멀미 방지: 회전 흔들림을 끈다 (위치만 흔들림)")]
        [SerializeField] private bool positionOnly = false;

        private readonly ShakeState _state = new();
        private Vector3 _basePos;
        private Quaternion _baseRot;
        private bool _hadShake;

        /// <summary>실제로 적용되는 흔들림 세기 (0~1) = 인스펙터 값 x 설정 화면의 흔들림 세기. 설정은 SettingsManager가 저장한다.</summary>
        public float Intensity => intensity * SettingsManager.Current.Shake01;

        private void Awake()
        {
            Instance = this;
            if (target == null) target = transform;
            _basePos = target.localPosition;
            _baseRot = target.localRotation;
            _state.Frequency = frequency;
            _state.ZRotationBias = rollBias;
        }

        private void OnDestroy() { if (Instance == this) Instance = null; }

        /// <summary>등급으로 흔들기. scale은 이 흔들림만의 추가 배율, hold는 감쇠 전 유지 시간.</summary>
        public void Shake(ShakeGrade grade, float scale = 1f, float hold = 0f)
        {
            if (grade == ShakeGrade.None) return;
            _state.Add(grade, scale, hold);
        }

        public static void ShakeStatic(ShakeGrade grade) { if (Instance != null) Instance.Shake(grade); }

        public void Stop() { _state.Clear(); }

        private void LateUpdate()
        {
            if (target == null) return;
            var s = _state.Step(Time.unscaledDeltaTime, Noise, Intensity);

            if (s.IsZero)
            {
                if (_hadShake) { target.localPosition = _basePos; target.localRotation = _baseRot; _hadShake = false; }
                return;
            }

            _hadShake = true;
            target.localPosition = _basePos + new Vector3(s.px, s.py, s.pz);
            target.localRotation = positionOnly ? _baseRot : _baseRot * Quaternion.Euler(s.rx, s.ry, s.rz);
        }

        /// <summary>채널마다 다른 줄을 읽도록 y축을 채널 번호로 벌린다.</summary>
        private static float Noise(float channel, float t) => Mathf.PerlinNoise(t, channel * 17.3f + 0.5f);
    }
}
