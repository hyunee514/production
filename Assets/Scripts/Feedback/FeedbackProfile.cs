using System;
using System.Collections.Generic;
using UnityEngine;

namespace AutoSurvival.Feedback
{
    /// <summary>큐 하나가 일으킬 연출 한 묶음. 비어 있는 칸은 "그 연출은 없음"이다.</summary>
    [Serializable]
    public class FeedbackEntry
    {
        [Tooltip("큐 이름. 예: Hydraulic.HoseBurst, Rhythm.Perfect (FeedbackKeys 참고)")]
        public string key;

        [Header("파티클")]
        public GameObject particlePrefab;
        [Tooltip("true면 퍼즐 위치 대신 카메라 앞에 생성 (화면 전체 효과용)")]
        public bool particleAtCamera;
        public float particleScale = 1f;
        [Tooltip("이 시간(초) 뒤에 파티클 삭제. 0이면 자동으로 3초")]
        public float particleLifetime = 3f;

        [Header("카메라")]
        public ShakeGrade shake = ShakeGrade.None;
        [Tooltip("감쇠 전에 같은 세기로 유지할 시간(초). Strain 같은 연속 진동용")]
        public float shakeHold = 0f;
        [Tooltip("히트스톱 시간(초). 큰 충격에만 쓴다. 실제로는 HitstopGate가 0.1초로 제한")]
        [Range(0f, 0.2f)] public float hitstopSeconds = 0f;

        [Header("효과음")]
        public AudioClip[] sfx;
        public Vector2 pitchRange = new Vector2(0.97f, 1.03f);
        [Range(0f, 1f)] public float volume = 1f;
        [Tooltip("true면 콤보에 따른 피치 사다리를 곱한다 (연속 성공 소리)")]
        public bool usePitchLadder = false;
    }

    /// <summary>
    /// 연출 데이터 모음 (ScriptableObject). 코드를 고치지 않고 파티클·소리·흔들림만 바꿔 끼울 수 있다.
    /// 만들기: Project 창 우클릭 > Create > AutoSurvival > Feedback Profile
    ///        (또는 메뉴 AutoSurvival > 프로젝트 기본 세팅 > 전체 실행 이 기본 에셋을 만든다)
    /// </summary>
    [CreateAssetMenu(menuName = "AutoSurvival/Feedback Profile", fileName = "FeedbackProfile")]
    public class FeedbackProfile : ScriptableObject
    {
        public List<FeedbackEntry> entries = new();

        private Dictionary<string, FeedbackEntry> _map;

        public FeedbackEntry Find(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            if (_map == null) BuildMap();
            return _map.TryGetValue(key, out var e) ? e : null;
        }

        private void BuildMap()
        {
            _map = new Dictionary<string, FeedbackEntry>();
            foreach (var e in entries)
            {
                if (e == null || string.IsNullOrEmpty(e.key)) continue;
                _map[e.key] = e;   // 같은 키가 둘이면 아래쪽이 이긴다
            }
        }

        private void OnValidate() { _map = null; }
        private void OnEnable() { _map = null; }

        /// <summary>FeedbackKeys의 기본 표로 빠진 키를 채운다. 이미 있는 항목은 건드리지 않는다. 추가한 개수를 돌려준다.</summary>
        public int SeedDefaults()
        {
            var have = new HashSet<string>();
            foreach (var e in entries) if (e != null && !string.IsNullOrEmpty(e.key)) have.Add(e.key);

            int added = 0;
            foreach (var d in FeedbackKeys.Defaults)
            {
                if (have.Contains(d.key)) continue;
                entries.Add(new FeedbackEntry
                {
                    key = d.key, shake = d.shake, shakeHold = d.shakeHold, hitstopSeconds = d.hitstop,
                    pitchRange = new Vector2(d.pitchMin, d.pitchMax), usePitchLadder = d.ladder,
                });
                added++;
            }
            _map = null;
            return added;
        }

#if UNITY_EDITOR
        [ContextMenu("기본 항목 채우기")]
        private void SeedFromMenu() { int n = SeedDefaults(); UnityEditor.EditorUtility.SetDirty(this); Debug.Log($"[FeedbackProfile] 기본 항목 {n}개 추가"); }
#endif
    }
}
