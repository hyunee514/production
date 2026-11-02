using UnityEngine;

namespace AutoSurvival.Level
{
    public enum LevelMarkerKind { PlayerSpawn, Basecamp, Wood, Stone, MetalScrap, Food, Water, Landmark }

    /// <summary>
    /// 씬에 놓는 '구역 표시'. 게임 동작에는 영향이 없고, 에디터 Scene 뷰에서 베이스캠프·자원 구역·랜드마크 위치를
    /// 색 원과 이름으로 보여 준다 (레벨 디자인 문서의 배치를 눈으로 확인하고 손으로 옮기기 위한 용도).
    /// </summary>
    public class LevelMarker : MonoBehaviour
    {
        public LevelMarkerKind kind;
        public string label;
        public float radius = 5f;
        [Tooltip("이 구역에 놓을 채집물 수 (참고용)")] public int nodeCount;
        [Tooltip("채집물 하나가 주는 양 (참고용)")] public int amountPerNode;

        public static Color ColorOf(LevelMarkerKind k)
        {
            switch (k)
            {
                case LevelMarkerKind.PlayerSpawn: return new Color(1f, 1f, 1f);
                case LevelMarkerKind.Basecamp: return new Color(1f, 0.6f, 0.2f);
                case LevelMarkerKind.Wood: return new Color(0.55f, 0.35f, 0.15f);
                case LevelMarkerKind.Stone: return new Color(0.6f, 0.6f, 0.65f);
                case LevelMarkerKind.MetalScrap: return new Color(0.4f, 0.7f, 0.9f);
                case LevelMarkerKind.Food: return new Color(0.9f, 0.4f, 0.3f);
                case LevelMarkerKind.Water: return new Color(0.2f, 0.5f, 1f);
                default: return new Color(0.8f, 0.3f, 0.9f);
            }
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = ColorOf(kind);
            const int steps = 40;
            Vector3 prev = transform.position + new Vector3(radius, 0.1f, 0f);
            for (int i = 1; i <= steps; i++)
            {
                float a = i * Mathf.PI * 2f / steps;
                var next = transform.position + new Vector3(Mathf.Cos(a) * radius, 0.1f, Mathf.Sin(a) * radius);
                Gizmos.DrawLine(prev, next);
                prev = next;
            }
            Gizmos.DrawSphere(transform.position + Vector3.up * 0.3f, 0.3f);
#if UNITY_EDITOR
            UnityEditor.Handles.Label(transform.position + Vector3.up * 1.2f, string.IsNullOrEmpty(label) ? kind.ToString() : label);
#endif
        }
    }
}
