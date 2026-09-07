using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// v1.1.0 — 좌측 상단 [SERVING BOT SYSTEM] 배너 아래에 로봇 위치/배터리/충돌 정보를 표시.
/// 듀얼 모드일 때는 robot2 정보까지 함께 표시합니다.
/// </summary>
public class RobotStatusHUD : MonoBehaviour
{
    public Text label;
    public RobotController robot1;
    public RobotController robot2;
    public RobotModeManager modeManager;

    void Update()
    {
        if (label == null || robot1 == null) return;

        string text = FormatRobot("로봇1", robot1);

        bool dualActive = modeManager != null
            && modeManager.currentMode == RobotModeManager.Mode.Dual
            && robot2 != null
            && robot2.gameObject.activeInHierarchy;

        if (dualActive)
            text += "\n" + FormatRobot("로봇2", robot2);

        label.text = text;
    }

    private string FormatRobot(string name, RobotController r)
    {
        Vector3 p = r.transform.position;
        return $"{name}  위치({p.x:F1}, {p.z:F1})  배터리 {r.batteryLevel:F0}%  충돌 {r.LastCollision}";
    }
}
