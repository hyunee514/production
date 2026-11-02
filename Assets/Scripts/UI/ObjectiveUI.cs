using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using AutoSurvival.Objectives;

namespace AutoSurvival.UI
{
    /// <summary>
    /// 화면 한쪽의 목표 추적창. 현재 목표(진행 문구 포함)와 힌트, 직전에 달성한 목표(체크), 다음 목표 한두 개를 보여 준다.
    ///  - 목표가 달성되면 그 줄에 체크가 켜지고 살짝 튀며 초록으로 바뀌고, 곧 다음 목표가 현재 목표가 된다.
    ///  - J 키로 접고 펼친다 (접으면 현재 목표 한 줄만).
    ///  - 모두 달성하면 잠시 보여 준 뒤 서서히 사라진다.
    ///
    /// lineTemplate 규칙: 비활성 오브젝트 하나. 자식에 Text(제목)가 있고, 이름이 "Check"인 자식(체크 표시)이 있으면 달성 시 켜진다.
    /// 줄은 이 템플릿을 복제해서 만든다 (VerticalLayoutGroup이 있는 content 아래).
    /// </summary>
    public class ObjectiveUI : MonoBehaviour
    {
        [SerializeField] private GameObject root;
        [SerializeField] private CanvasGroup group;
        [SerializeField] private Text headerText;
        [SerializeField] private RectTransform content;
        [SerializeField] private GameObject lineTemplate;
        [SerializeField] private Text hintText;

        [Header("동작")]
        [SerializeField, Range(1, 6)] private int maxLines = 4;
        [SerializeField] private KeyCode collapseKey = KeyCode.J;
        [SerializeField] private float hideAfterAllDoneSeconds = 6f;

        [Header("색상")]
        [SerializeField] private Color activeColor = Color.white;
        [SerializeField] private Color doneColor = new Color(0.55f, 0.9f, 0.6f);
        [SerializeField] private Color upcomingColor = new Color(1f, 1f, 1f, 0.5f);

        private sealed class LineView { public GameObject go; public Text label; public GameObject check; public string id; }

        private readonly List<LineView> _lines = new();
        private ObjectiveManager _mgr;
        private bool _collapsed;
        private float _allDoneTimer = -1f;
        private string _progress = "";
        private string _justCompletedId;

        private void Awake()
        {
            if (lineTemplate != null) lineTemplate.SetActive(false);
            if (group == null && root != null) group = root.GetComponent<CanvasGroup>();
        }

        private void OnEnable() { TryBind(); Rebuild(); }
        private void OnDisable() { Unbind(); }

        private void TryBind()
        {
            var m = ObjectiveManager.Instance;
            if (m == null || m == _mgr) return;
            Unbind();
            _mgr = m;
            _mgr.OnChanged += Rebuild;
            _mgr.OnCompleted += HandleCompleted;
            _mgr.OnProgressText += HandleProgress;
        }

        private void Unbind()
        {
            if (_mgr == null) return;
            _mgr.OnChanged -= Rebuild;
            _mgr.OnCompleted -= HandleCompleted;
            _mgr.OnProgressText -= HandleProgress;
            _mgr = null;
        }

        private void Update()
        {
            if (_mgr == null) { TryBind(); if (_mgr != null) Rebuild(); }
            if (Input.GetKeyDown(collapseKey) && !IsTyping()) { _collapsed = !_collapsed; Rebuild(); }

            if (_allDoneTimer >= 0f)
            {
                _allDoneTimer += Time.unscaledDeltaTime;
                if (group != null)
                    group.alpha = Mathf.Clamp01(1f - (_allDoneTimer - hideAfterAllDoneSeconds) / 1.5f);
            }
        }

        private static bool IsTyping() => UnityEngine.EventSystems.EventSystem.current != null
            && UnityEngine.EventSystems.EventSystem.current.currentSelectedGameObject != null
            && UnityEngine.EventSystems.EventSystem.current.currentSelectedGameObject.GetComponent<InputField>() != null;

