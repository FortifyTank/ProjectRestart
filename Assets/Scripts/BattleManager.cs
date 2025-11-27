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
    
    [Header("Stat Boost UI")]
    public Button spAttackBoostButton;      // Button to activate Special Attack boost
    public Button spDefenseBoostButton;     // Button to activate Special Defense boost
    public TMP_Text spAttackBoostText;      // Display: "SpAtk Boosts: 5"
    public TMP_Text spDefenseBoostText;     // Display: "SpDef Boosts: 5" 

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
        
        // ===== Setup Boost Button Listeners =====
        if (spAttackBoostButton != null)
        {
            spAttackBoostButton.onClick.AddListener(ActivateSpecialAttackBoost);
        }
        if (spDefenseBoostButton != null)
        {
            spDefenseBoostButton.onClick.AddListener(ActivateSpecialDefenseBoost);
        }
        // ========================================================
    }

    public void SetupBattle(bool isHost)
    {
        isGameOver = false;
        Debug.Log($"Setting up battle. Am I Host? {isHost}");

        // 1. Load the CSV Database
        PokemonDatabase.LoadData();

        // --- [NEW] SPECTATOR LOGIC ---
        if (networkManager.isSpectator)
        {
            Debug.Log("Spectator Mode Active: Initializing View...");

            // 1. Create dummies so the UI doesn't crash (Wait for network updates to fill real data)
            myPokemon = PokemonDatabase.GetPokemon("Bulbasaur");
            enemyPokemon = PokemonDatabase.GetPokemon("Charmander");

            // 2. Disable Controls completely
            SetButtonsInteractable(false);
            
            // Optional: Hide the button area entirely so it looks like a TV stream
            if (moveButtons.Length > 0 && moveButtons[0] != null)
            {
                foreach(var btn in moveButtons) 
            {
                if(btn != null) btn.gameObject.SetActive(false);
            }
            }

            // 3. Update UI with dummies
            UpdateBattleUI();
            
            // 4. IMPORTANT: Return immediately. Spectators do NOT send BATTLE_SETUP packets.
            return; 
        }
        // -----------------------------

        // 2. GET POKEMON (Standard Player Logic)
        string myName = PokemonSelector.UserSelection; 
        
        if (string.IsNullOrEmpty(myName) || PokemonDatabase.GetPokemon(myName) == null)
        {
            myName = "Pikachu"; 
            Debug.LogWarning("Invalid selection, defaulting to Pikachu");
        }

        Pokemon p1 = PokemonDatabase.GetPokemon(myName);
        // Dummy enemy for now
        Pokemon p2 = PokemonDatabase.GetPokemon("Bulbasaur"); 

        if (isHost)
        {
            myPokemon = p1; 
            enemyPokemon = p2; 
            SetButtonsInteractable(true);
        }
        else
        {
            myPokemon = p2; 
            enemyPokemon = p1;
            SetButtonsInteractable(false);
        }
        
        myPokemon = p1;
        enemyPokemon = p2;

        // ===== Allocate Stat Boosts =====
        // Players decide how many boosts they want during setup phase
        // For now, using default values (you can add UI for this later)
        myPokemon.specialAttackBoostsRemaining = 5;   // Default: 5 special attack boosts
        myPokemon.specialDefenseBoostsRemaining = 5;  // Default: 5 special defense boosts
        
        Debug.Log($"[SETUP] Allocated boosts: {myPokemon.specialAttackBoostsRemaining} SpAtk, {myPokemon.specialDefenseBoostsRemaining} SpDef");
        // =================================================

        UpdateBattleUI();
        
        // Standard Players send their info (INCLUDING stat boosts)
        if (networkManager != null)
        {
            networkManager.SendBattleSetup(
                myPokemon.name,
                myPokemon.specialAttackBoostsRemaining,
                myPokemon.specialDefenseBoostsRemaining
            );
        }
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
        
        // ===== Update Boost Counter Displays =====
        UpdateBoostDisplay();
        // ==========================================================
    }

    // --- BATTLE LOGIC ---

    public void OnMoveSelected(int moveIndex)
    {
        if (isGameOver) return;
        string moveName = myPokemon.moves[moveIndex];
        lastMoveUsedByMe = moveName;
        
        // Consistent format: "Charizard used Tackle!"
        networkManager.AddChatMessage("Battle", $"{myPokemon.name} used {moveName}!");
        
        if (networkManager != null) networkManager.SendAttackAnnounce(moveName);
        SetButtonsInteractable(false);
    }
    
    // ===== Boost Activation Methods =====
    /// <summary>
    /// Activates a Special Attack boost for the current turn (if available)
    /// Call this BEFORE selecting a Special move
    /// </summary>
    public void ActivateSpecialAttackBoost()
    {
        if (isGameOver) return;
        
        if (myPokemon.UseSpecialAttackBoost())
        {
            networkManager.AddChatMessage("System", $"{myPokemon.name} used Special Attack Boost! ({myPokemon.specialAttackBoostsRemaining} remaining)");
            UpdateBoostDisplay();  // Refresh UI
        }
        else
        {
            networkManager.AddChatMessage("System", $"{myPokemon.name} has no Special Attack boosts left!");
        }
    }
    
    /// <summary>
    /// Activates a Special Defense boost for the current turn (if available)
    /// Call this BEFORE the opponent attacks
    /// </summary>
    public void ActivateSpecialDefenseBoost()
    {
        if (isGameOver) return;
        
        if (myPokemon.UseSpecialDefenseBoost())
        {
            networkManager.AddChatMessage("System", $"{myPokemon.name} used Special Defense Boost! ({myPokemon.specialDefenseBoostsRemaining} remaining)");
            UpdateBoostDisplay();  // Refresh UI
        }
        else
        {
            networkManager.AddChatMessage("System", $"{myPokemon.name} has no Special Defense boosts left!");
        }
    }
    // ====================================================
    
    public void OnOpponentAttackAnnounce(string moveName)
    {
        // SPECTATOR: Just say a move happened (Since we can't be 100% sure who sent it without checking IP)
        if (networkManager.isSpectator) 
        {
            networkManager.AddChatMessage("Battle", $"A Pokemon used {moveName}!");
            return;
        }
        
        // PLAYER: Say the specific enemy name
        networkManager.AddChatMessage("Battle", $"{enemyPokemon.name} used {moveName}!");

        if (isGameOver) return;
        pendingMoveName = moveName;
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
                myPokemon.hp         // [NEW] Attacker HP Remaining 
            );
        }
    }

    public void OnCalculationReport(string attackerName, int damageDealt, int hpRemaining)
    {   
        // --- SPECTATOR LOGIC ---
        if (networkManager.isSpectator) 
        {
            // Determine who got hit based on who attacked
            if (attackerName == myPokemon.name) 
            {
                // Host Attacked -> Joiner (Enemy) took damage
                enemyPokemon.hp = hpRemaining;
                if(enemyHpBar != null) enemyHpBar.value = hpRemaining;
            }
            else if (attackerName == enemyPokemon.name)
            {
                // Joiner Attacked -> Host (My) took damage
                myPokemon.hp = hpRemaining;
                if(playerHpBar != null) playerHpBar.value = hpRemaining;
            }
            return; 
        }
        // -----------------------

        if (isGameOver) return;

        // If I have a pending move (I am the defender)
        if (!string.IsNullOrEmpty(pendingMoveName))
        {
            int myCalculatedDamage = CalculateDamage(pendingMoveName, enemyPokemon, myPokemon);
            
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
            else
            {
                // ===== Reset Boost Multipliers =====
                // Boosts only last for ONE turn, so reset them after damage is applied
                myPokemon.ResetBoostMultipliers();
                enemyPokemon.ResetBoostMultipliers();
                Debug.Log("[BOOST] Multipliers reset for next turn");
                // ===================================================
                
                SetButtonsInteractable(true);
            } 
        }
        else
        {
            // I am the attacker, just visual update
            enemyPokemon.hp = hpRemaining;
            if (enemyHpBar != null) enemyHpBar.value = hpRemaining;
            
            // =====  Reset Boost Multipliers After Attack =====
            // After attacker's turn completes, reset multipliers for both Pokemon
            myPokemon.ResetBoostMultipliers();
            enemyPokemon.ResetBoostMultipliers();
            // =================================================================
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
        if (isGameOver) return;
        isGameOver = true;
        SetButtonsInteractable(false);
        
        // 1. Print to Chat (Crucial for Spectators)
        networkManager.AddChatMessage("System", $"GAME OVER! Winner: {winner}");

        // 2. Update Top Labels
        if (playerNameText != null) 
        {
            if(winner == myPokemon.name) playerNameText.text += " (WINNER)";
            else playerNameText.text += " (FAINTED)";
        }
        
        if (enemyNameText != null) 
        {
            if(winner == enemyPokemon.name) enemyNameText.text += " (WINNER)";
            else enemyNameText.text += " (FAINTED)";
        }
    }

    // --- DAMAGE CALCULATION ---
    private int CalculateDamage(string moveName, Pokemon attacker, Pokemon defender)
    {
        if (!MoveDatabase.Moves.ContainsKey(moveName)) 
        {
            Debug.LogError($"Move {moveName} not found in database!");
            return 0;
        }
        MoveData move = MoveDatabase.Moves[moveName];

        // 1. Determine Stats (Physical vs Special)
        // "The formula uses the appropriate attack and defense stats"
        bool isPhysical = move.category == "Physical";
        float atkStat = isPhysical ? attacker.attack : attacker.spAttack;
        float defStat = isPhysical ? defender.defense : defender.spDefense;
        
        // ===== Apply Stat Boosts =====
        // If boosts are active, multiply the stats by their multipliers
        if (!isPhysical)
        {
            // Special move: Apply boost multipliers
            atkStat *= attacker.currentSpAttackMultiplier;
            defStat *= defender.currentSpDefenseMultiplier;
            
            // Log boost usage for debugging
            if (attacker.currentSpAttackMultiplier > 1.0f)
            {
                Debug.Log($"[BOOST ACTIVE] {attacker.name}'s Special Attack boosted! ({attacker.spAttack} × {attacker.currentSpAttackMultiplier} = {atkStat})");
            }
            if (defender.currentSpDefenseMultiplier > 1.0f)
            {
                Debug.Log($"[BOOST ACTIVE] {defender.name}'s Special Defense boosted! ({defender.spDefense} × {defender.currentSpDefenseMultiplier} = {defStat})");
            }
        }
        // =============================================

        // 2. Type Effectiveness
        // Type1Effectiveness x Type2Effectiveness
        float type1Mult = TypeChart.GetEffectiveness(move.type, defender.types[0]);
        float type2Mult = (defender.types.Count > 1) ? TypeChart.GetEffectiveness(move.type, defender.types[1]) : 1.0f;
        float totalTypeMult = type1Mult * type2Mult;

        // 3. Formula
        //  Damage = (BasePower * AttackerStat * Type1 * Type2) / DefenderStat
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
    
    //Update Boost Display UI =====
    /// <summary>
    /// Updates the boost counter text displays and button states
    /// </summary>
    private void UpdateBoostDisplay()
    {
        if (myPokemon == null) return;
        
        // Update Special Attack Boost Display
        if (spAttackBoostText != null)
        {
            spAttackBoostText.text = $"SpAtk Boosts: {myPokemon.specialAttackBoostsRemaining}";
        }
        
        // Update Special Defense Boost Display
        if (spDefenseBoostText != null)
        {
            spDefenseBoostText.text = $"SpDef Boosts: {myPokemon.specialDefenseBoostsRemaining}";
        }
        
        // Enable/Disable boost buttons based on availability
        if (spAttackBoostButton != null)
        {
            spAttackBoostButton.interactable = (myPokemon.specialAttackBoostsRemaining > 0) && !isGameOver;
        }
        
        if (spDefenseBoostButton != null)
        {
            spDefenseBoostButton.interactable = (myPokemon.specialDefenseBoostsRemaining > 0) && !isGameOver;
        }
    }
    // ====================================================

    // Accept stat boosts from BATTLE_SETUP message
    public void SetOpponentPokemon(string pokemonName, int spAtkBoosts = 5, int spDefBoosts = 5)
    {
        Pokemon p = PokemonDatabase.GetPokemon(pokemonName);
        if (p != null)
        {
            enemyPokemon = p;
            
            // Apply the opponent's allocated stat boosts
            enemyPokemon.specialAttackBoostsRemaining = spAtkBoosts;
            enemyPokemon.specialDefenseBoostsRemaining = spDefBoosts;
            
            Debug.Log($"Opponent is using: {pokemonName} (SpAtk Boosts: {spAtkBoosts}, SpDef Boosts: {spDefBoosts})");
            
            UpdateBattleUI();
        }
        else
        {
            Debug.LogError($"Could not find opponent pokemon: {pokemonName}");
        }
    }

    public string GetMyPokemonName() 
    { 
        return myPokemon != null ? myPokemon.name : "Unknown"; 
    }

    public string GetEnemyPokemonName() 
    { 
        return enemyPokemon != null ? enemyPokemon.name : "Unknown"; 
    }

    // Accept stat boosts from BATTLE_SETUP message (for spectators)
    public void SetMyPokemon(string pokemonName, int spAtkBoosts = 5, int spDefBoosts = 5)
    {
        Pokemon p = PokemonDatabase.GetPokemon(pokemonName);
        if (p != null)
        {
            myPokemon = p;
            
            // Apply the allocated stat boosts
            myPokemon.specialAttackBoostsRemaining = spAtkBoosts;
            myPokemon.specialDefenseBoostsRemaining = spDefBoosts;
            
            UpdateBattleUI();
        }
    }
}