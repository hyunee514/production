using System;
using System.Collections.Generic;
using AutoSurvival.Core;

namespace AutoSurvival.Data
{
    public enum PuzzleKind { Pipe, Ladder, Hydraulic, Pneumatic }

    /// <summary>
    /// Original: 출제 범위에 맞춰 새로 쓴 문제 / PastExam: 교사가 CSV로 가져온 실제 기출문제.
    /// 기출 원문은 저작권·정확성 때문에 코드에 하드코딩하지 않고, 사용자가 가져온 데이터만 PastExam으로 취급한다.
    /// </summary>
    public enum QuestionOrigin { Original, PastExam }

    /// <summary>
    /// 퍼즐 직후 '현장 노트'로 보여주는 4지선다 문제 한 개.
    /// 퍼즐을 먼저 풀고 나서 "방금 한 일이 시험에서는 이렇게 나온다"를 확인시키는 용도라 퍼즐을 가로막지 않는다.
    /// </summary>
    [Serializable]
    public class ExamQuestion
    {
        public string id;
        public QuestionOrigin origin;
        public string examLabel;                // 기출이면 "2023년 1회" 같은 출처 표기 (자체 문제는 빈 값)
        public ConceptTag concept;
        [UnityEngine.Range(1, 5)] public int difficulty = 1;
        [UnityEngine.TextArea] public string question;
        public string[] choices = new string[4];
        public int answerIndex;                 // 0-based
        [UnityEngine.TextArea] public string explanation;
        [UnityEngine.TextArea] public string fieldNote;   // 오피의 대사: 방금 퍼즐과의 연결 설명

        public bool IsValid(out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(id)) error = "id가 비어 있음";
            else if (string.IsNullOrWhiteSpace(question)) error = "문제 본문이 비어 있음";
            else if (choices == null || choices.Length < 2) error = "보기가 2개 미만";
            else if (answerIndex < 0 || answerIndex >= choices.Length) error = $"정답 번호({answerIndex + 1})가 보기 범위를 벗어남";
            else
                foreach (var c in choices)
                    if (string.IsNullOrWhiteSpace(c)) { error = "비어 있는 보기가 있음"; break; }
            return error == null;
        }
    }

    /// <summary>
    /// 인게임 퍼즐 한 판의 '주문서'. 어떤 퍼즐 종류를 어떤 파라미터로 열고,
    /// 끝난 뒤 어떤 문제를 현장 노트로 보여줄지를 연결한다.
    /// </summary>
    [Serializable]
    public class PuzzleScenarioEntry
    {
        public string id;
        public PuzzleKind kind;
        public ConceptTag concept;
        [UnityEngine.Range(1, 5)] public int minTier = 1;
        [UnityEngine.Range(1, 5)] public int maxTier = 5;

        [UnityEngine.Header("퍼즐 종류별 파라미터 (해당 없는 칸은 비움)")]
        public string ladderChallengeId;     // Ladder: LadderChallengeLibrary id
        public int pipeGridSize;             // Pipe: 0이면 티어 기본 크기
        public string hydraulicLevelId;      // Hydraulic: HydraulicLevelLibrary id
        public string pneumaticLevelId;      // Pneumatic: PneumaticLevelLibrary id

        [UnityEngine.Header("연결")]
        public List<string> questionIds = new();   // 퍼즐 종료 후 현장 노트로 뽑을 문제 후보
        [UnityEngine.TextArea] public string introLine;   // 오피의 도입 대사

        public bool Matches(PuzzleKind k, int tier) => kind == k && tier >= minTier && tier <= maxTier;
    }
}
