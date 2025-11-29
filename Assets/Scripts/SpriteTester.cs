using UnityEngine;
using UnityEngine.UI;

// Attach to a GameObject and assign a target Image and a dex number to test sprite loading.
public class SpriteTester : MonoBehaviour
{
    public LoadSprites loader;
    public Image targetImage;
    public int testDex = 1;

    void Start()
    {
        if (loader == null)
        {
            loader = FindObjectOfType<LoadSprites>();
            if (loader == null)
            {
                Debug.LogError("SpriteTester: No LoadSprites instance found in scene.");
                return;
            }
        }

        var sp = loader.LoadSpriteByNumber(testDex);
        if (sp == null)
        {
            Debug.LogWarning($"SpriteTester: sprite for dex {testDex} is null.");
        }
        else
        {
            Debug.Log($"SpriteTester: loaded sprite for dex {testDex}: {sp.name}");
            if (targetImage != null) targetImage.sprite = sp;
        }
    }
}
