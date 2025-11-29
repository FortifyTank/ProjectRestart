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
        
        // Calculate damage
        int damage = PerformAttack(lastMoveUsedByMe, myPokemon, enemyPokemon, enemyHpBar);
        
        // Send Report (Updated with myPokemon.hp as the 5th argument)
        if (networkManager != null)
        {
            // I am the one calculating damage.
            // If I am Host, then the Attacker was the Joiner (isHost = false).
            // If I am Joiner, then the Attacker was the Host (isHost = true).
            
            bool wasHostAttacker = !networkManager.isHosting; // Invert my role

            networkManager.SendCalculationReport(
                myPokemon.name,      
                lastMoveUsedByMe,    
                damage,              
                enemyPokemon.hp,     
                myPokemon.hp,
                wasHostAttacker // [NEW]
            );
            // [FIX] Tell Spectators that I just dealt damage!
            SendSpectatorUpdate();
        }
    }

    // [FIX 1] Added 'bool isHostAttacker' to the function signature
    public void OnCalculationReport(string attackerName, int damageDealt, int hpRemaining, bool isHostAttacker)
    {
        // --- SPECTATOR LOGIC ---
        if (networkManager.isSpectator) 
        {
            if (isHostAttacker) { enemyPokemon.hp = hpRemaining; if(enemyHpBar) enemyHpBar.value = hpRemaining; }
            else { myPokemon.hp = hpRemaining; if(playerHpBar) playerHpBar.value = hpRemaining; }
            return; 
        }
        // -----------------------

        if (isGameOver) return;

        // ================================================================
        // LOGIC PATH A: I AM THE DEFENDER (I got hit)
        // ================================================================
        if (!string.IsNullOrEmpty(pendingMoveName))
        {
            // 1. The enemy just finished their attack. Clear their flag.
            if (attackerName == enemyPokemon.name) hasEnemyCommitted = false;

            // 2. Calculate Damage
            int myCalculatedDamage = CalculateDamage(pendingMoveName, enemyPokemon, myPokemon);

            // 3. Discrepancy Check
            if (Mathf.Abs(myCalculatedDamage - damageDealt) > 1)
            {
                Debug.LogWarning($"[DISCREPANCY] Opponent said {damageDealt}, I calculated {myCalculatedDamage}");
                int myCorrectHp = myPokemon.hp - myCalculatedDamage;
                if (networkManager != null)
                    networkManager.SendResolutionRequest(enemyPokemon.name, pendingMoveName, myCalculatedDamage, myCorrectHp);
                return;
            }

            // 4. Apply Damage
            myPokemon.hp = hpRemaining;
            if (playerHpBar != null) playerHpBar.value = hpRemaining;
            if (networkManager != null) networkManager.SendCalculationConfirm();
            pendingMoveName = "";

            // 5. Check Life State
            if (myPokemon.hp <= 0)
            {
                // --- I FAINTED ---
                hasICommitted = false; 
                myPendingMove = ""; 
                hasEnemyCommitted = false; 
                
                BroadcastLog($"{myUsername}'s {myPokemon.name} fainted!");

                bool hasAlive = false;
                for(int i=0; i<myParty.Count; i++) if(myParty[i].hp > 0) hasAlive = true;

                if (hasAlive)
                {
                    // Force Switch
                    isForcedSwitch = true;
                    OpenParty();
                    if (btnPartyBack) btnPartyBack.gameObject.SetActive(false);
                    networkManager.AddChatMessage("System", "Choose a new Pokémon!");
                }
                else
                {
                    // Game Over
                    if (networkManager != null) networkManager.SendGameOver(enemyPokemon.name);
                    OnGameOver(enemyPokemon.name);
                }
            }
            else
            {
                // --- I SURVIVED ---
                // If I was slower and waiting to counter-attack, do it now.
                if (hasICommitted && !string.IsNullOrEmpty(myPendingMove))
                {
                    ExecuteMyMove();
                }
                else
                {
                    // I have no move left. My turn is done. Unlock.
                    TryEndTurn();
                }
            }
        }
        // ================================================================
        // LOGIC PATH B: I AM THE ATTACKER (I hit them)
        // ================================================================
        else
        {
            // 1. Apply Damage Visuals
            enemyPokemon.hp = hpRemaining;
            if (enemyHpBar != null) enemyHpBar.value = hpRemaining;

            // 2. Check Result
            if (enemyPokemon.hp <= 0)
            {
                // I killed them. Force Lock until replacement arrives.
                SetButtonsInteractable(false);
            }
            else
            {
                // [CRITICAL FIX FOR SLOWER PLAYER]
                // My attack landed successfully and they survived.
                // My part of the turn is 100% complete.
                
                // I MUST clear my flags, or TryEndTurn will think I'm still busy!
                hasICommitted = false;
                hasEnemyCommitted = false; 
                
                // Unlock Buttons
                TryEndTurn();
            }
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
        // 0. Handle "Dummy" moves (like Splash or Used Item)
        if (moveName == "used-item" || moveName == "Used Item") return 0;

        if (!MoveDatabase.Moves.ContainsKey(moveName)) 
        {
            Debug.LogError($"Move {moveName} not found in database!");
            return 0;
        }
        MoveData move = MoveDatabase.Moves[moveName];

        // 1. Determine Stats & Apply Stage Multipliers (CRITICAL STEP)
        bool isPhysical = move.category == "Physical";
        float atkStat, defStat;

        if (isPhysical)
        {
            // Physical: Use Attack vs Defense
            // "GetStatMultiplier" converts the stage (e.g., +2) into a float (e.g., 2.0x)
            atkStat = attacker.attack * GetStatMultiplier(attacker.stageAtk);
            defStat = defender.defense * GetStatMultiplier(defender.stageDef);
        }
        else
        {
            // Special: Use SpAttack vs SpDefense
            atkStat = attacker.spAttack * GetStatMultiplier(attacker.stageSpAtk);
            defStat = defender.spDefense * GetStatMultiplier(defender.stageSpDef);
        }

        // 2. Type Effectiveness
        float totalTypeMult = 1.0f;
        string moveTypeLower = move.type.ToLower();

        if (defender.typeMultipliers != null && defender.typeMultipliers.ContainsKey(moveTypeLower))
        {
            totalTypeMult = defender.typeMultipliers[moveTypeLower];
        }

        // 3. The Formula
        // Damage = (Power * Atk * Type) / Def
        float numerator = move.power * atkStat * totalTypeMult;
        float rawDamage = numerator / defStat;
        
        int finalDamage = Mathf.FloorToInt(rawDamage);
        if (finalDamage < 1 && move.power > 0) finalDamage = 1; 

        Debug.Log($"<color=cyan><b>[CALC]</b></color> {attacker.name} used {moveName}. " +
                $"AtkStage: {attacker.stageAtk} (x{GetStatMultiplier(attacker.stageAtk)}), " +
                $"DefStage: {defender.stageDef} (x{GetStatMultiplier(defender.stageDef)}) -> DMG: {finalDamage}");

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

            // Case A: Counter-Attack
            if (hasICommitted && !string.IsNullOrEmpty(myPendingMove))
            {
                 ExecuteMyMove();
            }
            // Case B: Unlock (This handles the "Killer Unlock" scenario)
            else
            {
                 Debug.Log("[SetOpponentPokemon] New opponent arrived. Unlocking.");
                 TryEndTurn();
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
        string logMsg = $"{myUsername} used X-{statName}!";
        BroadcastLog(logMsg);
        
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

    public void OnCalculationConfirm()
    {
        // The Protocol says we only do this if the report matched. 
        // If we are here, the opponent confirmed our damage was correct.
        
        // 1. Clear flags because our action (Attacking) is fully done.
        hasICommitted = false;
        hasEnemyCommitted = false; 

        // 2. Unlock the UI for the next turn
        // (In your simultaneous logic, this will unlock you because you were the last one to act)
        TryEndTurn();

        Debug.Log("RFC Protocol: Turn Cycle Complete. State set to WAITING_FOR_MOVE.");
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

public void ApplyStatusEffect(string moveName, Pokemon user, Pokemon target)
{
    if (!MoveDatabase.Moves.ContainsKey(moveName)) return;
    MoveData move = MoveDatabase.Moves[moveName];

    // AUTOMATION: Loop through the CSV data!
    foreach (var change in move.statChanges)
    {
        // Default target is the enemy
        Pokemon affectedMon = target;

        // Check target_id from moves.csv if you have it.
        // ID 7 = User. ID 13 = User-or-Ally.
        // For now, simple logic: If it's a "Status" move and raises stats (positive), 
        // it's probably for the user (like Swords Dance).
        // If it's negative, it's for the enemy (like Growl).
        if (change.changeAmount > 0) affectedMon = user;
        else affectedMon = target;

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
}