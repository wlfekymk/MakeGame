using UnityEngine;
using MakeGame.Data;

namespace MakeGame.Player
{
    /// <summary>
    /// 플레이어의 이동과 시점 회전을 담당하는 1인칭 스타일 컨트롤러.
    /// CharacterController로 WASD 이동, 스페이스 점프, 마우스로 시점을 조작한다.
    /// 골절 상태(SurvivalStats.hasBrokenBone)일 때는 이동 속도가 느려진다.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class PlayerController : MonoBehaviour
    {
        [Tooltip("기본 이동 속도(m/s)")]
        public float moveSpeed = 5f;

        [Tooltip("골절 상태일 때 적용되는 이동 속도 배율")]
        public float brokenBoneSpeedMultiplier = 0.4f;

        [Tooltip("점프 힘")]
        public float jumpForce = 6f;

        [Tooltip("중력 가속도")]
        public float gravity = -20f;

        [Header("수영/잠수")]
        [Tooltip("해수면 높이. 발 위치(transform.position)가 이보다 낮아지면 수영 모드로 전환된다.\n섬 지형은 항상 y=0 이상이므로 0으로 두면 섬 밖 바다에서만 수영 모드가 된다.")]
        public float waterLevel = 0f;

        [Tooltip("수영 중 수평 이동 속도(m/s)")]
        public float swimSpeed = 3f;

        [Tooltip("수영 중 위/아래로 움직이는 속도(m/s)")]
        public float swimVerticalSpeed = 2f;

        [Tooltip("입력이 없을 때 수면 쪽으로 떠오르는 부력 속도(m/s)")]
        public float buoyancy = 1f;

        [Tooltip("수영 판정에 쓰는 수면 높이가 파도(OceanWaves)를 따라가게 할지.\n" +
            "끄면 예전처럼 평평한 waterLevel 상수만 쓴다(동작 100% 회귀).")]
        public bool followOceanWaves = true;

        [Tooltip("파도를 얼마나 따라갈지(1 = 파고 그대로). 바다 평면 메시가 실제로 출렁이지는 못하므로" +
            "(OceanWaves 주석의 나머지 근거 참고) 1로 두면 수면 그림과 판정이 크게 벌어질 수 있다.")]
        public float oceanWaveFollowScale = 0.75f;

        [Tooltip("잠수(더 깊이 내려가기) 키")]
        public KeyCode diveKey = KeyCode.LeftControl;

        // [0.2.22 사용자 보고 "잠수 키가 설정 안되어 있어"] 씬에는 diveKey=LeftControl(306)이
        // 멀쩡히 직렬화돼 있었지만 실기에서 잠수가 안 된다는 보고가 있었다(원인 미재현 - Ctrl이
        // 다른 프로그램/OS 조합에 먹혔을 가능성). 재현 불가 상황의 견고책으로 보조 키를 둔다.
        // 새 직렬화 필드는 기존 씬에 값이 없으므로 이 코드 기본값(LeftShift)이 그대로 적용된다.
        [Tooltip("보조 잠수 키 (주 키와 어느 쪽이든 동작)")]
        public KeyCode diveKeyAlt = KeyCode.LeftShift;

        // ── 전투 (회피 구르기 / 창 투척) ────────────────────────────────────────────
        //
        // [왜 여기서 입력을 받는가] 근접 공격·채집·상호작용의 분기 순서 규약을 가진
        // InteractionController.cs는 이 작업의 락 밖이다. 그 파일의 주석은 분기 순서를 한 줄도
        // 바꾸지 말 것을 명시하고 있으므로, 새 입력은 전부 이쪽(이동의 주인)에서 처리한다.
        // 회피는 어차피 이동이라 여기가 제자리이고, 투척은 우클릭이라 E 계열과 아예 겹치지 않는다.
        //
        // [키 선택 근거 - AGENT_BRIEF 3장 "키 배정(전수)" + 전수 grep 대조]
        // 이미 쓰이는 키: E R C G F Tab V J M B Q Space Esc LeftCtrl Shift(L/R) 1~7 = - F3~F11.
        // 마우스: 좌/우 버튼은 **건축 모드 안에서만** 쓰인다(BuildingSystem.Update는 buildMode가
        // 꺼져 있으면 그 두 줄에 도달하지 않는다). 그래서 건축 모드에서만 투척을 막으면 충돌이 없다.
        // X와 T는 프로젝트 전체에서 한 번도 쓰이지 않는다(KeyCode 전수 grep 확인).

        [Header("전투 - 회피 구르기")]
        [Tooltip("짧게 대시(구르기)하는 키. X는 프로젝트에서 쓰이지 않는 빈 키다.")]
        public KeyCode dodgeKey = KeyCode.X;

        [Tooltip("구르기 재사용 대기 시간(초)")]
        public float dodgeCooldownSeconds = 1.8f;

        [Tooltip("구르는 동안의 이동 속도(m/s). 기본 이동(5)보다 훨씬 빨라야 회피가 된다.")]
        public float dodgeSpeed = 13f;

        [Tooltip("구르는 시간(초). 이 시간 × 속도가 곧 회피 거리다(0.28 × 13 ≈ 3.6m).")]
        public float dodgeDurationSeconds = 0.28f;

        [Tooltip("구른 직후의 경직 시간(초). 무적 프레임 대신 이 짧은 빈틈이 회피의 대가다.")]
        public float dodgeRecoverySeconds = 0.22f;

        [Tooltip("경직 동안의 이동 속도 배율")]
        public float dodgeRecoverySpeedMultiplier = 0.35f;

        [Tooltip("구르기 1회의 허기 소모량. 스태미나가 없는 프로젝트라 RaftSailing의 노 젓기 대가(허기/갈증)를 따른다.")]
        public float dodgeHungerCost = 1.5f;

        [Tooltip("구르기 1회의 갈증 소모량")]
        public float dodgeThirstCost = 2f;

        [Header("전투 - 창 투척")]
        [Tooltip("창을 던지는 마우스 버튼(1 = 우클릭). 음수로 두면 마우스 투척이 꺼진다.")]
        public int throwMouseButton = 1;

        [Tooltip("창을 던지는 보조 키(마우스를 못 쓰는 상황용). T는 프로젝트에서 쓰이지 않는 빈 키다.")]
        public KeyCode throwKey = KeyCode.T;

        [Tooltip("투척 재사용 대기 시간(초)")]
        public float throwCooldownSeconds = 0.8f;

        [Tooltip("투척 초기 속도(m/s)")]
        public float throwSpeed = 26f;

        [Tooltip("머리가 물속일 때 투척 속도에 곱하는 배율. 사거리가 눈에 띄게 짧아진다.")]
        public float underwaterThrowSpeedMultiplier = 0.5f;

        [Tooltip("사냥/전투 경험치를 받을 스킬 컴포넌트. 비워두면 같은 오브젝트에서 자동으로 찾는다.")]
        public PlayerSkills playerSkills;

        [Tooltip("마우스 좌우/상하 회전 감도")]
        public float lookSensitivity = 2f;

        [Tooltip("시점 카메라 (상하 회전은 이 카메라에만 적용한다)")]
        public Transform cameraTransform;

        [Header("커서 잠금")]
        [Tooltip("커서 잠금 상태가 바뀐 직후 시야 회전을 건너뛸 프레임 수.\n" +
            "Input.GetAxis(\"Mouse X\")는 잠금이 걸리거나 풀리는 순간 큰 값이 한 번 튀는 경우가 있어, " +
            "그 프레임을 버리지 않으면 커서를 다시 잠그는 순간 시야가 홱 돌아간다. 0이면 이 보호가 꺼진다.")]
        public int lookSettleFrames = 2;

        [Tooltip("이동 속도 판정에 사용할 생존 수치 컴포넌트 (골절 시 감속 등)")]
        public SurvivalStats survivalStats;

        // ── 소지 패시브 장비 (오리발/산소통) ─────────────────────────────────────────────
        //
        // 이 프로젝트에는 "장착" 슬롯이 없다. 부력통이 재료로만 쓰이듯 장비류도 인벤토리 항목일
        // 뿐이므로, "인벤토리에 들어 있기만 하면 효과가 켜지는" 소지 패시브로 구현한다.
        // 프레임마다 인벤토리를 뒤지지 않도록 PlayerInventory.InventoryChanged(추가/제거/복원
        // 모두에서 발행됨 - 세이브 복원의 AddItemIgnoringCapacity도 발행한다)에서만 다시 검사해
        // bool로 캐시한다. 판정 키는 세이브와 같은 itemName 문자열이다(ItemData 에셋 참조를
        // 쓰려면 씬 직렬화가 필요한데, 이 기능은 씬을 건드리지 않고 붙는다).

        /// <summary>소지 패시브 판정에 쓰는 오리발 아이템 이름(Item_오리발.asset의 itemName).</summary>
        public const string SwimFinsItemName = "오리발";

        /// <summary>소지 패시브 판정에 쓰는 산소통 아이템 이름(Item_산소통.asset의 itemName).</summary>
        public const string OxygenTankItemName = "산소통";

        [Header("소지 패시브 장비")]
        [Tooltip("오리발을 소지 중일 때 수영/잠수 이동 속도에 곱하는 배율 (1.4 = +40%).\n" +
            "수평(swimSpeed)과 수직 입력(swimVerticalSpeed)에만 적용되고, 입력 없이 떠오르는 부력(buoyancy)은 그대로다.")]
        public float finSwimSpeedMultiplier = 1.4f;

        [Tooltip("산소통을 소지 중일 때 SurvivalStats의 산소 감소 속도에 곱하는 배율 (0.5 = 지속시간 2배).")]
        public float oxygenTankDrainMultiplier = 0.5f;

        // 소지 패시브 캐시. RefreshPassiveEquipment(인벤토리 변경 이벤트)에서만 갱신된다.
        private PlayerInventory inventory;
        private bool hasSwimFins;
        private bool hasOxygenTank;

        private CharacterController controller;
        private Vector3 verticalVelocity;
        private float cameraPitch = 0f;

        /// <summary>
        /// 이동 입력을 통째로 넘겨받은 시스템이 있는가(지금은 뗏목 조종 - MakeGame.Systems.RaftSailing).
        ///
        /// [왜 "이동만" 끄는가] 조종 중에도 **시점 회전은 살아 있어야 한다**(HandleLook은 이 값을 보지
        /// 않는다). 배를 돌리면서 주위를 둘러보지 못하면 항해가 아니라 궤도 이동이 된다.
        ///
        /// [왜 컨트롤러를 끄지 않는가] CharacterController를 비활성화하면 RaftStructure.CarryRider가
        /// 쓰는 Move가 죽어 플레이어가 갑판에 붙어 있지 못한다. 조종 중 플레이어를 옮기는 것은 오직
        /// CarryRider뿐이고, 그러려면 컨트롤러는 켜져 있되 스스로 걷지만 않으면 된다.
        ///
        /// [직렬화하지 않는다] 순수 런타임 상태다. 이 값이 켜진 채 씬이 저장/복원되면 플레이어가
        /// 영영 못 움직이므로, 소유자(RaftSailing)가 OnDisable에서도 반드시 되돌린다.
        /// </summary>
        [System.NonSerialized] public bool MovementSuspended;

        // 커서 잠금 상태가 바뀐 프레임을 알아보기 위한 직전 상태와, 그때 회전을 건너뛸 남은 프레임 수.
        private bool lookCursorWasLocked;
        private int lookSettleCounter;

        // ── 전투 런타임 상태 (직렬화하지 않는다 - 전부 순수 런타임 타이머다) ──────────
        private float dodgeCooldownTimer;
        private float dodgeActiveTimer;
        private float dodgeRecoveryTimer;
        private Vector3 dodgeDirection;
        private float throwCooldownTimer;

        /// <summary>필요한 컴포넌트 참조를 캐싱하고, 카메라 흔들림 컴포넌트를 붙인다.</summary>
        private void Awake()
        {
            controller = GetComponent<CharacterController>();
            lookCursorWasLocked = Cursor.lockState == CursorLockMode.Locked;
            lookSettleCounter = Mathf.Max(0, lookSettleFrames);
            EnsureCameraShake();
            HookPassiveEquipment();

            // 투척 명중 시 사냥/전투 경험치를 줄 대상. 인스펙터에서 비어 있어도 동작해야 하므로
            // 같은 오브젝트 → 씬 전역 순으로 폴백한다(HookPassiveEquipment의 인벤토리 탐색과 같은 패턴).
            if (playerSkills == null)
                playerSkills = GetComponent<PlayerSkills>();
            if (playerSkills == null)
                playerSkills = FindAnyObjectByType<PlayerSkills>();
        }

        /// <summary>
        /// 이벤트 구독 해제. 인벤토리와 같은 GameObject라 수명이 같지만,
        /// 파괴 순서에 따라 죽은 구독이 남지 않도록 명시적으로 푼다.
        /// </summary>
        private void OnDestroy()
        {
            if (inventory != null)
                inventory.InventoryChanged -= RefreshPassiveEquipment;
        }

        /// <summary>
        /// 소지 패시브 장비(오리발/산소통) 감시를 시작한다. PlayerInventory는 같은 Player
        /// GameObject에 붙어 있으므로(SampleScene 실측) GetComponent로 찾고, 혹시 분리된
        /// 씬 구성을 위해 전역 검색으로 한 번 더 폴백한다.
        /// Awake에서 호출한다 - 이 컴포넌트는 타이틀 화면 동안 꺼져 있지만(enabled=false)
        /// C# 이벤트는 enabled와 무관하게 도착하므로, 시작 지급(GrantStartingLoadout)이나
        /// 세이브 복원으로 생기는 변경도 놓치지 않는다.
        /// </summary>
        private void HookPassiveEquipment()
        {
            inventory = GetComponent<PlayerInventory>();
            if (inventory == null)
                inventory = FindAnyObjectByType<PlayerInventory>();
            if (inventory == null)
                return;

            inventory.InventoryChanged += RefreshPassiveEquipment;
            RefreshPassiveEquipment();
        }

        /// <summary>
        /// 인벤토리 변경 시에만 호출되어 오리발/산소통 소지 여부를 다시 검사해 캐시한다
        /// (프레임당 인벤토리 순회 비용을 0으로 유지하기 위한 구조 - HandleSwimMove와
        /// SurvivalStats.UpdateOxygen은 여기서 만든 캐시/배율만 읽는다).
        /// </summary>
        private void RefreshPassiveEquipment()
        {
            hasSwimFins = false;
            hasOxygenTank = false;

            var items = inventory.items;
            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (item == null || item.data == null)
                    continue;

                if (item.data.itemName == SwimFinsItemName)
                    hasSwimFins = true;
                else if (item.data.itemName == OxygenTankItemName)
                    hasOxygenTank = true;

                if (hasSwimFins && hasOxygenTank)
                    break;
            }

            // 산소통 효과는 산소 로직의 주인인 SurvivalStats로 밀어 넣는다. 0 이하(잘못된 설정)는
            // SurvivalStats 쪽 관례대로 미설정으로 취급되므로 그대로 1(효과 없음)로 동작한다.
            if (survivalStats != null)
                survivalStats.oxygenDrainMultiplier = hasOxygenTank ? oxygenTankDrainMultiplier : 1f;
        }

