using UnityEngine;
using UnityEngine.UI;

// Simple test helper: attach to any GameObject, set a target Image and a
// Pokédex number, and it will try to load the sprite via the legacy loader
// so you can quickly check paths/imports.
public class SpriteTester : MonoBehaviour
{
    public LoadSprites loader;
    public Image targetImage;
    public int testDex = 1;

    /*
    On start, grabs a `LoadSprites` instance if one isn’t assigned, then tries
    to load the sprite for the chosen dex number. Logs what happened and sets
    the Image if something was found.
    */
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