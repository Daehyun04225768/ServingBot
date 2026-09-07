using UnityEngine;

public class AppManager : MonoBehaviour
{
    public void QuitApplication()
    {
        #if UNITY_EDITOR
            // 유니티 에디터에서 실행 중일 때
            UnityEditor.EditorApplication.isPlaying = false;
        #else
            // 실제 빌드된 앱에서 실행 중일 때
            Application.Quit();
        #endif
    }
}