using System;
using System.Collections.Generic;

namespace AutoSurvival.Feedback
{
    /// <summary>카메라 흔들림 등급. 규격은 폴리싱 기획서 3장.</summary>
    public enum ShakeGrade { None, S, M, L }

    /// <summary>한 등급의 흔들림 규격: 위치 진폭(m), 회전 진폭(도, Z축 기준), 지속 시간(초).</summary>
    public readonly struct ShakeSpec
    {
        public readonly float posAmp, rotAmp, duration;
        public ShakeSpec(float posAmp, float rotAmp, float duration) { this.posAmp = posAmp; this.rotAmp = rotAmp; this.duration = duration; }
    }

    public static class ShakeTable
    {
        public static ShakeSpec Of(ShakeGrade g)
        {
            switch (g)
            {
                case ShakeGrade.S: return new ShakeSpec(0.03f, 0.4f, 0.15f);
                case ShakeGrade.M: return new ShakeSpec(0.08f, 1.0f, 0.30f);
                case ShakeGrade.L: return new ShakeSpec(0.18f, 2.2f, 0.50f);
                default: return new ShakeSpec(0f, 0f, 0f);
            }
        }
    }

    public struct ShakeSample
    {
        public float px, py, pz;      // 위치 오프셋 (m)
        public float rx, ry, rz;      // 회전 오프셋 (도). 회전의 중심은 Z축(롤)
        public bool IsZero => px == 0f && py == 0f && pz == 0f && rx == 0f && ry == 0f && rz == 0f;
    }

    /// <summary>
    /// 카메라 흔들림 계산기 (UnityEngine 의존 없음 -> 테스트 가능).
    ///  - 진폭은 지수 감쇠: A(t) = A0 * exp(-k * (t - hold)), 지속 시간 끝에서 처음의 2%가 되도록 k를 정한다.
    ///  - 여러 흔들림이 겹치면 합산하지 않고 가장 큰 값만 쓴다 (화면이 폭주하지 않게).
    ///  - hold > 0이면 그 시간 동안은 감쇠 없이 유지한다 (Strain 같은 연속 진동).
    ///  - 실제 움직임은 노이즈 함수로 만든다 (Unity에서는 Mathf.PerlinNoise). 회전은 Z축이 가장 크고 X·Y는 작다.
    /// </summary>
    public sealed class ShakeState
    {
        public const float EndFraction = 0.02f;
        public float Frequency { get; set; } = 22f;
        public float ZRotationBias { get; set; } = 1f;       // Z축 회전 배율
        public float XYRotationBias { get; set; } = 0.35f;   // X·Y축 회전 배율
        public float DepthBias { get; set; } = 0.35f;        // 앞뒤(Z) 위치 배율

        private sealed class Impulse { public float pos, rot, age, hold, k, end; }
        private readonly List<Impulse> _list = new();
        private float _time;

        public int ActiveCount => _list.Count;

        /// <param name="scale">설정의 흔들림 세기 등과 곱할 배율</param>
        /// <param name="holdSeconds">감쇠 전에 유지할 시간</param>
        public void Add(ShakeGrade grade, float scale = 1f, float holdSeconds = 0f)
        {
            var spec = ShakeTable.Of(grade);
            if (spec.duration <= 0f || scale <= 0f) return;
            float decay = spec.duration;
            _list.Add(new Impulse
            {
                pos = spec.posAmp * scale, rot = spec.rotAmp * scale,
                hold = Math.Max(0f, holdSeconds),
                k = (float)(-Math.Log(EndFraction) / decay),
                end = Math.Max(0f, holdSeconds) + decay,
            });
        }

        /// <summary>현재 진폭(가장 큰 흔들림의 위치 진폭, 회전 진폭).</summary>
        public (float pos, float rot) CurrentAmplitude()
        {
            float p = 0f, r = 0f;
            foreach (var i in _list)
            {
                float f = i.age <= i.hold ? 1f : (float)Math.Exp(-i.k * (i.age - i.hold));
                p = Math.Max(p, i.pos * f);
                r = Math.Max(r, i.rot * f);
            }
            return (p, r);
        }

