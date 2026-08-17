using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace MakeGame.EditorTools
{
    /// <summary>
    /// [스팀 출시 1단계] Windows 스탠드얼론 빌드를 메뉴 한 번으로 만든다.
    ///
    /// 왜 필요한가: 이 게임은 지금까지 **에디터 안에서만** 돌았다. 스팀에 올리는 물건은
    /// 에디터 밖에서 단독 실행되는 빌드라, "빌드가 되는가/돌아가는가"가 출시의 첫 관문이다.
    /// SmokeTest처럼 결과를 JSON(build_result.json)으로 남겨 원격 디렉터가 파일만 읽고
    /// 성패를 알 수 있게 한다.
    ///
    /// 산출물: Builds/LostSurvivor/LostSurvivor.exe (+ Data 폴더).
    /// Builds/ 폴더는 .gitignore 대상이다(수백 MB 산출물 - 저장소는 소스만 담는다).
    /// 스팀 업로드(SteamPipe) 시에는 이 폴더를 depot 루트로 그대로 가리키면 된다.
    ///
    /// 개발 빌드가 아니다(Development Build 꺼짐) - DebugHud의 F10 전체지도/자유이동 등
    /// UNITY_EDITOR || DEVELOPMENT_BUILD 가드 코드는 이 빌드에서 데드 코드로 빠진다.
    /// 출시 전 QA용 개발 빌드가 필요하면 아래 메뉴의 Dev 변형을 쓴다.
    /// </summary>
    public static class BuildTool
    {
        private const string OutputDir = "Builds/LostSurvivor";
        private const string ExeName = "LostSurvivor.exe";
        private const string ResultPath = "build_result.json";

        [MenuItem("MakeGame/Windows 빌드 실행")]
        public static void BuildWindows()
        {
            Build(BuildOptions.None);
        }

        [MenuItem("MakeGame/Windows 개발 빌드 실행 (QA용)")]
        public static void BuildWindowsDev()
        {
            Build(BuildOptions.Development);
        }

        private static void Build(BuildOptions options)
        {
            string exePath = Path.Combine(OutputDir, ExeName);
            Directory.CreateDirectory(OutputDir);

            var buildOptions = new BuildPlayerOptions
            {
                // 씬은 단일 씬 프로젝트의 그 씬 하나다. EditorBuildSettings에 씬이 등록돼 있지
                // 않아도 여기 명시하면 빌드에 들어간다(빌드 설정 창을 손댈 필요가 없다).
                scenes = new[] { "Assets/Scenes/SampleScene.unity" },
                locationPathName = exePath,
                target = BuildTarget.StandaloneWindows64,
                options = options,
            };

            BuildReport report = BuildPipeline.BuildPlayer(buildOptions);
            BuildSummary summary = report.summary;

            string json = "{\n"
                + "    \"result\": \"" + summary.result + "\",\n"
                + "    \"development\": " + ((options & BuildOptions.Development) != 0 ? "true" : "false") + ",\n"
                + "    \"errors\": " + summary.totalErrors + ",\n"
                + "    \"warnings\": " + summary.totalWarnings + ",\n"
                + "    \"totalSizeMB\": " + (summary.totalSize / (1024 * 1024)) + ",\n"
                + "    \"durationSec\": " + ((int)summary.totalTime.TotalSeconds) + ",\n"
                + "    \"output\": \"" + exePath.Replace("\\", "/") + "\",\n"
                + "    \"unityVersion\": \"" + Application.unityVersion + "\"\n"
                + "}\n";
            File.WriteAllText(ResultPath, json);

            if (summary.result == BuildResult.Succeeded)
                Debug.Log("[BuildTool] 빌드 성공 - " + exePath + " (" + summary.totalSize / (1024 * 1024) + "MB, "
                    + (int)summary.totalTime.TotalSeconds + "초). 결과: " + ResultPath);
            else
                Debug.LogError("[BuildTool] 빌드 실패(" + summary.result + ") - 에러 " + summary.totalErrors
                    + "건. 결과: " + ResultPath);
        }
    }
}
