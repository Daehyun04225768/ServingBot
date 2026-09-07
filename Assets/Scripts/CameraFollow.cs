using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    public enum ViewMode { Single = 0, Dual = 1 }

    [Header("Target Settings")]
    public Transform target;        // 싱글 모드: 따라갈 로봇(robot1)의 Transform
    public Vector3 offset = new Vector3(0, 5, -7); // 로봇과의 상대적 거리 (높이 5, 뒤로 7)

    [Header("Dual Mode (듀얼 모드 전용 — 두 로봇을 함께 담는 고정 각도 3인칭 쿼터뷰)")]
    public Transform target2;       // 듀얼 모드에서 함께 담을 두 번째 로봇(robot2)
    public Vector3 dualOffset = new Vector3(8, 7, -8); // 두 로봇 중간 지점 기준 오프셋 — 이 방향이 곧 "고정 각도"를 결정
    public ViewMode mode = ViewMode.Single;

    [Header("Smooth Settings")]
    public float smoothSpeed = 0.125f; // 위치가 따라가는 속도 (낮을수록 부드러움)
    public float rotationSmoothSpeed = 4f; // 회전 보간 속도 — 모드 전환 시 각도가 부드럽게 바뀌도록 함

    private Vector3 velocity = Vector3.zero;

    // 우상단 [로봇 모드] 드롭다운의 OnValueChanged(Int32)에 RobotModeManager.SetMode와 함께 연결 —
    // 0=Single(기존 추적 카메라 그대로), 1=Dual(고정 각도 3인칭 쿼터뷰로 부드럽게 전환)
    public void SetMode(int modeIndex)
    {
        mode = (ViewMode)modeIndex;
    }

    // 체크박스 없는 "글자 버튼"(Toggle) UI용 오버로드 — false=Single, true=Dual
    public void SetMode(bool isDual)
    {
        SetMode(isDual ? 1 : 0);
    }

    void LateUpdate()
    {
        if (target == null) return;

        Vector3 desiredPosition;
        Quaternion desiredRotation;

        if (mode == ViewMode.Dual && target2 != null)
        {
            // 두 로봇의 중간 지점을 고정된 오프셋/각도로 바라봅니다.
            // dualOffset은 상수이므로 로봇이 움직여도 "각도" 자체는 항상 동일하게 유지됩니다 (고정 각도).
            Vector3 midpoint = (target.position + target2.position) * 0.5f;
            desiredPosition = midpoint + dualOffset;
            desiredRotation = Quaternion.LookRotation(-dualOffset.normalized, Vector3.up);
        }
        else
        {
            desiredPosition = target.position + offset;
            desiredRotation = Quaternion.LookRotation(target.position - transform.position, Vector3.up);
        }

        transform.position = Vector3.SmoothDamp(transform.position, desiredPosition, ref velocity, smoothSpeed);
        transform.rotation = Quaternion.Slerp(transform.rotation, desiredRotation, rotationSmoothSpeed * Time.deltaTime);
    }
}
