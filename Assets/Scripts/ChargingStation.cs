using UnityEngine;

/// <summary>
/// v1.1.0 — 충전소 마커 컴포넌트. Detectable 태그가 붙은 오브젝트에 함께 붙이면
/// get_camera_view()/get_nearby_objects()가 이 오브젝트를 "charging_station" 타입으로 인식하고,
/// charge_at_station()이 반경 판정 대상으로 사용합니다.
/// 기본 배치 좌표(x:0, z:0)는 씬 에디터에서 이 오브젝트의 Transform으로 설정합니다.
/// </summary>
public class ChargingStation : MonoBehaviour
{
}
