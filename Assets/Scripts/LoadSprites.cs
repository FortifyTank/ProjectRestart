using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// This is the original, now‑deprecated sprite loader MonoBehaviour that
/// either builds a cache from a ScriptableObject map or bulk‑loads sprites
/// from the Resources folder by Pokédex number. At startup it fills a
/// dictionary for quick lookups and logs any obvious gaps so path/import
/// issues are easier to spot.
/// 
/// The project currently prefers loading directly via Resources in
/// `BattleManager.UpdateBattleUI()` for fewer moving parts and simpler
/// timing, but this component sticks around as a lightweight reference and
/// editor helper if a prebuilt map or early caching workflow is desired.
/// </summary>
public class LoadSprites : MonoBehaviour
{
    private Dictionary<int, Sprite> spritesByDex = new Dictionary<int, Sprite>();

    [SerializeField] private int maxDex = 801;
    [Tooltip("Optional: assign the generated PokemonSpriteMap asset to load sprites from it instead of Resources.")]
    [SerializeField] private PokemonSpriteMap spriteMap;

    /*
    Kicks on the legacy sprite loader: if there’s a ScriptableObject map,
    use it; otherwise grab sprites from Resources as a simple fallback.
    Also logs a quick “hey these look missing” so path/import hiccups are easy
    to spot.
    */
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

    /*
    Clears the cache and loads entries from the assigned `PokemonSpriteMap`.
    Skips nulls and keeps things tidy.
    */
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

    /*
    Bulk-load from `Resources/Sprites/{dex}` into a dictionary keyed by
    Pokédex number. Simple, reliable, and great for small projects or editor
    tinkering.
    */
    private void PreloadAll(int maxDex = 801)
    {
        spritesByDex.Clear();
        for (int i = 1; i <= maxDex; i++)
        {
            var s = Resources.Load<Sprite>($"Sprites/{i}");
            if (s != null) spritesByDex[i] = s;
        }
    }

    /*
    Quick lookup by Pokédex number. If it’s cached, return the sprite;
    otherwise return null (no fuss).
    */
    public Sprite LoadSpriteByNumber(int pokedexNumber)
    {
        return spritesByDex.TryGetValue(pokedexNumber, out var sp) ? sp : null;
    }

    /*
    Takes a CSV sprite field (extensions/paths optional) and turns it into a
    Resource path. If there’s a sprite map and the field parses as a dex
    number, prefer the cached entry.
    */
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

    /*
    Cleans up a path-like CSV field: trims `.png`, removes `Resources/` or
    `Assets/Resources/`, and returns a neat relative path.
    */
    private string WithoutExtension(string v)
    {
        if (string.IsNullOrEmpty(v)) return v;
        if (v.EndsWith(".png")) v = v.Substring(0, v.Length - 4);
        if (v.StartsWith("Assets/Resources/")) v = v.Substring("Assets/Resources/".Length);
        if (v.StartsWith("Resources/")) v = v.Substring("Resources/".Length);
        return v.TrimStart('/');
    }

    /*
    Editor/debug helper: rebuild the cache from Resources at runtime. Handy
    when tweaking import settings or shuffling sprite folders.
    */
    public void RebuildFromResources()
    {
        PreloadAll(maxDex);
        Debug.Log($"LoadSprites: Rebuilt from Resources; total {spritesByDex.Count} sprites.");
    }
}