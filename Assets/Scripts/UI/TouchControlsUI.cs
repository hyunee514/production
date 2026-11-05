using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AutoSurvival.UI
{
    /// <summary>
    /// 모바일 터치 컨트롤: 왼쪽 가상 조이스틱, 화면 드래그 시점 회전, 오른쪽 액션 버튼.
    /// 코드로 자신의 캔버스를 만든다 (프리팹 불필요). GameInput.TouchMode가 true일 때만 보인다.
    /// 버튼은 GameInput의 가상 키(E, Space, Q, Shift, Escape...)를 눌러 준다 → 기존 퍼즐/메뉴 키 입력이 그대로 동작.
    /// </summary>
    public class TouchControlsUI : MonoBehaviour
    {
        [SerializeField] private Font font;
        [SerializeField] private bool forceShow;            // 에디터/PC 테스트용

        private Canvas _lookCanvas, _btnCanvas;
        private CanvasGroup _group;
        private TouchStick _stick;
        private Sprite _circle;

        private void Awake()
        {
            _circle = MakeCircleSprite();
            BuildLookArea();
            BuildButtons();
        }

        private void Update()
        {
            bool show = forceShow || GameInput.TouchMode;
            if (_lookCanvas != null && _lookCanvas.gameObject.activeSelf != show) _lookCanvas.gameObject.SetActive(show);
            if (_btnCanvas != null && _btnCanvas.gameObject.activeSelf != show) _btnCanvas.gameObject.SetActive(show);
            if (!show) return;
            // 메뉴가 열려 있으면 컨트롤을 숨겨 메뉴 버튼을 가리지 않는다
            bool blocked = GameInput.MovementBlocked;
            _group.alpha = blocked ? 0f : 1f;
            _group.blocksRaycasts = !blocked;
            if (blocked && _stick != null) _stick.ResetStick();
        }

        // ───────── 구성 ─────────

        private Canvas NewCanvas(string name, int order)
        {
            var go = new GameObject(name, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            go.transform.SetParent(transform, false);
            var c = go.GetComponent<Canvas>();
            c.renderMode = RenderMode.ScreenSpaceOverlay;
            c.sortingOrder = order;
            var sc = go.GetComponent<CanvasScaler>();
            sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            sc.referenceResolution = new Vector2(1280f, 720f);
            sc.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            sc.matchWidthOrHeight = 0.5f;
            return c;
        }

        private void BuildLookArea()
        {
            _lookCanvas = NewCanvas("TouchLook", -50);                    // 다른 모든 UI 아래
            var go = new GameObject("LookArea", typeof(RectTransform), typeof(Image), typeof(TouchLookArea));
            go.transform.SetParent(_lookCanvas.transform, false);
            var r = (RectTransform)go.transform;
            r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = r.offsetMax = Vector2.zero;
            var img = go.GetComponent<Image>(); img.color = new Color(0, 0, 0, 0);
        }

        private void BuildButtons()
        {
            _btnCanvas = NewCanvas("TouchButtons", 30);
            _group = _btnCanvas.gameObject.AddComponent<CanvasGroup>();

            // 조이스틱 (왼쪽 아래)
            var baseRt = Circle("Stick", _btnCanvas.transform, new Vector2(0f, 0f), new Vector2(190f, 190f), new Vector2(230f, 230f), new Color(1f, 1f, 1f, 0.22f));
            var knobRt = Circle("Knob", baseRt, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(84f, 84f), new Color(1f, 1f, 1f, 0.55f));
            knobRt.anchorMin = knobRt.anchorMax = new Vector2(0.5f, 0.5f);
            baseRt.GetComponent<Image>().raycastTarget = true;
            _stick = baseRt.gameObject.AddComponent<TouchStick>();
            _stick.Init(baseRt, knobRt);

            // 액션 버튼 (오른쪽 아래)
            Key("상호작용", KeyCode.E, new Vector2(1f, 0f), new Vector2(-150f, 150f), 120f, new Color(1f, 0.78f, 0.3f, 0.75f));
            Key("점프", KeyCode.Space, new Vector2(1f, 0f), new Vector2(-300f, 100f), 92f, new Color(0.5f, 0.85f, 1f, 0.7f));
            Key("Q", KeyCode.Q, new Vector2(1f, 0f), new Vector2(-300f, 215f), 70f, new Color(1f, 1f, 1f, 0.5f));
            Toggle("달리기", KeyCode.LeftShift, new Vector2(1f, 0f), new Vector2(-70f, 290f), 78f);

            // 상단 작은 버튼
            Key("지도", KeyCode.M, new Vector2(1f, 1f), new Vector2(-300f, -50f), 64f, new Color(1f, 1f, 1f, 0.5f), tap: true);
            Key("목표", KeyCode.J, new Vector2(1f, 1f), new Vector2(-225f, -50f), 64f, new Color(1f, 1f, 1f, 0.5f), tap: true);
            Key("저장", KeyCode.F5, new Vector2(1f, 1f), new Vector2(-150f, -50f), 64f, new Color(1f, 1f, 1f, 0.5f), tap: true);
            Key("설정", KeyCode.F10, new Vector2(1f, 1f), new Vector2(-75f, -50f), 64f, new Color(1f, 1f, 1f, 0.5f), tap: true);
            Key("취소", KeyCode.Escape, new Vector2(0f, 1f), new Vector2(75f, -50f), 64f, new Color(1f, 0.6f, 0.6f, 0.6f), tap: true);
        }

        private RectTransform Circle(string name, Transform parent, Vector2 anchor, Vector2 pos, Vector2 size, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var r = (RectTransform)go.transform;
            r.anchorMin = r.anchorMax = anchor; r.pivot = new Vector2(0.5f, 0.5f);
            r.anchoredPosition = pos; r.sizeDelta = size;
            var img = go.GetComponent<Image>(); img.sprite = _circle; img.color = color; img.raycastTarget = false;
            return r;
        }

        private RectTransform Circle(string name, RectTransform parent, Vector2 anchor, Vector2 pos, Vector2 size, Color color)
            => Circle(name, (Transform)parent, anchor, pos, size, color);

        // pos는 앵커 기준 오프셋 (좌하단 기준 조이스틱은 x,y를 그대로 쓴다)
        private void Key(string label, KeyCode key, Vector2 anchor, Vector2 pos, float size, Color color, bool tap = false)
        {
            var r = Circle("Btn_" + key, _btnCanvas.transform, anchor, pos, new Vector2(size, size), color);
            r.GetComponent<Image>().raycastTarget = true;
            var b = r.gameObject.AddComponent<TouchKeyButton>();
            b.key = key; b.tap = tap; b.toggle = false;
            AddLabel(r, label, size > 100f ? 26 : 18);
        }

        private void Toggle(string label, KeyCode key, Vector2 anchor, Vector2 pos, float size)
        {
            var r = Circle("Btn_" + key, _btnCanvas.transform, anchor, pos, new Vector2(size, size), new Color(1f, 1f, 1f, 0.5f));
            r.GetComponent<Image>().raycastTarget = true;
            var b = r.gameObject.AddComponent<TouchKeyButton>();
            b.key = key; b.toggle = true;
            AddLabel(r, label, 18);
        }

        private void AddLabel(RectTransform parent, string text, int size)
        {
            var go = new GameObject("Label", typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var r = (RectTransform)go.transform;
            r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = r.offsetMax = Vector2.zero;
            var t = go.GetComponent<Text>();
            t.font = font != null ? font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            t.text = text; t.fontSize = size; t.alignment = TextAnchor.MiddleCenter;
            t.color = new Color(0.12f, 0.1f, 0.08f, 0.95f); t.raycastTarget = false;
        }

        private static Sprite MakeCircleSprite()
        {
            const int n = 96;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            float c = (n - 1) * 0.5f;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                    float a = Mathf.Clamp01((c - d) / 1.5f);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f);
        }
    }

    /// <summary>가상 조이스틱. 반경 안으로 클램프한 값을 GameInput.JoystickMove에 넣는다.</summary>
    public class TouchStick : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        private RectTransform _base, _knob;
        private float _radius;

        public void Init(RectTransform b, RectTransform k) { _base = b; _knob = k; _radius = b.sizeDelta.x * 0.4f; }

        public void OnPointerDown(PointerEventData e) => OnDrag(e);

        public void OnDrag(PointerEventData e)
        {
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_base, e.position, e.pressEventCamera, out var local)) return;
            var v = Vector2.ClampMagnitude(local, _radius);
            _knob.anchoredPosition = v;
            var n = v / _radius;
            GameInput.JoystickMove = n.magnitude < 0.12f ? Vector2.zero : n;    // 데드존
        }

        public void OnPointerUp(PointerEventData e) => ResetStick();

        public void ResetStick()
        {
            if (_knob != null) _knob.anchoredPosition = Vector2.zero;
            GameInput.JoystickMove = Vector2.zero;
        }

        private void OnDisable() => ResetStick();
    }

    /// <summary>화면 빈 곳을 끌면 시점 회전. 버튼/메뉴가 먼저 터치를 받으므로 남는 영역만 반응한다.</summary>
    public class TouchLookArea : MonoBehaviour, IPointerDownHandler, IDragHandler
    {
        public void OnPointerDown(PointerEventData e) { }

        public void OnDrag(PointerEventData e)
        {
            // 마우스(PC)는 오른쪽 버튼 드래그를 GameInput.Look이 처리하므로 터치/왼쪽 드래그 중 터치 모드일 때만 반영
            if (!GameInput.TouchMode) return;
            GameInput.TouchLook += e.delta * 0.6f;
        }
    }

    /// <summary>가상 키 버튼. 누르는 동안 키가 눌린 상태(hold), tap이면 눌렀다 떼는 한 번, toggle이면 켜고 끄기.</summary>
    public class TouchKeyButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
    {
        public KeyCode key;
        public bool tap, toggle;
        private bool _on;
        private Image _img;

        private void Awake() => _img = GetComponent<Image>();

        public void OnPointerDown(PointerEventData e)
        {
            if (toggle)
            {
                _on = !_on;
                if (_on) GameInput.Press(key); else GameInput.Release(key);
                if (_img != null) _img.color = _on ? new Color(0.6f, 1f, 0.6f, 0.8f) : new Color(1f, 1f, 1f, 0.5f);
            }
            else if (tap) GameInput.Tap(key);
            else GameInput.Press(key);
        }

        public void OnPointerUp(PointerEventData e)
        {
            if (!toggle && !tap) GameInput.Release(key);
        }

        private void OnDisable()
        {
            if (!toggle && !tap) GameInput.Release(key);
        }
    }
}