        /// <summary>dt만큼 시간을 진행하고 이번 프레임의 오프셋을 돌려준다.</summary>
        /// <param name="noise01">(채널 번호, 시간) -> 0~1 노이즈. 예: Mathf.PerlinNoise(채널 * 17.3f, time)</param>
        /// <param name="intensity">0~1. 사용자 설정(흔들림 세기)</param>
        public ShakeSample Step(float dt, Func<float, float, float> noise01, float intensity = 1f)
        {
            _time += dt;
            foreach (var i in _list) i.age += dt;
            _list.RemoveAll(i => i.age >= i.end);

            var s = new ShakeSample();
            if (_list.Count == 0 || intensity <= 0f) return s;

            var (pos, rot) = CurrentAmplitude();
            pos *= intensity; rot *= intensity;
            float t = _time * Frequency;
            float N(int ch) => noise01(ch, t) * 2f - 1f;

            s.px = pos * N(0); s.py = pos * N(1); s.pz = pos * DepthBias * N(2);
            s.rx = rot * XYRotationBias * N(3); s.ry = rot * XYRotationBias * N(4); s.rz = rot * ZRotationBias * N(5);
            return s;
        }

        public void Clear() { _list.Clear(); }
    }

    /// <summary>
    /// 연속 성공 콤보와 피치 상승 사다리.
    ///  - 완벽: 콤보 +1, 피치가 반음씩 올라간다 (처음 완벽은 원래 음, 최대 MaxSemitones까지)
    ///  - 좋음: 콤보 유지, 피치 유지
    ///  - 헛바람: 콤보와 피치 모두 초기화
    /// </summary>
    public sealed class PitchLadder
    {
        public int StepSemitones { get; set; } = 1;
        public int MaxSemitones { get; set; } = 4;
        public int Combo { get; private set; }

        public int Semitones => Math.Min(MaxSemitones, Math.Max(0, Combo - 1) * StepSemitones);
        public float PitchMultiplier => SemitonesToRatio(Semitones);

        public void OnPerfect() => Combo++;
        public void OnGood() { /* 콤보 유지 */ }
        public void OnMiss() => Combo = 0;
        public void Reset() => Combo = 0;

        public static float SemitonesToRatio(float semitones) => (float)Math.Pow(2.0, semitones / 12.0);
    }

    /// <summary>히트스톱(순간 정지) 남용 방지: 한 번 최대 MaxSeconds, 끝난 뒤 CooldownSeconds 동안은 새 요청을 무시한다.</summary>
    public sealed class HitstopGate
    {
        public float MaxSeconds { get; set; } = 0.1f;
        public float CooldownSeconds { get; set; } = 0.5f;
        private float _blockedUntil = float.NegativeInfinity;

        /// <param name="nowRealtime">멈춰도 흐르는 시계(Time.unscaledTime)</param>
        /// <returns>실제로 멈출 시간 (0이면 요청이 무시됨)</returns>
        public float Request(float seconds, float nowRealtime)
        {
            if (seconds <= 0f || nowRealtime < _blockedUntil) return 0f;
            float granted = Math.Min(seconds, MaxSeconds);
            _blockedUntil = nowRealtime + granted + CooldownSeconds;
            return granted;
        }
    }

    /// <summary>효과음 목록에서 직전과 같은 것을 연달아 뽑지 않는다.</summary>
    public sealed class NoRepeatPicker
    {
        private int _last = -1;

        public int Pick(int count, Func<float> rng)
        {
            if (count <= 0) return -1;
            if (count == 1) { _last = 0; return 0; }
            int i = Math.Min(count - 1, (int)(rng() * count));
            if (i == _last) i = (i + 1) % count;
            _last = i;
            return i;
        }
    }

    /// <summary>시간 압박 알림: 남은 시간이 처음의 25% 이하(또는 20초 이하)가 되면 한 번 TimeLow, 한 프레임에 크게 깎이면 Mistake.</summary>
    public sealed class TimeAlarm
    {
        public float LowFraction { get; set; } = 0.25f;
        public float LowSeconds { get; set; } = 20f;
        public float MistakeDrop { get; set; } = 1.5f;      // 프레임 시간(dt)보다 이만큼 이상 더 깎이면 실수로 본다

        private readonly float _initial;
        private float _prev;
        private bool _lowFired;

