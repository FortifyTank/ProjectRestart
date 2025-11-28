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

    // [NEW] Stores effectiveness data from CSV (e.g., "fire": 2.0)
    public Dictionary<string, float> typeMultipliers = new Dictionary<string, float>(); 
    
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
        // Add more moves here if needed
    };

    // Helper to auto-assign moves when loading from CSV
    public static List<string> GetMovesForType(string type)
    {
        List<string> learnedMoves = new List<string>();
        
        // Default move
        learnedMoves.Add("Tackle");

        foreach (var move in Moves)
        {
            if (move.Value.type.Equals(type, System.StringComparison.OrdinalIgnoreCase))
            {
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