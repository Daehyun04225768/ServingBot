using UnityEngine;
using System.Collections;

public class AspectRatioController : MonoBehaviour
{
    [Header("Target Ratio Settings")]
    public float targetWidth = 1200f;
    public float targetHeight = 2160f;

    private float targetAspect;
    private int lastWidth = 0;
    private int lastHeight = 0;

    void Start()
    {
        // 목표 비율 계산 (1200 / 2160 = 0.555..., 개발 당시 카메라 비율과 동일)
        targetAspect = targetWidth / targetHeight;

#if !UNITY_EDITOR
        // Windows에서는 Start() 시점에 곧바로 SetResolution을 호출하면
        // 창이 아직 완전히 생성되기 전이라 요청이 무시되는 경우가 있어,
        // 한 프레임 뒤로 미뤄서 적용합니다. (Mac에서는 문제없이 즉시 적용됨)
        StartCoroutine(ApplyTargetResolution());
#endif
    }

#if !UNITY_EDITOR
    IEnumerator ApplyTargetResolution()
    {
        yield return null; // 한 프레임 대기 (창이 완전히 생성된 뒤 적용)

        int screenW = (int)targetWidth;
        int screenH = (int)targetHeight;

        // 목표 높이가 실제 모니터보다 크면(작업표시줄/제목표시줄 여유 10% 확보),
        // 창이 화면 밖으로 잘리지 않도록 "같은 비율"을 유지한 채 자동으로 축소합니다.
        int maxUsableHeight = Mathf.RoundToInt(Screen.currentResolution.height * 0.9f);
        if (screenH > maxUsableHeight)
        {
            screenH = maxUsableHeight;
            screenW = Mathf.RoundToInt(screenH * targetAspect);
        }

        // 구버전 bool 오버로드 대신, 명시적으로 FullScreenMode.Windowed를 지정합니다.
        Screen.SetResolution(screenW, screenH, FullScreenMode.Windowed);
        lastWidth = screenW;
        lastHeight = screenH;
    }
#endif

    void Update()
    {
        // 1. 유니티 에디터 환경에서는 해상도 강제 조절 로직을 실행하지 않음
        // (에디터 레이아웃 충돌 및 '도달할 수 없는 코드' 경고 방지)
        #if !UNITY_EDITOR
            HandleAspectRatio();
        #endif
    }

#if !UNITY_EDITOR
    /// <summary>
    /// 실제 빌드된 앱에서 창 크기 조절 시 비율을 유지시키는 로직
    /// </summary>
    void HandleAspectRatio()
    {
        int currentWidth = Screen.width;
        int currentHeight = Screen.height;

        // 해상도가 변했을 때만 계산 실행 (성능 최적화)
        if (currentWidth != lastWidth || currentHeight != lastHeight)
        {
            // 가로 길이에 맞춰서 목표 세로 길이를 계산
            int calculatedHeight = Mathf.RoundToInt(currentWidth / targetAspect);

            // 현재 세로 길이가 계산된 높이와 다를 경우에만 해상도 재설정
            if (currentHeight != calculatedHeight)
            {
                Screen.SetResolution(currentWidth, calculatedHeight, FullScreenMode.Windowed);
            }

            lastWidth = currentWidth;
            lastHeight = currentHeight;
        }
    }
#endif
}
