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

    public int stageAtk = 0;
    public int stageDef = 0;
    public int stageSpAtk = 0;
    public int stageSpDef = 0;
    public int stageSpeed = 0;

    public void ResetStages()
    {
        stageAtk = 0;
        stageDef = 0;
        stageSpAtk = 0;
        stageSpDef = 0;
        stageSpeed = 0;
    }
}

public class StatChangeEntry
{
    public int statId;
    public int changeAmount;
}

// 2. Add the list to your MoveData
public class MoveData
{
    public string name;
    public int id; // Make sure you have the ID stored!
    public string type;
    public int power;
    public int accuracy;
    public int pp;
    public int damageClassId; // 1 = Status, 2 = Physical, 3 = Special
    
    // [NEW] The list of changes this move causes
    public List<StatChangeEntry> statChanges = new List<StatChangeEntry>(); 
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