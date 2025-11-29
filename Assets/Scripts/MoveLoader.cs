using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Text.RegularExpressions; // Needed for splitting

[System.Serializable]
public class MoveData
{
    public string name;
    public int id;
    public string type;
    public int power;
    public int accuracy;
    public int pp;
    public string category; 
    public int damageClassId; 
    public int ailmentId;     
    public int ailmentChance; 
    public List<StatChangeEntry> statChanges = new List<StatChangeEntry>();
}

public class MoveLoader : MonoBehaviour
{
    public static Dictionary<string, MoveData> Moves = new Dictionary<string, MoveData>();
    public static Dictionary<int, MoveData> MovesById = new Dictionary<int, MoveData>();
    public static Dictionary<int, List<string>> Learnsets = new Dictionary<int, List<string>>();

    [Header("CSV Files")]
    public TextAsset movesCsv;
    public TextAsset moveMetaCsv;
    public TextAsset moveStatChangesCsv;
    public TextAsset pokemonMovesCsv; // Make sure this is assigned!

    void Awake()
    {
        LoadMoves();
        if(moveMetaCsv != null) LoadMoveMeta();
        if(moveStatChangesCsv != null) LoadStatChanges();
        if(pokemonMovesCsv != null) LoadLearnsets();
    }

    public static void LoadAllMoves() { } // Stub

    void LoadMoves()
    {
        if (movesCsv == null) return;
        Moves.Clear();
        MovesById.Clear();

        string[] lines = movesCsv.text.Split('\n');
        
        // Loop starts at 1 to skip Header
        for (int i = 1; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            if (string.IsNullOrEmpty(line)) continue;
            
            // Simple split (if your CSV has no commas inside quotes)
            string[] parts = line.Split(',');
            
            // Safety: Skip rows that look broken (too short)
            if (parts.Length < 10) continue;

            try
            {
                MoveData m = new MoveData();
                
                // USE SAFE PARSING HERE
                m.id = ParseIntSafe(parts[0]);
                m.name = parts[1]; 
                
                // Parse Type ID (Col 3) and convert to Name
                int typeId = ParseIntSafe(parts[3]); 
                m.type = GetTypeFromId(typeId); 

                // Power/PP can be empty in CSV, so we check
                m.power = (parts[4] == "") ? 0 : ParseIntSafe(parts[4]);
                m.pp = (parts[5] == "") ? 0 : ParseIntSafe(parts[5]);
                
                m.damageClassId = ParseIntSafe(parts[9]);

                m.category = (m.damageClassId == 2) ? "Physical" : (m.damageClassId == 3) ? "Special" : "Status";

                // Add to Dictionaries
                // Convert key to Lowercase to fix "Tackle" vs "tackle" issues
                string key = m.name.ToLower();
                
                if (!Moves.ContainsKey(key)) Moves.Add(key, m);
                if (!MovesById.ContainsKey(m.id)) MovesById.Add(m.id, m);
            }
            catch (System.Exception e)
            {
                // This prints the EXACT line that caused the crash so you can fix the CSV
                Debug.LogError($"CRITICAL ERROR parsing Moves.csv at Line {i}: '{line}'. Error: {e.Message}");
            }
        }
        Debug.Log($"Successfully loaded {Moves.Count} moves.");
    }

    // --- NEW HELPER: Prevents "Input string was not in a correct format" ---
    int ParseIntSafe(string val)
    {
        if (string.IsNullOrEmpty(val)) return 0;
        if (int.TryParse(val, out int result)) return result;
        return 0; // Return 0 if it's text or garbage
    }

    string GetTypeFromId(int id)
    {
        switch(id)
        {
            case 1: return "Normal";
            case 2: return "Fighting";
            case 3: return "Flying";
            case 4: return "Poison";
            case 5: return "Ground";
            case 6: return "Rock";
            case 7: return "Bug";
            case 8: return "Ghost";
            case 9: return "Steel";
            case 10: return "Fire";
            case 11: return "Water";
            case 12: return "Grass";
            case 13: return "Electric";
            case 14: return "Psychic";
            case 15: return "Ice";
            case 16: return "Dragon";
            case 17: return "Dark";
            case 18: return "Fairy";
            default: return "Normal";
        }
    }

    void LoadMoveMeta()
    {
        string[] lines = moveMetaCsv.text.Split('\n');
        for (int i = 1; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            if (string.IsNullOrEmpty(line)) continue;
            string[] parts = line.Split(',');
            if (parts.Length >= 11)
            {
                int id = ParseIntSafe(parts[0]);
                if (MovesById.ContainsKey(id))
                {
                    MovesById[id].ailmentId = ParseIntSafe(parts[2]);
                    MovesById[id].ailmentChance = ParseIntSafe(parts[10]);
                }
            }
        }
    }

    void LoadStatChanges()
    {
        string[] lines = moveStatChangesCsv.text.Split('\n');
        for (int i = 1; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            if (string.IsNullOrEmpty(line)) continue;
            string[] parts = line.Split(',');
            if (parts.Length >= 3)
            {
                int id = ParseIntSafe(parts[0]);
                if (MovesById.ContainsKey(id))
                {
                    MovesById[id].statChanges.Add(new StatChangeEntry { 
                        statId = ParseIntSafe(parts[1]), 
                        changeAmount = ParseIntSafe(parts[2]) 
                    });
                }
            }
        }
    }
    
    void LoadLearnsets()
    {
        // This stops the crash if you forgot to assign the CSV in inspector
        if (pokemonMovesCsv == null) { Debug.LogWarning("PokemonMovesCsv is NULL!"); return; }

        string[] lines = pokemonMovesCsv.text.Split('\n');
        Learnsets.Clear();

        for (int i = 1; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            if (string.IsNullOrEmpty(line)) continue;
            string[] parts = line.Split(',');
            
            if (parts.Length < 3) continue;

            int pokeId = ParseIntSafe(parts[0]);
            int moveId = ParseIntSafe(parts[2]);
            int methodId = ParseIntSafe(parts[3]); 
            
            // Load "Level Up" moves (Method 1)
            if (methodId == 1) 
            {
                if (!MovesById.ContainsKey(moveId)) continue;
                string moveName = MovesById[moveId].name; // This is the original name "Karate Chop"

                if (!Learnsets.ContainsKey(pokeId))
                    Learnsets[pokeId] = new List<string>();

                // Store as lowercase to match our BattleManager logic!
                string lowerName = moveName.ToLower();

                if (!Learnsets[pokeId].Contains(lowerName))
                {
                    Learnsets[pokeId].Add(lowerName);
                }
            }
        }
        Debug.Log($"Loaded learnsets for {Learnsets.Count} Pokemon.");
    }
}