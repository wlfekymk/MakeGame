using UnityEngine;

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

        [Tooltip("잠수(더 깊이 내려가기) 키")]
        public KeyCode diveKey = KeyCode.LeftControl;

        // [0.2.22 사용자 보고 "잠수 키가 설정 안되어 있어"] 씬에는 diveKey=LeftControl(306)이
        // 멀쩡히 직렬화돼 있었지만 실기에서 잠수가 안 된다는 보고가 있었다(원인 미재현 - Ctrl이
        // 다른 프로그램/OS 조합에 먹혔을 가능성). 재현 불가 상황의 견고책으로 보조 키를 둔다.
        // 새 직렬화 필드는 기존 씬에 값이 없으므로 이 코드 기본값(LeftShift)이 그대로 적용된다.
        [Tooltip("보조 잠수 키 (주 키와 어느 쪽이든 동작)")]
        public KeyCode diveKeyAlt = KeyCode.LeftShift;

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

        // 커서 잠금 상태가 바뀐 프레임을 알아보기 위한 직전 상태와, 그때 회전을 건너뛸 남은 프레임 수.
        private bool lookCursorWasLocked;
        private int lookSettleCounter;

        /// <summary>필요한 컴포넌트 참조를 캐싱하고, 카메라 흔들림 컴포넌트를 붙인다.</summary>
        private void Awake()
        {
            controller = GetComponent<CharacterController>();
            lookCursorWasLocked = Cursor.lockState == CursorLockMode.Locked;
            lookSettleCounter = Mathf.Max(0, lookSettleFrames);
            EnsureCameraShake();
            HookPassiveEquipment();
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
            HandleMove();
            PushAirPocketState();
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
        /// WASD 입력으로 수평 이동을 처리하고, 중력/점프로 수직 이동을 처리한다.
        /// 골절 상태이면 이동 속도가 느려진다. 해수면보다 낮은 위치(섬 밖 바다)에서는 수영 모드로 전환된다.
        /// </summary>
        private void HandleMove()
        {
            float speed = moveSpeed;
            if (survivalStats != null && survivalStats.hasBrokenBone)
                speed *= brokenBoneSpeedMultiplier;

            float h = Input.GetAxis("Horizontal");
            float v = Input.GetAxis("Vertical");

            if (transform.position.y < waterLevel)
            {
                HandleSwimMove(h, v);
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
