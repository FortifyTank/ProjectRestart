using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

public class BattleManager : MonoBehaviour
{
    public UDPChatManager networkManager; 

    [Header("UI Menus")]
    public GameObject actionMenu; // The container with Fight/Bag/Pkmn/Run
    public GameObject movesPanel; // The container with the 4 move buttons
    public GameObject partyPanel; // The container for switching (we'll build logic later)
    
    // Add reference to the NEW buttons so we can listen to them
    public Button btnFight;
    public Button btnBag;
    public Button btnPokemon;
    public Button btnRun;
    public Button btnBack; // Shared back button for sub-menus
    public Button btnPartyBack;

    [Header("Bag UI")]
    public GameObject bagPanel;      // Drag 'BagPanel' here
    public Button btnBagBack;        // Drag 'Btn_BagBack' here
    
    // The 4 Item Buttons
    public Button btnXAttack;
    public Button btnXDefense;
    public Button btnXSpAtk;
    public Button btnXSpDef;
    public Button btnXSpeed; // Drag your new button here in Unity Inspector!

    [Header("Bag Inventory")]
    public int itemUsesAtk = 5;
    public int itemUsesDef = 5;
    public int itemUsesSpAtk = 5;
    public int itemUsesSpDef = 5;
    public int itemUsesSpeed = 5;

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
    public string myUsername = "Me";
    public string enemyUsername = "Opponent";
    private bool isGameOver = false;

    // [FIX] New Flag to enforce the wait
    public bool waitingForOpponentSwitch = false;
    
    // State tracking for handshake
    private string pendingMoveName = "";
    private string lastMoveUsedByMe = "";

    private Pokemon myPokemon;
    
    private Pokemon enemyPokemon;

    [Header("Turn Logic")]
    // MY pending action
    public string myPendingMove = "";   // The move I clicked
    public int myPendingSpeed = 0;      // My speed stat
    public bool isMyActionSwitch = false; // Did I choose to switch? (Priority!)

    // ENEMY pending action
    public string enemyPendingMove = "";
    public int enemyPendingSpeed = 0;
    public bool isEnemyActionSwitch = false;

    // State Flags
    public bool hasICommitted = false;
    public bool hasEnemyCommitted = false;
    public int myTieBreaker = 0;    // [NEW] Random number for ties
    public int enemyTieBreaker = 0; // [NEW] Enemy's random number

    [Header("Party UI")]
    // Drag your 6 buttons here!
    public Button[] partyButtons;

    public bool isForcedSwitch = false;

    void Start()
    {
        InitializeMenus();
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
        // 2. BUILD MY PARTY (6v6 Logic)
        myParty.Clear();
        enemyParty.Clear(); // We will fill this as we discover enemies

        // A. Get the user's chosen starter
        string starterName = PokemonSelector.UserSelection;
        if (string.IsNullOrEmpty(starterName) || PokemonDatabase.GetPokemon(starterName) == null) 
            starterName = "Pikachu";

        myParty.Add(PokemonDatabase.GetPokemon(starterName));

        // B. Fill the rest with 5 Random Pokemon for testing
        // (You can change this later to a specific list if you want)
        string[] randomPool = { "Charizard", "Blastoise", "Venusaur", "Gengar", "Snorlax", "Dragonite", "Mewtwo", "Eevee" };
        
        for (int i = 0; i < 5; i++)
        {
            string randName = randomPool[UnityEngine.Random.Range(0, randomPool.Length)];
            myParty.Add(PokemonDatabase.GetPokemon(randName));
        }

        // C. Set the Active Pokemon
        myActiveIndex = 0;
        myPokemon = myParty[0]; // The single variable now points to the first party member

        // 3. ENEMY SETUP
        // Start with a dummy. When we receive BATTLE_SETUP, we will overwrite this.
        enemyPokemon = PokemonDatabase.GetPokemon("Bulbasaur"); 

        // 4. UI & NETWORKING
        UpdateBattleUI();
        
        // Send just my ACTIVE pokemon to the enemy
        if (networkManager != null) networkManager.SendBattleSetup(myPokemon.name);

        // [NEW] Announce the starter to everyone!
        BroadcastLog($"{myUsername} sent out {myPokemon.name}!");

        SetButtonsInteractable(true);
    }

    void UpdateBattleUI()
    {
        // Update player stats and name
        if (playerNameText != null) playerNameText.text = myPokemon.name;
        if (playerHpBar != null) 
        { 
            playerHpBar.maxValue = myPokemon.maxHp; 
            playerHpBar.value = myPokemon.hp; 
        }
        
        // Load and display player sprite
        // Sprites are loaded from Resources/Sprites/{pokedexId}.png
        // Color is set to white to remove any tint from the UI Image component
        if (playerImage != null)
        {
            playerImage.color = Color.white;
            Sprite sprite = Resources.Load<Sprite>($"Sprites/{myPokemon.pokedexId}");
            if (sprite != null)
            {
                playerImage.sprite = sprite;
            }
            else
            {
                Debug.LogWarning($"Sprite not found for {myPokemon.name} at path: Sprites/{myPokemon.pokedexId}");
            }
        }

        // Update enemy stats and name
        if (enemyNameText != null) enemyNameText.text = enemyPokemon.name;
        if (enemyHpBar != null) 
        { 
            enemyHpBar.maxValue = enemyPokemon.maxHp; 
            enemyHpBar.value = enemyPokemon.hp; 
        }
        
        // Load and display enemy sprite
        if (enemyImage != null)
        {
            enemyImage.color = Color.white;
            Sprite sprite = Resources.Load<Sprite>($"Sprites/{enemyPokemon.pokedexId}");
            if (sprite != null)
            {
                enemyImage.sprite = sprite;
            }
            else
            {
                Debug.LogWarning($"Sprite not found for {enemyPokemon.name} at path: Sprites/{enemyPokemon.pokedexId}");
            }
        }
        
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
        
        // [NEW] Calculate Effective Speed based on Stages
        float mult = GetStatMultiplier(myPokemon.stageSpeed);
        int effectiveSpeed = Mathf.FloorToInt(myPokemon.speed * mult);
        
        Debug.Log($"Base Speed: {myPokemon.speed} | Stage: {myPokemon.stageSpeed} | Effective: {effectiveSpeed}");

        // Send the BOOSTED speed to the network!
        CommitAction(moveName, effectiveSpeed, false);
        
        ShowMainMenu();
    }
    
