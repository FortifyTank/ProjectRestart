using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class Pokemon
{
    public string name;
    public List<string> types;
    
    // --- HEALTH ---
    public int hp;
    public int maxHp;

    // --- BASE STATS (Used for calculation) ---
    // We keep these named "attack", "defense" etc so BattleManager doesn't break
    public int attack;
    public int defense;
    public int spAttack;
    public int spDefense;
    public int speed;

    // --- VOLATILE STATS (Reset on switch) ---
    // Stages go from -6 to +6. 
    public int statStageAttack = 0;
    public int statStageDefense = 0;
    public int statStageSpAttack = 0;
    public int statStageSpDefense = 0;
    public int statStageSpeed = 0;

    // --- STATUS CONDITIONS ---
    public string statusCondition = "None"; // "Burn", "Paralyze", etc.

    public List<string> moves;

    // Type Chart Cache
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

    // Helper to get the REAL stat value after Boosts
    public float GetModifiedStat(string statType)
    {
        float baseVal = 0;
        int stage = 0;

        switch(statType)
        {
            case "Attack": baseVal = attack; stage = statStageAttack; break;
            case "Defense": baseVal = defense; stage = statStageDefense; break;
            case "SpAttack": baseVal = spAttack; stage = statStageSpAttack; break;
            case "SpDefense": baseVal = spDefense; stage = statStageSpDefense; break;
            case "Speed": baseVal = speed; stage = statStageSpeed; break;
        }

        // Gen 3+ Formula:
        // Stage >= 0: Multiplier = (2 + Stage) / 2
        // Stage < 0:  Multiplier = 2 / (2 + |Stage|)
        float multiplier = (stage >= 0) ? (2f + stage) / 2f : 2f / (2f + Mathf.Abs(stage));
        
        return baseVal * multiplier;
    }
}

// [NEW] PARTY CLASS (For 6v6)
[System.Serializable]
public class PokemonParty
{
    public List<Pokemon> members = new List<Pokemon>();
    public int activeIndex = 0;

    public Pokemon GetActive()
    {
        if (members.Count == 0) return null;
        if (activeIndex >= members.Count) activeIndex = 0;
        return members[activeIndex];
    }
}

public class MoveData
{
    public string type;
    public string category; 
    public int power;
    public int id; // [NEW] Added ID for CSV matching

    public MoveData(string t, string c, int p, int id = 0)
    {
        type = t;
        category = c;
        power = p;
        this.id = id;
    }
}

public class MoveDatabase
{
    // This will be populated by MoveLoader
    public static Dictionary<string, MoveData> Moves = new Dictionary<string, MoveData>();

    // Fallback if CSV fails
    public static List<string> GetMovesForType(string type)
    {
        List<string> learnedMoves = new List<string>();
        learnedMoves.Add("Tackle");
        return learnedMoves;
    }
}