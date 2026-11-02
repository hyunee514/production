#if UNITY_EDITOR
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using AutoSurvival.Data;

namespace AutoSurvival.EditorTools
{
    /// <summary>
    /// 메뉴: AutoSurvival ▸ 기출문제 CSV 가져오기
    /// 프로젝트 창에서 PuzzleScenarioDB 에셋을 선택한 뒤 실행하면 CSV(UTF-8)의 문제를 병합한다.
    /// CSV 양식은 Assets/Data/exam_template.csv 참고.
    /// </summary>
    public static class ExamCsvImporter
    {
        [MenuItem("AutoSurvival/기출문제 CSV 가져오기")]
        public static void Import()
        {
            var db = Selection.activeObject as PuzzleScenarioDB;
            if (db == null)
            {
                EditorUtility.DisplayDialog("기출문제 가져오기", "먼저 프로젝트 창에서 PuzzleScenarioDB 에셋을 선택하세요.", "확인");
                return;
            }

            string path = EditorUtility.OpenFilePanel("기출문제 CSV 선택", "", "csv");
            if (string.IsNullOrEmpty(path)) return;

            string text = File.ReadAllText(path, Encoding.UTF8);
            Undo.RecordObject(db, "기출문제 CSV 가져오기");

            int before = db.Questions.Count;
            var errors = db.ImportCsv(text);
            EditorUtility.SetDirty(db);
            AssetDatabase.SaveAssets();

            string report = $"가져온 문제: {db.Questions.Count - before}개 추가 (같은 id는 덮어씀)\n오류: {errors.Count}건";
            if (errors.Count > 0) report += "\n\n" + string.Join("\n", errors);

            var issues = db.Validate();
            if (issues.Count > 0) report += $"\n\n데이터 점검 경고 {issues.Count}건 (Console 참고)";
            foreach (var i in issues) Debug.LogWarning($"[PuzzleScenarioDB] {i}", db);

            EditorUtility.DisplayDialog("기출문제 가져오기", report, "확인");
        }
    }
}
#endif
