Dictionary<int, Sprite> spritesByDex = new Dictionary<int, Sprite>();

void PreloadAll(int maxDex = 801)
{
    for (int i = 1; i <= maxDex; i++)
    {
        var s = Resources.Load<Sprite>($"Sprites/{i}");
        if (s != null) spritesByDex[i] = s;
    }
}

Sprite LoadSpriteByNumber(int pokedexNumber)
{
    return spritesByDex.TryGetValue(pokedexNumber, out var sp) ? sp : null;
}

Sprite LoadSpriteFromCsvField(string spritePathWithoutExt)
{
    return Resources.Load<Sprite>(spritePathWithoutExt);
}

string WithoutExtension(string v)
{
    if (v.EndsWith(".png")) v = v.Substring(0, v.Length - 4);
    if (v.StartsWith("Assets/Resources/")) v = v.Substring("Assets/Resources/".Length);
    if (v.StartsWith("Resources/")) v = v.Substring("Resources/".Length);
    return v.TrimStart('/');
}