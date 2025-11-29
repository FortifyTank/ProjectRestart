using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "pokemonSpriteMap", menuName = "Data/Pokemon Sprite Map")]
public class PokemonSpriteMap : ScriptableObject
{
    [System.Serializable]
    public class Entry
    {
        public int dex;
        public Sprite sprite;
    }

    public List<Entry> map = new List<Entry>();

    // Runtime lookup cache
    private Dictionary<int, Sprite> _lookup;

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
