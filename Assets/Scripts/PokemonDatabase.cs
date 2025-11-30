using UnityEngine;
using System.Collections.Generic;
using System.IO;
using System;
using System.Text.RegularExpressions;

/// <summary>
/// Loads Pokémon from a CSV into a static dictionary for quick lookups.
/// It used to handle sprites through a separate loader, but that caused timing
/// issues, so the project now loads sprites directly from Resources in the
/// battle UI. Sprite files should live at `Assets/Resources/Sprites/{pokedexId}.png`.
/// </summary>
public class PokemonDatabase : MonoBehaviour
{
    // Dictionary to store all loaded Pokemon by Name
    public static Dictionary<string, Pokemon> AllPokemon = new Dictionary<string, Pokemon>();
    public static bool IsLoaded = false;
    private static LoadSprites spriteLoader; // [DEPRECATED] No longer used for sprite loading

    /*
    Reads the CSV from Resources, builds Pokémon objects with stats, types,
    and moves, and puts them in a global dictionary. Also tries to hook up a
    sprite via the legacy loader if present, but the main UI loads sprites
    directly. Safe-parses each row and skips anything that looks broken.
    */
    public static void LoadData()
    {
        if (IsLoaded) return;

        // Try to find the legacy sprite loader (optional)
        if (spriteLoader == null)
        {
            spriteLoader = UnityEngine.Object.FindObjectOfType<LoadSprites>();
            if (spriteLoader == null)
            {
                Debug.LogWarning("PokemonDatabase: No LoadSprites found in scene. Sprites will be null.");
            }
        }

        MoveLoader.LoadAllMoves();

        // Note: sprite loading here is legacy. Battle UI grabs sprites directly.
        // Load text file from Assets/Resources/pokemon_with_sprites.csv
        TextAsset csvFile = Resources.Load<TextAsset>("pokemon_with_sprites");
        if (csvFile == null)
        {
            Debug.LogError("CRITICAL: pokemon.csv not found in Resources folder!");
            return;
        }

        string[] lines = csvFile.text.Split('\n');
        
        // First line is the header
        string[] headers = ParseCSVLine(lines[0]);
        
        // Find indices dynamically so column order doesn't matter
        int nameIndex = Array.IndexOf(headers, "name");
        int hpIndex = Array.IndexOf(headers, "hp");
        int atkIndex = Array.IndexOf(headers, "attack");
        int defIndex = Array.IndexOf(headers, "defense");
        int spAtkIndex = Array.IndexOf(headers, "sp_attack");
        int spDefIndex = Array.IndexOf(headers, "sp_defense");
        int speedIndex = Array.IndexOf(headers, "speed");

        int idIndex = Array.IndexOf(headers, "pokedex_number"); // [NEW] Find the ID column

        int type1Index = Array.IndexOf(headers, "type1");
        int type2Index = Array.IndexOf(headers, "type2");
        int spriteIndex = Array.IndexOf(headers, "sprite"); // [NEW] Find sprite column

        if (nameIndex == -1 || hpIndex == -1) 
        {
            Debug.LogError("CRITICAL: Could not find required columns in CSV header!");
            return;
        }

        for (int i = 1; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i])) continue;

            string[] data = ParseCSVLine(lines[i]);
            
            // If a row looks off (wrong number of columns), skip it
            if (data.Length < headers.Length) 
            {
                // Debug.LogWarning($"Skipping line {i}: Not enough columns.");
                continue; 
            }

            try 
            {
                string name = data[nameIndex];
                
                // [NEW] Parse the ID (Default to 0 if missing)
                int id = (idIndex != -1) ? ParseInt(data[idIndex]) : 0; 

                // [FIX] Existing stats parsing...
                int hp = ParseInt(data[hpIndex]);
                int atk = ParseInt(data[atkIndex]);
                int def = ParseInt(data[defIndex]);
                int spAtk = ParseInt(data[spAtkIndex]);
                int spDef = ParseInt(data[spDefIndex]);
                int speed = ParseInt(data[speedIndex]);
                
                List<string> types = new List<string>();
                types.Add(data[type1Index]);
                if (!string.IsNullOrEmpty(data[type2Index])) types.Add(data[type2Index]);

                // [FIX] Pass 'id' as the FIRST argument now
                Pokemon p = new Pokemon(id, name, types, hp, atk, def, spAtk, spDef, speed);
                
                // Try loading a sprite using the CSV sprite column
                if (spriteLoader != null && spriteIndex != -1 && !string.IsNullOrEmpty(data[spriteIndex]))
                {
                    string spritePath = data[spriteIndex];
                    p.sprite = spriteLoader.LoadSpriteFromCsvField(spritePath);
                    if (p.sprite != null)
                    {
                        Debug.Log($"Loaded sprite for {name} from path: {spritePath}");
                    }
                    else
                    {
                        Debug.LogWarning($"Failed to load sprite for {name} from path: {spritePath}");
                    }
                }
                else if (spriteLoader != null && id > 0)
                {
                    // Fallback: use pokedex number if sprite column missing
                    p.sprite = spriteLoader.LoadSpriteByNumber(id);
                }
                
                // Pull type effectiveness values like "against_bug" and store them
                
                string[] allTypes = { "bug", "dark", "dragon", "electric", "fairy", "fight", "fire", "flying", "ghost", "grass", "ground", "ice", "normal", "poison", "psychic", "rock", "steel", "water" };

                foreach (string typeKey in allTypes)
                {
                    // The CSV header is "against_bug", "against_fire", etc.
                    string headerName = "against_" + typeKey;
                    int colIndex = Array.IndexOf(headers, headerName);
                    
                    if (colIndex != -1)
                    {
                        // Parse numbers like 0.5, 2.0, 1
                        if (float.TryParse(data[colIndex], out float mult))
                        {
                            p.typeMultipliers[typeKey] = mult;
                        }
                    }
                }
                // ---------------------------------------------------

                // Auto-assign moves based on Type
                if (MoveLoader.Learnsets.ContainsKey(p.pokedexId) && MoveLoader.Learnsets[p.pokedexId].Count > 0)
                {
                    List<string> validMoves = MoveLoader.Learnsets[p.pokedexId];

                    // Pick up to 4 random moves from the learnset
                    if (validMoves.Count > 4)
                    {
                        // Shuffle the list locally so we don't mess up the master list
                        List<string> pool = new List<string>(validMoves);
                        p.moves = new List<string>();

                        for (int k = 0; k < 4; k++)
                        {
                            int randIndex = UnityEngine.Random.Range(0, pool.Count);
                            p.moves.Add(pool[randIndex]);
                            pool.RemoveAt(randIndex); // Remove so we don't pick it twice
                        }
                    }
                    else
                    {
                        // If there are 4 or fewer, just use all of them
                        p.moves = new List<string>(validMoves);
                    }
                }
                else
                {
                    // Fallback if no moves found
                    p.moves = new List<string> { "tackle" }; 
                    // Debug.LogWarning($"No moves found for {p.name} (ID: {p.pokedexId})! Defaulting to Tackle.");
                }

                if (!AllPokemon.ContainsKey(name))
                {
                    AllPokemon.Add(name, p);
                }
            }
            catch (System.Exception e)
            {
                // This catches weird rows so the whole game doesn't crash
                // Debug.LogWarning($"Skipping line {i}: {e.Message}");
            }
        }

        IsLoaded = true;
        Debug.Log($"SUCCESS: Loaded {AllPokemon.Count} Pokémon from CSV!");
    }

    /*
    Returns a fresh copy of a Pokémon by name so callers don’t modify the
    shared template. Loads the database on demand if needed.
    */
    public static Pokemon GetPokemon(string name)
    {
        if (!IsLoaded) LoadData();

        if (AllPokemon.ContainsKey(name))
        {
            Pokemon original = AllPokemon[name];
            // [FIX] Added 'original.pokedexId' as the first argument
            Pokemon copy = new Pokemon(original.pokedexId, original.name, original.types, original.hp, original.attack, original.defense, original.spAttack, original.spDefense, original.speed);
            copy.sprite = original.sprite; // Keep the same sprite reference
            copy.moves = new List<string>(original.moves);
            copy.typeMultipliers = new Dictionary<string, float>(original.typeMultipliers); // Copy the dictionary too
            return copy;
        }
        
        Debug.LogError($"Pokemon '{name}' not found! Did you spell it correctly in BattleManager?");
        return null;
    }

    /*
    Improved CSV splitter using a regex that ignores commas inside quotes,
    so fields like "Ability One, Ability Two" don’t get split in the middle.
    */
    private static string[] ParseCSVLine(string line)
    {
        // Example: "Ability One, Ability Two", 100, 50 -> ["Ability One, Ability Two", "100", "50"]
        string pattern = ",(?=(?:[^\"]*\"[^\"]*\")*[^\"]*$)";
        
        string[] rawValues = Regex.Split(line, pattern);

        // Clean up quotes and spaces
        for (int i = 0; i < rawValues.Length; i++)
        {
            rawValues[i] = rawValues[i].Trim(' ', '"');
        }

        return rawValues;
    }

    /*
    Safe integer parse that treats empty or bad values as 0.
    */
    private static int ParseInt(string val)
    {
        if (string.IsNullOrEmpty(val)) return 0;
        // Try parse, if fail return 0
        if (int.TryParse(val, out int result)) return result;
        return 0;
    }
}