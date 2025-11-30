using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using UnityEngine.SceneManagement;

public class BattleManager : MonoBehaviour
{
    public UDPChatManager networkManager; 
    
    [Header("UI Menus")]
    public GameObject actionMenu; // Fight, Bag, Pokemon, Run buttons
    public GameObject movesPanel; // Panel for the 4 moves
    public GameObject partyPanel; // Switching panel
    
    // Buttons for actions
    public Button btnFight;
    public Button btnBag;
    public Button btnPokemon;
    public Button btnRun;
    public Button btnBack; // Back button for sub-menus
    public Button btnPartyBack;

    [Header("Waiting Screen")]
    public GameObject panelWaiting;

    [Header("Stats UI")]
    public GameObject panelStats; // Panel for stats
    public Button btnShowStats;  
    public Button btnCloseStats;

    // Txt for ME
    public TMPro.TMP_Text txtMyAtk, txtMyDef, txtMySpAtk, txtMySpDef, txtMySpd;
    // Txt for ENEMY
    public TMPro.TMP_Text txtEnAtk, txtEnDef, txtEnSpAtk, txtEnSpDef, txtEnSpd;

    [Header("End Game UI")]
    public GameObject btnReturnToLobby;

    [Header("Spectator UI")]
    public GameObject btnSpectatorLeave;

    [Header("Bag UI")]
    public GameObject bagPanel;      // Assign the BagPanel object
    public Button btnBagBack;        // Assign the Btn_BagBack button
    
    // The 4 Item Buttons
    public Button btnXAttack;
    public Button btnXDefense;
    public Button btnXSpAtk;
    public Button btnXSpDef;
    public Button btnXSpeed; // Assign your new speed button

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

    //  New Flag to enforce the wait
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
    public bool isMyActionSwitch = false; // Did I choose to switch? 

    // ENEMY pending action
    public string enemyPendingMove = "";
    public int enemyPendingSpeed = 0;
    public bool isEnemyActionSwitch = false;

    // State Flags
    public bool hasICommitted = false;
    public bool hasEnemyCommitted = false;
    public int myTieBreaker = 0;    //  Random number for ties
    public int enemyTieBreaker = 0; //  Enemy's random number

    [Header("Party UI")]
    // Drag your 6 buttons here!
    public Button[] partyButtons;

    public bool isForcedSwitch = false;

    /*
    Runs once when the battle scene wakes up. Hooks up menus, obtains the
    network manager if needed, and keeps inputs disabled until setup completes
    to prevent early actions.
    */
    void Start()
    {
        InitializeMenus();
        if (networkManager == null) networkManager = GetComponent<UDPChatManager>();
        SetButtonsInteractable(false);
    }