        /// <summary>
        /// [B34] 카메라에 CameraShake를 붙인다(없을 때만). 곰의 충격 훅(CreatureMotion.OnImpact)을
        /// 받아 화면을 흔드는 컴포넌트이며, 씬 파일을 편집하지 않고 붙이기 위해 카메라를 소유한
        /// 이쪽에서 런타임에 단다.
        ///
        /// PlayerController가 아니라 **카메라 오브젝트**에 붙이는 이유: 이 컴포넌트는 타이틀 화면
        /// 동안 꺼져 있고(MainMenuController.SetGameplayEnabled) 사망·엔딩에서도 꺼지는데, 흔들림은
        /// 카메라의 위치 채널을 스스로 정리(원위치 복구)해야 하므로 컨트롤러의 on/off에 묶이면 안 된다.
        /// 흔들림은 localPosition만 쓰므로 HandleLook의 회전(localEulerAngles)과 채널이 겹치지 않는다.
        ///
        /// Awake는 컴포넌트가 비활성(enabled = false) 상태여도 호출되므로, 타이틀에서 시작하는
        /// 흐름에서도 부착은 반드시 한 번 일어난다.
        /// </summary>
        private void EnsureCameraShake()
        {
            if (cameraTransform == null)
                return;

            CameraShake shake = cameraTransform.GetComponent<CameraShake>();
            if (shake == null)
                shake = cameraTransform.gameObject.AddComponent<CameraShake>();

            // 이미 붙어 있고 대상이 잡혀 있으면 그대로 둔다. 원래 위치(basePosition)를 Awake에서
            // 기억해 둔 뒤라, 여기서 대상을 갈아끼우면 기준점이 어긋난다.
            if (shake.shakeTarget == null)
                shake.shakeTarget = cameraTransform;
        }

