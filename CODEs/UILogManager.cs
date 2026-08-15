using UnityEngine;
using TMPro;
using UnityEngine.UI;
using System.Collections.Concurrent;

public class UILogManager : MonoBehaviour
{
    public TextMeshProUGUI logText;
    public ScrollRect scrollRect;
    private const int MaxLines = 30; 

    private ConcurrentQueue<string> logQueue = new ConcurrentQueue<string>();

    void OnEnable()
    {
        // 스레드에 안전하게 모든 로그를 수집합니다.
        Application.logMessageReceivedThreaded += HandleLog;
    }

    void OnDisable()
    {
        Application.logMessageReceivedThreaded -= HandleLog;
    }

    void HandleLog(string logString, string stackTrace, LogType type)
    {
        // 1. 시스템 경고(Warning)와 에러는 무시하여 로그창 오염 방지
        if (type == LogType.Warning || type == LogType.Error) return;

        // 2. [ ] 대괄호가 포함된 '사용자 정의 로그'만 UI에 표시
        // ProtocolManager에서 보낸 [USER REQUEST], [ROBOT ACK] 등이 모두 포함됩니다.
        if (logString.Contains("[") && logString.Contains("]"))
        {
            logQueue.Enqueue($"[{System.DateTime.Now:HH:mm:ss}] {logString}");
        }
    }

    void Update()
    {
        // 메인 스레드에서 큐에 쌓인 로그를 하나씩 UI에 업데이트
        while (logQueue.TryDequeue(out string message))
        {
            UpdateUIText(message);
        }
    }

    void UpdateUIText(string message)
    {
        logText.text += "\n" + message;

        // 줄 수 제한 로직 (성능 유지)
        string[] lines = logText.text.Split('\n');
        if (lines.Length > MaxLines)
        {
            logText.text = string.Join("\n", lines, lines.Length - MaxLines, MaxLines);
        }

        // 스크롤을 항상 가장 아래로 유지
        Canvas.ForceUpdateCanvases();
        scrollRect.verticalNormalizedPosition = 0f;
    }
}