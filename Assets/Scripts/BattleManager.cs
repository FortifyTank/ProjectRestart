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

    public List<Pokemon> myParty = new List<Pokemon>();
    public List<Pokemon> enemyParty = new List<Pokemon>(); // For the host to track the opponent
    public int myActiveIndex = 0;
    private bool isGameOver = false;
    
    // State tracking for handshake
    private string pendingMoveName = "";
    private string lastMoveUsedByMe = "";

    private Pokemon myPokemon;
    
    private Pokemon enemyPokemon;

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

        UpdateBattleUI();
        
        // Standard Players send their info
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
        
        // Consistent format: "Charizard used Tackle!"
        networkManager.AddChatMessage("Battle", $"{myPokemon.name} used {moveName}!");
        
        if (networkManager != null) networkManager.SendAttackAnnounce(moveName);
        SetButtonsInteractable(false);
    }
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
                myPokemon.hp         // [NEW] Attacker HP Remaining (Required by RFC)
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
            else SetButtonsInteractable(true); 
        }
        else
        {
            // I am the attacker, just visual update
            enemyPokemon.hp = hpRemaining;
            if (enemyHpBar != null) enemyHpBar.value = hpRemaining;
        }
        SendSpectatorUpdate();
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
        bool isPhysical = move.category == "Physical";
        float atkStat = isPhysical ? attacker.attack : attacker.spAttack;
        float defStat = isPhysical ? defender.defense : defender.spDefense;

        // 2. Type Effectiveness (RFC Compliant via CSV)
        float totalTypeMult = 1.0f;
        string moveTypeLower = move.type.ToLower();

        if (defender.typeMultipliers != null && defender.typeMultipliers.ContainsKey(moveTypeLower))
        {
            totalTypeMult = defender.typeMultipliers[moveTypeLower];
        }
        else
        {
            Debug.LogWarning($"No type data found for {moveTypeLower} vs {defender.name}. Defaulting to 1.0");
        }

        // 3. Formula
        // RFC: Damage = (BasePower * AttackerStat * TypeEffectiveness) / DefenderStat
        float numerator = move.power * atkStat * totalTypeMult;
        float rawDamage = numerator / defStat;
        
        int finalDamage = Mathf.FloorToInt(rawDamage);
        if (finalDamage < 1) finalDamage = 1; 

        // --- NEW DETAILED LOG ---
        Debug.Log($"<color=cyan><b>[CALCULATION REPORT]</b></color> {attacker.name} used {moveName} on {defender.name}\n" +
                  $"<b>Stats:</b> Atk: {atkStat} | Def: {defStat}\n" +
                  $"<b>Math:</b> ({move.power} * {atkStat} * {totalTypeMult}) / {defStat}\n" +
                  $"<b>Result:</b> {numerator} / {defStat} = <b>{finalDamage} DMG</b>");

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
        SendSpectatorUpdate();
    }

    public string GetMyPokemonName() 
    { 
        return myPokemon != null ? myPokemon.name : "Unknown"; 
    }

    public string GetEnemyPokemonName() 
    { 
        return enemyPokemon != null ? enemyPokemon.name : "Unknown"; 
    }

    public void SetMyPokemon(string pokemonName)
    {
        Pokemon p = PokemonDatabase.GetPokemon(pokemonName);
        if (p != null)
        {
            myPokemon = p;
            UpdateBattleUI();
        }
    }

    // 1. Add this function inside BattleManager
    public void ForceUpdateSpectatorView(string myMonName, int myHp, int myMax, string enemyMonName, int enemyHp, int enemyMax)
    {
        // FORCE UPDATE PLAYER SIDE (Host)
        if (playerNameText != null) playerNameText.text = myMonName;
        if (playerHpBar != null) 
        {
            playerHpBar.maxValue = myMax;
            playerHpBar.value = myHp;
        }

        // FORCE UPDATE ENEMY SIDE (Joiner)
        if (enemyNameText != null) enemyNameText.text = enemyMonName;
        if (enemyHpBar != null) 
        {
            enemyHpBar.maxValue = enemyMax;
            enemyHpBar.value = enemyHp;
        }
    }

    // 2. Add this helper to send the data (Only Host runs this)
    public void SendSpectatorUpdate()
    {
        if (networkManager != null && networkManager.isHosting)
        {
            networkManager.SendSpectatorSync(
                myPokemon.name, myPokemon.hp, myPokemon.maxHp,
                enemyPokemon.name, enemyPokemon.hp, enemyPokemon.maxHp
            );
        }
    }
}