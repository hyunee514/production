using System;
using System.Collections.Generic;
using System.Text;
using AutoSurvival.Core;

namespace AutoSurvival.Data
{
    public class ExamCsvResult
    {
        public readonly List<ExamQuestion> questions = new();
        public readonly List<string> errors = new();
    }

    /// <summary>
    /// 기출문제 CSV 파서 (UnityEngine 호출 없음 -> 테스트 가능).
    /// 엑셀에서 'CSV UTF-8'로 저장한 파일을 그대로 읽는다. 큰따옴표로 감싼 칸 안의 쉼표·줄바꿈·"" 이스케이프 지원.
    ///
    /// 열 순서(첫 줄 헤더는 있어도 없어도 됨):
    ///   id, origin, examLabel, concept, difficulty, question, choice1, choice2, choice3, choice4, answer(1~4), explanation, fieldNote
    ///
    /// origin  : PastExam / Original  (또는 기출 / 자체)
    /// concept : ConceptTag 이름 (예: LadderSelfHold, HydraulicPressure)
    /// answer  : 정답 보기 번호 (1부터)
    /// </summary>
    public static class ExamCsvParser
    {
        public const int ColumnCount = 13;

        public static ExamCsvResult Parse(string csv)
        {
            var result = new ExamCsvResult();
            if (string.IsNullOrWhiteSpace(csv)) { result.errors.Add("CSV가 비어 있습니다."); return result; }

            var rows = ReadRows(csv);
            int start = 0;
            if (rows.Count > 0 && rows[0].Count > 0 &&
                rows[0][0].Trim().Equals("id", StringComparison.OrdinalIgnoreCase)) start = 1;

            var seen = new HashSet<string>();
            for (int i = start; i < rows.Count; i++)
            {
                var row = rows[i];
                int lineNo = i + 1;
                if (row.Count == 1 && string.IsNullOrWhiteSpace(row[0])) continue;   // 빈 줄
                if (row.Count < ColumnCount)
                {
                    result.errors.Add($"{lineNo}행: 열이 {row.Count}개입니다 ({ColumnCount}개 필요).");
                    continue;
                }

                var q = new ExamQuestion
                {
                    id = row[0].Trim(),
                    examLabel = row[2].Trim(),
                    question = row[5].Trim(),
                    choices = new[] { row[6].Trim(), row[7].Trim(), row[8].Trim(), row[9].Trim() },
                    explanation = row[11].Trim(),
                    fieldNote = row[12].Trim()
                };

                if (!TryParseOrigin(row[1], out q.origin)) { result.errors.Add($"{lineNo}행: origin '{row[1]}'을(를) 알 수 없습니다."); continue; }
                if (!Enum.TryParse(row[3].Trim(), true, out q.concept)) { result.errors.Add($"{lineNo}행: concept '{row[3]}'을(를) 알 수 없습니다."); continue; }
                if (!int.TryParse(row[4].Trim(), out q.difficulty) || q.difficulty < 1 || q.difficulty > 5) { result.errors.Add($"{lineNo}행: difficulty는 1~5여야 합니다."); continue; }
                if (!int.TryParse(row[10].Trim(), out int answer1Based)) { result.errors.Add($"{lineNo}행: answer '{row[10]}'이(가) 숫자가 아닙니다."); continue; }
                q.answerIndex = answer1Based - 1;

                if (!q.IsValid(out string err)) { result.errors.Add($"{lineNo}행({q.id}): {err}"); continue; }
                if (!seen.Add(q.id)) { result.errors.Add($"{lineNo}행: id '{q.id}'가 중복됩니다."); continue; }

                result.questions.Add(q);
            }
            return result;
        }

        private static bool TryParseOrigin(string text, out QuestionOrigin origin)
        {
            string t = text.Trim();
            if (t.Equals("PastExam", StringComparison.OrdinalIgnoreCase) || t == "기출") { origin = QuestionOrigin.PastExam; return true; }
            if (t.Equals("Original", StringComparison.OrdinalIgnoreCase) || t == "자체") { origin = QuestionOrigin.Original; return true; }
            origin = QuestionOrigin.Original;
            return false;
        }

        /// <summary>RFC 4180 스타일 상태기계 파서.</summary>
        internal static List<List<string>> ReadRows(string text)
        {
            var rows = new List<List<string>>();
            var row = new List<string>();
            var cell = new StringBuilder();
            bool inQuotes = false;

            if (text.Length > 0 && text[0] == '﻿') text = text.Substring(1);   // BOM 제거

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (inQuotes)
                {
                    if (c == '"')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; }
                        else inQuotes = false;
                    }
                    else cell.Append(c);
                }
                else if (c == '"') inQuotes = true;
                else if (c == ',') { row.Add(cell.ToString()); cell.Clear(); }
                else if (c == '\r') { /* \r\n 처리: \n에서 줄 종료 */ }
                else if (c == '\n') { row.Add(cell.ToString()); cell.Clear(); rows.Add(row); row = new List<string>(); }
                else cell.Append(c);
            }

            if (cell.Length > 0 || row.Count > 0) { row.Add(cell.ToString()); rows.Add(row); }
            return rows;
        }
    }
}
