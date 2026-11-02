using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using AutoSurvival.Feedback;

namespace AutoSurvival.UI
{
    /// <summary>
    /// 가벼운 UI 트윈 도구 (코루틴 기반, 외부 라이브러리 없음).
    ///  - 시간은 unscaledDeltaTime: 히트스톱이나 일시정지(timeScale 0) 중에도 UI 연출은 멈추지 않는다.
    ///  - 같은 대상의 같은 종류(채널) 트윈을 새로 시작하면 이전 것을 자동으로 끊는다 (연타해도 겹치지 않는다).
    ///  - 대상이 중간에 파괴되어도 에러 없이 조용히 끝난다.
    ///  - 보이지 않는 러너 오브젝트가 자동으로 만들어진다. 씬에 따로 붙일 것이 없다.
    ///  - 곡선 계산은 FeedbackCore의 Ease(순수 로직)를 쓴다.
    ///
    /// 사용 예:
    ///   UiTween.PunchScale(gradeText.rectTransform, 0.5f, 0.12f);                  // 완벽! 팝업 (1.5 -> 1.0)
    ///   UiTween.ColorTo(staminaFill, StaminaPalette 색, 0.15f);                    // 색 전환
    ///   UiTween.Fade(bubbleGroup, 1f, 0.12f);                                       // 말풍선 등장
    ///   UiTween.PopIn(bubbleRect);                                                  // 0.85 -> 1 오버슈트 등장
    ///   UiTween.Value(0.3f, t => bar.value = Mathf.Lerp(a, b, t));                  // 직접 만든 트윈
    /// </summary>
    public static class UiTween
    {
        public enum Channel { Scale = 0, Color = 1, Alpha = 2, Position = 3, Custom = 4 }

        public const float DefaultDuration = 0.12f;

        // ───────── 기본 트윈 ─────────

        /// <summary>0~1로 진행하는 값 트윈. onUpdate(이징이 적용된 t). 대상(owner)과 채널을 주면 같은 것끼리 자동으로 교체된다.</summary>
        public static void Value(float duration, Action<float> onUpdate, Func<float, float> ease = null,
                                 Action onComplete = null, UnityEngine.Object owner = null, Channel channel = Channel.Custom, float delay = 0f)
        {
            Runner.Instance.Start(Key(owner, channel), Run(duration, delay, onUpdate, ease ?? Ease.OutQuad, onComplete, owner));
        }

        /// <summary>크기를 from에서 to로 부드럽게 바꾼다.</summary>
        public static void ScaleTo(Transform t, Vector3 to, float duration = DefaultDuration, Func<float, float> ease = null, Action onComplete = null)
        {
            if (t == null) return;
            var from = t.localScale;
            Value(duration, k => { if (t != null) t.localScale = Vector3.LerpUnclamped(from, to, k); }, ease, onComplete, t, Channel.Scale);
        }

        /// <summary>
        /// 스케일 펀치: 평소 크기에서 (1+amount)배로 튀었다가 평소 크기로 돌아온다.
        /// 완벽! 팝업은 PunchScale(rect, 0.5f, 0.12f)이면 1.5 -> 1.0 이 된다.
        /// 연타해도 "튀기 전 평소 크기"를 기억해 두므로 크기가 점점 커지지 않는다.
        /// </summary>
        public static void PunchScale(Transform t, float amount, float duration = DefaultDuration, int vibrato = 0, Action onComplete = null)
        {
            if (t == null) return;
            int id = t.GetInstanceID();
            if (!_scaleBase.TryGetValue(id, out var baseScale) || !Runner.Instance.IsRunning(Key(t, Channel.Scale)))
                baseScale = t.localScale;
            _scaleBase[id] = baseScale;

            if (vibrato <= 0)
            {
                // 튀었다가 부드럽게 내려오는 기본 형태: 시작 즉시 (1+amount), 끝에서 1
                Value(duration, k =>
                {
                    if (t == null) return;
                    t.localScale = baseScale * (1f + amount * (1f - k));
                }, Ease.OutQuad, () => { if (t != null) t.localScale = baseScale; _scaleBase.Remove(id); onComplete?.Invoke(); }, t, Channel.Scale);
            }
            else
            {
                // 감쇠 사인: 여러 번 튕기며 가라앉는 펀치
                Value(duration, k =>
                {
                    if (t == null) return;
                    t.localScale = baseScale * (1f + Ease.Punch(k, amount, vibrato));
                }, Ease.Linear, () => { if (t != null) t.localScale = baseScale; _scaleBase.Remove(id); onComplete?.Invoke(); }, t, Channel.Scale);
            }
        }