    public void OnOpponentAttackAnnounce(string moveName)
    {
        // SPECTATOR: Just say a move happened (Since we can't be 100% sure who sent it without checking IP)
        if (networkManager.isSpectator) 
        {
            return;
        }

        if (isGameOver) return;
        pendingMoveName = moveName;
        if (networkManager != null) networkManager.SendDefenseAnnounce();
    }

    public void OnDefenseAnnounceReceived()
    {
        if (string.IsNullOrEmpty(lastMoveUsedByMe)) return;
        
        // 1. Calculate damage
        // (Note: PerformAttack applies the damage locally to the enemyPokemon object)
        int damage = PerformAttack(lastMoveUsedByMe, myPokemon, enemyPokemon, enemyHpBar);
        
        // 2. Determine which stats to sync (NEW LOGIC)
        int sentAtkStage = 0;
        int sentDefStage = 0;

        if (MoveLoader.Moves.ContainsKey(lastMoveUsedByMe.ToLower()))
        {
            MoveData move = MoveLoader.Moves[lastMoveUsedByMe.ToLower()];

            if (move.category == "Physical")
            {
                sentAtkStage = myPokemon.stageAtk;      // My (Attacker) Physical Atk
                sentDefStage = enemyPokemon.stageDef;   // Enemy (Defender) Physical Def
            }
            else if (move.category == "Special")
            {
                sentAtkStage = myPokemon.stageSpAtk;    // My (Attacker) Sp. Atk
                sentDefStage = enemyPokemon.stageSpDef; // Enemy (Defender) Sp. Def
            }
        }

        // 3. Send Report (Updated to include the 2 new stat arguments)
        if (networkManager != null)
        {
            // Calculate "Was Host Attacker?" logic
            // If I am hosting, and I am attacking, then Host is Attacker (true).
            // If I am joining, and I am attacking, then Host is NOT Attacker (false).
            bool wasHostAttacker = networkManager.isHosting; 

            networkManager.SendCalculationReport(
                myPokemon.name,      
                lastMoveUsedByMe,    
                damage,              
                enemyPokemon.hp,     
                myPokemon.hp,
                wasHostAttacker,
                sentAtkStage, // Argument 7: Attack Stage (New!)
                sentDefStage  // Argument 8: Defense Stage (New!)
            );
            
            SendSpectatorUpdate();
        }
    }

