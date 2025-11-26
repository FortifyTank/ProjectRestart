using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

public class BattleManager : MonoBehaviour
{
    public UDPChatManager networkManager; 

    [Header("Player UI")]
    public Image playerImage; 
    public Slider playerHpBar; 
    public TMP_Text playerNameText; 

    [Header("Enemy UI")]
    public Image enemyImage; 
    public Slider enemyHpBar; 
    public TMP_Text enemyNameText; 

    [Header("Controls")]
    public Button[] moveButtons; 
    public TMP_Text[] moveButtonLabels; 

    private Pokemon myPokemon;
    private Pokemon enemyPokemon;
    private bool isGameOver = false;
    
    // State tracking for handshake
    private string pendingMoveName = "";
    private string lastMoveUsedByMe = "";

    void Start()
    {
        if (networkManager == null) networkManager = GetComponent<UDPChatManager>();
        SetButtonsInteractable(false);
    }

    public void SetupBattle(bool isHost)
    {
        isGameOver = false;
        Debug.Log($"Setting up battle. Am I Host? {isHost}");

        // 1. Load the CSV Database
        PokemonDatabase.LoadData();

        // 2. GET POKEMON (Updated)
        // "myPokemon" comes from what we clicked in the Pokedex
        // "enemyPokemon" is temporary placeholder until we receive BATTLE_SETUP from opponent
        
        string myName = PokemonSelector.UserSelection; 
        
        // Safety check
        if (string.IsNullOrEmpty(myName) || PokemonDatabase.GetPokemon(myName) == null)
        {
            myName = "Pikachu"; // Fallback
            Debug.LogWarning("Invalid selection, defaulting to Pikachu");
        }

        Pokemon p1 = PokemonDatabase.GetPokemon(myName);
        
        // For now, we just create a dummy enemy. 
        // The actual enemy data will be overwritten when the UDP handshake finishes.
        Pokemon p2 = PokemonDatabase.GetPokemon("Bulbasaur"); 

        if (isHost)
        {
            myPokemon = p1; 
            enemyPokemon = p2; 
            SetButtonsInteractable(true);
        }
        else
        {
            myPokemon = p2; // This logic is tricky in P2P, but we will fix it in the Networking step
            enemyPokemon = p1;
            SetButtonsInteractable(false);
        }
        
        // IMPORTANT: Actually set the objects
        // In P2P, "myPokemon" is always the one controlled by this client.
        myPokemon = p1;
        enemyPokemon = p2;

        UpdateBattleUI();
        
        // Send my chosen pokemon name to the opponent
        if (networkManager != null) networkManager.SendBattleSetup(myPokemon.name);
    }

    void UpdateBattleUI()
    {
        if (playerNameText != null) playerNameText.text = myPokemon.name;
        if (playerHpBar != null) { playerHpBar.maxValue = myPokemon.maxHp; playerHpBar.value = myPokemon.hp; }

        if (enemyNameText != null) enemyNameText.text = enemyPokemon.name;
        if (enemyHpBar != null) { enemyHpBar.maxValue = enemyPokemon.maxHp; enemyHpBar.value = enemyPokemon.hp; }

        for (int i = 0; i < moveButtons.Length; i++)
        {
            if (i < myPokemon.moves.Count)
            {
                moveButtons[i].gameObject.SetActive(true);
                if(moveButtonLabels[i] != null) moveButtonLabels[i].text = myPokemon.moves[i];
                
                int index = i; 
                moveButtons[i].onClick.RemoveAllListeners();
                moveButtons[i].onClick.AddListener(() => OnMoveSelected(index));
            }
            else moveButtons[i].gameObject.SetActive(false);
        }
    }

    // --- BATTLE LOGIC ---

    public void OnMoveSelected(int moveIndex)
    {
        if (isGameOver) return;
        string moveName = myPokemon.moves[moveIndex];
        lastMoveUsedByMe = moveName;
        
        if (networkManager != null) networkManager.SendAttackAnnounce(moveName);
        SetButtonsInteractable(false);
    }

    public void OnOpponentAttackAnnounce(string moveName)
    {
        if (isGameOver) return;
        pendingMoveName = moveName;
        // RFC Step: Acknowledge attack
        if (networkManager != null) networkManager.SendDefenseAnnounce();
    }

    public void OnDefenseAnnounceReceived()
    {
        if (string.IsNullOrEmpty(lastMoveUsedByMe)) return;
        
        // Calculate damage
        int damage = PerformAttack(lastMoveUsedByMe, myPokemon, enemyPokemon, enemyHpBar);
        
        // Send Report (Updated with myPokemon.hp as the 5th argument)
        if (networkManager != null)
        {
            networkManager.SendCalculationReport(
                myPokemon.name,      // Attacker Name
                lastMoveUsedByMe,    // Move Name
                damage,              // Damage Dealt
                enemyPokemon.hp,     // Defender HP Remaining
                myPokemon.hp         // [NEW] Attacker HP Remaining (Required by RFC)
            );
        }
    }

