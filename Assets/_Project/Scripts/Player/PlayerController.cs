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
        /// 골절 상태이면 이동 속도가 느려진다.
        /// </summary>
        private void HandleMove()
        {
            float speed = moveSpeed;
            if (survivalStats != null && survivalStats.hasBrokenBone)
                speed *= brokenBoneSpeedMultiplier;

            float h = Input.GetAxis("Horizontal");
            float v = Input.GetAxis("Vertical");
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
    }
}
