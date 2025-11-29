#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System.IO;

public class BuildPokemonSpriteMap
{
    [MenuItem("Tools/Build Pokemon Sprite Map")]
    public static void Build()
    {
        var so = ScriptableObject.CreateInstance<PokemonSpriteMap>();
        so.map = new System.Collections.Generic.List<PokemonSpriteMap.Entry>();
        for (int i = 1; i <= 801; i++)
        {
            string path = $"Assets/Resources/Sprites/{i}.png";
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite != null) so.map.Add(new PokemonSpriteMap.Entry { dex = i, sprite = sprite });
        }
        AssetDatabase.CreateAsset(so, "Assets/Resources/pokemonSpriteMap.asset");
        AssetDatabase.SaveAssets();
    }
}
#endif