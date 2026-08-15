using UnityEngine;
using TMPro;

public class ServingBotManager : MonoBehaviour
{
    [Header("References")]
    public RobotController robot; // 연결할 로봇
    public TextMeshProUGUI statusText; // 상단 HUD 텍스트

    void Update()
    {
        if (robot == null || statusText == null) return;

        // 로봇으로부터 데이터를 가져와서 UI 갱신
        UpdateHUD();
    }

    void UpdateHUD()
    {
        Vector3 pos = robot.transform.position;
        string ledStatus = robot.IsLEDOn ? "<color=green>ON</color>" : "<color=red>OFF</color>";
        
        statusText.text = $"<b>[SERVING BOT SYSTEM]</b>\n" +
                          $"Position: X:{pos.x:F2}, Z:{pos.z:F2}\n" +
                          $"Rotation: {robot.transform.eulerAngles.y:F1}°\n" +
                          $"Battery: {robot.batteryLevel:F0}%\n" +
                          $"LED: {ledStatus}\n" +
                          $"Collision: <color=yellow>{robot.LastCollision}</color>";
    }
}