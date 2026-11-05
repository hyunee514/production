#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace AutoSurvival.EditorTools
{
    /// <summary>
    /// 클라우드(GitHub Actions + GameCI) 빌드 진입점.
    /// 사람이 Unity 메뉴에서 하던 "프로젝트 기본 세팅 > 전체 실행"을 서버에서 대신하고(씬·데이터·프리팹 생성),
    /// WebGL 설정을 맞춘 뒤 빌드한다. 워크플로우의 buildMethod에서 호출:
    ///     AutoSurvival.EditorTools.CiBuild.BuildWebGL
    /// 출력 폴더는 GameCI가 넘겨 주는 -customBuildPath 를 쓴다 (없으면 build/WebGL).
    /// 실패하면 종료 코드 1 또는 2로 끝나서 워크플로우가 빨간 불이 된다.
    /// </summary>
    public static class CiBuild
    {
        public static void BuildWebGL()
        {
            try
            {
                Debug.Log("[CiBuild] 1/3 씬·데이터·프리팹 자동 생성");
                ProjectSetupTool.RunAllSilent();

                Debug.Log("[CiBuild] 2/3 WebGL 설정");
                ApplyWebGlSettings();

                var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
                if (scenes.Length == 0) throw new Exception("빌드할 씬이 없습니다. ProjectSetupTool이 씬을 만들지 못했습니다 (위쪽 로그 확인).");
                Debug.Log("[CiBuild] 씬 " + scenes.Length + "개: " + string.Join(", ", scenes));

                string outPath = ArgValue("-customBuildPath") ?? "build/WebGL/Game";
                Directory.CreateDirectory(outPath);

                Debug.Log("[CiBuild] 3/3 빌드 시작 -> " + outPath);
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = scenes,
                    locationPathName = outPath,
                    target = BuildTarget.WebGL,
                    options = BuildOptions.None,
                });

                var summary = report.summary;
                Debug.Log($"[CiBuild] 결과: {summary.result}, 크기 {summary.totalSize / (1024 * 1024)} MB, 오류 {summary.totalErrors}, 경고 {summary.totalWarnings}");
                EditorApplication.Exit(summary.result == BuildResult.Succeeded ? 0 : 2);
            }
            catch (Exception e)
            {
                Debug.LogError("[CiBuild] 실패: " + e);
                EditorApplication.Exit(1);
            }
        }

        private static void ApplyWebGlSettings()
        {
            PlayerSettings.productName = "Agen";
            PlayerSettings.companyName = "AutoSurvival";
            PlayerSettings.runInBackground = false;
            PlayerSettings.defaultWebScreenWidth = 1280;
            PlayerSettings.defaultWebScreenHeight = 720;

            // GitHub Pages는 .br/.gz 파일에 Content-Encoding 헤더를 붙이지 않는다 -> 압축을 끄는 것이 가장 안전하다.
            // (파일이 100MB를 넘으면 Gzip + decompressionFallback = true 로 바꾸는 방법이 있다. 가이드 참고)
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled;
            PlayerSettings.WebGL.decompressionFallback = false;
            PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.ExplicitlyThrownExceptionsOnly;
            PlayerSettings.WebGL.dataCaching = true;

            // 모바일 브라우저: 경고 배너 없는 Minimal 템플릿, 가볍게 그리기
            PlayerSettings.WebGL.template = "APPLICATION:Minimal";
            QualitySettings.antiAliasing = 0;
            QualitySettings.shadowDistance = 45f;
            QualitySettings.pixelLightCount = 2;
            QualitySettings.shadowResolution = ShadowResolution.Medium;

            // JsonUtility로 저장하는 클래스가 코드 스트리핑으로 지워지지 않도록 보수적으로
            PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.WebGL, ManagedStrippingLevel.Low);
        }

        private static string ArgValue(string name)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == name) return args[i + 1];
            return null;
        }
    }
}
#endif
