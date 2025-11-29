using System.Collections.Generic;

// --- GLOBAL ENUMS (Visible to ALL scripts) ---

public enum StatusCondition 
{ 
    None = 0, 
    Paralysis = 1, 
    Sleep = 2, 
    Freeze = 3, 
    Burn = 4, 
    Poison = 5 
}

public enum StatID 
{ 
    HP = 1, 
    Attack = 2, 
    Defense = 3, 
    SpAttack = 4, 
    SpDefense = 5, 
    Speed = 6, 
    Accuracy = 7, 
    Evasion = 8 
}

// --- SHARED HELPER CLASSES ---

[System.Serializable]
public class StatChangeEntry
{
    public int statId;
    public int changeAmount;
}