        /// <summary>
        /// 이 컴포넌트는 타이틀 화면 동안 꺼져 있다가 켜진다(MainMenuController.SetGameplayEnabled).
        /// 다시 켜지는 첫 프레임에도 마우스 델타가 튈 수 있으므로 같은 보호를 건다.
        /// </summary>
        private void OnEnable()
        {
            lookCursorWasLocked = Cursor.lockState == CursorLockMode.Locked;
            lookSettleCounter = Mathf.Max(0, lookSettleFrames);
        }

        /// <summary>
        /// 매 프레임 입력을 받아 시점 회전과 이동을 처리한다.
        /// </summary>
        private void Update()
        {
            HandleLook();
            // 전투 입력은 반드시 HandleMove보다 **먼저** 돈다. 이 프레임에 시작된 구르기가
            // 같은 프레임의 이동에 바로 반영돼야 입력 지연(1프레임)이 생기지 않는다.
            HandleCombatInput();
            HandleMove();
            PushAirPocketState();
        }

        /// <summary>
        /// [전투 깊이 확장] 회피 구르기(X)와 창 투척(우클릭/T) 입력을 처리하고 관련 타이머를 굴린다.
        ///
        /// 입력을 받지 않는 조건은 이 프로젝트가 이미 쓰는 두 게이트를 그대로 따른다:
        ///  · Time.timeScale &lt;= 0 — 타이틀·설정·엔딩·사망 화면(AGENT_BRIEF 4장).
        ///  · 커서가 잠겨 있지 않음 — 창이 열려 있거나 Shift로 커서를 푼 상태다. 판정처는
        ///    CursorLockController 하나이고(직접 lockState를 만지지 않는다) HandleLook도 같은 조건을 쓴다.
        ///    이 게이트 하나가 인벤토리·제작·퀘스트·지도·상자·뗏목 창을 전부 덮으므로, 창 위에서
        ///    우클릭했다고 창이 날아가는 일이 없다.
        ///  · MovementSuspended — 뗏목 조종 중. 조종은 이동 권한을 통째로 넘긴 상태다.
        /// 타이머는 게임 시간(deltaTime)으로 센다 - 멈춘 화면에서 쿨다운이 흐르면 안 된다.
        /// </summary>
        private void HandleCombatInput()
        {
            float dt = Time.deltaTime;

            if (dodgeCooldownTimer > 0f)
                dodgeCooldownTimer = Mathf.Max(0f, dodgeCooldownTimer - dt);

            if (dodgeActiveTimer > 0f)
            {
                dodgeActiveTimer -= dt;
                if (dodgeActiveTimer <= 0f)
                {
                    dodgeActiveTimer = 0f;
                    dodgeRecoveryTimer = Mathf.Max(0f, dodgeRecoverySeconds);
                }
            }
            else if (dodgeRecoveryTimer > 0f)
            {
                dodgeRecoveryTimer = Mathf.Max(0f, dodgeRecoveryTimer - dt);
            }

            if (throwCooldownTimer > 0f)
                throwCooldownTimer = Mathf.Max(0f, throwCooldownTimer - dt);

            if (Time.timeScale <= 0f || MovementSuspended)
                return;

            if (Cursor.lockState != CursorLockMode.Locked)
                return;

            if (Input.GetKeyDown(dodgeKey))
                TryStartDodge();

            bool throwPressed = Input.GetKeyDown(throwKey)
                || (throwMouseButton >= 0 && Input.GetMouseButtonDown(throwMouseButton));

            // 건축 모드에서는 좌/우클릭이 배치·철거다(BuildingSystem.Update). 그 동안에는 던지지 않는다.
            var building = MakeGame.Systems.BuildingSystem.Instance;
            if (throwPressed && (building == null || !building.IsBuildModeOn))
                TryThrowSpear();
        }

