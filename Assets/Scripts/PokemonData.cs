using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class Pokemon
{
    public int pokedexId;
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
    
    public Pokemon(int id, string name, List<string> types, int hp, int atk, int def, int spAtk, int spDef, int spd)
    {
        this.pokedexId = id;
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
    public int id; // [NEW] Added ID field

    // [FIX] Constructor now accepts 4 arguments
    public MoveData(string t, string c, int p, int i)
    {
        type = t;
        category = c;
        power = p;
        id = i;
    }
}

public class MoveDatabase
{
    public static Dictionary<string, MoveData> Moves = new Dictionary<string, MoveData>()
    {
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
                if (!learnedMoves.Contains(move.Key)) learnedMoves.Add(move.Key);
                if (learnedMoves.Count >= 4) break;
            }
        }
        return learnedMoves;
    }
}