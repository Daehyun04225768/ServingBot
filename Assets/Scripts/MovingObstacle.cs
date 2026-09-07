using UnityEngine;

/// <summary>
/// v1.1.0 — 피지컬AI 6주차 전용 동적 장애물. 우상단 [동적 장애물] 토글의 OnValueChanged에서
/// SetActive(bool)를 호출해 켜고 끕니다 (기본값 OFF).
/// </summary>
public class MovingObstacle : MonoBehaviour
{
    [Header("동적 장애물 설정")]
    public float rangeX = 3f;   // X축 왕복 범위 (±rangeX)
    public float period = 4f;   // 왕복 주기(초)

    [Header("장애물 회피 설정")]
    public LayerMask obstacleLayerMask; // Wall/Environment가 속한 레이어 — 이 범위를 넘어서면 안 됨
    public float avoidMargin = 0.3f;    // 벽과의 안전 여유 거리

    private bool isActive = false;
    private Vector3 startPosition;
    private float safeRangeX;

    void Awake()
    {
        // Toggle의 초기 Rebuild가 Start() 전에 SetActive(false)를 호출할 수 있어(우상단 토글 배선 시 확인됨),
        // Start()가 아니라 Awake()에서 원위치를 기록해야 씬 시작 시 원점으로 순간이동하지 않습니다.
        startPosition = transform.position;

        // 벽/환경물은 고정되어 있으므로, 왕복 범위를 한 번만 계산해 두면 매 프레임 검사할 필요가 없습니다.
        safeRangeX = ComputeSafeRangeX();
    }

    float ComputeSafeRangeX()
    {
        BoxCollider box = GetComponent<BoxCollider>();
        Vector3 halfExtents = box != null
            ? Vector3.Scale(box.size, transform.lossyScale) * 0.5f
            : Vector3.one * 0.5f;

        float safe = rangeX;
        RaycastHit hit;

        if (Physics.BoxCast(startPosition, halfExtents, Vector3.right, out hit, transform.rotation, rangeX, obstacleLayerMask))
        {
            safe = Mathf.Min(safe, Mathf.Max(0f, hit.distance - avoidMargin));
        }
        if (Physics.BoxCast(startPosition, halfExtents, Vector3.left, out hit, transform.rotation, rangeX, obstacleLayerMask))
        {
            safe = Mathf.Min(safe, Mathf.Max(0f, hit.distance - avoidMargin));
        }

        return safe;
    }

    void Update()
    {
        if (!isActive) return;

        float t = Mathf.Sin(2f * Mathf.PI * Time.time / period);
        Vector3 pos = startPosition;
        pos.x += t * safeRangeX;
        transform.position = pos;
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