    /*
    Starts a new battle session. Loads data, handles spectator mode (read‑only
    UI), builds the local player’s team, announces the active Pokémon, updates
    the HUD, and finally enables inputs.
    */
    public void SetupBattle(bool isHost)
    {
        isGameOver = false;
        Debug.Log($"Setting up battle. Am I Host? {isHost}");

        // Load Pokemon data
        PokemonDatabase.LoadData();

        // Spectator mode
        if (networkManager.isSpectator)
        {   
            if (btnSpectatorLeave) btnSpectatorLeave.SetActive(true); // have the leave game option
            Debug.Log("Spectator Mode Active: Initializing View...");

            // Use dummy Pokemon for UI
            myPokemon = PokemonDatabase.GetPokemon("Bulbasaur");
            enemyPokemon = PokemonDatabase.GetPokemon("Charmander");

            // Turn off controls
            SetButtonsInteractable(false);
            
            // Hide buttons to look like TV
            if (moveButtons.Length > 0 && moveButtons[0] != null)
            {
                foreach(var btn in moveButtons) 
            {
                if(btn != null) btn.gameObject.SetActive(false);
            }
            }

            // Show dummy UI
            UpdateBattleUI();
            
            // Spectators don't send setup
            return; 
        }
        else
        {
            if (btnSpectatorLeave) btnSpectatorLeave.SetActive(false);// if not spectator then dont show leave game option
        }
        // 2. BUILD MY PARTY (6v6 Logic)
        myParty.Clear();
        enemyParty.Clear(); // Track opponent's team

        // Loop through the 6 slots from the Selector
        int validCount = 0;
        for (int i = 0; i < 6; i++)
        {
            string name = PokemonSelector.PartyData[i];
            
            if (!string.IsNullOrEmpty(name))
            {
                Pokemon p = PokemonDatabase.GetPokemon(name);
                if (p != null) 
                {
                    myParty.Add(p);
                    validCount++;
                }
            }
        }

        // If user didn't pick ANY, give them a Pikachu so game doesn't crash
        if (validCount == 0)
        {
            myParty.Add(PokemonDatabase.GetPokemon("Pikachu"));
        }

        // Set active one
        myActiveIndex = 0;
        myPokemon = myParty[0]; // Points to the first in party

        // Setup enemy
        enemyPokemon = PokemonDatabase.GetPokemon("Bulbasaur"); // Dummy for now

        if (isHost)
        {
            // if enemy name default or empty, show waiting panel
            if (string.IsNullOrEmpty(enemyUsername) || enemyUsername == "Opponent")
            {
                SetWaitingMode(true);
            }
            else
            {
                // If handshake pass, hide waiting panel
                SetWaitingMode(false);
            }
        }
        else
        {
            // If joiner or spectator, hide black screen
            SetWaitingMode(false);
        }

        // Update UI and network
        UpdateBattleUI();
        
        // Tell enemy my Pokemon
        if (networkManager != null) networkManager.SendBattleSetup(myPokemon.name);

        // Announce to everyone
        BroadcastLog($"{myUsername} sent out {myPokemon.name}!");

        SetButtonsInteractable(true);
    }

    public void SetWaitingMode(bool isWaiting)
    {
        if (panelWaiting != null)
        {
            panelWaiting.SetActive(isWaiting);
        }
    }