        public TimeAlarm(float initialTimeLeft) { _initial = initialTimeLeft; _prev = initialTimeLeft; }

        public bool IsLow => _lowFired;

        public (bool low, bool mistake) Update(float timeLeft, float dt)
        {
            bool mistake = (_prev - timeLeft) > dt + MistakeDrop;
            _prev = timeLeft;

            bool low = false;
            if (!_lowFired && timeLeft > 0f && (timeLeft <= _initial * LowFraction || timeLeft <= LowSeconds))
            {
                _lowFired = true; low = true;
            }
            return (low, mistake);
        }
    }

    /// <summary>색 (UnityEngine.Color와 따로 쓰는 0~1 RGB).</summary>
    public readonly struct Rgb
    {
        public readonly float r, g, b;
        public Rgb(float r, float g, float b) { this.r = r; this.g = g; this.b = b; }
        public static Rgb Lerp(Rgb a, Rgb c, float t)
        {
            t = Math.Max(0f, Math.Min(1f, t));
            return new Rgb(a.r + (c.r - a.r) * t, a.g + (c.g - a.g) * t, a.b + (c.b - a.b) * t);
        }
    }

    /// <summary>스태미나 바 색: 50% 이상 청록, 50~25% 노랑, 25% 이하 빨강. 경계는 ±Blend 구간에서 선형으로 섞어 부드럽게 바뀐다.</summary>
    public static class StaminaPalette
    {
        public static readonly Rgb Good = new Rgb(0.30f, 0.80f, 0.75f);
        public static readonly Rgb Warn = new Rgb(0.95f, 0.85f, 0.25f);
        public static readonly Rgb Danger = new Rgb(0.90f, 0.30f, 0.25f);
        public const float WarnAt = 0.5f, DangerAt = 0.25f, Blend = 0.06f;

        public static Rgb Evaluate(float normalized)
        {
            float n = Math.Max(0f, Math.Min(1f, normalized));
            if (n >= WarnAt + Blend) return Good;
            if (n >= WarnAt - Blend) return Rgb.Lerp(Warn, Good, (n - (WarnAt - Blend)) / (2f * Blend));
            if (n >= DangerAt + Blend) return Warn;
            if (n >= DangerAt - Blend) return Rgb.Lerp(Danger, Warn, (n - (DangerAt - Blend)) / (2f * Blend));
            return Danger;
        }

        /// <summary>25% 이하에서 1Hz로 0~1 맥동. 그 위에서는 0.</summary>
        public static float Pulse(float normalized, float timeSeconds)
        {
            if (normalized > DangerAt) return 0f;
            return 0.5f + 0.5f * (float)Math.Sin(timeSeconds * 2.0 * Math.PI);
        }
    }

    /// <summary>이징 함수와 펀치 곡선 (모두 t는 0~1).</summary>
    public static class Ease
    {
        public static float Clamp01(float t) => t < 0f ? 0f : (t > 1f ? 1f : t);
        public static float Linear(float t) => Clamp01(t);
        public static float OutQuad(float t) { t = Clamp01(t); return 1f - (1f - t) * (1f - t); }
        public static float OutCubic(float t) { t = Clamp01(t); float u = 1f - t; return 1f - u * u * u; }
        public static float InOutQuad(float t) { t = Clamp01(t); return t < 0.5f ? 2f * t * t : 1f - (float)Math.Pow(-2f * t + 2f, 2) / 2f; }

        /// <summary>끝에서 살짝 넘쳤다가 돌아오는 이징 (등장 연출용).</summary>
        public static float OutBack(float t, float overshoot = 1.70158f)
        {
            t = Clamp01(t);
            float c3 = overshoot + 1f;
            float u = t - 1f;
            return 1f + c3 * u * u * u + overshoot * u * u;
        }

        /// <summary>감쇠 사인 펀치: 0에서 시작해 amount만큼 튀었다가 0으로 돌아온다 (vibrato는 왕복 횟수).</summary>
        public static float Punch(float t, float amount, int vibrato = 2)
        {
            t = Clamp01(t);
            float decay = (1f - t) * (1f - t);
            return amount * (float)Math.Sin(t * vibrato * Math.PI) * decay;
        }
    }
}
