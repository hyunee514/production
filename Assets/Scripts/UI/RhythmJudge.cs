using System;

namespace AutoSurvival.UI
{
    public enum RhythmGrade { Miss, Good, Perfect }

    public readonly struct RhythmJudgement
    {
        public readonly RhythmGrade grade;
        public readonly float errorSeconds;    // 가장 가까운 박자와의 차이 (+ 늦음 / - 이름)
        public readonly int beatIndex;         // 가장 가까운 박자 번호 (0부터)

        public RhythmJudgement(RhythmGrade grade, float errorSeconds, int beatIndex)
        {
            this.grade = grade; this.errorSeconds = errorSeconds; this.beatIndex = beatIndex;
        }

        /// <summary>펌핑 품질: 완벽 1.0 / 좋음 0.5 / 헛바람 0.</summary>
        public float Quality => grade == RhythmGrade.Perfect ? 1f : grade == RhythmGrade.Good ? 0.5f : 0f;
        public bool IsEarly => errorSeconds < 0f;
    }

    /// <summary>
    /// 박자 판정기 (UnityEngine 의존 없음 -> EditMode 테스트 가능).
    /// 박자는 시계 t = 0, beat, 2·beat ... 에서 울린다. 판정 창은 박자 앞뒤 ±window.
    /// MetronomeUI(표시·입력)와 HydraulicPuzzle(내장 리듬 모드)이 같은 클래스를 써서 판정이 어긋나지 않는다.
    /// </summary>
    public sealed class RhythmJudge
    {
        public float BeatSeconds { get; }
        public float PerfectWindow { get; }
        public float GoodWindow { get; }

        public RhythmJudge(float beatSeconds, float perfectWindow, float goodWindow)
        {
            if (beatSeconds <= 0f) throw new ArgumentOutOfRangeException(nameof(beatSeconds));
            if (perfectWindow < 0f || goodWindow < perfectWindow)
                throw new ArgumentException("판정 창은 0 <= perfect <= good 이어야 합니다.");
            BeatSeconds = beatSeconds; PerfectWindow = perfectWindow; GoodWindow = goodWindow;
        }

        /// <summary>
        /// clock: 박자 시작 이후 경과 시간(초). inputLatency: 입력·화면 지연 보정(초).
        /// 보정값만큼 입력 시점을 앞당겨(음수면 늦춰) 계산한다.
        /// </summary>
        public RhythmJudgement Judge(float clock, float inputLatency = 0f)
        {
            float t = clock - inputLatency;
            int nearest = (int)Math.Round(t / BeatSeconds, MidpointRounding.AwayFromZero);
            if (nearest < 0) nearest = 0;
            float error = t - nearest * BeatSeconds;
            float abs = Math.Abs(error);

            var grade = abs <= PerfectWindow ? RhythmGrade.Perfect
                      : abs <= GoodWindow ? RhythmGrade.Good
                      : RhythmGrade.Miss;
            return new RhythmJudgement(grade, error, nearest);
        }

        /// <summary>다음 박자까지의 진행도 0~1 (직전 박자 = 0, 다음 박자 = 1). 접근 링 연출용.</summary>
        public float ApproachProgress(float clock)
        {
            float phase = clock % BeatSeconds;
            if (phase < 0f) phase += BeatSeconds;
            return phase / BeatSeconds;
        }

        /// <summary>이번 프레임에 박자를 넘었는가 (틱 사운드·플래시 트리거용).</summary>
        public bool CrossedBeat(float previousClock, float currentClock) =>
            (int)Math.Floor(currentClock / BeatSeconds) > (int)Math.Floor(previousClock / BeatSeconds);
    }

    /// <summary>
    /// 입력 한 번당 판정 한 번을 보장하는 상태형 래퍼. 같은 박자를 두 번 이상 누르면(연타) 뒤의 입력은 Miss로 처리한다.
    /// 박자 앞뒤 창이 겹쳐 연타로 '완벽'을 반복하는 편법을 막는다.
    /// </summary>
    public sealed class RhythmSession
    {
        public RhythmJudge Judge { get; }
        private int _lastBeat = -1;

        public RhythmSession(RhythmJudge judge) { Judge = judge; }

        public RhythmJudgement Press(float clock, float inputLatency = 0f)
        {
            var j = Judge.Judge(clock, inputLatency);
            if (j.grade != RhythmGrade.Miss)
            {
                if (j.beatIndex == _lastBeat) return new RhythmJudgement(RhythmGrade.Miss, j.errorSeconds, j.beatIndex);
                _lastBeat = j.beatIndex;
            }
            return j;
        }

        public void Reset() { _lastBeat = -1; }
    }
}
