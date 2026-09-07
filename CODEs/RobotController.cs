using UnityEngine;
using System.Collections.Concurrent;

public class RobotController : MonoBehaviour
{
    // 서버로부터 명령을 받는 스레드 안전 큐
    public static ConcurrentQueue<string> commandQueue = new ConcurrentQueue<string>();

    [Header("Movement Settings")]
    public float moveSpeed = 5.0f;
    public float turnSpeed = 120.0f;
    public float smoothTime = 0.1f; // 가감속 부드러움 정도

    [Header("Precision Settings")]
    public float distanceThreshold = 0.01f; // 정지 판단 거리 (데드존)
    public float angleThreshold = 0.1f;    // 정지 판단 각도

    [Header("Sensor Settings")]
    public float scanRange = 20f;                 // 전방 장애물 감지 최대 거리 (LiDAR 유사)
    public float detectRadius = 15f;              // 사물 인식(카메라 유사) 감지 반경
    public string detectableTag = "Detectable";   // 인식 대상 오브젝트에 붙일 Tag (Unity Editor > Tags에서 미리 생성 후 대상 오브젝트에 지정 필요)

    [Header("Battery Settings")]
    public float batteryLevel = 100f;
    public float batteryDrainPerDistance = 0.5f;  // 이동 거리 1 단위당 배터리 소모(%)

    [Header("Rotation Lock (특정 주차 실습용)")]
    public bool rotationLocked = false;  // 체크하면 Turn 명령이 거부됩니다 ("제자리 회전 불가" 하드웨어 제약 시뮬레이션).
                                          // 평소엔 꺼두고, 회전 제약 극복이 주제인 주차의 빌드에서만 켜서 사용합니다.

    // 내부 상태 관리를 위한 변수
    private Vector3 targetPosition;
    private Quaternion targetRotation;
    private Vector3 moveVelocity = Vector3.zero;
    private Rigidbody rb;

    // 매니저 참조용 프로퍼티
    public bool IsLEDOn { get; private set; }
    public string LastCollision { get; private set; } = "None";

    void Start()
    {
        rb = GetComponent<Rigidbody>();

        // Inspector 체크박스는 빌드 시점 값으로 고정되므로, 실행 인자로도 덮어쓸 수 있게 합니다.
        // 예: ServingRobot.exe -rotationLocked=true  → 빌드 하나로 모든 주차를 커버 가능
        string[] args = System.Environment.GetCommandLineArgs();
        foreach (string arg in args)
        {
            if (arg.StartsWith("-rotationLocked="))
            {
                bool.TryParse(arg.Substring("-rotationLocked=".Length), out rotationLocked);
            }
        }

        // 시작 지점을 목표 지점으로 초기 설정
        targetPosition = transform.position;
        targetRotation = transform.rotation;

        // 리지드바디 설정 (충돌 시 로봇이 뒤집히지 않도록 고정)
        if (rb != null)
        {
            rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
        }
    }

