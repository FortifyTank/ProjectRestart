using UnityEngine;
using System.Collections.Generic;
using System.IO;
using System;
using System.Text.RegularExpressions; 

public class PokemonDatabase : MonoBehaviour
{
    public static Dictionary<string, Pokemon> AllPokemon = new Dictionary<string, Pokemon>();
    public static bool IsLoaded = false;

    public static void LoadData()
    {
        if (IsLoaded) return;

        // Ensure Moves are loaded first!
        if (!MoveLoader.IsLoaded) MoveLoader.LoadAllMoves();

        TextAsset csvFile = Resources.Load<TextAsset>("pokemon");
        if (csvFile == null) { Debug.LogError("pokemon.csv missing!"); return; }

        string[] lines = csvFile.text.Split('\n');
        string[] headers = ParseCSVLine(lines[0]);
        
        int nameIndex = Array.IndexOf(headers, "name");
        int hpIndex = Array.IndexOf(headers, "hp");
        int atkIndex = Array.IndexOf(headers, "attack");
        int defIndex = Array.IndexOf(headers, "defense");
        int spAtkIndex = Array.IndexOf(headers, "sp_attack");
        int spDefIndex = Array.IndexOf(headers, "sp_defense");
        int speedIndex = Array.IndexOf(headers, "speed");
        int type1Index = Array.IndexOf(headers, "type1");
        int type2Index = Array.IndexOf(headers, "type2");
        int dexNumIndex = Array.IndexOf(headers, "pokedex_number"); // [NEW] Needed for Move Lookup

        for (int i = 1; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i])) continue;
            string[] data = ParseCSVLine(lines[i]);
            if (data.Length < headers.Length) continue; 

            try 
            {
                string name = data[nameIndex];
                int hp = ParseInt(data[hpIndex]);
                int atk = ParseInt(data[atkIndex]);
                int def = ParseInt(data[defIndex]);
                int spAtk = ParseInt(data[spAtkIndex]);
                int spDef = ParseInt(data[spDefIndex]);
                int speed = ParseInt(data[speedIndex]);
                
                List<string> types = new List<string>();
                types.Add(data[type1Index]);
                if (!string.IsNullOrEmpty(data[type2Index])) types.Add(data[type2Index]);

                Pokemon p = new Pokemon(name, types, hp, atk, def, spAtk, spDef, speed);
                
                // 1. Parse Type Effectiveness
                string[] allTypes = { "bug", "dark", "dragon", "electric", "fairy", "fight", "fire", "flying", "ghost", "grass", "ground", "ice", "normal", "poison", "psychic", "rock", "steel", "water" };
                foreach (string typeKey in allTypes)
                {
                    int colIndex = Array.IndexOf(headers, "against_" + typeKey);
                    if (colIndex != -1 && float.TryParse(data[colIndex], out float mult))
                        p.typeMultipliers[typeKey] = mult;
                }

                // 2. [NEW] Assign Real Moves from MoveLoader
                int pokeID = (dexNumIndex != -1) ? ParseInt(data[dexNumIndex]) : 0;
                
                if (MoveLoader.Learnsets.ContainsKey(pokeID))
                {
                    p.moves = new List<string>(MoveLoader.Learnsets[pokeID]);
                    // Limit to 4 moves for UI safety
                    if (p.moves.Count > 4) p.moves = p.moves.GetRange(0, 4);
                }
                else
                {
                    p.moves = MoveDatabase.GetMovesForType(types[0]);
                }

                if (!AllPokemon.ContainsKey(name)) AllPokemon.Add(name, p);
            }
            catch {}
        }

        IsLoaded = true;
        Debug.Log($"SUCCESS: Loaded {AllPokemon.Count} Pokemon!");
    }

    public static Pokemon GetPokemon(string name)
    {
        if (!IsLoaded) LoadData();
        if (AllPokemon.ContainsKey(name))
        {
            Pokemon original = AllPokemon[name];
            // Clone it so we don't modify the database
            Pokemon p = new Pokemon(original.name, original.types, original.hp, original.attack, original.defense, original.spAttack, original.spDefense, original.speed);
            p.moves = new List<string>(original.moves);
            p.typeMultipliers = new Dictionary<string, float>(original.typeMultipliers);
            return p;
        }
        return null;
    }

    private static string[] ParseCSVLine(string line)
    {
        string pattern = ",(?=(?:[^\"]*\"[^\"]*\")*[^\"]*$)";
        string[] rawValues = Regex.Split(line, pattern);
        for (int i = 0; i < rawValues.Length; i++) rawValues[i] = rawValues[i].Trim(' ', '"');
        return rawValues;
    }

    private static int ParseInt(string val)
    {
        if (int.TryParse(val, out int result)) return result;
        return 0;
    }
}