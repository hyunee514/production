using System;
using System.Collections.Generic;

namespace AutoSurvival.Audio
{
    /// <summary>오디오 레이어. 마스터는 따로 있고, 이 셋이 마스터 아래에 놓인다.</summary>
    public enum AudioBus
    {
        Ambient,   // 환경음(파도, 바람) + 기계 루프음. 설정 화면에서는 "환경음" 슬라이더 하나로 묶는다.
        Sfx,       // 퍼즐 효과음, 채집음, 판정음. 더킹의 영향을 받지 않는다.
    }

    /// <summary>오디오 계산 모음 (UnityEngine 의존 없음 -> 테스트 가능).</summary>
    public static class AudioMath
    {
        /// <summary>슬라이더 퍼센트(0~100) -> 소리 크기(0~1). 제곱 곡선이라 낮은 쪽을 세밀하게 조절할 수 있다. 50% = 0.25.</summary>
        public static float PercentToGain(int percent)
        {
            float t = Math.Max(0, Math.Min(100, percent)) / 100f;
            return t * t;
        }

        /// <summary>기계 루프음의 피치: 티어가 높을수록 조금 더 높고 힘찬 소리 (티어 1 = 1.0, 티어당 +4%).</summary>
        public static float MachineLoopPitch(int tier) => 1f + 0.04f * (Math.Max(1, Math.Min(5, tier)) - 1);

        /// <summary>크로스페이드 (등전력 곡선): t 0~1 -> (나가는 소리, 들어오는 소리). 중간에서 소리가 꺼지는 느낌이 없다.</summary>
        public static (float outGain, float inGain) EqualPowerCrossfade(float t)
        {
            t = Math.Max(0f, Math.Min(1f, t));
            double a = t * Math.PI / 2.0;
            return ((float)Math.Cos(a), (float)Math.Sin(a));
        }

        /// <summary>
        /// 같은 장면에서 기계가 여러 대 돌아도 소리가 겹쳐 터지지 않게 하는 보정: n대가 돌 때 각 기계 소리를 1/sqrt(n)로.
        /// </summary>
        public static float CrowdGain(int runningCount) => runningCount <= 1 ? 1f : (float)(1.0 / Math.Sqrt(runningCount));
    }

    /// <summary>
    /// 더킹 (퍼즐 중 배경음 낮추기). 여러 곳에서 동시에 요청할 수 있고, 가장 깊게 낮추라는 요청이 이긴다.
    /// 내려갈 때는 빠르게(attack), 돌아올 때는 천천히(release) 움직인다. 프레임 속도와 무관하게 같은 시간에 같은 결과.
    /// </summary>
    public sealed class DuckingMixer
    {
        public float AttackSeconds { get; set; } = 0.25f;
        public float ReleaseSeconds { get; set; } = 0.9f;
        public float Gain { get; private set; } = 1f;

        private readonly Dictionary<string, float> _requests = new();

        public int RequestCount => _requests.Count;

        /// <summary>목표 소리 크기 (요청이 없으면 1)</summary>
        public float Target
        {
            get
            {
                float t = 1f;
                foreach (var g in _requests.Values) if (g < t) t = g;
                return t;
            }
        }

        /// <param name="gain">0~1. 낮출수록 깊은 더킹. 같은 id로 다시 요청하면 값만 바뀐다.</param>
        public void Request(string id, float gain) => _requests[id] = Math.Max(0f, Math.Min(1f, gain));
        public void Release(string id) => _requests.Remove(id);
        public void ReleaseAll() => _requests.Clear();

        public float Step(float dt)
        {
            float target = Target;
            float tau = target < Gain ? AttackSeconds : ReleaseSeconds;
            if (dt <= 0f) return Gain;
            if (tau <= 0f) Gain = target;
            else Gain = target + (Gain - target) * (float)Math.Exp(-dt / tau);
            if (Math.Abs(Gain - target) < 0.001f) Gain = target;
            return Gain;
        }

        public void Snap() { Gain = Target; }
    }

    /// <summary>
    /// 한 번에 울릴 수 있는 효과음 수를 제한하는 동시 재생 관리 (가장 오래된 소리부터 끊는다).
    /// </summary>
    public sealed class VoiceBudget
    {
        private readonly int _max;
        private readonly Queue<int> _order = new();
        public VoiceBudget(int max) { _max = Math.Max(1, max); }

        /// <summary>새 소리를 시작할 때 부른다. 돌려주는 값이 -1이 아니면 그 음성 번호를 끊고 재사용하라는 뜻.</summary>
        public int Acquire(int newVoiceId)
        {
            int steal = -1;
            if (_order.Count >= _max) steal = _order.Dequeue();
            _order.Enqueue(newVoiceId);
            return steal;
        }
    }
}
