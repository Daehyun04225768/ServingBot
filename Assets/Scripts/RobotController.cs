using UnityEngine;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;

public class RobotController : MonoBehaviour
{
    [Header("Agent Identity (싱글 모드=1, 듀얼 모드=1,2)")]
    public int agentId = 1;

    // v1.1.0: 기존 static 큐는 멀티 로봇 환경에서 다른 로봇의 명령과 뒤섞이는 문제가 있어 인스턴스 큐로 변경.
    public ConcurrentQueue<string> commandQueue = new ConcurrentQueue<string>();

    // v1.1.0: 센서/상태 조회 전용 큐. 네트워크 스레드에서 곧바로 Unity API(Physics.Raycast 등)를
    // 호출하면 스레드 안전하지 않으므로, 여기 쌓아두고 메인 스레드 Update()에서 매 프레임 전부 처리합니다.
    public ConcurrentQueue<string> immediateQueue = new ConcurrentQueue<string>();

    [Header("Movement Settings")]
    public float moveSpeed = 5.0f;
    public float turnSpeed = 120.0f;
    public float smoothTime = 0.1f;
    [Range(0.1f, 2.0f)] public float speedRatio = 1.0f;   // v1.1.0: set_speed()
    private float activeMoveSpeedRatio = 1.0f;             // v1.1.0(7주차): move(distance, speed=1.0) — 이번 이동에만 적용, 기본은 speedRatio를 따름

    [Header("Precision Settings")]
    public float distanceThreshold = 0.01f;
    public float angleThreshold = 0.1f;

    [Header("Sensor Settings")]
    public float scanRange = 20f;
    public float detectRadius = 15f;
    public string detectableTag = "Detectable";
    public float ultrasonicRange = 3f;              // v1.1.0
    public float ultrasonicWarningDistance = 1.0f;  // v1.1.0
    public float cameraFov = 60f;                   // v1.1.0: get_camera_view 판정 시야각
    public float cameraRange = 10f;                 // v1.1.0

    [Header("Battery Settings")]
    public float batteryLevel = 100f;
    public float batteryDrainPerDistance = 0.5f;
    private float batteryLowThreshold = -1f;   // v1.1.0: SetBatteryLowThreshold (음수면 비활성)
    private bool batteryLowAlreadyFired = false;
    private float obstacleWarnThreshold = -1f; // v1.1.0: SetObstacleThreshold (음수면 비활성) — on_obstacle(threshold, callback) 지원
    private bool obstacleWarnAlreadyFired = false;

    [Header("Rotation Lock (특정 주차 실습용)")]
    public bool rotationLocked = false;

    [Header("Speech Bubble (display()/speak() 텍스트 표시)")]
    public SpeechBubble speechBubble; // 씬에서 이 로봇 전용 말풍선 오브젝트를 연결 (비워두면 시각 표시 생략)

    [Header("Charging LED (충전 중 파란색 점멸)")]
    public Renderer ledRenderer;                 // LED 머티리얼을 가진 Renderer — 씬에서 연결(비워두면 시각 효과 생략)
    public Color ledNormalColor = Color.white;
    public Color ledChargingColor = Color.blue;
    public float chargingBlinkInterval = 0.3f;
    public float chargingVisualDuration = 2f;    // charge() 호출 후 점멸을 유지할 연출 시간(초)
    private float chargingVisualTimer = 0f;

    private Vector3 initialPosition;
    private Quaternion initialRotation;

    private Vector3 targetPosition;
    private Quaternion targetRotation;
    private Vector3 moveVelocity = Vector3.zero;
    private Rigidbody rb;

    public bool IsLEDOn { get; private set; }
    public string LastCollision { get; private set; } = "None";

    // v1.1.0: 11주차 SLAM 체험용 — 이동하며 지나간 좌표 / 스캔 중 감지된 장애물 좌표
    private readonly HashSet<Vector2Int> visitedCells = new HashSet<Vector2Int>();
    private readonly HashSet<Vector2Int> obstacleCells = new HashSet<Vector2Int>();
    private const float CellSize = 1f;

    // [탐색 맵] 토글 오버레이(ExploredMapOverlay)에서 격자를 그릴 때 사용하는 읽기 전용 접근자
    public IReadOnlyCollection<Vector2Int> VisitedCells => visitedCells;
    public IReadOnlyCollection<Vector2Int> ObstacleCells => obstacleCells;

    // v1.1.0: 7주차(CSV) + 9/10주차(get_log/get_event_history) 이벤트 로그
    private EventLogger eventLogger;
    public bool csvLoggingEnabled = false;