        // ───────── 이벤트 ─────────

        private void HandleCompleted(ObjectiveDef def) { _justCompletedId = def.id; }

        private void HandleProgress(string text)
        {
            _progress = text ?? "";
            UpdateActiveLabel();
        }

        // ───────── 그리기 ─────────

        private void Rebuild()
        {
            if (_mgr == null || lineTemplate == null || content == null) return;

            var view = _mgr.CurrentLines(_collapsed ? 1 : maxLines);
            bool allDone = _mgr.Tracker.AllDone;

            if (view.Count == 0) { SetRootVisible(false); return; }
            SetRootVisible(true);

            if (allDone) { if (_allDoneTimer < 0f) _allDoneTimer = 0f; }
            else { _allDoneTimer = -1f; if (group != null) group.alpha = 1f; }

            if (_collapsed) view = view.FindAll(l => l.state == LineState.Active);   // 접으면 현재 목표만
            if (view.Count == 0 && allDone) view = _mgr.CurrentLines(1);

            while (_lines.Count < view.Count) _lines.Add(MakeLine());

            for (int i = 0; i < _lines.Count; i++)
            {
                var lv = _lines[i];
                bool use = i < view.Count;
                lv.go.SetActive(use);
                if (!use) continue;

                var l = view[i];
                lv.id = l.def.id;
                bool done = l.state == LineState.Done;
                if (lv.check != null) lv.check.SetActive(done);
                if (lv.label != null)
                {
                    lv.label.text = LabelFor(l);
                    lv.label.color = done ? doneColor : l.state == LineState.Active ? activeColor : upcomingColor;
                    lv.label.fontStyle = l.state == LineState.Active ? FontStyle.Bold : FontStyle.Normal;
                }

                if (done && lv.id == _justCompletedId)
                {
                    UiTween.PunchScale(lv.go.transform, 0.15f, 0.2f, 2);
                    _justCompletedId = null;
                }
            }

            // 힌트: 현재 목표의 설명 한 줄 (접었을 땐 숨김)
            var active = view.Find(l => l.state == LineState.Active);
            if (hintText != null)
            {
                bool show = !_collapsed && active.def != null && !string.IsNullOrEmpty(active.def.hint);
                hintText.gameObject.SetActive(show);
                if (show) hintText.text = active.def.hint;
            }

            if (headerText != null) headerText.text = _collapsed ? "목표 (J)" : "목표";
            _progress = active.def != null ? active.progress : "";
        }

        private string LabelFor(ObjectiveLine l)
        {
            string p = l.state == LineState.Active && !string.IsNullOrEmpty(l.progress) ? $"  ({l.progress})" : "";
            return l.def.title + p;
        }

        private void UpdateActiveLabel()
        {
            if (_mgr == null) return;
            foreach (var lv in _lines)
            {
                if (!lv.go.activeSelf || lv.label == null || lv.label.fontStyle != FontStyle.Bold) continue;
                var def = _mgr.Tracker.Defs.FirstOrDefaultById(lv.id);
                if (def == null) continue;
                lv.label.text = def.title + (string.IsNullOrEmpty(_progress) ? "" : $"  ({_progress})");
                UiTween.PunchScale(lv.go.transform, 0.06f, 0.1f);
            }
        }

        private LineView MakeLine()
        {
            var go = Instantiate(lineTemplate, content);
            go.name = "Line";
            var lv = new LineView { go = go, label = go.GetComponentInChildren<Text>(true) };
            var chk = go.transform.Find("Check");
            lv.check = chk != null ? chk.gameObject : null;
            return lv;
        }

        private void SetRootVisible(bool v)
        {
            if (root != null && root.activeSelf != v) root.SetActive(v);
        }
    }

    internal static class ObjectiveListExt
    {
        public static ObjectiveDef FirstOrDefaultById(this IReadOnlyList<ObjectiveDef> defs, string id)
        {
            for (int i = 0; i < defs.Count; i++) if (defs[i].id == id) return defs[i];
            return null;
        }
    }
}
