using UnityEngine;
using System.Collections.Generic;
using System.IO;
using System;
using System.Text.RegularExpressions; // [NEW] Needed for smart splitting

public class PokemonDatabase : MonoBehaviour
{
    // Dictionary to store all loaded Pokemon by Name
    public static Dictionary<string, Pokemon> AllPokemon = new Dictionary<string, Pokemon>();
    public static bool IsLoaded = false;

    // Load data from Resources/pokemon.csv
    public static void LoadData()
    {
        if (IsLoaded) return;

        // Load text file from Assets/Resources/pokemon.csv
        TextAsset csvFile = Resources.Load<TextAsset>("pokemon");
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
        int type1Index = Array.IndexOf(headers, "type1");
        int type2Index = Array.IndexOf(headers, "type2");

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
                
                // Fix: Some CSVs have empty fields, treat them as 0 or handle gracefully
                int hp = ParseInt(data[hpIndex]);
                int atk = ParseInt(data[atkIndex]);
                int def = ParseInt(data[defIndex]);
                int spAtk = ParseInt(data[spAtkIndex]);
                int spDef = ParseInt(data[spDefIndex]);
                int speed = ParseInt(data[speedIndex]);
                
                List<string> types = new List<string>();
                types.Add(data[type1Index]);
                if (!string.IsNullOrEmpty(data[type2Index])) types.Add(data[type2Index]);

                // Create the Pokemon Object
                Pokemon p = new Pokemon(name, types, hp, atk, def, spAtk, spDef, speed);
                
                // Auto-assign moves based on Type
                p.moves = MoveDatabase.GetMovesForType(types[0]);

                if (!AllPokemon.ContainsKey(name))
                {
                    AllPokemon.Add(name, p);
                }
            }
            catch (System.Exception)
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
            // Return a COPY so we don't modify the database original
            Pokemon original = AllPokemon[name];
            return new Pokemon(original.name, original.types, original.hp, original.attack, original.defense, original.spAttack, original.spDefense, original.speed) 
            { 
                moves = new List<string>(original.moves) 
            };
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