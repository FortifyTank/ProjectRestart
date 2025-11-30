using UnityEngine;
using System.Collections.Generic;
using System.IO;
using System;
using System.Text.RegularExpressions;

/// <summary>
/// Loads Pokemon data from CSV and stores them in a static dictionary.
/// 
/// SPRITE LOADING (Legacy approach - no longer used):
/// - Originally attempted to load sprites via LoadSprites component
/// - This approach had timing/dependency issues
/// - Current implementation: BattleManager loads sprites directly using Resources.Load
/// - Sprite files must be at: Assets/Resources/Sprites/{pokedexId}.png
/// </summary>
public class PokemonDatabase : MonoBehaviour
{
    // Dictionary to store all loaded Pokemon by Name
    public static Dictionary<string, Pokemon> AllPokemon = new Dictionary<string, Pokemon>();
    public static bool IsLoaded = false;
    private static LoadSprites spriteLoader; // [DEPRECATED] No longer used for sprite loading

    // Load data from Resources/pokemon.csv
    public static void LoadData()
    {
        if (IsLoaded) return;

        // Find LoadSprites instance
        if (spriteLoader == null)
        {
            spriteLoader = UnityEngine.Object.FindObjectOfType<LoadSprites>();
            if (spriteLoader == null)
            {
                Debug.LogWarning("PokemonDatabase: No LoadSprites found in scene. Sprites will be null.");
            }
        }

        MoveLoader.LoadAllMoves();

        // [SPRITE LOADING - DEPRECATED]
        // This section attempts to load sprites during CSV parsing.
        // However, BattleManager now loads sprites directly in UpdateBattleUI() for better reliability.
        // This code remains for backward compatibility but is not actively used.
        // Load text file from Assets/Resources/pokemon_with_sprites.csv
        TextAsset csvFile = Resources.Load<TextAsset>("pokemon_with_sprites");
        if (csvFile == null)
        {
            Debug.LogError("CRITICAL: pokemon.csv not found in Resources folder!");
            return;
        }

        string[] lines = csvFile.text.Split('\n');
        
        // We assume the first line is the header
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
            
            // Safety check: If a row was parsed weirdly, skip it to prevent crash
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
                
                // Load sprite using CSV sprite column
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
                
                // --- NEW: Parse Resistance Columns Automatically ---
                // The CSV has columns like "against_bug", "against_dark"
                // We map these directly to the Pokemon's internal dictionary.
                
                string[] allTypes = { "bug", "dark", "dragon", "electric", "fairy", "fight", "fire", "flying", "ghost", "grass", "ground", "ice", "normal", "poison", "psychic", "rock", "steel", "water" };

                foreach (string typeKey in allTypes)
                {
                    // The CSV header is "against_bug", "against_fire", etc.
                    string headerName = "against_" + typeKey;
                    int colIndex = Array.IndexOf(headers, headerName);
                    
                    if (colIndex != -1)
                    {
                        // Parse the float value (e.g., 0.5, 2.0, 1)
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

                    // [NEW] RANDOMIZATION LOGIC
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
                        // If they have 4 or less moves, just give them all
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
        Debug.Log($"SUCCESS: Loaded {AllPokemon.Count} Pokemon from CSV!");
    }

    public static Pokemon GetPokemon(string name)
    {
        if (!IsLoaded) LoadData();

        if (AllPokemon.ContainsKey(name))
        {
            Pokemon original = AllPokemon[name];
            // [FIX] Added 'original.pokedexId' as the first argument
            Pokemon copy = new Pokemon(original.pokedexId, original.name, original.types, original.hp, original.attack, original.defense, original.spAttack, original.spDefense, original.speed);
            copy.sprite = original.sprite; // Copy sprite reference
            copy.moves = new List<string>(original.moves);
            copy.typeMultipliers = new Dictionary<string, float>(original.typeMultipliers); // Copy dictionary too!
            return copy;
        }
        
        Debug.LogError($"Pokemon '{name}' not found! Did you spell it correctly in BattleManager?");
        return null;
    }

    // [FIX] Improved CSV Parser using Regex to handle commas inside quotes
    private static string[] ParseCSVLine(string line)
    {
        // This regex splits by comma, BUT ignores commas that are inside quotes
        // Example: "Ability One, Ability Two", 100, 50 -> ["Ability One, Ability Two", "100", "50"]
        string pattern = ",(?=(?:[^\"]*\"[^\"]*\")*[^\"]*$)";
        
        string[] rawValues = Regex.Split(line, pattern);

        // Clean up the quotes
        for (int i = 0; i < rawValues.Length; i++)
        {
            rawValues[i] = rawValues[i].Trim(' ', '"');
        }

        return rawValues;
    }

    // Helper to handle empty strings safely
    private static int ParseInt(string val)
    {
        if (string.IsNullOrEmpty(val)) return 0;
        // Try parse, if fail return 0
        if (int.TryParse(val, out int result)) return result;
        return 0;
    }
}