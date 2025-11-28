using UnityEngine;

public class WindowSetup : MonoBehaviour
{
    void Start()
    {
        // Force 720p Windowed mode
        // 1280 = Width
        // 720 = Height
        // false = Windowed (true would be Fullscreen)
        Screen.SetResolution(1280, 720, FullScreenMode.Windowed);
        
        Debug.Log("Game set to 1280x720 Windowed Mode.");
    }
}