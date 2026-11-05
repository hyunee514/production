using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 키보드/마우스와 터치 UI를 하나로 합치는 입력 브리지.
/// 게임 스크립트는 Input.GetKey* 대신 GameInput.Key / KeyDown / KeyUp 을 쓴다.
/// 터치 버튼은 Press/Release 로 "가상 키"를 눌러 준다. (E, Q, Space, Shift, Escape ...)
/// </summary>
public static class GameInput
{
    private static readonly HashSet<KeyCode> Held = new();
    private static readonly HashSet<KeyCode> Pending = new();
    private static readonly HashSet<KeyCode> PendingUp = new();
    private static readonly HashSet<KeyCode> DownNow = new();
    private static readonly HashSet<KeyCode> UpNow = new();

    /// <summary>가상 조이스틱 값 (-1..1). TouchControlsUI가 채운다.</summary>
    public static Vector2 JoystickMove;
    /// <summary>이번 프레임 화면 드래그 시점 이동량(픽셀). 프레임이 끝나면 0으로 돌아간다.</summary>
    public static Vector2 TouchLook;
    /// <summary>터치 UI를 보여 줄지. 모바일 플랫폼이거나 한 번이라도 터치가 들어오면 true.</summary>
    public static bool TouchMode;

    static GameInput()
    {
        TouchMode = Application.isMobilePlatform;
    }

    // ───────── 가상 키 ─────────

    public static void Press(KeyCode k)
    {
        if (Held.Add(k)) Pending.Add(k);
    }

    public static void Release(KeyCode k)
    {
        if (Held.Remove(k)) PendingUp.Add(k);
    }

    /// <summary>눌렀다 떼는 한 번의 탭.</summary>
    public static void Tap(KeyCode k)
    {
        Pending.Add(k);
        PendingUp.Add(k);
    }

    public static bool KeyDown(KeyCode k) => Input.GetKeyDown(k) || DownNow.Contains(k);
    public static bool KeyUp(KeyCode k) => Input.GetKeyUp(k) || UpNow.Contains(k);
    public static bool Key(KeyCode k) => Input.GetKey(k) || Held.Contains(k);

    // ───────── 메뉴가 열려 있는 동안 이동·시점 잠금 ─────────

    private static readonly HashSet<object> Blockers = new();

    /// <summary>메뉴/지도/저장창 등이 열려 있으면 owner를 등록해 이동·시점을 멈춘다. 닫을 때 false.</summary>
    public static void BlockMovement(object owner, bool on)
    {
        if (on) Blockers.Add(owner); else Blockers.Remove(owner);
    }

    public static bool MovementBlocked => Blockers.Count > 0;

    // ───────── 이동·시점 ─────────

    /// <summary>WASD/화살표 + 조이스틱을 합친 이동 입력 (길이 1 이하).</summary>
    public static Vector2 Move
    {
        get
        {
            var v = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical")) + JoystickMove;
            return v.sqrMagnitude > 1f ? v.normalized : v;
        }
    }

    /// <summary>시점 회전 입력(픽셀). 마우스는 오른쪽 버튼을 누른 채 드래그, 터치는 화면 드래그.</summary>
    public static Vector2 Look
    {
        get
        {
            var v = TouchLook;
            if (Input.GetMouseButton(1)) v += new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y")) * 10f;
            return v;
        }
    }

    public static float Zoom => Input.mouseScrollDelta.y;

    // ───────── 프레임 갱신 ─────────

    internal static void BeginFrame()
    {
        DownNow.Clear(); UpNow.Clear();
        foreach (var k in Pending) DownNow.Add(k);
        foreach (var k in PendingUp) UpNow.Add(k);
        Pending.Clear(); PendingUp.Clear();
        if (!TouchMode && Input.touchCount > 0) TouchMode = true;
    }

    internal static void EndFrame() => TouchLook = Vector2.zero;

    /// <summary>씬을 다시 불러올 때 눌린 상태가 남지 않도록.</summary>
    public static void ResetAll()
    {
        Held.Clear(); Pending.Clear(); PendingUp.Clear(); DownNow.Clear(); UpNow.Clear();
        JoystickMove = Vector2.zero; TouchLook = Vector2.zero; Blockers.Clear();
    }
}

/// <summary>GameInput의 프레임 갱신을 가장 먼저/가장 나중에 실행하는 보조 오브젝트 (자동 생성).</summary>
[DefaultExecutionOrder(-10000)]
public class GameInputPump : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Boot()
    {
        var go = new GameObject("_GameInputPump");
        go.hideFlags = HideFlags.HideAndDontSave;
        DontDestroyOnLoad(go);
        go.AddComponent<GameInputPump>();
        go.AddComponent<GameInputPumpLate>();
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += (_, __) => GameInput.ResetAll();
    }

    private void Update() => GameInput.BeginFrame();
}

[DefaultExecutionOrder(10000)]
public class GameInputPumpLate : MonoBehaviour
{
    private void LateUpdate() => GameInput.EndFrame();
}