    // [FIX 1] Added 'bool isHostAttacker' to the function signature
    // THIS FIXES ERROR CS0103 (remoteAtkStage now exists!)
public void OnCalculationReport(string attackerName, int damageDealt, int hpRemaining, bool isHostAttacker, int remoteAtkStage = 0, int remoteDefStage = 0)
{
    // --- SPECTATOR LOGIC ---
    if (networkManager.isSpectator) 
    {
        if (isHostAttacker) { enemyPokemon.hp = hpRemaining; if(enemyHpBar) enemyHpBar.value = hpRemaining; }
        else { myPokemon.hp = hpRemaining; if(playerHpBar) playerHpBar.value = hpRemaining; }
        return; 
    }

    // We force our local stats to match the reporter's stats to prevent future desyncs.
    bool isMe = (networkManager.isHosting == isHostAttacker);
    Pokemon attackerMon = isMe ? myPokemon : enemyPokemon;
    Pokemon defenderMon = isMe ? enemyPokemon : myPokemon;
    
    // We need to know the move category to know WHICH stat to update.
    // If I am Attacker, the move is 'lastMoveUsedByMe'.
    // If I am Defender, the move is 'pendingMoveName'.
    string relevantMove = isMe ? lastMoveUsedByMe : pendingMoveName;

    if (!string.IsNullOrEmpty(relevantMove) && MoveLoader.Moves.ContainsKey(relevantMove.ToLower()))
    {
        MoveData move = MoveLoader.Moves[relevantMove.ToLower()];
        
        if (move.category == "Physical")
        {
            // Sync ONLY Physical stats
            if (attackerMon.stageAtk != remoteAtkStage) 
            {
                Debug.LogWarning($"[SYNC] Fixing {attackerMon.name} Atk: {attackerMon.stageAtk} -> {remoteAtkStage}");
                attackerMon.stageAtk = remoteAtkStage;
            }
            if (defenderMon.stageDef != remoteDefStage) 
            {
                Debug.LogWarning($"[SYNC] Fixing {defenderMon.name} Def: {defenderMon.stageDef} -> {remoteDefStage}");
                defenderMon.stageDef = remoteDefStage;
            }
        }
        else if (move.category == "Special")
        {
            // Sync ONLY Special stats
            if (attackerMon.stageSpAtk != remoteAtkStage) 
            {
                Debug.LogWarning($"[SYNC] Fixing {attackerMon.name} SpAtk: {attackerMon.stageSpAtk} -> {remoteAtkStage}");
                attackerMon.stageSpAtk = remoteAtkStage;
            }
            if (defenderMon.stageSpDef != remoteDefStage) 
            {
                Debug.LogWarning($"[SYNC] Fixing {defenderMon.name} SpDef: {defenderMon.stageSpDef} -> {remoteDefStage}");
                defenderMon.stageSpDef = remoteDefStage;
            }
        }
    }
    // ================================================================

    if (isGameOver) return;

    // ================================================================
    // LOGIC PATH A: I AM THE DEFENDER (I got hit)
    // ================================================================
    if (!string.IsNullOrEmpty(pendingMoveName))
    {
        if (attackerName == enemyPokemon.name) hasEnemyCommitted = false;

        int myCalculatedDamage = CalculateDamage(pendingMoveName, enemyPokemon, myPokemon);
        
        // [FIX] CLAMPING: Treat negative HP as 0 for comparison
        int expectedHp = myPokemon.hp - myCalculatedDamage;
        if (expectedHp < 0) expectedHp = 0; 
        // [FIX END] ----------------------

        // [FIX] Discrepancy Check (Using Clamped Values)
        if (Mathf.Abs(myCalculatedDamage - damageDealt) > 1 || Mathf.Abs(expectedHp - hpRemaining) > 1)
        {
            Debug.LogWarning($"[DISCREPANCY-DEF] Packet: {damageDealt}dmg/{hpRemaining}hp. Me: {myCalculatedDamage}dmg/{expectedHp}hp");
            if (networkManager != null)
                networkManager.SendResolutionRequest(enemyPokemon.name, pendingMoveName, myCalculatedDamage, expectedHp);
            return;
        }

        // Accept the sync
        myPokemon.hp = hpRemaining;
        if (playerHpBar != null) playerHpBar.value = hpRemaining;
        if (networkManager != null) networkManager.SendCalculationConfirm();
        pendingMoveName = "";

        if (myPokemon.hp <= 0)
        {
            // I FAINTED
            hasICommitted = false; 
            myPendingMove = ""; 
            hasEnemyCommitted = false; 
            
            BroadcastLog($"{myUsername}'s {myPokemon.name} fainted!");

            bool hasAlive = false;
            for(int i=0; i<myParty.Count; i++) if(myParty[i].hp > 0) hasAlive = true;

            if (hasAlive)
            {
                isForcedSwitch = true;
                OpenParty();
                if (btnPartyBack) btnPartyBack.gameObject.SetActive(false);
                networkManager.AddChatMessage("System", "Choose a new Pokémon!");
            }
            else
            {
                if (networkManager != null) networkManager.SendGameOver(enemyPokemon.name);
                OnGameOver(enemyPokemon.name);
            }
        }
        else
        {
            // [FIX] Verify we actually have a move before executing (Prevent Empty Move Bug)
            if (hasICommitted && !string.IsNullOrEmpty(myPendingMove))
            {
                ExecuteMyMove();
            }
            else
            {
                TryEndTurn();
            }
        }
    }
    // ================================================================
    // LOGIC PATH B: I AM THE ATTACKER (I hit them)
    // ================================================================
    else
    {
        // 1. Calculate what I think happened
        int myCalculatedDamage = CalculateDamage(lastMoveUsedByMe, myPokemon, enemyPokemon);
        
        // [FIX START] --- CLAMPING FIX ---
        int myCalculatedEnemyHp = enemyPokemon.hp - myCalculatedDamage;
        if (myCalculatedEnemyHp < 0) myCalculatedEnemyHp = 0;
        // [FIX END] ----------------------

        // 2. Discrepancy Check
        if (Mathf.Abs(myCalculatedEnemyHp - hpRemaining) > 1)
        {
             Debug.LogWarning($"[DISCREPANCY-ATK] Enemy claims {hpRemaining} HP. I calculated {myCalculatedEnemyHp} HP.");
             if (networkManager != null)
                networkManager.SendResolutionRequest(myPokemon.name, lastMoveUsedByMe, myCalculatedDamage, myCalculatedEnemyHp);
             return; 
        }

        // 3. Accept the values
        enemyPokemon.hp = hpRemaining;
        if (enemyHpBar != null) enemyHpBar.value = hpRemaining;

        // 4. Check Result
        if (enemyPokemon.hp <= 0)
        {
            Debug.Log("<color=red>[REPORT LOCK]</color> Enemy confirmed dead. Locking UI.");
            waitingForOpponentSwitch = true; 
            SetButtonsInteractable(false);
        }
        else
        {
            hasICommitted = false;
            hasEnemyCommitted = false; 
            TryEndTurn();
        }
    }
    
    SendSpectatorUpdate();
}