    void Update()
    {
        // 1. 현재 오차 계산
        float dist = Vector3.Distance(transform.position, targetPosition);
        float angle = Quaternion.Angle(transform.rotation, targetRotation);

        // 2. [순차 제어] 로봇이 멈춘 상태에서만 다음 명령을 수행
        if (dist < distanceThreshold && angle < angleThreshold)
        {
            if (commandQueue.TryDequeue(out string command))
            {
                ProcessCommand(command);
            }
        }

        // 3. 부드러운 이동 (SmoothDamp)
        transform.position = Vector3.SmoothDamp(transform.position, targetPosition, ref moveVelocity, smoothTime, moveSpeed);

        // 4. 부드러운 회전 (RotateTowards)
        transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, turnSpeed * Time.deltaTime);
    }

    void ProcessCommand(string cmd)
    {
        string[] parts = cmd.Split(':');
        string action = parts[0];

        if (action == "Move")
        {
            if (parts.Length < 2 || !float.TryParse(parts[1], out float value)) return;

            // 현재 앞방향을 기준으로 목표 지점 누적
            targetPosition += transform.forward * value;
            DrainBattery(Mathf.Abs(value));
            ProtocolManager.Instance.SendResponse($"이동 명령 접수: {value} (목표 지점 갱신, 배터리 {batteryLevel:F0}%)");
        }
        else if (action == "Turn")
        {
            if (parts.Length < 2 || !float.TryParse(parts[1], out float value)) return;

            if (rotationLocked)
            {
                // 연결이 끊기는 [SYSTEM ALERT]가 아니라, 세션은 유지한 채 "이 명령은 거부됨"만 알립니다.
                ProtocolManager.Instance.SendResponse("[REJECTED] 이 로봇은 하드웨어 제약으로 제자리 회전(Turn)이 불가능합니다. MoveTo로 목표 좌표를 재계산해 곡선 경로로 이동하세요.");
                return;
            }

            // 현재 회전값에 목표 각도 누적
            targetRotation *= Quaternion.Euler(0, value, 0);
            ProtocolManager.Instance.SendResponse($"회전 명령 접수: {value} (목표 각도 갱신)");
        }
        else if (action == "MoveTo")
        {
            // 형식: MoveTo:x,z  (절대 좌표로 직접 이동)
            if (parts.Length < 2) return;
            string[] coords = parts[1].Split(',');
            if (coords.Length < 2) return;
            if (!float.TryParse(coords[0], out float x) || !float.TryParse(coords[1], out float z)) return;

            Vector3 destination = new Vector3(x, transform.position.y, z);
            float dist = Vector3.Distance(transform.position, destination);

            Vector3 direction = destination - transform.position;
            if (direction != Vector3.zero)
            {
                targetRotation = Quaternion.LookRotation(direction);
            }
            targetPosition = destination;

            DrainBattery(dist);
            ProtocolManager.Instance.SendResponse($"목표 좌표 이동 명령 접수: ({x:F1}, {z:F1}) (배터리 {batteryLevel:F0}%)");
        }
        else if (action == "LED")
        {
            if (parts.Length < 2 || !float.TryParse(parts[1], out float value)) return;

            IsLEDOn = value > 0;
            ProtocolManager.Instance.SendResponse($"LED 상태 변경: {(IsLEDOn ? "ON" : "OFF")}");
        }
    }

    /// <summary>
    /// 이동 큐를 거치지 않고 즉시 처리하는 명령(센서 조회, 상태 조회, 충전).
    /// 로봇이 이동/회전 중이어도 바로 응답합니다.
    /// </summary>
    public void HandleImmediateCommand(string action)
    {
        if (action == "Scan")
        {
            PerformScan();
        }
        else if (action == "Status")
        {
            ReportStatus();
        }
        else if (action == "Charge")
        {
            batteryLevel = 100f;
            ProtocolManager.Instance.SendResponse("배터리 충전 완료: 100%");
        }
    }

    /// <summary>
    /// 전방 장애물까지 거리(LiDAR 유사) + 주변 인식 대상 오브젝트(카메라 유사)를 탐색합니다.
    /// 인식 대상 오브젝트는 Unity Editor에서 Tag를 detectableTag(기본값 "Detectable")로 지정해야 합니다.
    /// </summary>
    void PerformScan()
    {
        string obstacleStr = "None";
        if (Physics.Raycast(transform.position, transform.forward, out RaycastHit hit, scanRange))
        {
            obstacleStr = $"{hit.distance:F1}";
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

        ProtocolManager.Instance.SendResponse($"[SCAN] ObstacleDistance:{obstacleStr} | DetectedObjects:{objectsInfo}");
    }

    /// <summary>
    /// 현재 위치·회전·배터리·LED·최근 충돌 정보를 한 번에 보고합니다.
    /// </summary>
    void ReportStatus()
    {
        string ledStatus = IsLEDOn ? "ON" : "OFF";
        string status = $"Position:({transform.position.x:F2},{transform.position.z:F2}) | Rotation:{transform.eulerAngles.y:F1} | Battery:{batteryLevel:F0}% | LED:{ledStatus} | LastCollision:{LastCollision}";
        ProtocolManager.Instance.SendResponse($"[STATUS] {status}");
    }

    void DrainBattery(float distanceMoved)
    {
        batteryLevel = Mathf.Max(0f, batteryLevel - distanceMoved * batteryDrainPerDistance);
    }

    private void OnCollisionEnter(Collision collision)
    {
        LastCollision = collision.gameObject.name;

        // 1. [물리 정지] 충돌 순간 모든 물리적 속도와 관성을 제거 (떨림 방지 핵심)
        if (rb != null)
        {
            // 구버전(2022 이하) API인 velocity를 사용합니다.
            rb.velocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        // 2. [명령 취소] 벽을 계속 밀지 않도록 남은 명령 전체 삭제
        while (commandQueue.TryDequeue(out _)) { }

        // 3. [좌표 고정] 현재 위치를 목표 지점으로 강제 설정
        targetPosition = transform.position;
        targetRotation = transform.rotation;
        moveVelocity = Vector3.zero;

        // 4. 경고 전송
        ProtocolManager.Instance.SendWarning($"{LastCollision}와(과) 충돌하여 모든 작업이 중단되었습니다.");
    }
}
