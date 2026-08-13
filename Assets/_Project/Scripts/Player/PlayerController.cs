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

        [Tooltip("마우스 좌우/상하 회전 감도")]
        public float lookSensitivity = 2f;

        [Tooltip("시점 카메라 (상하 회전은 이 카메라에만 적용한다)")]
        public Transform cameraTransform;

        [Tooltip("이동 속도 판정에 사용할 생존 수치 컴포넌트 (골절 시 감속 등)")]
        public SurvivalStats survivalStats;

        private CharacterController controller;
        private Vector3 verticalVelocity;
        private float cameraPitch = 0f;

        /// <summary>필요한 컴포넌트 참조를 캐싱한다.</summary>
        private void Awake()
        {
            controller = GetComponent<CharacterController>();
        }

        /// <summary>
        /// 매 프레임 입력을 받아 시점 회전과 이동을 처리한다.
        /// </summary>
        private void Update()
        {
            HandleLook();
            HandleMove();
        }

        /// <summary>
        /// 마우스 입력으로 좌우(플레이어 몸체)/상하(카메라) 시점을 회전시킨다.
        /// </summary>
        private void HandleLook()
        {
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
            Vector3 horizontalMove = (transform.right * h + transform.forward * v).normalized * swimSpeed;

            float verticalInput = 0f;
            if (Input.GetButton("Jump"))
                verticalInput += 1f;
            if (Input.GetKey(diveKey))
                verticalInput -= 1f;

            // 입력이 없으면 부력으로 서서히 떠오르고, 입력이 있으면 그 방향으로 움직인다.
            float verticalSpeed = verticalInput != 0f ? verticalInput * swimVerticalSpeed : buoyancy;

            Vector3 finalMove = horizontalMove + Vector3.up * verticalSpeed;
            controller.Move(finalMove * Time.deltaTime);

            // 수영 중에는 중력 누적을 초기화해, 물 밖으로 나가는 순간 급격히 낙하하지 않게 한다.
            verticalVelocity = Vector3.zero;
        }
    }
}
