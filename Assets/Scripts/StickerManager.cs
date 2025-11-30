using UnityEngine;
using UnityEngine.UI;
using System;
using System.Collections; 

/*
Simple UI controller for chat stickers: builds a grid of buttons from a list
of textures, handles auto-resizing for a neat layout, and sends the selected
sticker over UDP (with a small cooldown to prevent spam). The panel can be
toggled on/off and hides itself after a send.
*/
public class StickerManager : MonoBehaviour
{
    [Header("References")]
    public UDPChatManager udpManager;
    public GameObject stickerPanel; 
    
    [Header("Dynamic UI")]
    public Transform scrollContent;        
    public GameObject stickerButtonPrefab; 
    
    [Header("Layout Settings")]
    public bool autoResizeStickers = true; 
    public float manualWidthOverride = 0f; 
    
    [Header("Data")]
    public Texture2D[] availableStickers; 
    
    [Header("Settings")]
    public float spamCooldown = 1.5f; 
    private bool canSend = true;

    /*
    Kicks off the grid build after the first frame so the layout has proper
    sizing. Everything else is handled inside the generator.
    */
    void Start()
    {
        StartCoroutine(GenerateStickerGrid());
    }

    /*
    Builds the sticker grid: shows the panel briefly so Unity can measure it,
    forces the layout to rebuild, sizes cells if auto-resize is on, clears any
    old buttons, then spawns one button per texture and wires up the click.
    Finally hides the panel again.
    */
    private IEnumerator GenerateStickerGrid()
    {
        if (stickerPanel != null) stickerPanel.SetActive(true);
        yield return new WaitForEndOfFrame(); 

        if (stickerPanel != null)
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(stickerPanel.GetComponent<RectTransform>());
        }

        // --- GRID SETUP ---
        GridLayoutGroup grid = scrollContent.GetComponent<GridLayoutGroup>();
        RectTransform viewportRect = scrollContent.parent.GetComponent<RectTransform>(); 

        if (grid != null)
        {
            // Always force 5 columns constraint
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 5; 

            // [FIX] Only change Cell Size if Auto-Resize is ON
            if (autoResizeStickers)
            {
                float totalWidth = (manualWidthOverride > 0) ? manualWidthOverride : viewportRect.rect.width;
                float spacing = grid.spacing.x * 4; 
                float padding = grid.padding.left + grid.padding.right;
                float scrollbarMargin = 20f; 

                if (totalWidth > 0)
                {
                    float cellSize = (totalWidth - padding - spacing - scrollbarMargin) / 5f;
                    if (cellSize < 30) cellSize = 30; 
                    grid.cellSize = new Vector2(cellSize, cellSize);
                    Debug.Log($"[UI] Auto-Resized to {cellSize}px");
                }
            }
        }

        // --- SPAWN BUTTONS ---
        foreach (Transform child in scrollContent) Destroy(child.gameObject);

        for (int i = 0; i < availableStickers.Length; i++)
        {
            int index = i; 
            GameObject newBtn = Instantiate(stickerButtonPrefab, scrollContent);
            
            Texture2D tex = availableStickers[i];
            if (tex != null)
            {
                Sprite sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
                Image btnImage = newBtn.GetComponent<Image>();
                if (btnImage != null) 
                {
                    btnImage.sprite = sprite;
                    btnImage.preserveAspect = true; 
                }
            }
            newBtn.GetComponent<Button>().onClick.AddListener(() => OnStickerClicked(index));
        }

        if (stickerPanel != null) stickerPanel.SetActive(false);
    }

    // ... (ToggleStickerPanel, OnStickerClicked, etc. remain exactly the same) ...
    /*
    Quick toggle to show or hide the sticker panel.
    */
    public void ToggleStickerPanel() { stickerPanel.SetActive(!stickerPanel.activeSelf); }
    /*
    When a sticker button gets clicked, validate the index, resize the texture
    to a small PNG, base64 it, and send via UDP. Applies a short cooldown and
    closes the panel after sending.
    */
    public void OnStickerClicked(int index) { /* Copy previous logic here */ 
        // Keep existing logic; only comments changed.
        if (!canSend) return;
        if (index < 0 || index >= availableStickers.Length) return;
        try {
            Texture2D originalTex = availableStickers[index];
            Texture2D resizedTex = ResizeTexture(originalTex, 100, 100);
            byte[] imageBytes = resizedTex.EncodeToPNG(); 
            string base64Data = Convert.ToBase64String(imageBytes);
            if (udpManager != null) {
                udpManager.SendStickerMessage(base64Data);
                StartCoroutine(CooldownRoutine());
            }
            stickerPanel.SetActive(false);
            Destroy(resizedTex);
        } catch (System.Exception e) { Debug.LogError($"Error: {e.Message}"); }
    }
    /*
    Simple throttle: wait a bit before allowing another send.
    */
    private IEnumerator CooldownRoutine() {
        canSend = false;
        yield return new WaitForSeconds(spamCooldown);
        canSend = true;
    }
    /*
    Downscales a texture to the requested size using a temporary RenderTexture,
    then reads back into a new Texture2D.
    */
    private Texture2D ResizeTexture(Texture2D source, int targetWidth, int targetHeight) {
        RenderTexture rt = RenderTexture.GetTemporary(targetWidth, targetHeight);
        RenderTexture.active = rt;
        Graphics.Blit(source, rt);
        Texture2D result = new Texture2D(targetWidth, targetHeight);
        result.ReadPixels(new Rect(0, 0, targetWidth, targetHeight), 0, 0);
        result.Apply();
        RenderTexture.active = null;
        RenderTexture.ReleaseTemporary(rt);
        return result;
    }
}