    public void OnCalculationReport(int damageDealt, int hpRemaining)
    {
        if (isGameOver) return;

        // If I have a pending move (I am the defender)
        if (!string.IsNullOrEmpty(pendingMoveName))
        {
            // RFC Step: Independent verification
            int myCalculatedDamage = CalculateDamage(pendingMoveName, enemyPokemon, myPokemon);
            
            // Discrepancy Check
            if (Mathf.Abs(myCalculatedDamage - damageDealt) > 1)
            {
                Debug.LogWarning($"[DISCREPANCY] Opponent said {damageDealt}, I calculated {myCalculatedDamage}");
                int myCorrectHp = myPokemon.hp - myCalculatedDamage;
                if (networkManager != null)
                    networkManager.SendResolutionRequest(enemyPokemon.name, pendingMoveName, myCalculatedDamage, myCorrectHp);
                return;
            }

            // Apply Damage
            myPokemon.hp = hpRemaining;
            if (playerHpBar != null) playerHpBar.value = hpRemaining;

            if (networkManager != null) networkManager.SendCalculationConfirm();
            pendingMoveName = "";

            if (myPokemon.hp <= 0)
            {
                if (networkManager != null) networkManager.SendGameOver(enemyPokemon.name);
                OnGameOver(enemyPokemon.name);
            }
            else SetButtonsInteractable(true); // Turn flip
        }
        else
        {
            // I am the attacker, just visual update
            enemyPokemon.hp = hpRemaining;
            if (enemyHpBar != null) enemyHpBar.value = hpRemaining;
        }
    }

    public void OnResolutionRequest(int correctDamage, int correctHp)
    {
        enemyPokemon.hp = correctHp;
        if (enemyHpBar != null) enemyHpBar.value = correctHp;
        Debug.Log("Resolution Accepted.");
    }

    public void OnGameOver(string winner)
    {
        isGameOver = true;
        SetButtonsInteractable(false);
        Debug.Log($"GAME OVER! Winner: {winner}");
        if (playerNameText != null) playerNameText.text += (winner == myPokemon.name) ? " (WINNER)" : " (FAINTED)";
        if (enemyNameText != null) enemyNameText.text += (winner == enemyPokemon.name) ? " (WINNER)" : " (FAINTED)";
    }

    // --- RFC 6: DAMAGE CALCULATION ---
    // --- RFC 6: DAMAGE CALCULATION ---
    private int CalculateDamage(string moveName, Pokemon attacker, Pokemon defender)
    {
        if (!MoveDatabase.Moves.ContainsKey(moveName)) 
        {
            Debug.LogError($"Move {moveName} not found in database!");
            return 0;
        }
        MoveData move = MoveDatabase.Moves[moveName];

        // 1. Determine Stats (Physical vs Special)
        // RFC: "The formula uses the appropriate attack and defense stats"
        bool isPhysical = move.category == "Physical";
        float atkStat = isPhysical ? attacker.attack : attacker.spAttack;
        float defStat = isPhysical ? defender.defense : defender.spDefense;

        // 2. Type Effectiveness
        // RFC: Type1Effectiveness x Type2Effectiveness
        float type1Mult = TypeChart.GetEffectiveness(move.type, defender.types[0]);
        float type2Mult = (defender.types.Count > 1) ? TypeChart.GetEffectiveness(move.type, defender.types[1]) : 1.0f;
        float totalTypeMult = type1Mult * type2Mult;

        // 3. Formula
        // RFC: Damage = (BasePower * AttackerStat * Type1 * Type2) / DefenderStat
        float numerator = move.power * atkStat * totalTypeMult;
        float rawDamage = numerator / defStat;
        
        int finalDamage = Mathf.FloorToInt(rawDamage);
        if (finalDamage < 1) finalDamage = 1; // Minimum 1 damage rule usually applies

        // --- CONSOLIDATED DEBUG LOG FOR RUBRIC ---
        string defenderTypes = string.Join("/", defender.types);
        
        Debug.Log($"<color=cyan><b>[CALCULATION REPORT]</b></color> {attacker.name} used {moveName} on {defender.name}\n" +
                  $"----------------------------------------------------------------\n" +
                  $"<b>Context:</b>      Move: {move.type}/{move.category} ({move.power} Pwr) | Def Types: {defenderTypes}\n" +
                  $"<b>Stats Used:</b>   Atk: {atkStat} vs Def: {defStat}\n" +
                  $"<b>Effectiveness:</b> x{type1Mult} (Type1) * x{type2Mult} (Type2) = <b>x{totalTypeMult} Total</b>\n" +
                  $"<b>Equation:</b>     ({move.power} * {atkStat} * {totalTypeMult}) / {defStat}\n" +
                  $"<b>Result:</b>       {rawDamage:F2} -> <b><color=red>{finalDamage} DMG</color></b>\n" +
                  $"----------------------------------------------------------------");

        return finalDamage;
    }
    
    private int PerformAttack(string moveName, Pokemon attacker, Pokemon defender, Slider targetHealthBar)
    {
        int damage = CalculateDamage(moveName, attacker, defender);
        defender.hp -= damage;
        if (defender.hp < 0) defender.hp = 0;
        if (targetHealthBar != null) targetHealthBar.value = defender.hp;
        return damage;
    }

    public void SetButtonsInteractable(bool state)
    {
        foreach (var btn in moveButtons) if(btn != null) btn.interactable = state;
    }

    public void SetOpponentPokemon(string pokemonName)
    {
        Pokemon p = PokemonDatabase.GetPokemon(pokemonName);
        if (p != null)
        {
            enemyPokemon = p;
            UpdateBattleUI();
            Debug.Log($"Opponent is using: {pokemonName}");
        }
        else
        {
            Debug.LogError($"Could not find opponent pokemon: {pokemonName}");
        }
    }
}