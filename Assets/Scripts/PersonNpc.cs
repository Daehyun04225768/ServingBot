using UnityEngine;

/// <summary>
/// v1.1.0 — 10주차 전용 NPC 사람 오브젝트. get_camera_view()에서 "person" 타입으로 분류됩니다.
/// 우상단 [NPC 사람] 토글의 OnValueChanged에서 SetActive(bool)를 호출합니다 (기본값 OFF).
/// 기본 배치 수 2개는 씬 에디터에서 이 컴포넌트를 붙인 오브젝트 2개를 배치하는 방식으로 구성합니다.
/// </summary>
public class PersonNpc : MonoBehaviour
{
    [Header("무작위 이동 설정")]
    public float wanderRadius = 5f;
    public float moveSpeed = 1.5f;

    [Header("장애물 회피 설정")]
    public LayerMask obstacleLayerMask; // Wall/Environment가 속한 레이어 — 이 방향으로는 목적지를 잡지 않음
    public int maxPickAttempts = 8;     // 막힌 방향을 뽑았을 때 다시 시도할 횟수
    public float avoidMargin = 0.3f;    // 벽과의 안전 여유 거리

    private bool isActive = false;
    private Vector3 startPosition;
    private Vector3 targetPosition;
    private float waitTimer = 0f;

    void Awake()
    {
        // Toggle의 초기 Rebuild가 Start() 전에 SetActive(false)를 호출할 수 있어(우상단 토글 배선 시 확인됨),
        // Start()가 아니라 Awake()에서 원위치를 기록해야 씬 시작 시 원점으로 순간이동하지 않습니다.
        startPosition = transform.position;
        targetPosition = startPosition;
    }

    void Update()
    {
        if (!isActive) return;

        if (Vector3.Distance(transform.position, targetPosition) < 0.2f)
        {
            waitTimer -= Time.deltaTime;
            if (waitTimer <= 0f) PickNewTarget();
            return;
        }

        transform.position = Vector3.MoveTowards(transform.position, targetPosition, moveSpeed * Time.deltaTime);
    }

    private void PickNewTarget()
    {
        CapsuleCollider capsule = GetComponent<CapsuleCollider>();
        float radius = capsule != null
            ? capsule.radius * Mathf.Max(transform.lossyScale.x, transform.lossyScale.z)
            : 0.5f;

        for (int attempt = 0; attempt < maxPickAttempts; attempt++)
        {
            Vector2 offset = Random.insideUnitCircle * wanderRadius;
            Vector3 candidate = startPosition + new Vector3(offset.x, 0f, offset.y);

            Vector3 toCandidate = candidate - transform.position;
            float distance = toCandidate.magnitude;
            if (distance < 0.01f) continue;

            // 벽/환경물에 걸리는 방향인지 미리 검사 — 정적 지형이므로 목적지를 정할 때 한 번만 검사하면 충분합니다.
            bool blocked = Physics.SphereCast(transform.position, radius, toCandidate.normalized, out _, distance + avoidMargin, obstacleLayerMask);
            if (!blocked)
            {
                targetPosition = candidate;
                waitTimer = Random.Range(1f, 3f);
                return;
            }
        }

        // 시도한 방향이 전부 막혔으면 원래 출발 지점으로 — 항상 안전하다고 간주합니다.
        targetPosition = startPosition;
        waitTimer = Random.Range(1f, 3f);
    }

    public void SetActive(bool active)
    {
        // 에디터에서 Toggle을 연결/편집할 때 Unity가 Awake() 이전에 OnValueChanged를 한 번 호출할 수 있어,
        // Play 모드가 아닐 때는 무시합니다 — 그렇지 않으면 startPosition이 초기화되기 전에 원점으로 리셋됩니다.
        if (!Application.isPlaying) return;
        isActive = active;
        if (!active) transform.position = startPosition;
    }
}
