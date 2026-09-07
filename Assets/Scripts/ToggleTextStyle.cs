using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// v1.1.0 — 체크박스 없이 "글자 버튼"만으로 on/off 상태를 표현하는 토글 스타일러.
/// 해당 Toggle의 OnValueChanged(Boolean)에 기존 기능 콜백과 나란히 추가로 연결해서 사용합니다
/// (이 스크립트는 시각 효과만 담당하고, 실제 기능은 그대로 RobotController 등이 처리합니다).
/// onText/offText를 둘 다 채우면 상태에 따라 글자 내용 자체도 바뀝니다(예: 싱글 모드 ↔ 듀얼 모드).
/// 비워두면 색상/굵기만 바뀌는 단순 on/off 버튼이 됩니다.
/// </summary>
public class ToggleTextStyle : MonoBehaviour
{
    [Header("References")]
    public Text label;

    [Header("Colors")]
    public Color onColor = new Color(1f, 0.55f, 0f);          // 오렌지 (On)
    public Color offColor = new Color(0.85f, 0.85f, 0.85f);   // 밝은 회색 (Off)

    [Header("Optional: 상태별로 글자 내용도 바꾸고 싶을 때")]
    public string onText = "";
    public string offText = "";

    void Awake()
    {
        Toggle toggle = GetComponent<Toggle>();
        if (toggle != null) Apply(toggle.isOn);
    }

    // 해당 Toggle의 OnValueChanged(Boolean)에 연결
    public void Apply(bool isOn)
    {
        if (label == null) return;

        label.color = isOn ? onColor : offColor;
        label.fontStyle = isOn ? FontStyle.Bold : FontStyle.Normal;

        if (!string.IsNullOrEmpty(onText) && !string.IsNullOrEmpty(offText))
        {
            label.text = isOn ? onText : offText;
        }
    }
}
