using System.Collections.Generic;
using UnityEngine;

public class LoadSprites : MonoBehaviour
{
    private Dictionary<int, Sprite> spritesByDex = new Dictionary<int, Sprite>();

    [SerializeField] private int maxDex = 801;
    [Tooltip("Optional: assign the generated PokemonSpriteMap asset to load sprites from it instead of Resources.")]
    [SerializeField] private PokemonSpriteMap spriteMap;

    private void Awake()
    {
        // Prefer using the ScriptableObject map if assigned
        if (spriteMap != null)
        {
            BuildFromSpriteMap();
            Debug.Log($"LoadSprites: Loaded {spritesByDex.Count} sprites from PokemonSpriteMap asset.");
        }
        else
        {
            PreloadAll(maxDex);
            Debug.Log($"LoadSprites: Preloaded {spritesByDex.Count} sprites from Resources (Sprites/).");
        }

        // Quick diagnostic: report a few missing entries
        var missing = new List<int>();
        for (int i = 1; i <= Mathf.Min(10, maxDex); i++)
            if (!spritesByDex.ContainsKey(i)) missing.Add(i);
        if (missing.Count > 0)
            Debug.LogWarning($"LoadSprites: Missing sprites for dex numbers: {string.Join(", ", missing)} (first 10 checked). Check Resources/Sprites and importer settings.");
    }

    private void BuildFromSpriteMap()
    {
        spritesByDex.Clear();
        if (spriteMap == null || spriteMap.map == null) return;
        foreach (var e in spriteMap.map)
        {
            if (e != null && e.sprite != null)
                spritesByDex[e.dex] = e.sprite;
        }
    }

    private void PreloadAll(int maxDex = 801)
    {
        spritesByDex.Clear();
        for (int i = 1; i <= maxDex; i++)
        {
            var s = Resources.Load<Sprite>($"Sprites/{i}");
            if (s != null) spritesByDex[i] = s;
        }
    }

    public Sprite LoadSpriteByNumber(int pokedexNumber)
    {
        return spritesByDex.TryGetValue(pokedexNumber, out var sp) ? sp : null;
    }

    public Sprite LoadSpriteFromCsvField(string spritePathWithoutExt)
    {
        if (string.IsNullOrEmpty(spritePathWithoutExt)) return null;
        var path = WithoutExtension(spritePathWithoutExt);
        // prefer spriteMap if available
        if (spriteMap != null)
        {
            // try to parse as a number
            if (int.TryParse(path, out var dex))
            {
                if (spritesByDex.TryGetValue(dex, out var sp)) return sp;
            }
        }
        return Resources.Load<Sprite>(path);
    }

    private string WithoutExtension(string v)
    {
        if (string.IsNullOrEmpty(v)) return v;
        if (v.EndsWith(".png")) v = v.Substring(0, v.Length - 4);
        if (v.StartsWith("Assets/Resources/")) v = v.Substring("Assets/Resources/".Length);
        if (v.StartsWith("Resources/")) v = v.Substring("Resources/".Length);
        return v.TrimStart('/');
    }

    // Editor / debug helper to rebuild from Resources at runtime
    public void RebuildFromResources()
    {
        PreloadAll(maxDex);
        Debug.Log($"LoadSprites: Rebuilt from Resources; total {spritesByDex.Count} sprites.");
    }
}
