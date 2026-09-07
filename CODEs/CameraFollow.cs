using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    [Header("Target Settings")]
    public Transform target;        // 따라갈 로봇의 Transform
    public Vector3 offset = new Vector3(0, 5, -7); // 로봇과의 상대적 거리 (높이 5, 뒤로 7)

    [Header("Smooth Settings")]
    public float smoothSpeed = 0.125f; // 따라가는 속도 (낮을수록 부드러움)
    
    private Vector3 velocity = Vector3.zero;

    void LateUpdate()
    {
        if (target == null) return;

        // 1. 목표 위치 계산 (로봇 위치 + 오프셋)
        Vector3 desiredPosition = target.position + offset;

        // 2. 부드러운 이동 (SmoothDamp 사용)
        transform.position = Vector3.SmoothDamp(transform.position, desiredPosition, ref velocity, smoothSpeed);

        // 3. 카메라가 항상 로봇을 바라보게 설정
        transform.LookAt(target);
    }
}