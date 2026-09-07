using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// v1.1.0 — 10주차 display()/speak() 표시용 말풍선. target(로봇)의 머리 위 위치를 매 프레임 따라다니며
/// 항상 카메라를 향하도록(빌보드) 회전합니다. RobotController가 Show(text)를 호출해 사용합니다.
/// 듀얼 모드에서는 로봇마다 이 컴포넌트를 하나씩 두고 각자의 target을 연결합니다.
/// </summary>
public class SpeechBubble : MonoBehaviour
{
    [Header("References")]
    public Transform target;       // 말풍선을 띄울 로봇의 Transform
    public GameObject bubbleRoot;  // 배경+텍스트를 담는 자식 오브젝트(평소엔 비활성 — 기본 UI, 이미지는 추후 교체)
    public Text bubbleText;

    [Header("Settings")]
    public Vector3 offset = new Vector3(0f, 2f, 0f);
    public float displayDuration = 3f;

    private float hideTimer = 0f;
    private Camera mainCamera;

    void Awake()
    {
        if (bubbleRoot != null) bubbleRoot.SetActive(false);
    }

    void LateUpdate()
    {
        if (target != null)
        {
            transform.position = target.position + offset;
        }

        if (mainCamera == null) mainCamera = Camera.main;
        if (mainCamera != null)
        {
            transform.forward = mainCamera.transform.forward;
        }

        if (hideTimer > 0f)
        {
            hideTimer -= Time.deltaTime;
            if (hideTimer <= 0f && bubbleRoot != null)
            {
                bubbleRoot.SetActive(false);
            }
        }
    }

    // display(text) / speak(text)에서 호출 — 지정 시간 동안 말풍선을 띄웠다가 자동으로 숨깁니다.
    public void Show(string text)
    {
        if (bubbleText != null) bubbleText.text = text;
        if (bubbleRoot != null) bubbleRoot.SetActive(true);
        hideTimer = displayDuration;
    }
}
