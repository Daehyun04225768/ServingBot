using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// v1.1.0 — 우상단 [탐색 맵] 토글용 오버레이. ON 시 로봇의 visited/obstacle 셀을 격자 텍스처로 그려
/// 화면 좌측에 배치한 RawImage(targetImage)에 표시합니다.
/// 씬 설정: 화면 좌측에 RawImage를 배치해 targetImage에 연결하고, robot에 표시할 로봇(RobotController)을
/// 지정한 뒤, [탐색 맵] Toggle의 OnValueChanged(Boolean)에 SetOverlayActive를 연결하세요.
/// </summary>
public class ExploredMapOverlay : MonoBehaviour
{
    [Header("References")]
    public RawImage targetImage;
    public RobotController robot;

    [Header("Grid Settings")]
    public int textureSize = 128;
    public float worldRange = 32f;      // 텍스처가 표현하는 월드 좌표 범위(-worldRange ~ +worldRange)
    public Color visitedColor = new Color(0.2f, 0.8f, 0.3f, 0.9f);
    public Color obstacleColor = new Color(0.9f, 0.2f, 0.2f, 0.9f);
    public Color backgroundColor = new Color(0f, 0f, 0f, 0f);
    public float refreshInterval = 0.5f;

    private Texture2D mapTexture;
    private float refreshTimer = 0f;
    private bool overlayActive = false;

    void Start()
    {
        mapTexture = new Texture2D(textureSize, textureSize, TextureFormat.RGBA32, false);
        mapTexture.filterMode = FilterMode.Point;
        if (targetImage != null) targetImage.texture = mapTexture;
        SetOverlayActive(false);
    }

    void Update()
    {
        if (!overlayActive || robot == null) return;

        refreshTimer -= Time.deltaTime;
        if (refreshTimer <= 0f)
        {
            refreshTimer = refreshInterval;
            RedrawTexture();
        }
    }

    // 우상단 [탐색 맵] 토글의 OnValueChanged(Boolean)에 연결
    public void SetOverlayActive(bool active)
    {
        overlayActive = active;
        if (targetImage != null) targetImage.enabled = active;
        if (active) RedrawTexture();
    }

    void RedrawTexture()
    {
        var pixels = new Color[textureSize * textureSize];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = backgroundColor;

        foreach (var cell in robot.ObstacleCells) PlotCell(pixels, cell, obstacleColor);
        foreach (var cell in robot.VisitedCells) PlotCell(pixels, cell, visitedColor);

        mapTexture.SetPixels(pixels);
        mapTexture.Apply();
    }

    void PlotCell(Color[] pixels, Vector2Int cell, Color color)
    {
        float u = (cell.x + worldRange) / (worldRange * 2f);
        float v = (cell.y + worldRange) / (worldRange * 2f);
        if (u < 0f || u > 1f || v < 0f || v > 1f) return;

        int px = Mathf.Clamp(Mathf.RoundToInt(u * (textureSize - 1)), 0, textureSize - 1);
        int py = Mathf.Clamp(Mathf.RoundToInt(v * (textureSize - 1)), 0, textureSize - 1);
        pixels[py * textureSize + px] = color;
    }
}
