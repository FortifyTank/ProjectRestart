using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// (If you have GameConstants.cs, keep Enums there. If not, uncomment them here.)
/* public enum StatusCondition { None, Paralysis, Sleep, Freeze, Burn, Poison }
   public enum StatID { HP=1, Attack=2, Defense=3, ... } */

[System.Serializable]
public class Pokemon 
{
    public string name;
    public int pokedexId; 
    public int hp;
    public int maxHp;
    public int attack;
    public int defense;
    public int spAttack;
    public int spDefense;
    public int speed;
    
    public List<string> types = new List<string>();
    public List<string> moves = new List<string>();
    
    // Dictionary for Type Effectiveness (e.g. "Fire" -> 2.0)
    public Dictionary<string, float> typeMultipliers = new Dictionary<string, float>();

    // Stats & Status
    public int stageAtk = 0;
    public int stageDef = 0;
    public int stageSpAtk = 0;
    public int stageSpDef = 0;
    public int stageSpeed = 0;
    public StatusCondition status = StatusCondition.None;
    public int sleepTurns = 0; 

    // --- FIXED CONSTRUCTOR ---
    // Now accepts List<string> _types instead of string _type
    public Pokemon(int id, string _name, List<string> _types, int _hp, int _atk, int _def, int _spAtk, int _spDef, int _spd)
    {
        pokedexId = id;
        name = _name;
        types = _types; // Assign the list directly!
        maxHp = _hp;
        hp = _hp;
        attack = _atk;
        defense = _def;
        spAttack = _spAtk;
        spDefense = _spDef;
        speed = _spd;
    }

    // Constructor Overload for backward compatibility (just in case)
    public Pokemon() {}
    
    public void ResetStages() { stageAtk = 0; stageDef = 0; stageSpAtk = 0; stageSpDef = 0; stageSpeed = 0; }
    public void HealStatus() { status = StatusCondition.None; sleepTurns = 0; }
}