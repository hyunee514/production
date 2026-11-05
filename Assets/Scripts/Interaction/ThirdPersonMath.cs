using System.Collections.Generic;
using UnityEngine;

namespace AutoSurvival.Interaction
{
    /// <summary>3인칭 조작의 순수 계산 (Python 포트로 검증).</summary>
    public static class ThirdPersonMath
    {
        /// <summary>카메라 yaw(도) 기준 입력(x=좌우, y=앞뒤)을 월드 이동 방향으로. 길이는 입력 길이(최대 1).</summary>
        public static Vector3 CameraRelative(float yawDeg, Vector2 input)
        {
            if (input.sqrMagnitude < 1e-6f) return Vector3.zero;
            if (input.sqrMagnitude > 1f) input.Normalize();
            float r = yawDeg * Mathf.Deg2Rad;
            var fwd = new Vector3(Mathf.Sin(r), 0f, Mathf.Cos(r));
            var right = new Vector3(Mathf.Cos(r), 0f, -Mathf.Sin(r));
            return right * input.x + fwd * input.y;
        }

        public static float ClampPitch(float pitch, float min, float max) => Mathf.Clamp(pitch, min, max);

        public static float WrapAngle(float a)
        {
            a %= 360f;
            if (a > 180f) a -= 360f;
            if (a < -180f) a += 360f;
            return a;
        }

        /// <summary>yaw/pitch/거리로 카메라 위치 오프셋(타깃 기준)을 구한다.</summary>
        public static Vector3 OrbitOffset(float yawDeg, float pitchDeg, float distance)
        {
            var rot = Quaternion.Euler(pitchDeg, yawDeg, 0f);
            return rot * new Vector3(0f, 0f, -distance);
        }

        /// <summary>
        /// 근접 상호작용 대상 고르기: 반경 안에서, 플레이어가 바라보는 방향(facing)에 가깝고 가까운 것.
        /// 점수 = 거리 + 각도 페널티. 후보가 없으면 -1.
        /// </summary>
        public static int PickTarget(Vector3 origin, Vector3 facing, IList<Vector3> candidates, float radius, float angleWeight = 1.5f)
        {
            int best = -1; float bestScore = float.MaxValue;
            facing.y = 0f;
            if (facing.sqrMagnitude > 1e-6f) facing.Normalize();
            for (int i = 0; i < candidates.Count; i++)
            {
                var d = candidates[i] - origin; d.y = 0f;
                float dist = d.magnitude;
                if (dist > radius) continue;
                float angle01 = 0f;
                if (dist > 0.01f && facing.sqrMagnitude > 0f)
                    angle01 = Mathf.Acos(Mathf.Clamp(Vector3.Dot(facing, d / dist), -1f, 1f)) / Mathf.PI;   // 0(정면)~1(뒤)
                float score = dist + angle01 * angleWeight * radius * 0.5f;
                if (score < bestScore) { bestScore = score; best = i; }
            }
            return best;
        }

        /// <summary>목표 각도로 부드럽게 회전 (도). maxDelta 이상 한 번에 돌지 않는다.</summary>
        public static float TurnToward(float current, float target, float maxDelta)
        {
            float diff = WrapAngle(target - current);
            if (Mathf.Abs(diff) <= maxDelta) return target;
            return current + Mathf.Sign(diff) * maxDelta;
        }
    }
}