    /*
    Refreshes the on‑screen state to match current battle data: names, HP bars,
    and sprites for both sides. Also wires move buttons to the active Pokémon’s
    move list.
    */
    void UpdateBattleUI()
    {
        // Update player stats and name
        if (playerNameText != null) playerNameText.text = myUsername + $" ({myPokemon.name})";
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
        if (enemyNameText != null) enemyNameText.text = enemyUsername + $" ({enemyPokemon.name})";
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

    // Battle logic

    /*
    Handles a move button click. Calculates effective speed after stage
    modifiers, queues the action for this turn, and returns the UI to the main
    menu while waiting for the opponent.
    */
    public void OnMoveSelected(int moveIndex)
    {   
        if (isGameOver) return;
        string moveName = myPokemon.moves[moveIndex];
        
        // Calculate effective speed with stages
        float mult = GetStatMultiplier(myPokemon.stageSpeed);
        int effectiveSpeed = Mathf.FloorToInt(myPokemon.speed * mult);
        
        Debug.Log($"Base Speed: {myPokemon.speed} | Stage: {myPokemon.stageSpeed} | Effective: {effectiveSpeed}");

        // Send boosted speed
        CommitAction(moveName, effectiveSpeed, false);
        
        ShowMainMenu();
    }
    
    /*
    Called when the opponent announces an attack. Spectators simply acknowledge
    and stop. Players record the incoming move and send a defense announce so
    the turn can resolve.
    */
    public void OnOpponentAttackAnnounce(string moveName)
    {
        // For spectators, just note a move happened
        if (networkManager.isSpectator) 
        {
            return;
        }

        if (isGameOver) return;
        pendingMoveName = moveName;
        if (networkManager != null) networkManager.SendDefenseAnnounce();
    }

    /*
    Once the opponent is ready to defend, resolves the last announced attack
    locally, determines which stat stages apply (physical vs special), and
    sends a calculation report with damage, HP, and stage info to stay in sync.
    */
    public void OnDefenseAnnounceReceived()
    {
        if (string.IsNullOrEmpty(lastMoveUsedByMe)) return;
        
        // Calculate damage
        int damage = PerformAttack(lastMoveUsedByMe, myPokemon, enemyPokemon, enemyHpBar);
        
        // Determine stats to sync
        int sentAtkStage = 0;
        int sentDefStage = 0;

        if (MoveLoader.Moves.ContainsKey(lastMoveUsedByMe.ToLower()))
        {
            MoveData move = MoveLoader.Moves[lastMoveUsedByMe.ToLower()];

            if (move.category == "Physical")
            {
                sentAtkStage = myPokemon.stageAtk;      // My attack
                sentDefStage = enemyPokemon.stageDef;   // Enemy defense
            }
            else if (move.category == "Special")
            {
                sentAtkStage = myPokemon.stageSpAtk;    // My sp. atk
                sentDefStage = enemyPokemon.stageSpDef; // Enemy sp. def
            }
        }

        // Send report
        if (networkManager != null)
        {
            bool wasHostAttacker = networkManager.isHosting; 

            networkManager.SendCalculationReport(
                myPokemon.name,      
                lastMoveUsedByMe,    
                damage,              
                enemyPokemon.hp,     
                myPokemon.hp,
                wasHostAttacker,
                sentAtkStage, // Attack stage
                sentDefStage  // Defense stage
            );
            
            SendSpectatorUpdate();
        }
    }

    // Fixed to include isHostAttacker
    /*
    Reconciles the attacker’s official numbers with the local view. Infers who
    attacked, syncs only the stages relevant to the move’s category, then
    accepts HP changes and handles faint/switch flow. Requests a resolution if
    the numbers don’t match.
    */
    public void OnCalculationReport(string attackerName, int damageDealt, int hpRemaining, bool isHostAttacker, int remoteAtkStage = 0, int remoteDefStage = 0)
    {
        // Spectator stuff
        if (networkManager.isSpectator) 
        {
            if (isHostAttacker) { enemyPokemon.hp = hpRemaining; if(enemyHpBar) enemyHpBar.value = hpRemaining; }
            else { myPokemon.hp = hpRemaining; if(playerHpBar) playerHpBar.value = hpRemaining; }
            return; 
        }

        // Force stats to match to avoid desync
        bool isMe = (networkManager.isHosting == isHostAttacker);
        Pokemon attackerMon = isMe ? myPokemon : enemyPokemon;
        Pokemon defenderMon = isMe ? enemyPokemon : myPokemon;
        
        // Know the move category to update right stats
        string relevantMove = isMe ? lastMoveUsedByMe : pendingMoveName;

        if (!string.IsNullOrEmpty(relevantMove) && MoveLoader.Moves.ContainsKey(relevantMove.ToLower()))
        {
            MoveData move = MoveLoader.Moves[relevantMove.ToLower()];
            
            if (move.category == "Physical")
            {
                // Sync physical stats
                if (attackerMon.stageAtk != remoteAtkStage) 
                {
                    Debug.LogWarning($"Syncing {attackerMon.name} Atk: {attackerMon.stageAtk} -> {remoteAtkStage}");
                    attackerMon.stageAtk = remoteAtkStage;
                }
                if (defenderMon.stageDef != remoteDefStage) 
                {
                    Debug.LogWarning($"Syncing {defenderMon.name} Def: {defenderMon.stageDef} -> {remoteDefStage}");
                    defenderMon.stageDef = remoteDefStage;
                }
            }
            else if (move.category == "Special")
            {
                // Sync special stats
                if (attackerMon.stageSpAtk != remoteAtkStage) 
                {
                    Debug.LogWarning($"Syncing {attackerMon.name} SpAtk: {attackerMon.stageSpAtk} -> {remoteAtkStage}");
                    attackerMon.stageSpAtk = remoteAtkStage;
                }
                if (defenderMon.stageSpDef != remoteDefStage) 
                {
                    Debug.LogWarning($"Syncing {defenderMon.name} SpDef: {defenderMon.stageSpDef} -> {remoteDefStage}");
                    defenderMon.stageSpDef = remoteDefStage;
                }
            }
        }
    if (isGameOver) return;

    // If I'm defending
    if (!string.IsNullOrEmpty(pendingMoveName))
    {
        if (attackerName == enemyPokemon.name) hasEnemyCommitted = false;

        int myCalculatedDamage = CalculateDamage(pendingMoveName, enemyPokemon, myPokemon);
        
        // Clamp HP to 0 for comparison
        int expectedHp = myPokemon.hp - myCalculatedDamage;
        if (expectedHp < 0) expectedHp = 0;
        
        // Check for discrepancies
        if (Mathf.Abs(myCalculatedDamage - damageDealt) > 1 || Mathf.Abs(expectedHp - hpRemaining) > 1)
        {
            Debug.LogWarning($"Discrepancy - Defender: Packet: {damageDealt}dmg/{hpRemaining}hp. Me: {myCalculatedDamage}dmg/{expectedHp}hp");
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
            // I fainted
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
                if (networkManager != null) networkManager.SendGameOver(enemyUsername);
                OnGameOver(enemyUsername);
            }
        }
        else
        {
            // Make sure I have a move before executing
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
    // If I'm attacking
    else
    {
        // Calculate what I think happened
        int myCalculatedDamage = CalculateDamage(lastMoveUsedByMe, myPokemon, enemyPokemon);
        
        int myCalculatedEnemyHp = enemyPokemon.hp - myCalculatedDamage;
        if (myCalculatedEnemyHp < 0) myCalculatedEnemyHp = 0;

        // Check for discrepancies
        if (Mathf.Abs(myCalculatedEnemyHp - hpRemaining) > 1)
        {
             Debug.LogWarning($"Discrepancy - Attacker: Enemy claims {hpRemaining} HP. I calculated {myCalculatedEnemyHp} HP.");
             if (networkManager != null)
                networkManager.SendResolutionRequest(myPokemon.name, lastMoveUsedByMe, myCalculatedDamage, myCalculatedEnemyHp);
             return; 
        }

        // Accept the values
        enemyPokemon.hp = hpRemaining;
        if (enemyHpBar != null) enemyHpBar.value = hpRemaining;

        // Check result
        if (enemyPokemon.hp <= 0)
        {
            Debug.Log("Enemy confirmed dead. Locking UI.");
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

    /*
    Accepts a correction when damage math disagrees. Applies the provided HP,
    locks if a KO occurred, sends confirmation, and proceeds if the battle is
    still live.
    */
    public void OnResolutionRequest(int correctDamage, int correctHp)
    {
        Debug.Log($"Opponent corrected my math. Updating Damage: {correctDamage}, Enemy HP: {correctHp}");

        // Update my view of the enemy
        enemyPokemon.hp = correctHp;
        if (enemyHpBar != null) enemyHpBar.value = correctHp;

        if (enemyPokemon.hp <= 0)
        {
            Debug.Log("Enemy died after resolution. Locking UI.");
            waitingForOpponentSwitch = true; 
            SetButtonsInteractable(false);
            
            // Clear flags
            hasICommitted = false;
            hasEnemyCommitted = false;
        }
        else
        {
            // Continue normally
            if (networkManager != null) networkManager.SendCalculationConfirm();
        }

        // Confirm so defender knows we agreed
        if (networkManager != null)
        {
            networkManager.SendCalculationConfirm();
        }

        // Unlock if needed
        TryEndTurn();
    }

    /*
    Ends the match cleanly: disables inputs, posts a simple winner line to
    chat for players and spectators, and marks the nameplates accordingly.
    */
    public void OnGameOver(string winner)
    {
        if (isGameOver) return;
        isGameOver = true;
        SetButtonsInteractable(false);
        
        // clean names
        string cleanWinner = winner.Trim();
        string cleanMe = myUsername.Trim();
        string cleanEnemy = enemyUsername.Trim();

        Debug.Log($"[GAME OVER] Winner: '{cleanWinner}' | Me: '{cleanMe}' | Enemy: '{cleanEnemy}'");

        // Print to Chat (Crucial for Spectators)
        networkManager.AddChatMessage("System", $"GAME OVER! Winner: {winner}");

        // Update Top Labels , NOTE TO SELF: MIGHT NEED TO CHANGE THIS 
        if (playerNameText != null) 
    {
        // Check if I won
            if (cleanWinner == cleanMe) 
                playerNameText.text += " <color=green>(WINNER)</color>";
            else 
                playerNameText.text += " <color=red>(FAINTED)</color>";
        }
        
        if (enemyNameText != null) 
        {
            // Check if Enemy Won
            if (cleanWinner == cleanEnemy) 
                enemyNameText.text += " <color=green>(WINNER)</color>";
            else 
                enemyNameText.text += " <color=red>(FAINTED)</color>";
        }

        // Show the Return to Lobby Button
        if (btnReturnToLobby != null) 
        {
            btnReturnToLobby.SetActive(true);
        }
    }

    // --- RFC 6: DAMAGE CALCULATION ---
    /*
    Computes damage for a single move. Applies stage multipliers, accounts for
    burn on physical attacks, includes type effectiveness, and clamps the
    result to at least 1 for standard damaging moves.
    */
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
    
    /*
    Executes a move locally. Items and pure status moves don’t deal damage;
    regular moves compute damage, update HP and the bar, then roll for side
    effects (e.g., a burn chance).
    */
    private int PerformAttack(string moveName, Pokemon attacker, Pokemon defender, Slider targetHealthBar)
    {   
        // Handle Items gracefully
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

    // // Spectators are always read‑only; when disabling controls, return the UI to the main menu so sub‑panels don’t
    // linger.
        public void SetButtonsInteractable(bool state)
{
    //  If we are a spectator, force state to false regardless of the input.
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

    /*
    Swaps in the opponent’s active mon on my UI. If I don’t already
    have it in my tracked list, I grab it from the DB, refresh the HUD,
    and unlock controls if we were waiting on their switch.
    */
    public void SetOpponentPokemon(string pokemonName)
    {
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
            hasEnemyCommitted = false; 
            if (waitingForOpponentSwitch)
            {
                Debug.Log($"<color=green>[UNLOCK]</color> New opponent {pokemonName} arrived. Releasing Lock.");
                waitingForOpponentSwitch = false;
                SetButtonsInteractable(true);
            }
            else
            {
                SetButtonsInteractable(true);
            } 
        }
        else Debug.LogError($"Could not find opponent pokemon: {pokemonName}");
    }

    /*
    Just returns my current mon’s name, or "Unknown" if we don’t have one yet.
    */
    public string GetMyPokemonName() 
    { 
        return myPokemon != null ? myPokemon.name : "Unknown"; 
    }

    /*
    Same deal for the enemy: give me their active name or "Unknown" if
    we’re not set up yet.
    */
    public string GetEnemyPokemonName() 
    { 
        return enemyPokemon != null ? enemyPokemon.name : "Unknown"; 
    }

    /*
    Sets my active mon by name and refreshes the HUD. Helpfull during setup
    or when we need to force a sync.
    */
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
    /*
    Spectator‑only helper: shove the live names + HP/maxHP from the host
    into our local copies, then refresh the UI so the view stays honest.
    */
    public void ForceUpdateSpectatorView(string hName, int hHp, int hMax, string cName, int cHp, int cMax)
    {
        // 1. Host Side (Player 1)
        // If the name is different (or null), load the Pokemon data from DB
        if (myPokemon == null || myPokemon.name != hName)
        {
            myPokemon = PokemonDatabase.GetPokemon(hName); 
        }
        //Overwrite the DB values with the Live values from the packet
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

    // 2. Add this helper to send the data,, only Host can run this
    /*
    Host pings spectators with a tiny snapshot (names + HP). That’s it.
    */
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

    /*
    Handles me swapping mid‑battle: reject bad picks, reset stages, update
    the UI, log the flavor, and tell everyone what just happened.
    */
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

    /*
    Prints a system line locally and sends the same text over the wire so
    opponent + spectators see exactly what I see.
    */
    public void BroadcastLog(string text)
    {
        // 1. Show it on my screen
        networkManager.AddChatMessage("System", text); 
        
        // 2. Send it to Everyone (Opponent + Spectators)
        // We use SendSystemMessage which we will add to UDPChatManager in a second
        if (networkManager != null) networkManager.SendSystemMessagePacket(text);
    }

    /*
    Wire up all the button listeners in one place so we don’t stack dupes
    when panels open/close. Run once on Start.
    */
    public void InitializeMenus()
    {
        // Hook up the Main Menu buttons
        if(btnFight) btnFight.onClick.AddListener(() => OpenMoves());
        if(btnBag) btnBag.onClick.AddListener(() => OpenBag());
        if(btnPokemon) btnPokemon.onClick.AddListener(() => OpenParty());
        if(btnRun) btnRun.onClick.AddListener(() => OnSurrender());
        
        InitStatsMenu();
        
        //  Hook up the Party Back Button
        if(btnBack) btnBack.onClick.AddListener(() => ShowMainMenu());

        if(btnPartyBack) btnPartyBack.onClick.AddListener(() => ShowMainMenu());
        

        //  Hook up Item Buttons (We will write UseItem later)
        if(btnBagBack) btnBagBack.onClick.AddListener(() => ShowMainMenu());
        if(btnXAttack)  btnXAttack.onClick.AddListener(() => UseItem("Attack"));
        if(btnXDefense) btnXDefense.onClick.AddListener(() => UseItem("Defense"));
        if(btnXSpAtk)   btnXSpAtk.onClick.AddListener(() => UseItem("SpAttack"));
        if(btnXSpDef)   btnXSpDef.onClick.AddListener(() => UseItem("SpDefense"));
        if(btnXSpeed) btnXSpeed.onClick.AddListener(() => UseItem("Speed"));

        ShowMainMenu();
    }

    /*
    Pop back to the main action menu and hide the sub‑menus.
    */
    public void ShowMainMenu()
    {
        if(actionMenu) actionMenu.SetActive(true);
        if(movesPanel) movesPanel.SetActive(false);
        if(partyPanel) partyPanel.SetActive(false);
        if(bagPanel)   bagPanel.SetActive(false); // Hide Bag
    }

    /*
    Show the moves panel for whoever’s out.
    */
    public void OpenMoves()
    {
        if(actionMenu) actionMenu.SetActive(false);
        if(movesPanel) movesPanel.SetActive(true);
    }

    /*
    Open the bag so I can use an item this turn.
    */
    public void OpenBag()
    {
        if(actionMenu) actionMenu.SetActive(false);
        if(bagPanel)   bagPanel.SetActive(true);  //\ Show Bag
    }

    /*
    Open my party list for switching and refresh the labels right away.
    */
    public void OpenParty()
    {
        if(actionMenu) actionMenu.SetActive(false);
        if(partyPanel) partyPanel.SetActive(true);
        RefreshPartyUI();// RefreshPartyUI(); // We will write this in the "Switching" step
    }

    /*
    Concede the match: post it, tell the opponent they win, and wrap up.
    */
    public void OnSurrender()
    {
        networkManager.AddChatMessage("System", "You surrendered!");
        networkManager.SendGameOver(enemyUsername); // Give win to enemy
        OnGameOver(enemyUsername);
        Invoke("ResetGame", 3.0f);
    }

    /*
    Lock in my choice for the turn—move or switch. I stash the speed and a
    tie‑breaker roll, send it to the other side, lock the UI, and wait until
    both of us are ready to resolve.
    */
    public void CommitAction(string moveName, int speed, bool isSwitch)
    {
        // 1. Save my choice
        myPendingMove = moveName;
        myPendingSpeed = speed;
        isMyActionSwitch = isSwitch;
        
    // this wld generate a tie breaker 
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

    /*
    Once we’ve both committed, figure out who goes first. Switch beats move,
    then it’s speed, then a random tie roll. If I’m up, I act; otherwise I wait.
    */
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
                else doIGoFirst = true; // Super rare double-tie if ever switch Default to Host/True
            }
        }

        // 3. Execute
        if (doIGoFirst)
        {
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

    /*
    Do the thing I picked. If I switched, perform it and maybe stay locked
    for the incoming hit. If I attacked, announce it and wait for the damage
    report from the other side.
    */
    public void ExecuteMyMove()
    {
        if (string.IsNullOrEmpty(myPendingMove))
        {
            Debug.LogWarning("[ExecuteMyMove] Aborted: myPendingMove is empty!");
            hasICommitted = false; // Fix the zombie state
            return;
        }
        

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

                // If the enemy has committed a move but hasn't acted yet (they are slower), we must NOT clear their flag, and we must NOT unlock buttons.
                // We must wait for their attack to hit our new pokemon.
                if (hasEnemyCommitted)
                {
                    // FIX: Changed "UNLOCKED" to "LOCKED" to be accurate
                    Debug.Log("[ExecuteMyMove] Switched. Enemy attack is incoming. STAYING LOCKED.");
    
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
                // We temporarily swap our move to "Splash" (or "used-item") so it does 0 damage.
                myPendingMove = "used-item"; 
            }

            // --- ATTACK LOGIC ---
            lastMoveUsedByMe = myPendingMove;
            BroadcastLog($"{myUsername}'s {myPokemon.name} used {myPendingMove}!");
            
            if (networkManager != null) networkManager.SendAttackAnnounce(myPendingMove);
            
            // Clear my move so we don't fire again
            myPendingMove = ""; 
            
        
        
            // We just attacked. We don't know the result yet.
            // We must WAIT for the Calculation Report (or Game Over) to unlock us.
            Debug.Log("[ExecuteMyMove] Attack sent. Waiting for damage report...");
        }
    }

    /*
    Fill the six party buttons with names and HP (or ACTIVE/FAINTED) and only
    enable clicks on valid switch targets.
    */
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

                // Handle TMP and Legacy Text separately
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

    /*
    Handle picking a party slot. If I’m forced after a faint, switch instantly;
    otherwise commit a normal switch and head back to the main menu.
    */
    public void OnPartyMemberClicked(int index)
    {
        // 1. Check if we are in "Emergency Mode"
        if (isForcedSwitch)
        {
            
            PerformSwitch(index);
            
        
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

    /*
    Spend an X‑item use to bump a stat by two stages, then pass the turn with
    a no‑damage "used‑item" action.
    */
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

    /*
    Turns a stage value (−6..+6) into the usual Pokémon multiplier.
    */
    public float GetStatMultiplier(int stage)
    {
        if (stage >= 0) return (2.0f + stage) / 2.0f;
        else return 2.0f / (2.0f + Mathf.Abs(stage));
    }
    /*
    Decides if we can start a fresh turn. If anyone’s KO’d, a move is pending,
    or the enemy still needs to act, we stay locked. Otherwise apply burn/poison
    chip and unlock inputs.
    */
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
            // Trigger faint logic
            SetButtonsInteractable(false);
            networkManager.SendGameOver(enemyUsername); // Enemy wins
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

    /*
    Big red button: clear all turn flags, unlock the UI, and bring back the
    main menu in case something got stuck.
    */
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

    /*
    If the enemy was KO’d and finally sends a new mon, re‑enable my buttons
    and clean up the turn flags.
    */
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

    /*
    After we both agree on the numbers, keep things locked if they’re KO’d and
    waiting to switch; otherwise move on to the next turn.
    */
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

    /*
    Apply stage changes from the move. Positive changes buff the user; negative
    ones debuff the target.
    */
    public void ApplyStatusEffect(MoveData move, Pokemon user, Pokemon target)
    {
        foreach (var change in move.statChanges)
        {
            // Positive = Buff User (Swords Dance), Negative = Nerf Enemy (Growl)
            Pokemon affectedMon = (change.changeAmount > 0) ? user : target;
            ApplyStatChange(affectedMon, (StatID)change.statId, change.changeAmount);
        }
    }

    /*
    Nudge the chosen stat, clamp it to −6..+6, and drop a simple line saying
    it rose or fell.
    */
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

    /*
    Roll the status effect chance for this move (sleep, burn, etc). Handles the
    "0 means guaranteed for pure status moves" quirk.
    */
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

    /*
    Quick status gate: check paralysis and sleep to see if a mon can act. Logs
    the outcome so players know why something didn’t move.
    */
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

        /*
        End‑of‑turn chip for burn/poison. Subtract a chunk and update the right
        HP bar so the UI stays in sync.
        */
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

    /*
    Tiny helper to make move IDs look decent: "mega-punch" → "Mega Punch".
    */
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

    public void ResetGame()
    {
        // 1. Disconnect Network
        if (networkManager != null)
        {
            networkManager.Shutdown(); // We will write this next
        }

        // 2. Clear Static Data (Crucial!)
        // If we don't clear this, the next game might try to load the old party
        myParty.Clear();
        enemyParty.Clear();
        
        // 3. Reload the Scene
        // This is the cleanest way to reset all UI/Buttons/Variables
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    public void InitStatsMenu()
    {
        if (btnShowStats) btnShowStats.onClick.AddListener(() => ToggleStatsPanel(true));
        if (btnCloseStats) btnCloseStats.onClick.AddListener(() => ToggleStatsPanel(false));
    }

    // Opens/Closes the window and refreshes data
    public void ToggleStatsPanel(bool show)
    {
        if (panelStats) 
        {
            panelStats.SetActive(show);
            if (show) UpdateStatDisplay(); // Only update numbers when we actually look at them
        }
    }

    // The Logic: Read the Pokemon variables and update the text
    public void UpdateStatDisplay()
    {
        // A helper to make positive numbers GREEN and negative numbers RED
        string Fmt(int stage) 
        {
            string color = stage > 0 ? "green" : (stage < 0 ? "red" : "white");
            string sign = stage > 0 ? "+" : ""; // Add a plus sign for positive numbers
            return $"<color={color}>{sign}{stage}</color>";
        }

        // 1. Update My Stats
        if (myPokemon != null)
        {
            if(txtMyAtk) txtMyAtk.text = $"Atk: {Fmt(myPokemon.stageAtk)}";
            if(txtMyDef) txtMyDef.text = $"Def: {Fmt(myPokemon.stageDef)}";
            if(txtMySpAtk) txtMySpAtk.text = $"SpAtk: {Fmt(myPokemon.stageSpAtk)}";
            if(txtMySpDef) txtMySpDef.text = $"SpDef: {Fmt(myPokemon.stageSpDef)}";
            if(txtMySpd) txtMySpd.text = $"Spd: {Fmt(myPokemon.stageSpeed)}";
        }

        // 2. Update Enemy Stats
        if (enemyPokemon != null)
        {
            if(txtEnAtk) txtEnAtk.text = $"Atk: {Fmt(enemyPokemon.stageAtk)}";
            if(txtEnDef) txtEnDef.text = $"Def: {Fmt(enemyPokemon.stageDef)}";
            if(txtEnSpAtk) txtEnSpAtk.text = $"SpAtk: {Fmt(enemyPokemon.stageSpAtk)}";
            if(txtEnSpDef) txtEnSpDef.text = $"SpDef: {Fmt(enemyPokemon.stageSpDef)}";
            if(txtEnSpd) txtEnSpd.text = $"Spd: {Fmt(enemyPokemon.stageSpeed)}";
        }
    }
}