    void Start()
    {
        rb = GetComponent<Rigidbody>();

        string[] args = System.Environment.GetCommandLineArgs();
        foreach (string arg in args)
        {
            if (arg.StartsWith("-rotationLocked="))
            {
                bool.TryParse(arg.Substring("-rotationLocked=".Length), out rotationLocked);
            }
            else if (arg.StartsWith("-agentId="))
            {
                int.TryParse(arg.Substring("-agentId=".Length), out agentId);
            }
        }

        RobotRegistry.Register(agentId, this);
        eventLogger = new EventLogger(agentId);

        initialPosition = transform.position;
        initialRotation = transform.rotation;
        targetPosition = transform.position;
        targetRotation = transform.rotation;
        activeMoveSpeedRatio = speedRatio;

        if (rb != null)
        {
            rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
        }

        MarkVisitedCell(transform.position);
    }

    void Update()
    {
        // 센서/상태 조회는 이동 상태와 무관하게 매 프레임 모두 처리 (메인 스레드에서만 Unity API 호출)
        while (immediateQueue.TryDequeue(out string immediateCmd))
        {
            HandleImmediateCommand(immediateCmd);
        }

        float dist = Vector3.Distance(transform.position, targetPosition);
        float angle = Quaternion.Angle(transform.rotation, targetRotation);

        if (dist < distanceThreshold && angle < angleThreshold)
        {
            if (commandQueue.TryDequeue(out string command))
            {
                ProcessCommand(command);
            }
        }

        float effectiveMoveSpeed = moveSpeed * activeMoveSpeedRatio;
        transform.position = Vector3.SmoothDamp(transform.position, targetPosition, ref moveVelocity, smoothTime, effectiveMoveSpeed);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, turnSpeed * speedRatio * Time.deltaTime);

