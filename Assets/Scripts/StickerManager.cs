using UnityEngine;
using UnityEngine.UI;
using System;
using System.Collections; // [NEW] Needed for Coroutine

public class StickerManager : MonoBehaviour
{
    [Header("References")]
    public UDPChatManager udpManager;
    public GameObject stickerPanel; 
    public Texture2D[] availableStickers; 
    
    [Header("Settings")]
    public float spamCooldown = 1.5f; // Wait 1.5 seconds between stickers
    private bool canSend = true;

    public void ToggleStickerPanel()
    {
        stickerPanel.SetActive(!stickerPanel.activeSelf);
    }

    public void OnStickerClicked(int index)
    {
        // [FIX] Prevent spamming
        if (!canSend) 
        {
            Debug.LogWarning("Sticker Cooldown! Wait a moment.");
            return;
        }

        if (index < 0 || index >= availableStickers.Length) return;

        try 
        {
            Texture2D originalTex = availableStickers[index];
            
            // 1. RFC COMPLIANCE: Resize to exactly 320x320
            Texture2D resizedTex = ResizeTexture(originalTex, 200, 200);

            // 2. RELIABILITY: Compress to JPG (Quality 15)
            byte[] imageBytes = resizedTex.EncodeToJPG(15); 
            
            string base64Data = Convert.ToBase64String(imageBytes);

            if (base64Data.Length > 10 * 1024 * 1024)
            {
                Debug.LogError("Sticker too large!");
                return;
            }

            if (udpManager != null)
            {
                udpManager.SendStickerMessage(base64Data);
                // [FIX] Start Cooldown
                StartCoroutine(CooldownRoutine());
            }
            
            stickerPanel.SetActive(false);
            Destroy(resizedTex);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Error processing sticker: {e.Message}");
        }
    }

    // [NEW] Simple Timer to re-enable sending
    private IEnumerator CooldownRoutine()
    {
        canSend = false;
        yield return new WaitForSeconds(spamCooldown);
        canSend = true;
    }

    // Helper function to resize texture to RFC specs
    private Texture2D ResizeTexture(Texture2D source, int targetWidth, int targetHeight)
    {
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