    public void OnResolutionRequest(int correctDamage, int correctHp)
    {
        Debug.Log($"[RESOLUTION] Opponent corrected my math. Updating Damage: {correctDamage}, Enemy HP: {correctHp}");

        // 1. Update MY view of the enemy to match what THEY calculated
        enemyPokemon.hp = correctHp;
        if (enemyHpBar != null) enemyHpBar.value = correctHp;

        if (enemyPokemon.hp <= 0)
        {
            Debug.Log("<color=red>[RESOLUTION LOCK]</color> Enemy died after resolution. Locking UI.");
            waitingForOpponentSwitch = true; // Engage Hard Lock
            SetButtonsInteractable(false);
            
            // Clear flags so we don't try to attack again
            hasICommitted = false;
            hasEnemyCommitted = false;
        }
        else
        {
            // If they are still alive, continue the turn normally
            if (networkManager != null) networkManager.SendCalculationConfirm();
        }

        // 2. Send ACK / Confirm so the Defender knows we agreed
        if (networkManager != null)
        {
            networkManager.SendCalculationConfirm();
        }

        // 3. Unlock myself if needed
        TryEndTurn();
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
        Debug.Log($"[CALC DEBUG] {attacker.name} AtkStage: {attacker.stageAtk} | {defender.name} DefStage: {defender.stageDef}");

        string lookupName = moveName.ToLower();

        if (lookupName.StartsWith("x-") || lookupName == "used-item") return 0;

        if (!MoveLoader.Moves.ContainsKey(lookupName)) 
        {
            Debug.LogWarning($"Move '{lookupName}' not found!");
            return 0;
        }
        MoveData move = MoveLoader.Moves[lookupName];

        // Status moves do 0 damage
        if (move.damageClassId == 1 || move.power == 0) return 0;

        // 1. Apply Stages
        float atkStat, defStat;
        if (move.category == "Physical")
        {
            atkStat = attacker.attack * GetStatMultiplier(attacker.stageAtk);
            defStat = defender.defense * GetStatMultiplier(defender.stageDef);
        }
        else
        {
            atkStat = attacker.spAttack * GetStatMultiplier(attacker.stageSpAtk);
            defStat = defender.spDefense * GetStatMultiplier(defender.stageSpDef);
        }
        
        // Burn halves physical attack!
        if (attacker.status == StatusCondition.Burn && move.category == "Physical") atkStat *= 0.5f;

        // --- NEW: Type Effectiveness ---
        float typeMult = 1.0f;
        string moveType = move.type.ToLower(); // Ensure lowercase to match dictionary keys!

        if (defender.typeMultipliers.ContainsKey(moveType))
        {
            typeMult = defender.typeMultipliers[moveType];
        }

        bool shouldAnnounce = (defender == myPokemon); 

        if (shouldAnnounce)
        {
            if (typeMult > 1.0f) BroadcastLog("It's Super Effective!");
            else if (typeMult < 1.0f && typeMult > 0f) BroadcastLog("It's not very effective...");
            else if (typeMult == 0f) BroadcastLog($"It had no effect on {defender.name}!");
        }

        // --- Math ---
        // (Power * Atk * Type) / Def
        float rawDamage = (move.power * atkStat * typeMult) / defStat;
        int damage = Mathf.FloorToInt(rawDamage);
        if (damage < 1) damage = 1;

        return damage;
    }
    
    private int PerformAttack(string moveName, Pokemon attacker, Pokemon defender, Slider targetHealthBar)
    {   
        // [NEW] Handle Items gracefully
        if (moveName.StartsWith("X-") || moveName == "used-item")
        {
            // If it's a specific item like "X-Attack", announce it now!
            if (moveName.StartsWith("X-"))
            {
                // Clean up the name "X-Attack" -> "X-Attack"
                BroadcastLog($"{attacker.name} used {moveName}!");
            }
            else
            {
                BroadcastLog($"{attacker.name} used an Item!");
            }
            return 0; 
        }
        // 1. Get the Move Data (Fixes the "move undefined" error)
        if (!MoveLoader.Moves.ContainsKey(moveName)) return 0;
        MoveData move = MoveLoader.Moves[moveName];

        // 2. Check for Status Moves (Don't deal damage if it's just a debuff)
        // (Assuming you added damageClassId from the CSV, otherwise check power == 0)
        if (move.damageClassId == 1 || move.power == 0)
        {
            TryApplyStatus(move, defender); // Apply Status/Debuff
            ApplyStatusEffect(MoveLoader.Moves[moveName], attacker, defender); // Apply Stat Changes (Growl etc)
            return 0; 
        }

        // 3. Regular Damage Logic
        int damage = CalculateDamage(moveName, attacker, defender);
        defender.hp -= damage;
        if (defender.hp < 0) defender.hp = 0;
        if (targetHealthBar != null) targetHealthBar.value = defender.hp;
        
        // 4. Try Apply Side Effects (e.g. Flamethrower burning)
        TryApplyStatus(move, defender);

        return damage;
    }

    public void SetButtonsInteractable(bool state)
{
    // [FIX] If we are a spectator, force state to false regardless of the input.
    if (networkManager != null && networkManager.isSpectator)
    {
        state = false;
        // Also ensure sub-panels are hidden, only showing the main action menu (Fight/Bag/etc)
        ShowMainMenu();
    }
    
    // Now apply the final state
    if (btnFight) btnFight.interactable = state;
    if (btnBag) btnBag.interactable = state;
    if (btnPokemon) btnPokemon.interactable = state;
    if (btnRun) btnRun.interactable = state;
    
    // This handles hiding/showing sub-menus if the main menu state changes.
    if (!state) ShowMainMenu(); 
}

