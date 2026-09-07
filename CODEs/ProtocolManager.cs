using UnityEngine;

public class ProtocolManager : MonoBehaviour
{
    // 싱글톤 인스턴스: 어디서든 ProtocolManager.Instance로 접근 가능합니다.
    public static ProtocolManager Instance { get; private set; }

    [Header("Connections")]
    public SocketServer socketServer;    // 유니티 서버 스크립트 연결
    public RobotController robotController; // 로봇 컨트롤러 스크립트 연결

    private void Awake()
    {
        // 싱글톤 설정
        if (Instance == null)
        {
            Instance = this;
            // 씬이 바뀌어도 파괴되지 않도록 설정 (필요 시)
            // DontDestroyOnLoad(gameObject); 
        }
        else
        {
            Destroy(gameObject);
        }
    }

    /// <summary>
    /// [INPUT] SocketServer가 파이썬으로부터 데이터를 받았을 때 호출합니다.
    /// </summary>
    public void OnReceiveCommand(string rawData)
    {
        // 1. 하단 로그창 기록 (파란색)
        // UILogManager의 필터인 '['와 ']'를 포함합니다.
        Debug.Log($"<color=#42A5F5>[USER REQUEST] {rawData}</color>");

        // 2. 명령 종류에 따라 처리 방식 분기
        //    Scan/Status/Charge는 센서·상태 조회라서 이동 큐를 기다리지 않고 즉시 처리합니다.
        string action = rawData.Split(':')[0];
        if (action == "Scan" || action == "Status" || action == "Charge")
        {
            if (robotController != null)
            {
                robotController.HandleImmediateCommand(action);
            }
        }
        else
        {
            // Move/Turn/LED/MoveTo는 기존처럼 순차 명령 큐에 전달
            RobotController.commandQueue.Enqueue(rawData);
        }
    }

    /// <summary>
    /// [RESPONSE] 로봇의 상태나 명령 처리 결과를 파이썬으로 보낼 때 호출합니다.
    /// </summary>
    public void SendResponse(string message)
    {
        string formattedMsg = $"[ROBOT ACK] {message}";

        // 1. 하단 로그창 기록 (초록색)
        Debug.Log($"<color=#66BB6A>{formattedMsg}</color>");

        // 2. 소켓을 통해 파이썬 클라이언트에게 전송
        if (socketServer != null)
        {
            socketServer.SendMessageToClient(formattedMsg);
        }
    }

    /// <summary>
    /// [WARNING] 충돌이나 시스템 에러 발생 시 호출합니다.
    /// </summary>
    public void SendWarning(string warningMsg)
    {
        string formattedMsg = $"[SYSTEM ALERT] {warningMsg}";

        // 1. 하단 로그창 기록 (빨간색)
        Debug.Log($"<color=#EF5350>{formattedMsg}</color>");

        // 2. 소켓을 통해 파이썬 클라이언트에게 즉시 경고 전송
        if (socketServer != null)
        {
            socketServer.SendMessageToClient(formattedMsg);
        }
    }
}