        /// <summary>등장 연출: 처음 크기의 fromScale 배에서 시작해 살짝 넘쳤다가 1로 안착 + 투명도 0->1 (CanvasGroup이 있으면).</summary>
        public static void PopIn(RectTransform rt, float fromScale = 0.85f, float duration = 0.14f, CanvasGroup group = null)
        {
            if (rt == null) return;
            rt.localScale = Vector3.one * fromScale;
            Value(duration, k => { if (rt != null) rt.localScale = Vector3.LerpUnclamped(Vector3.one * fromScale, Vector3.one, k); },
                  t => Ease.OutBack(t, 1.2f), () => { if (rt != null) rt.localScale = Vector3.one; }, rt, Channel.Scale);
            if (group != null) Fade(group, 1f, duration, 0f);
        }

        /// <summary>그래픽(이미지, 글자)의 색을 현재 색에서 to로 바꾼다.</summary>
        public static void ColorTo(Graphic g, Color to, float duration = DefaultDuration, Action onComplete = null)
        {
            if (g == null) return;
            var from = g.color;
            Value(duration, k => { if (g != null) g.color = Color.LerpUnclamped(from, to, k); }, Ease.OutQuad, onComplete, g, Channel.Color);
        }

        /// <summary>색을 to로 순간 바꾸고 원래 색으로 돌아온다 (번쩍 효과). 1초에 3번 이상 반복하지 않도록 호출하는 쪽에서 조절한다.</summary>
        public static void FlashColor(Graphic g, Color flash, float duration = 0.15f)
        {
            if (g == null) return;
            var original = _flashBase.TryGetValue(g.GetInstanceID(), out var o) && Runner.Instance.IsRunning(Key(g, Channel.Color)) ? o : g.color;
            _flashBase[g.GetInstanceID()] = original;
            Value(duration, k => { if (g != null) g.color = Color.LerpUnclamped(flash, original, k); }, Ease.OutQuad,
                  () => { if (g != null) g.color = original; _flashBase.Remove(g.GetInstanceID()); }, g, Channel.Color);
        }

        /// <summary>CanvasGroup 투명도를 to로.</summary>
        public static void Fade(CanvasGroup group, float to, float duration = DefaultDuration, float delay = 0f, Action onComplete = null)
        {
            if (group == null) return;
            float from = group.alpha;
            Value(duration, k => { if (group != null) group.alpha = Mathf.LerpUnclamped(from, to, k); }, Ease.OutQuad, onComplete, group, Channel.Alpha, delay);
        }

        /// <summary>RectTransform을 현재 위치에서 목표 위치로 (anchoredPosition).</summary>
        public static void MoveTo(RectTransform rt, Vector2 to, float duration = DefaultDuration, Func<float, float> ease = null, Action onComplete = null)
        {
            if (rt == null) return;
            var from = rt.anchoredPosition;
            Value(duration, k => { if (rt != null) rt.anchoredPosition = Vector2.LerpUnclamped(from, to, k); }, ease, onComplete, rt, Channel.Position);
        }

        /// <summary>
        /// 팝업 하나만 좌우로 떨고 원위치로 돌아오는 연출 (Miss 팝업용).
        /// 화면 전체 UI를 흔드는 용도가 아니다: UI는 고정하고 3D 카메라만 흔든다는 원칙을 지킨다.
        /// </summary>
        public static void WiggleX(RectTransform rt, float pixels = 8f, float duration = 0.2f, int vibrato = 3)
        {
            if (rt == null) return;
            int id = rt.GetInstanceID();
            if (!_posBase.TryGetValue(id, out var home) || !Runner.Instance.IsRunning(Key(rt, Channel.Position)))
                home = rt.anchoredPosition;
            _posBase[id] = home;
            Value(duration, k => { if (rt != null) rt.anchoredPosition = home + new Vector2(Ease.Punch(k, pixels, vibrato), 0f); },
                  Ease.Linear, () => { if (rt != null) rt.anchoredPosition = home; _posBase.Remove(id); }, rt, Channel.Position);
        }