        /// <summary>
        /// 회피 구르기를 시작한다. 무적 프레임은 **일부러 넣지 않는다**(감독 지시 - 판정이 복잡해진다).
        /// 회피의 가치는 "짧은 시간에 3.6m를 벌어 곰의 앞발 사거리 2.8m 밖으로 나간다"는 거리 자체이고,
        /// 대가는 (1) 직후 경직, (2) 쿨다운, (3) 허기/갈증 소모 세 가지다.
        ///
        /// 허기/갈증을 대가로 삼은 것은 이 프로젝트에 스태미나가 없기 때문이고, 선례는
        /// RaftSailing의 노 젓기다(RowingHungerPerSecond 0.05 / RowingThirstPerSecond 0.07 -
        /// 초당 값이다). 구르기는 1회성이라 초당이 아니라 회당으로 매기며, 1.5/2.0은 노 젓기
        /// 30초분에 해당한다 - 전투 한 번에 서너 번 구르면 한 끼가 필요해지는 정도다.
        /// </summary>
        private void TryStartDodge()
        {
            if (dodgeCooldownTimer > 0f || dodgeActiveTimer > 0f || dodgeRecoveryTimer > 0f)
                return;

            // **수영 중에는 구를 수 없다(판단).** 물속 이동(HandleSwimMove)은 중력도 접지도 쓰지 않는
            // 완전히 다른 경로라, 대시 벡터를 얹으면 수면 위로 튀어 오르거나 지형을 통과한다.
            // "약하게 허용"은 그 두 경로를 섞어야 해서 이번 범위에서는 넣지 않았다 - 토글로 남겨 두면
            // 켰을 때 대가(허기/갈증·쿨다운)만 나가고 아무 일도 안 일어나는 죽은 스위치가 된다.
            if (transform.position.y < CurrentWaterSurfaceY())
                return;

            // 공중 회피 금지 - 뛰어오른 김에 거리를 버는 것은 회피가 아니라 이단 점프가 된다.
            if (!controller.isGrounded)
                return;

            float h = Input.GetAxisRaw("Horizontal");
            float v = Input.GetAxisRaw("Vertical");
            Vector3 direction = transform.right * h + transform.forward * v;
            direction.y = 0f;

            // 입력이 없으면 정면으로 구른다. "방향키를 안 눌러서 아무 일도 안 일어났다"는
            // 이 프로젝트가 반복해서 낸 무반응 실패 패턴이라, 실패로 만들지 않는다.
            if (direction.sqrMagnitude < 0.0001f)
                direction = transform.forward;
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.0001f)
                return;

