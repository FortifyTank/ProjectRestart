using UnityEngine;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System;

public class MoveLoader : MonoBehaviour
{
    // Maps CSV IDs to Strings (e.g., 1 -> "Normal")
    public static Dictionary<int, string> TypeIdMap = new Dictionary<int, string>()
    {
        {1, "Normal"}, {2, "Fighting"}, {3, "Flying"}, {4, "Poison"}, {5, "Ground"},
        {6, "Rock"}, {7, "Bug"}, {8, "Ghost"}, {9, "Steel"}, {10, "Fire"},
        {11, "Water"}, {12, "Grass"}, {13, "Electric"}, {14, "Psychic"}, {15, "Ice"},
        {16, "Dragon"}, {17, "Dark"}, {18, "Fairy"}
    };

    // Maps Damage Class IDs (1=Status, 2=Physical, 3=Special)
    public static Dictionary<int, string> CategoryMap = new Dictionary<int, string>()
    {
        {1, "Status"}, {2, "Physical"}, {3, "Special"}
    };

    // [NEW] The Bridge: ID -> Name (e.g., 33 -> "Tackle")
    public static Dictionary<int, string> MoveIdToName = new Dictionary<int, string>();

    // Cache: Pokemon ID -> List of Move Names
    public static Dictionary<int, List<string>> Learnsets = new Dictionary<int, List<string>>();

    public static bool IsLoaded = false;

    public static void LoadAllMoves()
    {
        if (IsLoaded) return;

        LoadMoveDatabase();
        LoadLearnsets();
        
        IsLoaded = true;
        Debug.Log($"[MoveLoader] Ready! Loaded {MoveDatabase.Moves.Count} moves and learnsets for {Learnsets.Count} Pokemon.");
    }

    private static void LoadMoveDatabase()
    {
        TextAsset file = Resources.Load<TextAsset>("moves");
        if (!file) { Debug.LogError("moves.csv missing!"); return; }

        string[] lines = file.text.Split('\n');
        
        // CSV Cols: id(0), identifier(1), ... type_id(3), power(4), ... damage_class_id(9)
        for (int i = 1; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i])) continue;
            string[] data = ParseCSVLine(lines[i]);

            try 
            {
                int id = int.Parse(data[0]); // [NEW] Grab the ID
                string rawName = data[1]; 
                string cleanName = FormatName(rawName);

                int typeId = int.Parse(data[3]);
                string typeName = TypeIdMap.ContainsKey(typeId) ? TypeIdMap[typeId] : "Normal";

                int power = ParseInt(data[4]); 
                int catId = int.Parse(data[9]);
                string category = CategoryMap.ContainsKey(catId) ? CategoryMap[catId] : "Status";

                // Add to Database
                if (!MoveDatabase.Moves.ContainsKey(cleanName))
                {
                    MoveDatabase.Moves.Add(cleanName, new MoveData(typeName, category, power, id));
                }

                // [NEW] Build the Bridge
                if (!MoveIdToName.ContainsKey(id))
                {
                    MoveIdToName.Add(id, cleanName);
                }
            }
            catch { }
        }
    }

    private static void LoadLearnsets()
    {
        TextAsset file = Resources.Load<TextAsset>("pokemon_moves");
        if (!file) { Debug.LogError("pokemon_moves.csv missing!"); return; }

        string[] lines = file.text.Split('\n');

        // CSV Cols: pokemon_id(0), version_group_id(1), move_id(2) ...
        for (int i = 1; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i])) continue;
            string[] data = ParseCSVLine(lines[i]);

            try
            {
                int versionGroup = int.Parse(data[1]);
                // FILTER: Only load Gen 7 (Sun/Moon = 18) to save memory/time
                // If you want ALL moves, remove this line, but it might lag startup.
                if (versionGroup != 18) continue; 

                int pokeId = int.Parse(data[0]);
                int moveId = int.Parse(data[2]);

                // [NEW] Use the Bridge to find the name
                if (MoveIdToName.ContainsKey(moveId))
                {
                    string moveName = MoveIdToName[moveId];

                    if (!Learnsets.ContainsKey(pokeId))
                    {
                        Learnsets[pokeId] = new List<string>();
                    }

                    // Avoid duplicates
                    if (!Learnsets[pokeId].Contains(moveName))
                    {
                        Learnsets[pokeId].Add(moveName);
                    }
                }
            }
            catch { }
        }
    }

    // Helper to cleanup text "mega-punch" -> "Mega Punch"
    public static string FormatName(string input)
    {
        if (string.IsNullOrEmpty(input)) return "";
        string[] words = input.Split('-');
        for (int i = 0; i < words.Length; i++)
            if (words[i].Length > 0) words[i] = char.ToUpper(words[i][0]) + words[i].Substring(1);
        return string.Join(" ", words);
    }

    private static string[] ParseCSVLine(string line)
    {
        string pattern = ",(?=(?:[^\"]*\"[^\"]*\")*[^\"]*$)";
        return Regex.Split(line, pattern);
    }

    private static int ParseInt(string val)
    {
        if (string.IsNullOrEmpty(val)) return 0;
        if (int.TryParse(val, out int result)) return result;
        return 0;
    }
}