        /// <summary>대상의 특정 종류 트윈을 중단한다 (값은 현재 상태로 둔다).</summary>
        public static void Kill(UnityEngine.Object target, Channel channel) => Runner.Instance.Stop(Key(target, channel));

        public static void KillAll() => Runner.Instance.StopAll();

        // ───────── 내부 ─────────

        private static readonly Dictionary<int, Vector3> _scaleBase = new();
        private static readonly Dictionary<int, Color> _flashBase = new();
        private static readonly Dictionary<int, Vector2> _posBase = new();

        private static long Key(UnityEngine.Object owner, Channel ch)
        {
            // 소유자가 없으면 매번 고유한 키 -> 서로 끊지 않는다
            if (owner == null) return -(++_anonymous);
            return ((long)owner.GetInstanceID() << 4) | (long)ch;
        }
        private static long _anonymous;

        private static IEnumerator Run(float duration, float delay, Action<float> onUpdate, Func<float, float> ease, Action onComplete, UnityEngine.Object owner)
        {
            // owner가 있었는데 도중에 파괴되면 조용히 끝낸다 (owner == null 은 "처음부터 없음"과 구분해야 하므로 hadOwner를 기억)
            bool hadOwner = owner != null;

            while (delay > 0f) { delay -= Time.unscaledDeltaTime; yield return null; }
            if (hadOwner && owner == null) yield break;

            if (duration <= 0f)
            {
                onUpdate?.Invoke(ease(1f)); onComplete?.Invoke(); yield break;
            }

            float elapsed = 0f;
            while (elapsed < duration)
            {
                if (hadOwner && owner == null) yield break;
                elapsed += Time.unscaledDeltaTime;
                onUpdate?.Invoke(ease(elapsed / duration));
                yield return null;
            }
            if (hadOwner && owner == null) yield break;
            onUpdate?.Invoke(ease(1f));
            onComplete?.Invoke();
        }

        /// <summary>보이지 않는 코루틴 실행기. 첫 사용 때 자동 생성되고 씬이 바뀌어도 남는다.</summary>
        private sealed class Runner : MonoBehaviour
        {
            private static Runner _instance;
            public static Runner Instance
            {
                get
                {
                    if (_instance == null)
                    {
                        var go = new GameObject("[UiTween]") { hideFlags = HideFlags.HideAndDontSave };
                        if (Application.isPlaying) DontDestroyOnLoad(go);
                        _instance = go.AddComponent<Runner>();
                    }
                    return _instance;
                }
            }

            private readonly Dictionary<long, Coroutine> _running = new();

            public bool IsRunning(long key) => _running.ContainsKey(key);

            public void Start(long key, IEnumerator routine)
            {
                Stop(key);
                _running[key] = null;                       // 같은 프레임에 끝나는 트윈이 키를 지우고 나서 다시 쓰이지 않게 먼저 자리를 잡는다
                var c = StartCoroutine(Wrap(key, routine));
                if (_running.ContainsKey(key)) _running[key] = c;
            }

            public void Stop(long key)
            {
                if (_running.TryGetValue(key, out var c))
                {
                    if (c != null) StopCoroutine(c);
                    _running.Remove(key);
                }
            }

            public void StopAll() { StopAllCoroutines(); _running.Clear(); }

            private IEnumerator Wrap(long key, IEnumerator inner)
            {
                // 이미 같은 프레임에 끝나는 트윈도 키가 남지 않도록 try/finally 대신 단계별로 처리
                while (true)
                {
                    bool more;
                    try { more = inner.MoveNext(); }
                    catch (Exception ex) { Debug.LogException(ex); more = false; }
                    if (!more) break;
                    yield return inner.Current;
                }
                _running.Remove(key);
            }

            private void OnDestroy() { if (_instance == this) _instance = null; }
        }
    }
}
