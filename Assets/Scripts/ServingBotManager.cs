using UnityEngine;
using TMPro;

/// <summary>
/// v1.1.0 — 싱글/듀얼 모드 모두 대응하도록 로봇 1대 참조를 배열로 변경했습니다.
/// 씬 에디터의 Inspector에서 robots 배열에 로봇1(항상), 로봇2(듀얼 모드용)를 등록하세요.
/// 비활성화된(RobotModeManager가 꺼둔) 로봇은 HUD에 표시하지 않습니다.
/// </summary>
public class ServingBotManager : MonoBehaviour
{
    [Header("References")]
    public RobotController[] robots;
    public TextMeshProUGUI statusText;

    void Update()
    {
        if (robots == null || statusText == null) return;
        UpdateHUD();
    }

    void UpdateHUD()
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("<b>[SERVING BOT SYSTEM]</b>\n");

        foreach (var robot in robots)
        {
            if (robot == null || !robot.gameObject.activeInHierarchy) continue;

            Vector3 pos = robot.transform.position;
            string ledStatus = robot.IsLEDOn ? "<color=green>ON</color>" : "<color=red>OFF</color>";

            sb.Append($"--- Agent {robot.agentId} ---\n");
            sb.Append($"Position: X:{pos.x:F2}, Z:{pos.z:F2}\n");
            sb.Append($"Rotation: {robot.transform.eulerAngles.y:F1}°\n");
            sb.Append($"Battery: {robot.batteryLevel:F0}%\n");
            sb.Append($"LED: {ledStatus}\n");
            sb.Append($"Collision: <color=yellow>{robot.LastCollision}</color>\n");
        }

        statusText.text = sb.ToString();
    }
}