            dodgeDirection = direction.normalized;
            dodgeActiveTimer = Mathf.Max(0.05f, dodgeDurationSeconds);
            dodgeRecoveryTimer = 0f;
            dodgeCooldownTimer = Mathf.Max(dodgeActiveTimer, dodgeCooldownSeconds);

            if (survivalStats != null)
            {
                survivalStats.hunger = Mathf.Max(0f, survivalStats.hunger - Mathf.Max(0f, dodgeHungerCost));
                survivalStats.thirst = Mathf.Max(0f, survivalStats.thirst - Mathf.Max(0f, dodgeThirstCost));
            }
        }

        /// <summary>
        /// 구르기를 즉시 끝낸다(물에 들어간 순간 등). 쿨다운은 되돌리지 않는다 - 이미 쓴 회피다.
        /// </summary>
        private void CancelDodge()
        {
            dodgeActiveTimer = 0f;
            dodgeRecoveryTimer = 0f;
        }

        /// <summary>
        /// 구르는 동안의 이동. 방향은 시작 시점에 고정되고(구르는 중에 방향을 틀 수 있으면 회피가
        /// 아니라 단순 가속이 된다) 중력은 평소와 똑같이 적용한다.
        /// </summary>
        private void HandleDodgeMove()
        {
            if (controller.isGrounded)
                verticalVelocity.y = -1f;
            else
                verticalVelocity.y += gravity * Time.deltaTime;

            controller.Move((dodgeDirection * dodgeSpeed + verticalVelocity) * Time.deltaTime);
        }

        /// <summary>
        /// 인벤토리의 창을 하나 던진다. 실제 비행·명중·회수는 전부 ThrownWeapon이 맡는다.
        ///
        /// 인벤토리 처리 순서가 중요하다:
        ///  1. UseItem — 내구도 1 소모. 이번 사용으로 다 닳으면 PlayerInventory가 **알아서** 목록에서
        ///     빼고 파손음까지 낸다(기존 규약. 여기서 흉내내지 않는다).
        ///  2. 안 부러졌으면 RemoveItem — 던졌으니 가방에서 사라져야 한다. 회수하면 되돌아온다.
        ///     부러졌으면 1에서 이미 빠졌으므로 여기서 또 빼면 안 된다.
        /// 부러진 창은 회수 불가(recoverable=false)로 던져져, 맞히든 빗나가든 그대로 사라진다.
        ///
        /// 창이 없을 때는 **소리 없이 아무 일도 하지 않는다.** 우클릭은 평소에 아무 뜻도 없는 입력이라,
        /// 여기서 실패음을 내면 걷다가 무심코 누를 때마다 게임이 삑삑거린다(A단계 규칙 - 일상 입력에
        /// 화면/소리 이펙트를 붙이지 않는다). 무기가 필요하다는 안내는 조준 프롬프트가 이미 한다.
        /// </summary>
        private void TryThrowSpear()
        {
            if (throwCooldownTimer > 0f || inventory == null)
                return;

            InventoryItem spear = MakeGame.Systems.CombatSystem.FindThrowable(inventory);
            if (spear == null || spear.data == null)
                return;

            float damage = MakeGame.Systems.CombatSystem.GetThrowDamage(spear.data);
            ItemData data = spear.data;

            inventory.UseItem(spear);
            bool broken = !data.IsUnlimited && spear.remainingUses <= 0;
            if (!broken)
                inventory.RemoveItem(spear);

            float waterY = CurrentWaterSurfaceY();
            Vector3 origin = cameraTransform != null
                ? cameraTransform.position + cameraTransform.forward * 0.7f
                : transform.position + Vector3.up * 1.5f + transform.forward * 0.7f;
            Vector3 direction = cameraTransform != null ? cameraTransform.forward : transform.forward;

            // 머리(=발사 지점)가 수면 아래면 물속 투척이다. 발 기준이 아니라 머리 기준인 이유는
            // 허리까지 잠긴 얕은 물에서 던지는 것은 물속 투척이 아니기 때문이다.
            bool underwater = origin.y < waterY;
            float speed = underwater
                ? throwSpeed * Mathf.Clamp(underwaterThrowSpeedMultiplier, 0.1f, 1f)
                : throwSpeed;

            MakeGame.Systems.ThrownWeapon.Launch(data, spear.remainingUses, !broken,
                origin, direction, speed, damage, waterY, inventory, playerSkills, transform);

            throwCooldownTimer = Mathf.Max(0.1f, throwCooldownSeconds);
        }

        /// <summary>
        /// 머리(카메라 위치)가 에어포켓(AirPocketZone) 안인지 판정해 SurvivalStats에 밀어준다.
        /// oxygenDrainMultiplier(산소통 패시브)와 같은 "컨트롤러가 파생 상태를 밀어주는" 패턴 —
        /// SurvivalStats.UpdateOxygen은 이 bool만 읽고, 참이면 잠수 중에도 산소를 회복시킨다.
        ///
        /// 성능: O(n) 존 순회(AirPocketZone.IsInsideAny)는 수영/잠수 모드(발이 수면 아래)일 때만
        /// 프레임당 1회 수행한다. 육지에서는 산소가 어차피 정상 회복되므로 판정 자체가 무의미하고,
        /// 에어포켓은 정의상 수면 아래(동굴 천장)에만 있어 판정 누락도 생기지 않는다.
        /// </summary>
        private void PushAirPocketState()
        {
            if (survivalStats == null)
                return;

            bool inPocket = false;
            // 여기는 **일부러 평평한 waterLevel 그대로** 둔다(CurrentWaterSurfaceY를 쓰지 않는다).
            // 산소/에어포켓은 이 작업의 범위 밖이고, 수면 근처에서 파도를 태우면 이 게이트가 매 프레임
            // 켜졌다 꺼지며 존 순회가 깜빡인다. 파도는 이동(수영/보행) 판정에만 반영한다.
            if (transform.position.y < waterLevel)
            {
                // 판정 기준점은 카메라(=머리) 위치. 카메라가 연결 안 된 씬에서는 SurvivalTickDriver의
                // 산소 판정과 같은 머리 높이(발 + 1.6m)로 폴백한다.
                Vector3 headPos = cameraTransform != null
                    ? cameraTransform.position
                    : transform.position + Vector3.up * 1.6f;
                inPocket = MakeGame.Systems.AirPocketZone.IsInsideAny(headPos);
            }

            survivalStats.isHeadInAirPocket = inPocket;
        }

        /// <summary>
        /// 마우스 입력으로 좌우(플레이어 몸체)/상하(카메라) 시점을 회전시킨다.
        ///
        /// **커서가 잠겨 있을 때만 돈다.** 커서 잠금은 MakeGame.Systems.CursorLockController가 한 곳에서
        /// 판정하고(창 열림 · timeScale 0 · Shift 해제 키), 이 한 조건이 인벤토리·제작·퀘스트·지도 창
        /// 조작, 일시정지, 타이틀·설정·엔딩·사망 화면을 전부 덮는다. 커서가 풀린 동안 마우스를 움직여도
        /// 화면이 따라 돌지 않는다 - 감독이 요청한 "Shift를 누르면 마우스만 움직이고 시야는 그대로"가 이것이다.
        ///
        /// 이동(HandleMove)에는 이 조건을 걸지 않는다. 창을 열어 둔 채 걸을 수 있는 것은 기존 동작이다.
        /// </summary>
        private void HandleLook()
        {
            bool cursorLocked = Cursor.lockState == CursorLockMode.Locked;

            // 잠금 상태가 바뀐 프레임은 회전을 건너뛴다. 잠금 전환 직후 Input.GetAxis("Mouse X")가 큰
            // 값으로 한 번 튀는 경우가 있어, 그대로 쓰면 커서를 다시 잠그는 순간 시야가 홱 돌아간다.
            if (cursorLocked != lookCursorWasLocked)
            {
                lookCursorWasLocked = cursorLocked;
                lookSettleCounter = Mathf.Max(0, lookSettleFrames);
            }

            if (!cursorLocked)
                return;

            if (lookSettleCounter > 0)
            {
                lookSettleCounter--;
                return;
            }

            float mouseX = Input.GetAxis("Mouse X") * lookSensitivity;
            float mouseY = Input.GetAxis("Mouse Y") * lookSensitivity;

            transform.Rotate(Vector3.up, mouseX);

            if (cameraTransform != null)
            {
                cameraPitch = Mathf.Clamp(cameraPitch - mouseY, -80f, 80f);
                cameraTransform.localEulerAngles = new Vector3(cameraPitch, 0f, 0f);
            }
        }

        /// <summary>
        /// 지금 발밑의 수면 높이(m). 파도 연동이 켜져 있으면 평평한 waterLevel 대신 그 지점의 파고를 쓴다.
        ///
        /// **평균 해수면은 waterLevel 그대로 두고 파도 "편차"만 더한다**(SampleHeight가 아니라
        /// SampleWaveOffset을 쓰는 이유). PlayerController.waterLevel은 씬 직렬화 값(0)이고
        /// WorldMapManager.seaLevel과 별개의 필드이므로, 절대 높이를 통째로 갈아치우면 두 값이
        /// 갈라진 구성에서 수영 판정 기준이 조용히 바뀐다. 편차만 얹으면 기존 기준선이 100% 보존된다.
        ///
        /// **적용 범위(경계)**: 이 값은 오직 HandleMove의 수영/보행 전환에만 쓴다.
        ///  * 산소·잠수·에어포켓 판정(PushAirPocketState, SurvivalTickDriver.IsUnderwater)은 그대로
        ///    평평한 waterLevel을 쓴다 - 요청 범위 밖이고, 그쪽은 머리 높이 기준이라 파도를 태우면
        ///    수면 근처에서 산소가 깜빡인다.
        ///  * 월드 생성 경로(IslandMeshGenerator.Vegetation의 VegetationSeaLevelY, SeabedFloraSpawner ·
        ///    UnderwaterCaveSpawner의 seaLevel, HazardSpawner의 육상 판정)의 **평면 해수면 가정은
        ///    한 글자도 건드리지 않았다.** 그쪽에 파도를 넣으면 같은 worldSeed에서 배치가 달라져
        ///    월드 생성 결정성이 깨진다. 파도는 런타임 체감 전용이다.
        /// </summary>
        private float CurrentWaterSurfaceY()
        {
            if (!followOceanWaves)
                return waterLevel;

            return waterLevel
                + MakeGame.Systems.OceanWaves.SampleWaveOffset(transform.position) * oceanWaveFollowScale;
        }

        /// <summary>
        /// WASD 입력으로 수평 이동을 처리하고, 중력/점프로 수직 이동을 처리한다.
        /// 골절 상태이면 이동 속도가 느려진다. 해수면보다 낮은 위치(섬 밖 바다)에서는 수영 모드로 전환된다.
        /// </summary>
        private void HandleMove()
        {
            // [뗏목 조종] 이동 권한을 넘긴 동안에는 WASD/점프를 아예 읽지 않는다. 같은 키가 배 조종에
            // 쓰이므로 여기서 소비하면 갑판 위를 걸어 나가면서 배도 돌아간다. 자세 유지는 전적으로
            // RaftStructure.CarryRider가 맡는다(그쪽이 뗏목 로컬 좌표를 보존해 Move를 부른다).
            if (MovementSuspended)
            {
                // 중력 누적을 비워 둔다 - 조종을 그만두는 순간 그동안 쌓인 낙하 속도로 갑판을 뚫지 않게.
                verticalVelocity = Vector3.zero;
                return;
            }

            float speed = moveSpeed;
            if (survivalStats != null && survivalStats.hasBrokenBone)
                speed *= brokenBoneSpeedMultiplier;

            // [회피] 구른 직후의 짧은 경직. 무적 프레임 대신 이것이 회피의 대가다.
            if (dodgeRecoveryTimer > 0f)
                speed *= Mathf.Clamp01(dodgeRecoverySpeedMultiplier);

            float h = Input.GetAxis("Horizontal");
            float v = Input.GetAxis("Vertical");

            if (transform.position.y < CurrentWaterSurfaceY())
            {
                // 물에 들어가는 순간 구르기는 끝난다. 수영 이동은 중력도 접지도 쓰지 않아
                // 대시 벡터를 그대로 이어 붙이면 물 위로 튀어 오른다.
                CancelDodge();
                HandleSwimMove(h, v);
                return;
            }

            // [회피] 구르는 동안에는 WASD를 읽지 않는다(방향은 시작 시점에 고정).
            if (dodgeActiveTimer > 0f)
            {
                HandleDodgeMove();
                return;
            }

            Vector3 move = (transform.right * h + transform.forward * v).normalized * speed;

            if (controller.isGrounded)
            {
                verticalVelocity.y = -1f;
                if (Input.GetButtonDown("Jump"))
                    verticalVelocity.y = jumpForce;
            }
            else
            {
                verticalVelocity.y += gravity * Time.deltaTime;
            }

            Vector3 finalMove = move + verticalVelocity;
            controller.Move(finalMove * Time.deltaTime);
        }

        /// <summary>
        /// 수면 아래(바다)에 있을 때의 이동을 처리한다. 중력 대신 부력이 작용해 가만히 있으면 서서히
        /// 수면 쪽으로 떠오르고, 점프 키로 위로, diveKey로 더 깊이 잠수할 수 있다.
        /// </summary>
        private void HandleSwimMove(float h, float v)
        {
            // 오리발(소지 패시브) 보정. 캐시 갱신은 인벤토리 변경 이벤트에서만 일어난다(RefreshPassiveEquipment).
            // 부력(buoyancy)은 "입력 없이 떠오르는" 수동적인 움직임이라 이동 속도 보정 대상에서 제외한다.
            float finMultiplier = hasSwimFins && finSwimSpeedMultiplier > 0f ? finSwimSpeedMultiplier : 1f;

            Vector3 horizontalMove = (transform.right * h + transform.forward * v).normalized * (swimSpeed * finMultiplier);

            float verticalInput = 0f;
            if (Input.GetButton("Jump"))
                verticalInput += 1f;
            if (Input.GetKey(diveKey) || Input.GetKey(diveKeyAlt))
                verticalInput -= 1f;

            // 입력이 없으면 부력으로 서서히 떠오르고, 입력이 있으면 그 방향으로 움직인다.
            float verticalSpeed = verticalInput != 0f ? verticalInput * swimVerticalSpeed * finMultiplier : buoyancy;

            Vector3 finalMove = horizontalMove + Vector3.up * verticalSpeed;
            controller.Move(finalMove * Time.deltaTime);

            // 수영 중에는 중력 누적을 초기화해, 물 밖으로 나가는 순간 급격히 낙하하지 않게 한다.
            verticalVelocity = Vector3.zero;
        }
    }
}
