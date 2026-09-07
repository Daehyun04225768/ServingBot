using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// v1.1.0 — 명령을 agentId 기준으로 RobotRegistry에서 조회한 RobotController로 라우팅합니다.
/// 센서/상태 조회류(ImmediateActions)는 로봇의 이동 큐 상태와 무관하게 즉시 큐로 전달되고,
/// 그 외(Move/Turn/MoveTo/Patrol 등)는 기존처럼 순차 이동 큐로 전달됩니다.
/// </summary>
public class ProtocolManager : MonoBehaviour
{
    public static ProtocolManager Instance { get; private set; }

    [Header("Connections")]
    public SocketServer socketServer;

    private static readonly HashSet<string> ImmediateActions = new HashSet<string>
    {
        "Scan", "Status", "Charge",
        "ScanLidar", "ScanUltrasonic",
        "IsObstacleNear", "IsCharged", "HasDetected", "GetAlertLevel", "GetBattery",
        "GetPosition", "GetDirection",
        "GetCameraView", "GetNearbyObjects", "FindObject",
        "ChargeAtStation", "Display", "Speak", "PlaySound",
        "GetExploredMap", "GetRouteDistance", "NavigateAuto",
        "GetEventHistory", "GetLog", "SetBatteryLowThreshold", "SetObstacleThreshold",
        "Sweep", "IsMoving", "Stop"
    };

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    public void OnClientConnected(int agentId)
    {
        Debug.Log($"<color=#42A5F5>[USER REQUEST] Connect:{agentId}</color>");
        SendResponse(agentId, $"Connected as agent {agentId}");
    }

    public void OnReceiveCommand(int agentId, string rawData)
    {
        Debug.Log($"<color=#42A5F5>[USER REQUEST] (agent {agentId}) {rawData}</color>");

        RobotController robot = RobotRegistry.Get(agentId);
        if (robot == null)
        {
            SendResponse(agentId, $"[ERROR] agent {agentId} 로봇을 찾을 수 없습니다.");
            return;
        }

        string action = rawData.Split(':')[0];
        if (ImmediateActions.Contains(action))
        {
            robot.immediateQueue.Enqueue(rawData);
        }
        else
        {
            robot.commandQueue.Enqueue(rawData);
        }
    }

    public void SendResponse(int agentId, string message)
    {
        string formattedMsg = $"[ROBOT ACK] {message}";
        Debug.Log($"<color=#66BB6A>{formattedMsg}</color>");
        if (socketServer != null) socketServer.SendMessageToClient(agentId, formattedMsg);
    }

    public void SendWarning(int agentId, string warningMsg)
    {
        string formattedMsg = $"[SYSTEM ALERT] {warningMsg}";
        Debug.Log($"<color=#EF5350>{formattedMsg}</color>");
        if (socketServer != null) socketServer.SendMessageToClient(agentId, formattedMsg);
    }

    /// <summary>
    /// v1.1.0 — 요청 없이도 시뮬레이터가 스스로 밀어보내는 이벤트(예: 배터리 부족 감지).
    /// on_battery_low(callback) 같은 콜백 패턴은 파이썬 쪽에서 이 메시지를 수신 대기하는 방식으로 구현됩니다.
    /// </summary>
    public void SendEvent(int agentId, string eventMsg)
    {
        string formattedMsg = $"[EVENT] {eventMsg}";
        Debug.Log($"<color=#FFA726>{formattedMsg}</color>");
        if (socketServer != null) socketServer.SendMessageToClient(agentId, formattedMsg);
    }
}
