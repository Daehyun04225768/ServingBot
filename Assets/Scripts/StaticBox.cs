using UnityEngine;

/// <summary>
/// v1.1.0 — 고정 장애물 마커 컴포넌트. get_camera_view()/get_nearby_objects()에서 "object" 타입으로
/// 분류되며, Collider 충돌 시 RobotController.OnCollisionEnter의 기존 [SYSTEM ALERT] 로직을 그대로 탑니다.
/// 씬 에디터에서 맵 내 3개를 기본 배치하고, 이후 자유롭게 추가·이동할 수 있습니다.
/// </summary>
public class StaticBox : MonoBehaviour
{
}