    public void SetOpponentPokemon(string pokemonName)
    {
        // 1. Search Memory (Fixes Healing Bug)
        Pokemon p = null;
        for (int i = 0; i < enemyParty.Count; i++)
        {
            if (enemyParty[i].name == pokemonName) { p = enemyParty[i]; break; }
        }
        if (p == null)
        {
            p = PokemonDatabase.GetPokemon(pokemonName);
            if (p != null) enemyParty.Add(p);
        }

        if (p != null)
        {
            enemyPokemon = p;
            UpdateBattleUI();
            SendSpectatorUpdate();

            // [FIX] Enemy finished switching. Clear flag.
            hasEnemyCommitted = false; 

            // [FIX] The Key to Unlock
            if (waitingForOpponentSwitch)
            {
                Debug.Log($"<color=green>[UNLOCK]</color> New opponent {pokemonName} arrived. Releasing Lock.");
                waitingForOpponentSwitch = false; // <--- UNLOCK HERE
                SetButtonsInteractable(true);
            }
            else
            {
                // Standard unlock (Start of game)
                SetButtonsInteractable(true);
            } 
        }
        else Debug.LogError($"Could not find opponent pokemon: {pokemonName}");
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
    public void ForceUpdateSpectatorView(string hName, int hHp, int hMax, string cName, int cHp, int cMax)
    {
        // 1. Host Side (Player 1)
        // If the name is different (or null), load the Pokemon data from DB
        if (myPokemon == null || myPokemon.name != hName)
        {
            myPokemon = PokemonDatabase.GetPokemon(hName); 
        }
        // CRITICAL FIX: Overwrite the DB values with the Live values from the packet
        myPokemon.hp = hHp;
        myPokemon.maxHp = hMax;

        // 2. Client Side (Player 2)
        if (enemyPokemon == null || enemyPokemon.name != cName)
        {
            enemyPokemon = PokemonDatabase.GetPokemon(cName);
        }
        // CRITICAL FIX: Overwrite the DB values with the Live values
        enemyPokemon.hp = cHp;
        enemyPokemon.maxHp = cMax;

        // 3. Now update the Visuals (Sliders/Texts) using this corrected data
        UpdateBattleUI();
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

    public void PerformSwitch(int newIndex)
    {
        if (newIndex < 0 || newIndex >= myParty.Count) return;
        if (myParty[newIndex].hp <= 0) return; 

        // 1. Capture Old Name for the message
        string oldMonName = myPokemon.name;

        myPokemon.ResetStages();

        // 2. Update Data
        myActiveIndex = newIndex;
        myPokemon = myParty[newIndex]; 

        // 3. Update UI
        UpdateBattleUI();
        
        // 4. Send Messages (The "Original Game" Style)
        BroadcastLog($"{myUsername} withdrew {oldMonName}!");
        BroadcastLog($"{myUsername} sent out {myPokemon.name}!");

        // 5. Network: Tell Opponent & Spectators
        if (networkManager != null)
        {
            // Tell opponent we switched
            networkManager.SendBattleSetup(myPokemon.name);
            
            // Update spectators so they see the new mon and current HP
            SendSpectatorUpdate();
        }
    }

    public void BroadcastLog(string text)
    {
        // 1. Show it on my screen
        networkManager.AddChatMessage("System", text); 
        
        // 2. Send it to Everyone (Opponent + Spectators)
        // We use SendSystemMessage which we will add to UDPChatManager in a second
        if (networkManager != null) networkManager.SendSystemMessagePacket(text);
    }

    public void InitializeMenus()
    {
        // Hook up the Main Menu buttons
        if(btnFight) btnFight.onClick.AddListener(() => OpenMoves());
        if(btnBag) btnBag.onClick.AddListener(() => OpenBag());
        if(btnPokemon) btnPokemon.onClick.AddListener(() => OpenParty());
        if(btnRun) btnRun.onClick.AddListener(() => OnSurrender());
        
        // [NEW] Hook up the Party Back Button
        if(btnBack) btnBack.onClick.AddListener(() => ShowMainMenu());

        if(btnPartyBack) btnPartyBack.onClick.AddListener(() => ShowMainMenu());
        

        // [NEW] Hook up Item Buttons (We will write UseItem later)
        if(btnBagBack) btnBagBack.onClick.AddListener(() => ShowMainMenu());
        if(btnXAttack)  btnXAttack.onClick.AddListener(() => UseItem("Attack"));
        if(btnXDefense) btnXDefense.onClick.AddListener(() => UseItem("Defense"));
        if(btnXSpAtk)   btnXSpAtk.onClick.AddListener(() => UseItem("SpAttack"));
        if(btnXSpDef)   btnXSpDef.onClick.AddListener(() => UseItem("SpDefense"));
        if(btnXSpeed) btnXSpeed.onClick.AddListener(() => UseItem("Speed"));

        ShowMainMenu();
    }

    public void ShowMainMenu()
    {
        if(actionMenu) actionMenu.SetActive(true);
        if(movesPanel) movesPanel.SetActive(false);
        if(partyPanel) partyPanel.SetActive(false);
        if(bagPanel)   bagPanel.SetActive(false); // [NEW] Hide Bag
    }

    public void OpenMoves()
    {
        if(actionMenu) actionMenu.SetActive(false);
        if(movesPanel) movesPanel.SetActive(true);
    }

    public void OpenBag()
    {
        if(actionMenu) actionMenu.SetActive(false);
        if(bagPanel)   bagPanel.SetActive(true);  // [NEW] Show Bag
    }

    public void OpenParty()
    {
        if(actionMenu) actionMenu.SetActive(false);
        if(partyPanel) partyPanel.SetActive(true);
        RefreshPartyUI();// RefreshPartyUI(); // We will write this in the "Switching" step
    }

    public void OnSurrender()
    {
        networkManager.AddChatMessage("System", "You surrendered!");
        networkManager.SendGameOver(enemyUsername); // Give win to enemy
        OnGameOver(enemyUsername);
    }

    public void CommitAction(string moveName, int speed, bool isSwitch)
    {
        // 1. Save my choice
        myPendingMove = moveName;
        myPendingSpeed = speed;
        isMyActionSwitch = isSwitch;
        
        // [NEW] Generate a random tie-breaker
        myTieBreaker = UnityEngine.Random.Range(0, 1000);
        
        hasICommitted = true;

        // 2. Lock UI
        SetButtonsInteractable(false);
        networkManager.AddChatMessage("System", "Waiting for opponent...");

        // 3. Send the Packet (Now includes Tie Breaker)
        if (networkManager != null)
        {
            // Update this function call!
            networkManager.SendCommitPacket(moveName, speed, isSwitch, myTieBreaker);
        }
        //check if we ready to fight
        CheckForResolution();
    }

    public void CheckForResolution()
    {
        // 1. Wait until BOTH players have committed
        if (!hasICommitted || !hasEnemyCommitted) return;

        Debug.Log("Resolution: Both Ready!");

        // 2. Decide who is faster
        bool doIGoFirst = false;

        // Switch Priority: Switches always beat Attacks
        if (isMyActionSwitch && !isEnemyActionSwitch) doIGoFirst = true;
        else if (!isMyActionSwitch && isEnemyActionSwitch) doIGoFirst = false;
        else
        {
            // 2. Speed Check
            if (myPendingSpeed > enemyPendingSpeed) 
            {
                doIGoFirst = true;
            }
            else if (myPendingSpeed < enemyPendingSpeed)
            {
                doIGoFirst = false;
            }
            else
            {
                // 3. SPEED TIE! Use the Random Tie Breaker
                Debug.Log($"Speed Tie! My Roll: {myTieBreaker} vs Enemy: {enemyTieBreaker}");
                
                if (myTieBreaker > enemyTieBreaker) doIGoFirst = true;
                else if (myTieBreaker < enemyTieBreaker) doIGoFirst = false;
                else doIGoFirst = true; // Super rare double-tie (Default to Host/True)
            }
        }

        // 3. Execute
        if (doIGoFirst)
        {
            // I am faster! I attack now.
            Debug.Log("I am faster! Attacking...");
            ExecuteMyMove(); 
        }
        else
        {
            // I am slower. I wait for the enemy to attack me.
            // AFTER I survive their attack, I will counter-attack.
            Debug.Log("I am slower. Waiting for impact...");
        }
    }

    public void ExecuteMyMove()
    {
        if (string.IsNullOrEmpty(myPendingMove))
        {
            Debug.LogWarning("[ExecuteMyMove] Aborted: myPendingMove is empty!");
            hasICommitted = false; // Fix the zombie state
            return;
        }
        
        // [FUTURE ANIMATION SPACE]

        if (isMyActionSwitch)
        {
            // --- SWITCH LOGIC ---
            int switchIndex = -1;
            for (int i = 0; i < myParty.Count; i++)
            {
                if (myParty[i].name == myPendingMove) { switchIndex = i; break; }
            }

            if (switchIndex != -1)
            {
                PerformSwitch(switchIndex);
                
                // Clear my flags
                hasICommitted = false; 
                myPendingMove = "";

                // [CRITICAL FIX FOR BUG 5]
                // If the enemy has committed a move but hasn't acted yet (they are slower),
                // we must NOT clear their flag, and we must NOT unlock buttons.
                // We must wait for their attack to hit our new pokemon.
                if (hasEnemyCommitted)
                {
                    // FIX: Changed "UNLOCKED" to "LOCKED" to be accurate
                    Debug.Log("[ExecuteMyMove] Switched. Enemy attack is incoming. STAYING LOCKED.");
                    // Do NOT unlock. We wait for OnCalculationReport to trigger the unlock.
                }
                else
                {
                    // Both switched or enemy was idle. Turn is over.
                    TryEndTurn(); 
                }
            }
        }
        else
        {   
            if (!CanPokemonMove(myPokemon))
            {
                // We failed the check (Paralyzed/Sleep).
                // Crucial: We MUST still send a packet to keep the network flow going.
                // We temporarily swap our move to "Splash" (or "used-item") so it does 0 damage.
                myPendingMove = "used-item"; 
            }

            // --- ATTACK LOGIC ---
            lastMoveUsedByMe = myPendingMove;
            BroadcastLog($"{myUsername}'s {myPokemon.name} used {myPendingMove}!");
            
            if (networkManager != null) networkManager.SendAttackAnnounce(myPendingMove);
            
            // Clear my move so we don't fire again
            myPendingMove = ""; 
            
            // [CRITICAL FIX] 
            // DO NOT CALL TryEndTurn() HERE!
            // We just attacked. We don't know the result yet.
            // We must WAIT for the Calculation Report (or Game Over) to unlock us.
            Debug.Log("[ExecuteMyMove] Attack sent. Waiting for damage report...");
        }
    }

    public void RefreshPartyUI()
    {   
        if (partyButtons == null) Debug.LogError("Party Buttons Array is NULL!");
        else Debug.Log($"Refreshing UI. Party Count: {myParty.Count} | Button Slots: {partyButtons.Length}");

        for (int i = 0; i < partyButtons.Length; i++)
        {
            // 1. Check if we have a pokemon in this slot
            if (i < myParty.Count)
            {
                partyButtons[i].gameObject.SetActive(true);
                Pokemon p = myParty[i];
                
                string status = (p.hp <= 0) ? "(FAINTED)" : $"({p.hp}/{p.maxHp})";
                if (i == myActiveIndex) status = "(ACTIVE)";
                string finalString = $"{p.name} {status}";

                // [FIX] Handle TMP and Legacy Text separately
                var tmpLabel = partyButtons[i].GetComponentInChildren<TMPro.TMP_Text>();
                if (tmpLabel != null)
                {
                    tmpLabel.text = finalString;
                }
                else
                {
                    var legacyLabel = partyButtons[i].GetComponentInChildren<UnityEngine.UI.Text>();
                    if (legacyLabel != null)
                    {
                        legacyLabel.text = finalString;
                    }
                }

                // 3. Setup Click Event
                int index = i; // Capture index for the lambda
                partyButtons[i].onClick.RemoveAllListeners();
                
                // Logic: If fainted or active, can't switch. Otherwise, Commit Switch.
                if (p.hp > 0 && i != myActiveIndex)
                {
                    partyButtons[i].interactable = true;
                    partyButtons[i].onClick.AddListener(() => OnPartyMemberClicked(index));
                }
                else
                {
                    partyButtons[i].interactable = false;
                }
            }
            else
            {
                // Hide unused buttons
                partyButtons[i].gameObject.SetActive(false);
            }
        }
    }

    public void OnPartyMemberClicked(int index)
    {
        // 1. Check if we are in "Emergency Mode"
        if (isForcedSwitch)
        {
            // DIRECT SWITCH (No Commit, No Waiting)
            PerformSwitch(index);
            
            // Cleanup
            isForcedSwitch = false;
            if (btnPartyBack) btnPartyBack.gameObject.SetActive(true); // Restore Back button
            
            // Close UI and Start New Turn
            ShowMainMenu(); 
            TryEndTurn(); // Unlock buttons for the new round
        }
        else
        {
            // NORMAL MODE (Mid-battle switch)
            // This uses the Speed System (Costs a turn)
            string pokemonName = myParty[index].name;
            CommitAction(pokemonName, 9999, true);
            ShowMainMenu(); 
        }
    }

    public void UseItem(string statName)
    {
        bool canUse = false;
        
        // 1. Check Inventory
        if (statName == "Attack" && itemUsesAtk > 0) canUse = true;
        else if (statName == "Defense" && itemUsesDef > 0) canUse = true;
        else if (statName == "SpAttack" && itemUsesSpAtk > 0) canUse = true;
        else if (statName == "SpDefense" && itemUsesSpDef > 0) canUse = true;
        else if (statName == "Speed" && itemUsesSpeed > 0) canUse = true;

        if (!canUse)
        {
            networkManager.AddChatMessage("System", "You are out of that item!");
            return;
        }

        // 2. Apply Boost
        // We clamp it between -6 and 6 because that's the Pokemon rule
        if (statName == "Attack") { myPokemon.stageAtk = Mathf.Clamp(myPokemon.stageAtk + 2, -6, 6); itemUsesAtk--; }
        else if (statName == "Defense") { myPokemon.stageDef = Mathf.Clamp(myPokemon.stageDef + 2, -6, 6); itemUsesDef--; }
        else if (statName == "SpAttack") { myPokemon.stageSpAtk = Mathf.Clamp(myPokemon.stageSpAtk + 2, -6, 6); itemUsesSpAtk--; }
        else if (statName == "SpDefense") { myPokemon.stageSpDef = Mathf.Clamp(myPokemon.stageSpDef + 2, -6, 6); itemUsesSpDef--; }
        else if (statName == "Speed") { myPokemon.stageSpeed = Mathf.Clamp(myPokemon.stageSpeed + 2, -6, 6); itemUsesSpeed--; }

        // 3. Log & Commit
        string itemName = $"X-{statName}";
        
        // Use "Used Item" to pass the turn
        CommitAction("used-item", 9999, false); 
        ShowMainMenu();
    }

    public float GetStatMultiplier(int stage)
    {
        if (stage >= 0) return (2.0f + stage) / 2.0f;
        else return 2.0f / (2.0f + Mathf.Abs(stage));
    }
    private void TryEndTurn()
    {
        Debug.Log($"[TryEndTurn Check] MyHP: {myPokemon.hp}, EnemyHP: {enemyPokemon.hp}");
        Debug.Log($"[TryEndTurn Flags] Pending: '{myPendingMove}', I_Committed: {hasICommitted}, Enemy_Committed: {hasEnemyCommitted}");

        if (waitingForOpponentSwitch)
        {
            Debug.Log("[TryEndTurn] BLOCKED: Waiting for opponent to switch...");
            SetButtonsInteractable(false);
            return;
        }

        // 1. Am I dead? (Forced Switch)
        if (myPokemon.hp <= 0) 
        {
            Debug.Log("[TryEndTurn] BLOCKED: I fainted!");
            SetButtonsInteractable(false);
            return;
        }

        // 2. Is the enemy dead? (Waiting for replacement)
        if (enemyPokemon.hp <= 0)
        {
            Debug.Log("[TryEndTurn] BLOCKED: Enemy fainted! Waiting for new pokemon...");
            SetButtonsInteractable(false);
            return;
        }

        // 3. Did I finish my move?
        if (!string.IsNullOrEmpty(myPendingMove))
        {
            Debug.Log($"[TryEndTurn] BLOCKED: I still have a pending move: {myPendingMove}");
            SetButtonsInteractable(false);
            return;
        }

        // 4. Did the enemy finish their move?
        if (hasEnemyCommitted)
        {
            Debug.Log("[TryEndTurn] BLOCKED: Enemy still needs to act.");
            SetButtonsInteractable(false);
            return;
        }

        // If we reached here, the turn is officially OVER. 
        // Now we take Burn/Poison damage.
        ProcessStatusDamage(myPokemon);

        // If I died from Burn, I can't start a new turn!
        if (myPokemon.hp <= 0)
        {
            BroadcastLog($"{myPokemon.name} fainted from its condition!");
            // Trigger faint logic (same as OnCalculationReport)
            // ... (You can copy the faint logic here or make a helper function)
            // For now, simple safety:
            SetButtonsInteractable(false);
            networkManager.SendGameOver(enemyPokemon.name); // Enemy wins
            return;
        }

        // 5. ALL CLEAR!
        Debug.Log("[TryEndTurn] SUCCESS! Unlocking buttons.");
        SetButtonsInteractable(true);
        
        // Safety Reset
        hasICommitted = false;
        hasEnemyCommitted = false;
    }

    // Inside BattleManager.cs

    public void ForceUnlockAndResetTurn()
    {
        // This is the absolute final state reset.
        hasICommitted = false;
        hasEnemyCommitted = false;
        myPendingMove = "";
        
        // Clear the UI lock and update the menu
        SetButtonsInteractable(true);
        ShowMainMenu();

        Debug.Log("[TURN END] FORCED RESET: All flags cleared and buttons unlocked.");
    }

    public void OnEnemySwitch(string newPokemonName)
    {
        // 1. If the enemy was dead, this means they finally replaced it!
        if (enemyPokemon.hp <= 0)
        {
            BroadcastLog($"Opponent sent out {newPokemonName}!");
            
            // 2. Unlock my buttons so I can fight the new Pokemon
            SetButtonsInteractable(true);
            
            // 3. Reset turn flags just in case
            hasICommitted = false;
            hasEnemyCommitted = false;
        }
    }

    public void OnCalculationConfirm()
    {
        // 1. Clear flags
        hasICommitted = false;
        hasEnemyCommitted = false; 

        // 2. CHECK FOR DEATH
        if (enemyPokemon.hp <= 0)
        {
            Debug.Log("<color=red>[CONFIRM LOCK]</color> Enemy is dead. Keeping Hard Lock.");
            waitingForOpponentSwitch = true; // Ensure this is true
            SetButtonsInteractable(false);
            return;
        }

        // 3. Unlock the UI for the next turn
        TryEndTurn();
    }

    // Define Stat IDs for clarity (matches the CSV standard)
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

    public void ApplyStatusEffect(MoveData move, Pokemon user, Pokemon target)
    {
        foreach (var change in move.statChanges)
        {
            // Positive = Buff User (Swords Dance), Negative = Nerf Enemy (Growl)
            Pokemon affectedMon = (change.changeAmount > 0) ? user : target;
            ApplyStatChange(affectedMon, (StatID)change.statId, change.changeAmount);
        }
    }

    private void ApplyStatChange(Pokemon p, StatID stat, int amount)
    {
        string statName = "";
        string riseFall = amount > 0 ? "rose" : "fell";

        switch (stat)
        {
            case StatID.Attack: 
                p.stageAtk = Mathf.Clamp(p.stageAtk + amount, -6, 6); 
                statName = "Attack"; break;
            case StatID.Defense: 
                p.stageDef = Mathf.Clamp(p.stageDef + amount, -6, 6); 
                statName = "Defense"; break;
            case StatID.SpAttack: 
                p.stageSpAtk = Mathf.Clamp(p.stageSpAtk + amount, -6, 6); 
                statName = "Sp. Atk"; break;
            case StatID.SpDefense: 
                p.stageSpDef = Mathf.Clamp(p.stageSpDef + amount, -6, 6); 
                statName = "Sp. Def"; break;
            case StatID.Speed: 
                p.stageSpeed = Mathf.Clamp(p.stageSpeed + amount, -6, 6); 
                statName = "Speed"; break;
        }

        if (statName != "")
        {
            BroadcastLog($"{p.name}'s {statName} {riseFall}!");
        }
    }

    public void TryApplyStatus(MoveData move, Pokemon target)
    {
        if (move.ailmentId == 0) return; // No ailment
        if (target.status != StatusCondition.None) return; // Already has status

        // Determine Chance
        // If ailment_chance is 0, it usually means "Guaranteed" (like Will-O-Wisp), 
        // UNLESS it's a damaging move (like Flamethrower) where 0 means 0.
        // For simplicity:
        int chance = move.ailmentChance;
        if (chance == 0 && move.power == 0) chance = 100; // Status moves usually 100%
        if (chance == 0 && move.power > 0) return; // Damaging moves with 0 chance have no effect

        // Roll for it
        if (Random.Range(0, 100) < chance)
        {
            target.status = (StatusCondition)move.ailmentId;
            string msg = $"{target.name} is now {target.status}!";
            BroadcastLog(msg);
            
            if (target.status == StatusCondition.Sleep) target.sleepTurns = Random.Range(1, 4);
        }
    }

    public bool CanPokemonMove(Pokemon p)
    {
        if (p.status == StatusCondition.Paralysis)
        {
            // 25% chance to not move
            if (UnityEngine.Random.Range(0, 4) == 0) 
            {
                BroadcastLog($"{p.name} is fully paralyzed!");
                return false;
            }
        }
        else if (p.status == StatusCondition.Sleep)
        {
            if (p.sleepTurns > 0)
            {
                p.sleepTurns--;
                BroadcastLog($"{p.name} is fast asleep.");
                return false;
            }
            else
            {
                p.status = StatusCondition.None;
                BroadcastLog($"{p.name} woke up!");
            }
        }
        // Frozen logic is similar to sleep/paralysis
        return true;
    }

        public void ProcessStatusDamage(Pokemon p)
    {
        if (p.hp <= 0) return;

        if (p.status == StatusCondition.Burn || p.status == StatusCondition.Poison)
        {
            int dmg = Mathf.FloorToInt(p.maxHp / 8.0f); // 1/8th damage standard
            if (dmg < 1) dmg = 1;
            
            p.hp -= dmg;
            BroadcastLog($"{p.name} is hurt by its {p.status}!");
            
            // Update UI
            if (p == myPokemon) playerHpBar.value = p.hp;
            else enemyHpBar.value = p.hp;
        }
    }

    // Helper to clean up text "mega-punch" -> "Mega Punch"
    public static string FormatName(string input)
    {
        if (string.IsNullOrEmpty(input)) return "";
        
        // Split by dash (for "vine-whip")
        string[] words = input.Split('-');
        for (int i = 0; i < words.Length; i++)
        {
            if (words[i].Length > 0) 
                words[i] = char.ToUpper(words[i][0]) + words[i].Substring(1);
        }
        return string.Join(" ", words);
    }
}