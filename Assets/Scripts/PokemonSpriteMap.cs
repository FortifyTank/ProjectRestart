using System.Collections.Generic;
using UnityEngine;

// Simple asset that pairs a Pokédex number with a Sprite.
// Useful for editor workflows or when avoiding direct Resources lookups.
[CreateAssetMenu(fileName = "pokemonSpriteMap", menuName = "Data/Pokemon Sprite Map")]
public class PokemonSpriteMap : ScriptableObject
{
    [System.Serializable]
    public class Entry
    {
        // Dex number for the Pokémon (e.g., 25 for Pikachu)
        public int dex;
        // The sprite to use for that dex number
        public Sprite sprite;
    }

    public List<Entry> map = new List<Entry>();

    // Runtime lookup cache
    private Dictionary<int, Sprite> _lookup;

    /*
    Returns the sprite for a given dex number. Builds a simple cache the first
    time it’s called so repeated lookups stay fast. Skips any empty entries
    and only stores valid sprite pairs.
    */
    public Sprite GetSprite(int dex)
    {
        if (_lookup == null)
        {
            _lookup = new Dictionary<int, Sprite>();
            if (map != null)
            {
                foreach (var e in map)
                {
                    if (e != null && e.sprite != null)
                        _lookup[e.dex] = e.sprite;
                }
            }
        }
        return _lookup.TryGetValue(dex, out var s) ? s : null;
    }
}
