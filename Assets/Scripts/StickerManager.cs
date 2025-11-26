using UnityEngine;
using UnityEngine.UI;
using System;

public class StickerManager : MonoBehaviour
{
    [Header("References")]
    public UDPChatManager udpManager;
    public GameObject stickerPanel; // The panel you just made
    
    // Drag your 2 source images here in the Inspector
    public Texture2D[] availableStickers; 

    public void ToggleStickerPanel()
    {
        bool isActive = stickerPanel.activeSelf;
        stickerPanel.SetActive(!isActive);
    }

    // Call this from the Buttons in your Grid
    public void OnStickerClicked(int index)
    {
        if (index < 0 || index >= availableStickers.Length) return;

        try 
        {
            Texture2D tex = availableStickers[index];
            
            // Encode to JPG instead of PNG (Smaller size)
            byte[] imageBytes = tex.EncodeToJPG(50); // 50% quality
            
            string base64Data = Convert.ToBase64String(imageBytes);

            // Check size before sending (Limit ~60,000 chars for safety)
            if (base64Data.Length > 60000)
            {
                Debug.LogError("Sticker is too big to send! Reduce Max Size in Import Settings.");
                return;
            }

            if (udpManager != null)
            {
                udpManager.SendStickerMessage(base64Data);
            }
            
            stickerPanel.SetActive(false);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Error processing sticker: {e.Message}");
        }
    }
}