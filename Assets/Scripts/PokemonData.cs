using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class Pokemon
{
    public string name;
    public List<string> types;
    public int hp;
    public int maxHp;
    public int attack;
    public int defense;
    public int spAttack;
    public int spDefense;
    public int speed;
    public List<string> moves;
    
    public Pokemon(string name, List<string> types, int hp, int atk, int def, int spAtk, int spDef, int spd)
    {
        this.name = name;
        this.types = types;
        this.hp = hp;
        this.maxHp = hp;
        this.attack = atk;
        this.defense = def;
        this.spAttack = spAtk;
        this.spDefense = spDef;
        this.speed = spd;
        this.moves = new List<string>();
    }
}

public class MoveDatabase
{
    public static Dictionary<string, MoveData> Moves = new Dictionary<string, MoveData>()
    {
        { "Tackle", new MoveData("Normal", "Physical", 40) },
        { "Slash", new MoveData("Normal", "Physical", 70) },
        { "Flamethrower", new MoveData("Fire", "Special", 90) },
        { "Fire Spin", new MoveData("Fire", "Special", 35) },
        { "Ember", new MoveData("Fire", "Special", 40) },
        { "Hydro Pump", new MoveData("Water", "Special", 110) },
        { "Water Gun", new MoveData("Water", "Special", 40) },
        { "Bubble", new MoveData("Water", "Special", 40) },
        { "Vine Whip", new MoveData("Grass", "Physical", 45) },
        { "Solar Beam", new MoveData("Grass", "Special", 120) },
        { "Razor Leaf", new MoveData("Grass", "Physical", 55) },
        { "Wing Attack", new MoveData("Flying", "Physical", 60) },
        { "Peck", new MoveData("Flying", "Physical", 35) },
        { "Bite", new MoveData("Dark", "Physical", 60) },
        { "Crunch", new MoveData("Dark", "Physical", 80) },
        // Add more moves as needed to cover all types if you want!
    };

    // [THIS WAS MISSING] Helper to auto-assign moves when loading from CSV
    public static List<string> GetMovesForType(string type)
    {
        List<string> learnedMoves = new List<string>();
        
        // Default move
        learnedMoves.Add("Tackle");

        foreach (var move in Moves)
        {
            if (move.Value.type.Equals(type, System.StringComparison.OrdinalIgnoreCase))
            {
                // [FIX] Only add if we don't have it already
                if (!learnedMoves.Contains(move.Key))
                {
                    learnedMoves.Add(move.Key);
                }
                
                if (learnedMoves.Count >= 4) break;
            }
        }
        return learnedMoves;
    }
}

public class MoveData
{
    public string type;
    public string category; 
    public int power;

    public MoveData(string t, string c, int p)
    {
        type = t;
        category = c;
        power = p;
    }
}

public static class TypeChart
{
    public static float GetEffectiveness(string moveType, string pokemonType)
    {
        // 1. Normalize inputs to lowercase so "Water" matches "water"
        string atk = moveType.ToLower().Trim();
        string def = pokemonType.ToLower().Trim();

        // 1.0 is neutral, 2.0 super effective, 0.5 not very effective, 0.0 immune
        switch (atk)
        {
            case "normal":
                if (def == "rock" || def == "steel") return 0.5f;
                if (def == "ghost") return 0.0f;
                break;

            case "fire":
                if (def == "grass" || def == "ice" || def == "bug" || def == "steel") return 2.0f;
                if (def == "fire" || def == "water" || def == "rock" || def == "dragon") return 0.5f;
                break;

            case "water":
                if (def == "fire" || def == "ground" || def == "rock") return 2.0f;
                if (def == "water" || def == "grass" || def == "dragon") return 0.5f;
                break;

            case "grass":
                if (def == "water" || def == "ground" || def == "rock") return 2.0f;
                if (def == "fire" || def == "grass" || def == "poison" || def == "flying" || def == "bug" || def == "dragon" || def == "steel") return 0.5f;
                break;

            case "electric":
                if (def == "water" || def == "flying") return 2.0f;
                if (def == "ground") return 0.0f;
                if (def == "electric" || def == "grass" || def == "dragon") return 0.5f;
                break;

            case "ice":
                if (def == "grass" || def == "ground" || def == "flying" || def == "dragon") return 2.0f;
                if (def == "fire" || def == "water" || def == "ice" || def == "steel") return 0.5f;
                break;

            case "fighting":
                if (def == "normal" || def == "ice" || def == "rock" || def == "dark" || def == "steel") return 2.0f;
                if (def == "poison" || def == "flying" || def == "psychic" || def == "bug" || def == "fairy") return 0.5f;
                if (def == "ghost") return 0.0f;
                break;

            case "poison":
                if (def == "grass" || def == "fairy") return 2.0f;
                if (def == "poison" || def == "ground" || def == "rock" || def == "ghost") return 0.5f;
                if (def == "steel") return 0.0f;
                break;

            case "ground":
                if (def == "fire" || def == "electric" || def == "poison" || def == "rock" || def == "steel") return 2.0f;
                if (def == "grass" || def == "bug") return 0.5f;
                if (def == "flying") return 0.0f;
                break;

            case "flying":
                if (def == "grass" || def == "fighting" || def == "bug") return 2.0f;
                if (def == "electric" || def == "rock" || def == "steel") return 0.5f;
                break;

            case "psychic":
                if (def == "fighting" || def == "poison") return 2.0f;
                if (def == "psychic" || def == "steel") return 0.5f;
                if (def == "dark") return 0.0f;
                break;

            case "bug":
                if (def == "grass" || def == "psychic" || def == "dark") return 2.0f;
                if (def == "fire" || def == "fighting" || def == "poison" || def == "flying" || def == "ghost" || def == "steel" || def == "fairy") return 0.5f;
                break;

            case "rock":
                if (def == "fire" || def == "ice" || def == "flying" || def == "bug") return 2.0f;
                if (def == "fighting" || def == "ground" || def == "steel") return 0.5f;
                break;

            case "ghost":
                if (def == "psychic" || def == "ghost") return 2.0f;
                if (def == "dark") return 0.5f;
                if (def == "normal") return 0.0f;
                break;

            case "dragon":
                if (def == "dragon") return 2.0f;
                if (def == "steel") return 0.5f;
                if (def == "fairy") return 0.0f;
                break;

            case "dark":
                if (def == "psychic" || def == "ghost") return 2.0f;
                if (def == "fighting" || def == "dark" || def == "fairy") return 0.5f;
                break;

            case "steel":
                if (def == "ice" || def == "rock" || def == "fairy") return 2.0f;
                if (def == "fire" || def == "water" || def == "electric" || def == "steel") return 0.5f;
                break;

            case "fairy":
                if (def == "fighting" || def == "dragon" || def == "dark") return 2.0f;
                if (def == "fire" || def == "poison" || def == "steel") return 0.5f;
                break;
        }
        
        // Default neutral damage
        return 1.0f; 
    }
}