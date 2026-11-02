using System;
using System.Collections.Generic;
using AutoSurvival.Dialogue;

namespace AutoSurvival.UI
{
    /// <summary>화면에 띄울 대사 한 줄 (OopiSpeech를 UI용으로 풀어 둔 것).</summary>
    public sealed class DialogueEntry
    {
        public string id;
        public string text;
        public string tip;
        public OopiMood mood;
        public OopiSituation situation;
        public int priority;
        public float enqueuedAt;
    }

    /// <summary>
    /// 대사 중요도. 큐가 넘치면 낮은 것부터 버리고, 낮은 것은 오래 기다리면 낡은 말로 취급해 건너뛴다.
    /// 3: 지금 상황을 설명하는 말 (진단, 시간 촉박) / 2: 결과·이벤트 / 1: 생존 경고 / 0: 혼잣말
    /// </summary>
    public static class DialoguePriority
    {
        public static int Of(OopiSituation s)
        {
            switch (s)
            {
                case OopiSituation.Diagnosis:
                case OopiSituation.TimeLow: return 3;
                case OopiSituation.SurvivalWarning: return 1;
                case OopiSituation.Idle: return 0;
                default: return 2;
            }
        }
    }

    /// <summary>
    /// 대사 재생기 (UnityEngine 의존 없음 -> 테스트 가능). 여러 대사가 한꺼번에 들어와도
    /// 들어온 순서대로 한 줄씩 타자 효과 -> 유지 -> 다음 줄 순으로 재생한다.
    ///  - 같은 대사가 연달아 들어오면 무시 (중복 방지)
    ///  - 혼잣말(우선순위 0)은 이미 말하는 중이거나 대기열이 있으면 버린다
    ///  - 대기열이 최대치를 넘으면 가장 중요도가 낮은 것 중 가장 오래된 것을 버린다
    ///  - 중요도 1 이하는 StaleSeconds 넘게 기다렸으면 낡은 말이라 건너뛴다
    ///  - 대기열이 길어지면(CatchUpQueue 이상) 유지 시간이 절반으로 줄어 따라잡는다
    ///  - Skip(): 타자 중이면 한 줄을 즉시 완성, 이미 완성이면 바로 다음 줄로
    /// </summary>
    public sealed class DialoguePlayer
    {
        public enum State { Idle, Typing, Holding }

        public float CharsPerSecond { get; set; } = 30f;
        public float MinHoldSeconds { get; set; } = 1.8f;
        public float HoldPerChar { get; set; } = 0.05f;
        public float TipExtraHold { get; set; } = 1.2f;
        public int MaxQueue { get; set; } = 6;
        public float StaleSeconds { get; set; } = 10f;
        public int CatchUpQueue { get; set; } = 3;

        public State Phase { get; private set; } = State.Idle;
        public DialogueEntry Current { get; private set; }
        public int VisibleChars { get; private set; }
        public int Pending => _queue.Count;
        public bool TipVisible => Phase == State.Holding && Current != null && !string.IsNullOrEmpty(Current.tip);

        public event Action<DialogueEntry> LineStarted;
        public event Action<DialogueEntry> LineFinished;

        private readonly List<DialogueEntry> _queue = new();
        private float _clock, _typed, _hold;

        /// <summary>대기열에 넣는다. 받아들여졌으면 true.</summary>
        public bool Enqueue(DialogueEntry e)
        {
            if (e == null || string.IsNullOrEmpty(e.text)) return false;
            if (e.priority <= 0 && (Current != null || _queue.Count > 0)) return false;     // 혼잣말은 바쁘면 버림

            string lastId = _queue.Count > 0 ? _queue[_queue.Count - 1].id : Current?.id;
            if (lastId != null && lastId == e.id) return false;                              // 같은 말 연속 방지

            e.enqueuedAt = _clock;
            _queue.Add(e);

            if (_queue.Count > MaxQueue) DropLeastImportant();
            if (Phase == State.Idle) StartNext();
            return true;
        }

        private void DropLeastImportant()
        {
            int victim = 0;
            for (int i = 1; i < _queue.Count; i++)
                if (_queue[i].priority < _queue[victim].priority) victim = i;   // 같으면 앞쪽(오래된 것)이 유지되어 희생
            _queue.RemoveAt(victim);
        }

        public void Tick(float dt)
        {
            _clock += dt;
            switch (Phase)
            {
                case State.Idle:
                    if (_queue.Count > 0) StartNext();
                    break;

                case State.Typing:
                    _typed += CharsPerSecond * dt;
                    VisibleChars = Math.Min(Current.text.Length, (int)_typed);
                    if (VisibleChars >= Current.text.Length) BeginHold();
                    break;

                case State.Holding:
                    _hold -= dt * (_queue.Count >= CatchUpQueue ? 2f : 1f);
                    if (_hold <= 0f) Finish();
                    break;
            }
        }

        public void Skip()
        {
            if (Phase == State.Typing) { VisibleChars = Current.text.Length; BeginHold(); }
            else if (Phase == State.Holding) Finish();
        }

        public void Clear()
        {
            _queue.Clear();
            Current = null; Phase = State.Idle; VisibleChars = 0;
        }

        private void StartNext()
        {
            while (_queue.Count > 0)
            {
                var e = _queue[0];
                _queue.RemoveAt(0);
                if (e.priority <= 1 && _clock - e.enqueuedAt > StaleSeconds) continue;   // 낡은 말

                Current = e; Phase = State.Typing; _typed = 0f; VisibleChars = 0;
                LineStarted?.Invoke(e);
                return;
            }
            Current = null; Phase = State.Idle; VisibleChars = 0;
        }

        private void BeginHold()
        {
            Phase = State.Holding;
            float len = Current.text.Length * HoldPerChar + (string.IsNullOrEmpty(Current.tip) ? 0f : TipExtraHold);
            _hold = Math.Max(MinHoldSeconds, len);
        }

        private void Finish()
        {
            var done = Current;
            Current = null; Phase = State.Idle; VisibleChars = 0;
            LineFinished?.Invoke(done);
            StartNext();
        }
    }
}