        MarkVisitedCell(transform.position);
        CheckBatteryLowThreshold();
        CheckObstacleThreshold();
        UpdateChargingBlink();
    }

    // ================= 순차(이동) 명령 =================

    void ProcessCommand(string cmd)
    {
        string[] parts = cmd.Split(new char[] { ':' }, 2);
        string action = parts[0];
        string arg = parts.Length > 1 ? parts[1] : null;

        switch (action)
        {
            case "Move": HandleMove(arg); break;
            case "MoveSafe": HandleMove(arg); break;          // v1.1.0(10주차): 충돌 시 [SYSTEM ALERT] 발생 — 예외 변환은 파이썬 측 담당
            case "Turn": HandleTurn(arg); break;
            case "MoveTo": HandleMoveTo(arg); break;
            case "MoveCurve": HandleMoveCurve(arg); break;     // v1.1.0(6주차): 회전 없이 곡선 궤적 이동
            case "LED": HandleLed(arg); break;
            case "SetSpeed": HandleSetSpeed(arg); break;       // v1.1.0(6주차)
            case "MoveRepeat": HandleMoveRepeat(arg); break;   // v1.1.0(7주차)
            case "Patrol": HandlePatrol(arg); break;           // v1.1.0(7주차)
            default:
                Debug.Log($"[WARN] 알 수 없는 이동 명령: {cmd}");
                break;
        }
    }

    // "Move:distance" 또는 "Move:distance,speed" — speed 생략 시 set_speed()로 설정한 전역 배율을 그대로 사용하고,
    // 지정하면 이번 한 번의 이동에만 적용됩니다(함수 기본값 파라미터 학습용, v1.1.0 7주차).
    void HandleMove(string arg)
    {
        if (arg == null) return;
        string[] parts = arg.Split(',');
        if (!float.TryParse(parts[0], out float value)) return;

        activeMoveSpeedRatio = speedRatio;
        if (parts.Length > 1 && float.TryParse(parts[1], out float perCallSpeed))
        {
            activeMoveSpeedRatio = Mathf.Clamp(perCallSpeed, 0.1f, 2.0f);
        }

        targetPosition += transform.forward * value;
        DrainBattery(Mathf.Abs(value));
        LogEvent("MOVE", value.ToString("F1"));
        ProtocolManager.Instance.SendResponse(agentId, $"이동 명령 접수: {value} (목표 지점 갱신, 배터리 {batteryLevel:F0}%)");
    }

    void HandleTurn(string arg)
    {
        if (arg == null || !float.TryParse(arg, out float value)) return;

        if (rotationLocked)
        {
            ProtocolManager.Instance.SendResponse(agentId, "{\"error\": \"ROTATION_LOCKED\"}");
            return;
        }

        targetRotation *= Quaternion.Euler(0, value, 0);
        LogEvent("TURN", value.ToString("F1"));
        ProtocolManager.Instance.SendResponse(agentId, $"회전 명령 접수: {value} (목표 각도 갱신)");
    }

    void HandleMoveTo(string arg)
    {
        if (arg == null) return;
        string[] coords = arg.Split(',');
        if (coords.Length < 2) return;
        if (!float.TryParse(coords[0], out float x) || !float.TryParse(coords[1], out float z)) return;

        activeMoveSpeedRatio = speedRatio; // Move(speed=)의 1회성 오버라이드가 다른 이동 명령에 새어나가지 않도록 리셋
        Vector3 destination = new Vector3(x, transform.position.y, z);
        float dist = Vector3.Distance(transform.position, destination);

        Vector3 direction = destination - transform.position;
        if (direction != Vector3.zero)
        {
            targetRotation = Quaternion.LookRotation(direction);
        }
        targetPosition = destination;

        DrainBattery(dist);
        LogEvent("MOVE_TO", $"{x:F1},{z:F1}");
        ProtocolManager.Instance.SendResponse(agentId, $"목표 좌표 이동 명령 접수: ({x:F1}, {z:F1}) (배터리 {batteryLevel:F0}%)");
    }

    // MoveTo와 달리 targetRotation을 건드리지 않아 회전 없이(현재 방향을 유지한 채) 좌표로 이동합니다.
    void HandleMoveCurve(string arg)
    {
        if (arg == null) return;
        string[] coords = arg.Split(',');
        if (coords.Length < 2) return;
        if (!float.TryParse(coords[0], out float x) || !float.TryParse(coords[1], out float z)) return;

        activeMoveSpeedRatio = speedRatio; // Move(speed=)의 1회성 오버라이드가 다른 이동 명령에 새어나가지 않도록 리셋
        Vector3 destination = new Vector3(x, transform.position.y, z);
        float dist = Vector3.Distance(transform.position, destination);
        targetPosition = destination;

        DrainBattery(dist);
        LogEvent("MOVE_CURVE", $"{x:F1},{z:F1}");
        ProtocolManager.Instance.SendResponse(agentId, $"곡선 경로 이동 명령 접수: ({x:F1}, {z:F1}) (회전 없음, 배터리 {batteryLevel:F0}%)");
    }

    void HandleLed(string arg)
    {
        if (arg == null || !float.TryParse(arg, out float value)) return;
        IsLEDOn = value > 0;
        LogEvent(IsLEDOn ? "LED_ON" : "LED_OFF", "true");
        ProtocolManager.Instance.SendResponse(agentId, $"LED 상태 변경: {(IsLEDOn ? "ON" : "OFF")}");
    }

    void HandleStop()
    {
        while (commandQueue.TryDequeue(out _)) { }
        targetPosition = transform.position;
        targetRotation = transform.rotation;
        moveVelocity = Vector3.zero;
        ProtocolManager.Instance.SendResponse(agentId, "정지 완료. 대기 중이던 명령 전체 취소.");
    }

    void HandleSetSpeed(string arg)
    {
        if (arg == null || !float.TryParse(arg, out float value)) return;
        speedRatio = Mathf.Clamp(value, 0.1f, 2.0f);
        ProtocolManager.Instance.SendResponse(agentId, $"속도 배율 설정: {speedRatio:F2}");
    }

    // "MoveRepeat:distance,count" → count번의 Move 명령으로 펼쳐서 큐에 순서대로 적재
    void HandleMoveRepeat(string arg)
    {
        if (arg == null) return;
        string[] p = arg.Split(',');
        if (p.Length < 2) return;
        if (!float.TryParse(p[0], out float distance) || !int.TryParse(p[1], out int count)) return;

        for (int i = 0; i < count; i++)
        {
            commandQueue.Enqueue($"Move:{distance}");
        }
        ProtocolManager.Instance.SendResponse(agentId, $"반복 이동 등록: {distance}m x {count}회");
    }

    // "Patrol:x1,z1|x2,z2|..." → 각 좌표를 MoveTo로 펼쳐서 입력 순서 그대로 큐에 적재
    void HandlePatrol(string arg)
    {
        if (arg == null) return;
        foreach (var point in ParseWaypoints(arg))
        {
            commandQueue.Enqueue($"MoveTo:{point.x:F2},{point.y:F2}");
        }
        ProtocolManager.Instance.SendResponse(agentId, $"순찰 경로 등록: {arg}");
    }

    private static List<Vector2> ParseWaypoints(string arg)
    {
        var list = new List<Vector2>();
        foreach (var token in arg.Split('|'))
        {
            var xy = token.Split(',');
            if (xy.Length < 2) continue;
            if (float.TryParse(xy[0], out float x) && float.TryParse(xy[1], out float z))
            {
                list.Add(new Vector2(x, z));
            }
        }
        return list;
    }

    // ================= 즉시 처리 명령 (센서/상태 조회 등) =================

    public void HandleImmediateCommand(string rawData)
    {
        string[] parts = rawData.Split(new char[] { ':' }, 2);
        string action = parts[0];
        string arg = parts.Length > 1 ? parts[1] : null;

        switch (action)
        {
            case "Scan": PerformScan(); break;
            case "Status": ReportStatus(); break;
            case "Charge": PerformCharge(); break;
            case "ScanLidar": PerformScanLidar(); break;                     // v1.1.0(6주차)
            case "ScanUltrasonic": PerformScanUltrasonic(); break;           // v1.1.0(6주차)
            case "IsObstacleNear": ReportIsObstacleNear(arg); break;         // v1.1.0(6주차)
            case "IsCharged": ReportIsCharged(arg); break;                   // v1.1.0(6주차)
            case "HasDetected": ReportHasDetected(arg); break;               // v1.1.0(6주차)
            case "GetAlertLevel": ReportAlertLevel(); break;                 // v1.1.0(6주차)
            case "GetBattery": ProtocolManager.Instance.SendResponse(agentId, $"[BATTERY] {batteryLevel:F1}"); break;
            case "GetPosition": ProtocolManager.Instance.SendResponse(agentId, $"[POSITION] {transform.position.x:F2},{transform.position.z:F2}"); break; // v1.1.0(9주차)
            case "GetDirection": ProtocolManager.Instance.SendResponse(agentId, $"[DIRECTION] {GetCompassDirection()}"); break; // v1.1.0(9주차)
            case "GetCameraView": PerformCameraView(); break;                // v1.1.0(10주차)
            case "GetNearbyObjects": PerformNearbyObjects(); break;          // v1.1.0(10주차)
            case "FindObject": PerformFindObject(arg); break;                // v1.1.0(10주차)
            case "ChargeAtStation": PerformChargeAtStation(); break;         // v1.1.0(10주차)
            case "Display": ShowSpeechBubble(arg); ProtocolManager.Instance.SendResponse(agentId, $"[DISPLAY] {arg}"); break; // v1.1.0(10주차)
            case "Speak": ShowSpeechBubble(arg); ProtocolManager.Instance.SendResponse(agentId, $"[SPEAK] {arg}"); break;       // v1.1.0(10주차)
            case "PlaySound": ProtocolManager.Instance.SendResponse(agentId, $"[SOUND] {arg}"); break;   // v1.1.0(10주차)
            case "GetExploredMap": PerformExploredMap(); break;              // v1.1.0(11주차)
            case "GetRouteDistance": PerformRouteDistance(arg); break;       // v1.1.0(11주차)
            case "NavigateAuto": PerformNavigateAuto(arg); break;            // v1.1.0(11주차)
            case "GetEventHistory": PerformEventHistory(arg); break;         // v1.1.0(7주차)
            case "GetLog": ProtocolManager.Instance.SendResponse(agentId, $"[LOG] {eventLogger.AllAsJson()}"); break; // v1.1.0(7주차)
            case "Sweep": PerformSweep(arg); break;                         // v1.1.0(7주차)
            case "IsMoving": ReportIsMoving(); break;                        // v1.1.0(9주차)
            case "Stop": HandleStop(); break;                                // v1.1.0(6주차) — 이동 큐 게이트를 거치지 않고 즉시 처리해야 "즉시 정지"가 됨
            case "SetBatteryLowThreshold":
                if (float.TryParse(arg, out float pct)) { batteryLowThreshold = pct; batteryLowAlreadyFired = false; }
                ProtocolManager.Instance.SendResponse(agentId, $"배터리 경고 임계값 설정: {batteryLowThreshold}%");
                break;
            case "SetObstacleThreshold":                       // v1.1.0(9주차): on_obstacle(threshold, callback)
                if (float.TryParse(arg, out float obsThreshold)) { obstacleWarnThreshold = obsThreshold; obstacleWarnAlreadyFired = false; }
                ProtocolManager.Instance.SendResponse(agentId, $"장애물 근접 경고 임계값 설정: {obstacleWarnThreshold}m");
                break;
            default:
                Debug.Log($"[WARN] 알 수 없는 즉시 명령: {rawData}");
                break;
        }
    }

    /// <summary>
    /// 전방 장애물까지 거리(LiDAR 유사) + 주변 인식 대상 오브젝트(카메라 유사)를 탐색합니다. (v1.0.0 API — 하위 호환 유지)
    /// </summary>
    void PerformScan()
    {
        string obstacleStr = "None";
        if (Physics.Raycast(transform.position, transform.forward, out RaycastHit hit, scanRange))
        {
            obstacleStr = $"{hit.distance:F1}";
            MarkObstacleCell(hit.point);
        }

        string objectsInfo = "None";
        GameObject[] targets = GameObject.FindGameObjectsWithTag(detectableTag);
        if (targets.Length > 0)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            foreach (var obj in targets)
            {
                float dist = Vector3.Distance(transform.position, obj.transform.position);
                if (dist <= detectRadius)
                {
                    sb.Append($"{obj.name}(x:{obj.transform.position.x:F1},z:{obj.transform.position.z:F1},dist:{dist:F1}) ");
                }
            }
            if (sb.Length > 0) objectsInfo = sb.ToString().Trim();
        }

        LogEvent("SCAN_ULTRASONIC", obstacleStr);
        ProtocolManager.Instance.SendResponse(agentId, $"[SCAN] ObstacleDistance:{obstacleStr} | DetectedObjects:{objectsInfo}");
    }

    void ReportStatus()
    {
        string ledStatus = IsLEDOn ? "ON" : "OFF";
        string status = $"Position:({transform.position.x:F2},{transform.position.z:F2}) | Rotation:{transform.eulerAngles.y:F1} | Battery:{batteryLevel:F0}% | LED:{ledStatus} | LastCollision:{LastCollision}";
        ProtocolManager.Instance.SendResponse(agentId, $"[STATUS] {status}");
    }

    // v1.0.0부터 촬영된 기본 charge()는 하위 호환을 위해 위치 무관·즉시 100% 충전을 그대로 유지합니다.
    // 충전소 근접 조건이 걸린 버전은 10주차 신규 API인 PerformChargeAtStation()에서 별도로 제공합니다.
    void PerformCharge()
    {
        batteryLevel = 100f;
        batteryLowAlreadyFired = false;
        StartChargingBlink();
        LogEvent("CHARGE", "100");
        ProtocolManager.Instance.SendResponse(agentId, "배터리 충전 완료: 100%");
    }

    // ---- v1.1.0 6주차: LiDAR / 초음파 ----

    void PerformScanLidar()
    {
        var directions = new (string label, float angleOffset)[]
        {
            ("front", 0f), ("front_right", 45f), ("right", 90f),
            ("rear", 180f), ("left", -90f), ("front_left", -45f)
        };

        var readings = new List<string>();
        foreach (var d in directions)
        {
            Vector3 dir = Quaternion.Euler(0, d.angleOffset, 0) * transform.forward;
            string val = "None";
            if (Physics.Raycast(transform.position, dir, out RaycastHit hit, scanRange))
            {
                val = hit.distance.ToString("F1");
                MarkObstacleCell(hit.point);
            }
            readings.Add($"{d.label}:{val}");
        }

        GameObject[] targets = GameObject.FindGameObjectsWithTag(detectableTag);
        var objectEntries = new List<string>();
        foreach (var obj in targets)
        {
            float dist = Vector3.Distance(transform.position, obj.transform.position);
            if (dist <= detectRadius)
            {
                objectEntries.Add($"{obj.name}(x:{obj.transform.position.x:F1},z:{obj.transform.position.z:F1},dist:{dist:F1})");
            }
        }
        string objectsStr = objectEntries.Count > 0 ? string.Join(",", objectEntries) : "None";

        LogEvent("SCAN_LIDAR", string.Join("|", readings));
        ProtocolManager.Instance.SendResponse(agentId, $"[LIDAR] {string.Join("|", readings)} | detected_objects:{objectsStr}");
    }

    void PerformScanUltrasonic()
    {
        string distStr = "None";
        bool warning = false;
        if (Physics.Raycast(transform.position, transform.forward, out RaycastHit hit, ultrasonicRange))
        {
            distStr = hit.distance.ToString("F1");
            warning = hit.distance <= ultrasonicWarningDistance;
            MarkObstacleCell(hit.point);
        }
        LogEvent("SCAN_ULTRASONIC", distStr);
        ProtocolManager.Instance.SendResponse(agentId, $"[ULTRASONIC] distance:{distStr}|warning:{warning.ToString().ToLower()}");
    }

    void ReportIsObstacleNear(string arg)
    {
        float threshold = 1.0f;
        if (!string.IsNullOrEmpty(arg)) float.TryParse(arg, out threshold);

        bool near = Physics.Raycast(transform.position, transform.forward, out RaycastHit hit, scanRange) && hit.distance <= threshold;
        ProtocolManager.Instance.SendResponse(agentId, $"[IS_OBSTACLE_NEAR] {near.ToString().ToLower()}");
    }

    void ReportIsCharged(string arg)
    {
        float minPct = 80f;
        if (!string.IsNullOrEmpty(arg)) float.TryParse(arg, out minPct);
        bool charged = batteryLevel >= minPct;
        ProtocolManager.Instance.SendResponse(agentId, $"[IS_CHARGED] {charged.ToString().ToLower()}");
    }

    void ReportHasDetected(string name)
    {
        bool found = false;
        if (!string.IsNullOrEmpty(name))
        {
            foreach (var obj in GameObject.FindGameObjectsWithTag(detectableTag))
            {
                float dist = Vector3.Distance(transform.position, obj.transform.position);
                if (obj.name == name && dist <= detectRadius) { found = true; break; }
            }
        }
        ProtocolManager.Instance.SendResponse(agentId, $"[HAS_DETECTED] {found.ToString().ToLower()}");
    }

    void ReportAlertLevel()
    {
        string level = "safe";
        if (Physics.Raycast(transform.position, transform.forward, out RaycastHit hit, scanRange))
        {
            if (hit.distance < 1.0f) level = "danger";
            else if (hit.distance < 3.0f) level = "caution";
        }
        ProtocolManager.Instance.SendResponse(agentId, $"[ALERT_LEVEL] {level}");
    }

    // "Sweep:startAngle,endAngle,step" — 로봇 정면 기준 상대 각도 범위를 step 간격으로 훑으며 각 각도의 장애물 거리를 반환합니다. (v1.1.0 7주차: for range() 반복 스캔)
    void PerformSweep(string arg)
    {
        if (arg == null) { ProtocolManager.Instance.SendResponse(agentId, "[SWEEP] "); return; }
        string[] parts = arg.Split(',');
        if (parts.Length < 3) return;
        if (!float.TryParse(parts[0], out float startAngle)) return;
        if (!float.TryParse(parts[1], out float endAngle)) return;
        if (!float.TryParse(parts[2], out float step) || step <= 0f) return;

        var readings = new List<string>();
        for (float a = startAngle; a <= endAngle + 0.001f; a += step)
        {
            Vector3 dir = Quaternion.Euler(0, a, 0) * transform.forward;
            string val = "None";
            if (Physics.Raycast(transform.position, dir, out RaycastHit hit, scanRange))
            {
                val = hit.distance.ToString("F1");
                MarkObstacleCell(hit.point);
            }
            readings.Add($"{a:F0}:{val}");
        }

        LogEvent("SWEEP", string.Join("|", readings));
        ProtocolManager.Instance.SendResponse(agentId, $"[SWEEP] {string.Join("|", readings)}");
    }

    // robot.is_moving — 현재 이동/회전 목표에 도달하지 못한 상태인지 조회합니다. (v1.1.0 9주차)
    void ReportIsMoving()
    {
        float dist = Vector3.Distance(transform.position, targetPosition);
        float angle = Quaternion.Angle(transform.rotation, targetRotation);
        bool moving = dist >= distanceThreshold || angle >= angleThreshold;
        ProtocolManager.Instance.SendResponse(agentId, $"[IS_MOVING] {moving.ToString().ToLower()}");
    }

    void ShowSpeechBubble(string text)
    {
        if (speechBubble != null) speechBubble.Show(text);
    }

    string GetCompassDirection()
    {
        float y = transform.eulerAngles.y;
        if (y >= 315f || y < 45f) return "N";
        if (y < 135f) return "E";
        if (y < 225f) return "S";
        return "W";
    }

    // ---- v1.1.0 10주차: 카메라 뷰 / 딕셔너리형 오브젝트 조회 ----

    string ClassifyObject(GameObject obj)
    {
        if (obj.GetComponent<PersonNpc>() != null) return "person";
        if (obj.GetComponent<ChargingStation>() != null) return "charging_station";
        if (obj.GetComponent<RobotController>() != null) return "robot";
        return "object";
    }

    void PerformCameraView()
    {
        var entries = new List<string>();
        float nearestDist = -1f;

        foreach (var obj in GameObject.FindGameObjectsWithTag(detectableTag))
        {
            Vector3 toObj = obj.transform.position - transform.position;
            float dist = toObj.magnitude;
            if (dist > cameraRange) continue;

            float angle = Vector3.Angle(transform.forward, toObj);
            if (angle > cameraFov * 0.5f) continue;

            entries.Add($"{obj.name}|{ClassifyObject(obj)}|{dist:F1}|{angle:F0}");
            if (nearestDist < 0 || dist < nearestDist) nearestDist = dist;
        }

        string objectsStr = entries.Count > 0 ? string.Join(";", entries) : "None";
        string obstacleDistStr = nearestDist >= 0 ? nearestDist.ToString("F1") : "None";
        ProtocolManager.Instance.SendResponse(agentId, $"[CAMERA] objects:{objectsStr} | obstacle_distance:{obstacleDistStr}");
    }

    void PerformNearbyObjects()
    {
        var entries = new List<string>();
        foreach (var obj in GameObject.FindGameObjectsWithTag(detectableTag))
        {
            float dist = Vector3.Distance(transform.position, obj.transform.position);
            if (dist <= detectRadius)
            {
                entries.Add($"{obj.name}|{ClassifyObject(obj)}|{obj.transform.position.x:F1},{obj.transform.position.z:F1}|{dist:F1}");
            }
        }
        string objectsStr = entries.Count > 0 ? string.Join(";", entries) : "None";
        ProtocolManager.Instance.SendResponse(agentId, $"[NEARBY] {objectsStr}");
    }

    void PerformFindObject(string name)
    {
        if (!string.IsNullOrEmpty(name))
        {
            foreach (var obj in GameObject.FindGameObjectsWithTag(detectableTag))
            {
                if (obj.name == name)
                {
                    float dist = Vector3.Distance(transform.position, obj.transform.position);
                    ProtocolManager.Instance.SendResponse(agentId, $"[FIND] {obj.name}|{ClassifyObject(obj)}|{obj.transform.position.x:F1},{obj.transform.position.z:F1}|{dist:F1}");
                    return;
                }
            }
        }
        ProtocolManager.Instance.SendResponse(agentId, "[FIND] None");
    }

    T FindNearestComponent<T>(float maxRadius) where T : Component
    {
        T best = null;
        float bestDist = float.MaxValue;
        foreach (var c in FindObjectsByType<T>(FindObjectsSortMode.None))
        {
            float d = Vector3.Distance(transform.position, c.transform.position);
            if (d <= maxRadius && d < bestDist)
            {
                best = c;
                bestDist = d;
            }
        }
        return best;
    }

    void PerformChargeAtStation()
    {
        var station = FindNearestComponent<ChargingStation>(1.0f);
        if (station == null)
        {
            ProtocolManager.Instance.SendResponse(agentId, "[ERROR:NotAtStationError] 충전소 반경 1m 이내가 아닙니다.");
            return;
        }
        batteryLevel = 100f;
        batteryLowAlreadyFired = false;
        StartChargingBlink();
        LogEvent("CHARGE", "100");
        ProtocolManager.Instance.SendResponse(agentId, "충전소 충전 완료: 100%");
    }

    // ---- v1.1.0 11주차: SLAM 맵 + 동선 최적화 ----

    void MarkVisitedCell(Vector3 pos)
    {
        visitedCells.Add(new Vector2Int(Mathf.RoundToInt(pos.x / CellSize), Mathf.RoundToInt(pos.z / CellSize)));
    }

    void MarkObstacleCell(Vector3 pos)
    {
        obstacleCells.Add(new Vector2Int(Mathf.RoundToInt(pos.x / CellSize), Mathf.RoundToInt(pos.z / CellSize)));
    }

    void PerformExploredMap()
    {
        string visitedStr = string.Join(";", visitedCells.Select(c => $"{c.x},{c.y}"));
        string obstacleStr = string.Join(";", obstacleCells.Select(c => $"{c.x},{c.y}"));
        ProtocolManager.Instance.SendResponse(agentId, $"[MAP] visited:{visitedStr} | obstacles:{obstacleStr} | cell_size:{CellSize}");
    }

    void PerformRouteDistance(string arg)
    {
        if (arg == null) { ProtocolManager.Instance.SendResponse(agentId, "[ROUTE_DISTANCE] 0"); return; }

        var points = ParseWaypoints(arg);
        float total = 0f;
        Vector2 cursor = new Vector2(transform.position.x, transform.position.z);
        foreach (var p in points)
        {
            total += Vector2.Distance(cursor, p);
            cursor = p;
        }
        ProtocolManager.Instance.SendResponse(agentId, $"[ROUTE_DISTANCE] {total:F2}");
    }

    void PerformNavigateAuto(string arg)
    {
        if (arg == null) return;

        var points = ParseWaypoints(arg);
        var optimized = NearestNeighborOrder(new Vector2(transform.position.x, transform.position.z), points);

        string orderStr = string.Join("|", optimized.Select(p => $"{p.x:F2},{p.y:F2}"));
        LogEvent("NAVIGATE_AUTO", orderStr);
        ProtocolManager.Instance.SendResponse(agentId, $"[NAVIGATE_AUTO] order:{orderStr}");

        foreach (var p in optimized)
        {
            commandQueue.Enqueue($"MoveTo:{p.x:F2},{p.y:F2}");
        }
    }

    // 가장 가까운 다음 지점을 계속 선택하는 Nearest-Neighbor 근사 — TSP 최적해는 아니지만 11주차 학습 목적(입력 순서 재정렬 체험)에는 충분합니다.
    static List<Vector2> NearestNeighborOrder(Vector2 start, List<Vector2> points)
    {
        var remaining = new List<Vector2>(points);
        var ordered = new List<Vector2>();
        Vector2 cursor = start;

        while (remaining.Count > 0)
        {
            int bestIdx = 0;
            float bestDist = float.MaxValue;
            for (int i = 0; i < remaining.Count; i++)
            {
                float d = Vector2.Distance(cursor, remaining[i]);
                if (d < bestDist) { bestDist = d; bestIdx = i; }
            }
            cursor = remaining[bestIdx];
            ordered.Add(cursor);
            remaining.RemoveAt(bestIdx);
        }
        return ordered;
    }

    // ---- 이벤트 로그 ----

    void LogEvent(string type, string value)
    {
        eventLogger.Add(transform.position.x, transform.position.z, type, value);
    }

    void PerformEventHistory(string arg)
    {
        int limit = 10;
        if (!string.IsNullOrEmpty(arg)) int.TryParse(arg, out limit);
        ProtocolManager.Instance.SendResponse(agentId, $"[EVENT_HISTORY] {eventLogger.RecentAsJson(limit)}");
    }

    public void SetCsvLogging(bool enabled)
    {
        csvLoggingEnabled = enabled;
        eventLogger?.SetCsvEnabled(enabled);
    }

    // 우상단 [회전 잠금] 토글의 OnValueChanged(Boolean)에 연결 — 필드 직접 바인딩이 불가능해 별도 메서드로 노출합니다.
    public void SetRotationLocked(bool locked)
    {
        rotationLocked = locked;
    }

    // ---- 배터리 / 충돌 / 재시작 ----

    void DrainBattery(float distanceMoved)
    {
        batteryLevel = Mathf.Max(0f, batteryLevel - distanceMoved * batteryDrainPerDistance);
    }

    void CheckBatteryLowThreshold()
    {
        if (batteryLowThreshold < 0f || batteryLowAlreadyFired) return;
        if (batteryLevel <= batteryLowThreshold)
        {
            batteryLowAlreadyFired = true;
            LogEvent("BATTERY_LOW", batteryLevel.ToString("F0"));
            ProtocolManager.Instance.SendEvent(agentId, $"BATTERY_LOW:{batteryLevel:F0}");
        }
    }

    // on_obstacle(threshold, callback) 지원 — threshold 이내로 접근하면 이벤트 1회 발생,
    // 범위를 벗어나면 재무장되어 다시 접근할 때 재발생합니다(배터리 경고와 달리 반복 가능한 상황이므로).
    void CheckObstacleThreshold()
    {
        if (obstacleWarnThreshold < 0f) return;

        bool near = Physics.Raycast(transform.position, transform.forward, out RaycastHit hit, scanRange)
                    && hit.distance <= obstacleWarnThreshold;

        if (near && !obstacleWarnAlreadyFired)
        {
            obstacleWarnAlreadyFired = true;
            LogEvent("OBSTACLE_NEAR", hit.distance.ToString("F1"));
            ProtocolManager.Instance.SendEvent(agentId, $"OBSTACLE_NEAR:{hit.distance:F1}");
        }
        else if (!near)
        {
            obstacleWarnAlreadyFired = false;
        }
    }

    // 충전소 (ChargingStation) 표: "충전 중 표시 — 로봇 LED 파란색 점멸(자동)"
    void StartChargingBlink()
    {
        chargingVisualTimer = chargingVisualDuration;
    }

    void UpdateChargingBlink()
    {
        if (chargingVisualTimer <= 0f) return;

        chargingVisualTimer -= Time.deltaTime;
        bool blinkOn = Mathf.FloorToInt(chargingVisualTimer / chargingBlinkInterval) % 2 == 0;
        ApplyLedVisual(blinkOn ? ledChargingColor : ledNormalColor);

        if (chargingVisualTimer <= 0f)
        {
            ApplyLedVisual(ledNormalColor);
        }
    }

    void ApplyLedVisual(Color color)
    {
        if (ledRenderer != null) ledRenderer.material.color = color;
    }

    private void OnCollisionEnter(Collision collision)
    {
        HandleCollisionWith(collision.gameObject.name);
    }

    // StaticBox는 기본 큐브에 IsTrigger 콜라이더로 배치되어 물리적으로 로봇을 막지는 않지만,
    // 충돌 판정 자체는 벽에 부딪혔을 때와 동일하게 처리합니다. 충전소 등 다른 트리거는 대상이 아닙니다.
    private void OnTriggerEnter(Collider other)
    {
        if (other.GetComponent<StaticBox>() == null) return;
        HandleCollisionWith(other.gameObject.name);
    }

    private void HandleCollisionWith(string colliderName)
    {
        LastCollision = colliderName;

        if (rb != null)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        while (commandQueue.TryDequeue(out _)) { }

        targetPosition = transform.position;
        targetRotation = transform.rotation;
        moveVelocity = Vector3.zero;

        LogEvent("COLLISION", LastCollision);
        ProtocolManager.Instance.SendWarning(agentId, $"{LastCollision}와(과) 충돌하여 모든 작업이 중단되었습니다.");
        // on_collision(callback) 지원 — on_obstacle/on_battery_low와 동일하게 [EVENT] 채널로도 함께 보내
        // 파이썬 쪽 콜백 리스너가 하나의 이벤트 채널만 구독해도 충돌을 감지할 수 있게 합니다.
        ProtocolManager.Instance.SendEvent(agentId, $"COLLISION:{LastCollision}");
    }

    // 좌하단 [재시작] 버튼 / F5 — RestartManager가 호출
    public void ResetToInitialState()
    {
        while (commandQueue.TryDequeue(out _)) { }
        while (immediateQueue.TryDequeue(out _)) { }

        transform.position = initialPosition;
        transform.rotation = initialRotation;
        targetPosition = initialPosition;
        targetRotation = initialRotation;
        moveVelocity = Vector3.zero;

        batteryLevel = 100f;
        batteryLowAlreadyFired = false;
        obstacleWarnAlreadyFired = false;
        chargingVisualTimer = 0f;
        IsLEDOn = false;
        LastCollision = "None";
        ApplyLedVisual(ledNormalColor);

        eventLogger.Clear();
        visitedCells.Clear();
        obstacleCells.Clear();
        MarkVisitedCell(transform.position);
    }
}
