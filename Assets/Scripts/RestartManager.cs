using UnityEngine;

/// <summary>
/// v1.1.0 — 좌하단 [재시작] 버튼 / 단축키 F5. 등록된 모든 로봇을 초기 위치·배터리 100%·LED OFF·로그 초기화 상태로 되돌립니다.
/// 실행 중인 파이썬 스크립트 자체는 종료하지 않습니다(문서 명시 사항).
/// </summary>
public class RestartManager : MonoBehaviour
{
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.F5))
        {
            RestartAll();
        }
    }

    // 좌하단 [재시작] 버튼의 OnClick에 연결
    public void RestartAll()
    {
        foreach (var robot in RobotRegistry.All())
        {
            robot.ResetToInitialState();
        }
        Debug.Log("[RESTART] 모든 로봇 초기화 완료.");
    }
}
