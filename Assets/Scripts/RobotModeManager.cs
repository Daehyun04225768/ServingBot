using UnityEngine;

/// <summary>
/// v1.1.0 — 우상단 [로봇 모드] 드롭다운. 싱글(스마트로봇 운용과 활용) / 듀얼(피지컬 AI와 로봇 제어)을 전환합니다.
/// 드롭다운의 OnValueChanged(Int32)에 SetMode를 연결하세요 (0=Single, 1=Dual).
/// </summary>
public class RobotModeManager : MonoBehaviour
{
    public enum Mode { Single = 0, Dual = 1 }

    [Header("References")]
    public GameObject robot1;   // agentId=1 — 항상 활성
    public GameObject robot2;   // agentId=2 — 듀얼 모드에서만 활성

    public Mode currentMode = Mode.Single;

    void Start()
    {
        ApplyMode();
    }

    public void SetMode(int modeIndex)
    {
        currentMode = (Mode)modeIndex;
        ApplyMode();
    }

    // 체크박스 없는 "글자 버튼"(Toggle) UI용 오버로드 — false=Single, true=Dual
    public void SetMode(bool isDual)
    {
        SetMode(isDual ? 1 : 0);
    }

    private void ApplyMode()
    {
        if (robot1 != null) robot1.SetActive(true);
        if (robot2 != null) robot2.SetActive(currentMode == Mode.Dual);
    }
}
