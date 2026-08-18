using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MakeGame.EditorTools
{
    /// <summary>
    /// 스모크 테스트: 메뉴 [MakeGame/스모크 테스트 실행] 클릭 → 플레이 진입 → 20초 계측 →
    /// 플레이 이탈 → 프로젝트 루트 smoke_result.json 기록.
    ///
    /// 목적: 정적 검사로 못 잡는 부류(도메인 리로드 NRE, 생성자 시점 Resources.Load,
    /// 씬 참조 깨짐)를 배포 전에 잡는다. 콘솔 에러/예외/경고를 종류별로 집계하고
    /// 매 5초마다 씬 핵심 오브젝트(Player · Managers · Island_0_* 지형 · SurvivalHudUI ·
    /// Ocean · DayNightCycle · BuildingSystem · CursorLockController · GrassFieldDriver ·
    /// UnderwaterAmbience · SunkenCargo_* 침몰 화물 · AirlinerWreckVisual · UnderwaterCave_* 수중 동굴 ·
    /// MarineLife_* 해양 생물 ·
    /// ShoreRibbon_* 부서지는 파도 마루 리본 · Boss_* 엔드게임 보스) 존재를 확인한다.
    ///
    /// ★ 도메인 리로드 생존 구조 ★
    /// 플레이 진입/이탈(및 플레이 중 재컴파일) 때마다 에디터 static이 전부 초기화된다.
    /// 그래서 모든 상태(계측 중 플래그 · 단계 · 시작 시각 · 집계 카운터 · 체크 래치)를
    /// SessionState에 넣고, [InitializeOnLoad] 정적 생성자가 리로드 직후마다
    /// "계측 중" 플래그를 보고 훅(update / playModeStateChanged / logMessageReceived)을
    /// 다시 건다. static 카운터는 리로드 직후 SessionState 값으로 복원한 뒤 이어서 센다.
    ///
    /// 안전 규칙:
    /// - 플레이 자동 진입은 **메뉴 클릭으로만** 발동한다. InitializeOnLoad에서는 절대
    ///   EnterPlaymode를 부르지 않는다(플래그가 없으면 아무 것도 하지 않는다).
    /// - 이미 플레이 중이거나 SampleScene이 아닌 씬이 열려 있으면 결과 파일에 사유를
    ///   적고 중단한다.
    /// - 에디터 전용 어셈블리(Assets/.../Scripts/Editor/)에만 존재한다. 런타임 코드 무편집.
    /// </summary>
    [InitializeOnLoad]
    public static class SmokeTest
    {
        // ---- 설정 ----
        private const float MeasureDurationSec = 20f;
        private const float CheckIntervalSec = 5f;
        private const int MaxFirstMessages = 10;
        private const int MaxMessageLength = 300;
        private const string ExpectedSceneName = "SampleScene";

        // ---- SessionState 키 (도메인 리로드 생존용) ----
        private const string KeyActive = "MakeGame.SmokeTest.Active";
        private const string KeyPhase = "MakeGame.SmokeTest.Phase"; // entering / measuring / exiting
        private const string KeyStartedAtIso = "MakeGame.SmokeTest.StartedAtIso";
        private const string KeyPlayStart = "MakeGame.SmokeTest.PlayStart"; // EditorApplication.timeSinceStartup, "R" 문자열
        private const string KeyElapsed = "MakeGame.SmokeTest.Elapsed";
        private const string KeyErrors = "MakeGame.SmokeTest.Errors";
        private const string KeyExceptions = "MakeGame.SmokeTest.Exceptions";
        private const string KeyWarnings = "MakeGame.SmokeTest.Warnings";
        private const string KeyFirstMessages = "MakeGame.SmokeTest.FirstMessages";
        private const string KeyEarlyExit = "MakeGame.SmokeTest.EarlyExit";
        private const string KeyCheckPrefix = "MakeGame.SmokeTest.Check."; // + 체크 이름

        /// <summary>메시지 원문 보관용 구분자(원문에 나올 일 없는 RS 제어문자).</summary>
        private const char MessageSeparator = '\u001e';

        // ---- 체크 목록: (JSON 키, 씬 오브젝트 이름, 접두사 매칭 여부) ----
        // 근거: Player/Managers = SampleScene.unity 씬 배치 오브젝트.
        // Island_0_ = WorldMapManager.SpawnPlaceholder의 "Island_{id}_{size}" 명명(0번 = 시작 섬).
        // 나머지는 RuntimeInitializeOnLoadMethod 부트스트랩 또는 WorldMapManager가 만드는 이름.
        // grassField/underwaterAmbience = 부트스트랩이 만드는 "GrassFieldDriver"/"UnderwaterAmbience"
        // (GrassFieldSystem.cs:789 / UnderwaterAmbience.cs:112의 new GameObject 이름 그대로).
        // sunkenCargo = SeabedFloraSpawner.PlaceCargoPile의 "SunkenCargo_{pileIndex}" - 대형/특대 섬
        // 해저 전용이지만 월드 생성(WorldMapManager.Start의 동기 전체 섬 루프 → SeabedGenerator.Build →
        // SeabedFloraSpawner.Spawn) 때 일괄 생성되므로 시작 직후부터 존재한다(거리 지연 생성 아님).
        // airlinerWreck = AirlinerWreck.TryBuild의 "AirlinerWreckVisual"(시작 섬 해안, 모델 로드 후 생성).
        // marineLife = MarineLifeSpawner.Spawn의 "MarineLife_{섬 이름}" 배치 루트 - 모든 규모의 섬에
        // 최소 1개(sunkenCargo와 같은 동기 생성 경로: SeabedGenerator.Build → MarineLifeSpawner.Spawn)라
        // 시작 직후부터 존재한다. 거리 컷은 자식 컨테이너만 끄므로 이 루트는 항상 활성이다.
        // shoreRibbon = ShorelineRibbon.Build의 "ShoreRibbon_{섬 이름}" 마루 리본 - 월드 생성의 같은
        // 동기 흐름(IslandMeshGenerator.BuildGroundCaps → ShorelineRibbon.Build)에서 섬마다 1개씩
        // 만들어지므로 시작 직후부터 존재한다. 거리 컷은 MeshRenderer.enabled만 끄고 오브젝트는
        // 항상 활성이라(FindObjectsByType은 렌더러 상태를 보지 않는다) 먼 섬의 리본도 확인에 잡힌다.
        // 물가가 없는 섬이나 셰이더 부재 시에는 리본이 생기지 않는 것이 계약이지만, 시작 섬(0번)은
        // 항상 해수면 아래로 잠기는 테두리를 가져(BakeShoreField 주석의 실측: 등고선 선분 158개)
        // 등고선이 반드시 존재한다.
        // boss = BossCreature.Spawn의 "Boss_{BossKind}" 보스 루트(BossSpawner가 월드당 3마리까지
        // 배치한다. 컨테이너 "BossRoot"는 접두가 "Boss_"가 아니라 이 검사에 잡히지 않는다 - 실제
        // 보스 개체가 섰는지를 본다). 위 항목들과 달리 **월드 생성과 같은 프레임이 아니다**:
        // BossSpawner는 씬에 배선할 수 없어 스스로 붙은 뒤 0.5초 주기로 월드 준비를 폴링하므로
        // 첫 배치가 1초 안팎 늦는다. 이 검사는 5초마다 돌고 한 번이라도 관측되면 래치되므로
        // (RunSceneChecks 주석) 그 지연을 그대로 흡수한다.
        // 처치된 보스는 오브젝트가 사라지지만, 스모크 테스트는 새 판 20초라 그럴 일이 없다.
        private static readonly (string key, string objectName, bool prefix)[] Checks =
        {
            ("player", "Player", false),
            ("managers", "Managers", false),
            ("terrain", "Island_0_", true),
            ("hud", "SurvivalHudUI", false),
            ("ocean", "Ocean", false),
            ("dayNightCycle", "DayNightCycle", false),
            ("buildingSystem", "BuildingSystem", false),
            ("cursorLock", "CursorLockController", false),
            ("grassField", "GrassFieldDriver", false),
            ("underwaterAmbience", "UnderwaterAmbience", false),
            ("sunkenCargo", "SunkenCargo_", true),
            ("airlinerWreck", "AirlinerWreckVisual", false),
            ("underwaterCave", "UnderwaterCave_", true),
            ("marineLife", "MarineLife_", true),
            ("shoreRibbon", "ShoreRibbon_", true),
            ("boss", "Boss_", true),
        };

        // ---- 도메인 로컬 상태 (리로드 직후 SessionState에서 복원) ----
        private static int errorCount;
        private static int exceptionCount;
        private static int warningCount;
        private static List<string> firstMessages = new List<string>();
        private static bool countersDirty;
        private static double nextCheckAt;
        private static double nextPartialWriteAt;
        private static bool hooked;

        /// <summary>결과 파일 경로: 프로젝트 루트(Assets의 부모)/smoke_result.json</summary>
        private static string ResultPath =>
            Path.Combine(Path.GetDirectoryName(Application.dataPath), "smoke_result.json");

        // =====================================================================
        // 도메인 리로드 재진입점 — 계측 중 플래그가 있을 때만 훅을 다시 건다.
        // 여기서 EnterPlaymode를 부르는 일은 절대 없다.
        // =====================================================================
        static SmokeTest()
        {
            if (!SessionState.GetBool(KeyActive, false))
                return;

            RestoreCountersFromSession();
            Hook();
        }

        // =====================================================================
        // 유일한 발동 지점: 메뉴 클릭
        // =====================================================================
        [MenuItem("MakeGame/스모크 테스트 실행")]
        private static void RunSmokeTest()
        {
            if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                WriteAbortResult("이미 플레이 모드가 실행 중이라 스모크 테스트를 시작할 수 없다.");
                Debug.LogWarning("[SmokeTest] 이미 플레이 중 — 중단. smoke_result.json에 기록했다.");
                return;
            }

            var activeScene = SceneManager.GetActiveScene();
            if (activeScene.name != ExpectedSceneName)
            {
                WriteAbortResult($"열려 있는 씬이 '{activeScene.name}' — 이 프로젝트는 단일 씬({ExpectedSceneName}) 전제라 중단.");
                Debug.LogWarning("[SmokeTest] SampleScene이 아닌 씬이 열려 있어 중단. smoke_result.json에 기록했다.");
                return;
            }

            if (File.Exists(ResultPath))
                File.Delete(ResultPath);

            // 상태 초기화 + 계측 중 플래그 (도메인 리로드 너머로 전달)
            SessionState.SetBool(KeyActive, true);
            SessionState.SetString(KeyPhase, "entering");
            SessionState.SetString(KeyStartedAtIso, DateTime.Now.ToString("o"));
            SessionState.EraseString(KeyPlayStart);
            SessionState.SetFloat(KeyElapsed, 0f);
            SessionState.SetInt(KeyErrors, 0);
            SessionState.SetInt(KeyExceptions, 0);
            SessionState.SetInt(KeyWarnings, 0);
            SessionState.SetString(KeyFirstMessages, string.Empty);
            SessionState.SetBool(KeyEarlyExit, false);
            foreach (var check in Checks)
                SessionState.SetBool(KeyCheckPrefix + check.key, false);

            errorCount = exceptionCount = warningCount = 0;
            firstMessages = new List<string>();
            countersDirty = false;

            // 지금부터 수집 시작(진입 리로드 후에는 정적 생성자가 다시 건다)
            Hook();

            Debug.Log("[SmokeTest] 플레이 진입 — 20초 계측을 시작한다.");
            EditorApplication.EnterPlaymode();
        }

        // =====================================================================
        // 훅 관리
        // =====================================================================
        private static void Hook()
        {
            if (hooked)
                return;
            hooked = true;
            Application.logMessageReceived += OnLogMessage;
            EditorApplication.update += OnEditorUpdate;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        private static void Unhook()
        {
            if (!hooked)
                return;
            hooked = false;
            Application.logMessageReceived -= OnLogMessage;
            EditorApplication.update -= OnEditorUpdate;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        }

        // =====================================================================
        // 로그 집계
        // =====================================================================
        private static void OnLogMessage(string condition, string stackTrace, LogType type)
        {
            switch (type)
            {
                case LogType.Error:
                case LogType.Assert:
                    errorCount++;
                    break;
                case LogType.Exception:
                    exceptionCount++;
                    break;
                case LogType.Warning:
                    warningCount++;
                    break;
                default:
                    return; // LogType.Log는 집계하지 않는다
            }

            if (firstMessages.Count < MaxFirstMessages)
            {
                string message = $"[{type}] {condition}";
                if (message.Length > MaxMessageLength)
                    message = message.Substring(0, MaxMessageLength);
                firstMessages.Add(message.Replace(MessageSeparator, ' '));
            }

            countersDirty = true;
        }

        private static void FlushCountersToSession()
        {
            if (!countersDirty)
                return;
            countersDirty = false;
            SessionState.SetInt(KeyErrors, errorCount);
            SessionState.SetInt(KeyExceptions, exceptionCount);
            SessionState.SetInt(KeyWarnings, warningCount);
            SessionState.SetString(KeyFirstMessages, string.Join(MessageSeparator.ToString(), firstMessages));
        }

        private static void RestoreCountersFromSession()
        {
            errorCount = SessionState.GetInt(KeyErrors, 0);
            exceptionCount = SessionState.GetInt(KeyExceptions, 0);
            warningCount = SessionState.GetInt(KeyWarnings, 0);
            firstMessages = new List<string>();
            string joined = SessionState.GetString(KeyFirstMessages, string.Empty);
            if (!string.IsNullOrEmpty(joined))
                firstMessages.AddRange(joined.Split(MessageSeparator));
            countersDirty = false;
        }

        // =====================================================================
        // 플레이 모드 전이
        // =====================================================================
        private static void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            if (!SessionState.GetBool(KeyActive, false))
                return;

            switch (change)
            {
                case PlayModeStateChange.EnteredPlayMode:
                    // 진입 리로드가 끝난 새 도메인. 여기서부터 에디터 시계로 20초 계측.
                    SessionState.SetString(KeyPhase, "measuring");
                    SessionState.SetString(KeyPlayStart, EditorApplication.timeSinceStartup.ToString("R"));
                    nextCheckAt = CheckIntervalSec;
                    nextPartialWriteAt = 0.0;
                    break;

                case PlayModeStateChange.ExitingPlayMode:
                    // 이탈 리로드 직전 — 카운터를 SessionState에 확실히 넘긴다.
                    if (SessionState.GetString(KeyPhase, string.Empty) == "measuring")
                        SessionState.SetBool(KeyEarlyExit, true); // 20초 전에 밖에서 정지됨
                    countersDirty = true;
                    FlushCountersToSession();
                    break;

                case PlayModeStateChange.EnteredEditMode:
                    // 이탈 리로드가 끝난 새 도메인 — 최종 결과 기록 후 정리.
                    WriteFinalResult();
                    ClearSessionAndUnhook();
                    break;
            }
        }

        // =====================================================================
        // 계측 루프 (에디터 시계 기준)
        // =====================================================================
        private static void OnEditorUpdate()
        {
            if (!SessionState.GetBool(KeyActive, false))
                return;

            FlushCountersToSession();

            if (SessionState.GetString(KeyPhase, string.Empty) != "measuring" || !EditorApplication.isPlaying)
                return;

            string playStartText = SessionState.GetString(KeyPlayStart, string.Empty);
            if (string.IsNullOrEmpty(playStartText) ||
                !double.TryParse(playStartText, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out double playStart))
                return;

            double elapsed = EditorApplication.timeSinceStartup - playStart;
            SessionState.SetFloat(KeyElapsed, (float)elapsed);

            // 플레이 중 재컴파일 리로드 후에는 nextCheckAt이 0으로 돌아온다 — 경과 시간에 맞춰 재정렬.
            while (nextCheckAt + CheckIntervalSec <= elapsed)
                nextCheckAt += CheckIntervalSec;

            if (elapsed >= nextCheckAt)
            {
                nextCheckAt += CheckIntervalSec;
                RunSceneChecks();
            }

            // 에디터가 도중에 죽어도 판독할 수 있게 1초마다 중간 결과를 남긴다.
            if (elapsed >= nextPartialWriteAt)
            {
                nextPartialWriteAt = elapsed + 1.0;
                WriteResultFile(finished: false, abortReason: string.Empty, durationSec: (float)elapsed);
            }

            if (elapsed >= MeasureDurationSec)
            {
                SessionState.SetString(KeyPhase, "exiting");
                FlushCountersToSession();
                Debug.Log($"[SmokeTest] {MeasureDurationSec}초 경과 — 플레이 이탈.");
                EditorApplication.ExitPlaymode();
            }
        }

        /// <summary>
        /// 핵심 오브젝트 존재 확인. 스폰 타이밍 차이를 흡수하려고 한 번이라도 관측되면
        /// true로 래치한다(SessionState에 저장 — 리로드 생존).
        /// </summary>
        private static void RunSceneChecks()
        {
            Transform[] all = null;
            foreach (var check in Checks)
            {
                if (SessionState.GetBool(KeyCheckPrefix + check.key, false))
                    continue;

                bool found;
                if (check.prefix)
                {
                    if (all == null)
                        all = UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include);
                    found = false;
                    foreach (var t in all)
                    {
                        if (t != null && t.name.StartsWith(check.objectName, StringComparison.Ordinal))
                        {
                            found = true;
                            break;
                        }
                    }
                }
                else
                {
                    found = GameObject.Find(check.objectName) != null;
                }

                if (found)
                    SessionState.SetBool(KeyCheckPrefix + check.key, true);
            }
        }

        // =====================================================================
        // 결과 기록
        // =====================================================================
        [Serializable]
        private class SmokeChecks
        {
            public bool player;
            public bool managers;
            public bool terrain;
            public bool hud;
            public bool ocean;
            public bool dayNightCycle;
            public bool buildingSystem;
            public bool cursorLock;
            public bool grassField;
            public bool underwaterAmbience;
            public bool sunkenCargo;
            public bool airlinerWreck;
            public bool underwaterCave;
            public bool marineLife;
            public bool shoreRibbon;
            public bool boss;
        }

        [Serializable]
        private class SmokeResult
        {
            public bool finished;
            public string abortReason;
            public float durationSec;
            public int errors;
            public int exceptions;
            public int warnings;
            public string[] firstMessages;
            public SmokeChecks checks;
            public string startedAtIso;
            public string unityVersion;
        }

        private static void WriteFinalResult()
        {
            bool earlyExit = SessionState.GetBool(KeyEarlyExit, false);
            float elapsed = SessionState.GetFloat(KeyElapsed, 0f);
            RestoreCountersFromSession(); // 이탈 리로드 직후 도메인 — SessionState가 진실
            WriteResultFile(
                finished: !earlyExit,
                abortReason: earlyExit ? "플레이 모드가 20초 계측 완료 전에 종료됐다(수동 정지 또는 외부 요인)." : string.Empty,
                durationSec: elapsed);
            Debug.Log($"[SmokeTest] 완료 — 결과: {ResultPath} (errors {errorCount} / exceptions {exceptionCount} / warnings {warningCount})");
        }

        private static void WriteResultFile(bool finished, string abortReason, float durationSec)
        {
            var checks = new SmokeChecks
            {
                player = SessionState.GetBool(KeyCheckPrefix + "player", false),
                managers = SessionState.GetBool(KeyCheckPrefix + "managers", false),
                terrain = SessionState.GetBool(KeyCheckPrefix + "terrain", false),
                hud = SessionState.GetBool(KeyCheckPrefix + "hud", false),
                ocean = SessionState.GetBool(KeyCheckPrefix + "ocean", false),
                dayNightCycle = SessionState.GetBool(KeyCheckPrefix + "dayNightCycle", false),
                buildingSystem = SessionState.GetBool(KeyCheckPrefix + "buildingSystem", false),
                cursorLock = SessionState.GetBool(KeyCheckPrefix + "cursorLock", false),
                grassField = SessionState.GetBool(KeyCheckPrefix + "grassField", false),
                underwaterAmbience = SessionState.GetBool(KeyCheckPrefix + "underwaterAmbience", false),
                sunkenCargo = SessionState.GetBool(KeyCheckPrefix + "sunkenCargo", false),
                airlinerWreck = SessionState.GetBool(KeyCheckPrefix + "airlinerWreck", false),
                underwaterCave = SessionState.GetBool(KeyCheckPrefix + "underwaterCave", false),
                marineLife = SessionState.GetBool(KeyCheckPrefix + "marineLife", false),
                shoreRibbon = SessionState.GetBool(KeyCheckPrefix + "shoreRibbon", false),
                boss = SessionState.GetBool(KeyCheckPrefix + "boss", false),
            };

            var result = new SmokeResult
            {
                finished = finished,
                abortReason = abortReason,
                durationSec = (float)Math.Round(durationSec, 2),
                errors = errorCount,
                exceptions = exceptionCount,
                warnings = warningCount,
                firstMessages = firstMessages.ToArray(),
                checks = checks,
                startedAtIso = SessionState.GetString(KeyStartedAtIso, string.Empty),
                unityVersion = Application.unityVersion,
            };

            File.WriteAllText(ResultPath, JsonUtility.ToJson(result, true));
        }

        /// <summary>플레이 진입 전 중단 사유 기록(계측 상태를 만들지 않는다).</summary>
        private static void WriteAbortResult(string reason)
        {
            var result = new SmokeResult
            {
                finished = false,
                abortReason = reason,
                durationSec = 0f,
                errors = 0,
                exceptions = 0,
                warnings = 0,
                firstMessages = new string[0],
                checks = new SmokeChecks(),
                startedAtIso = DateTime.Now.ToString("o"),
                unityVersion = Application.unityVersion,
            };
            File.WriteAllText(ResultPath, JsonUtility.ToJson(result, true));
        }

        private static void ClearSessionAndUnhook()
        {
            SessionState.EraseBool(KeyActive);
            SessionState.EraseString(KeyPhase);
            SessionState.EraseString(KeyStartedAtIso);
            SessionState.EraseString(KeyPlayStart);
            SessionState.EraseFloat(KeyElapsed);
            SessionState.EraseInt(KeyErrors);
            SessionState.EraseInt(KeyExceptions);
            SessionState.EraseInt(KeyWarnings);
            SessionState.EraseString(KeyFirstMessages);
            SessionState.EraseBool(KeyEarlyExit);
            foreach (var check in Checks)
                SessionState.EraseBool(KeyCheckPrefix + check.key);
            Unhook();
        }
    }
}
