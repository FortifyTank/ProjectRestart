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

    private string myPendingMove = ""; // What I picked
    private int myPendingSpeed = 0;    
    
    private string enemyPendingMove = ""; // What enemy picked
    private int enemyPendingSpeed = 0;
    
    private bool hasICommitted = false;
    private bool hasEnemyCommitted = false;

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

        SetButtonsInteractable(isHost);
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

        // [FIX] New Format + Broadcast to everyone (including Spectators)
        string log = $"{myUsername}'s {myPokemon.name} used {moveName}!";
        BroadcastLog(log);

        if (networkManager != null) networkManager.SendAttackAnnounce(moveName);
        SetButtonsInteractable(false);
        SendSpectatorUpdate();
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
            networkManager.SendCalculationReport(
                myPokemon.name,      // Attacker Name
                lastMoveUsedByMe,    // Move Name
                damage,              // Damage Dealt
                enemyPokemon.hp,     // Defender HP Remaining
                myPokemon.hp         // [NEW] Attacker HP Remaining (Required by RFC)
            );

            // [FIX] Tell Spectators that I just dealt damage!
            SendSpectatorUpdate();
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
                // 1. My Pokemon Fainted. Check if I have others.
                bool hasAlivePokemon = false;
                int nextIndex = -1;

                for (int i = 0; i < myParty.Count; i++)
                {
                    if (myParty[i].hp > 0)
                    {
                        hasAlivePokemon = true;
                        nextIndex = i;
                        break; // Found one!
                    }
                }

                if (hasAlivePokemon)
                {
                    // 2. I have survivors! Auto-switch to the next one.
                    Debug.Log($"{myPokemon.name} fainted! Switching to {myParty[nextIndex].name}...");
                    
                    string log = $"{myUsername}'s {myPokemon.name} fainted!";
                    BroadcastLog(log);
                    PerformSwitch(nextIndex); // We will write this function next!
                }
                else
                {
                    // 3. Everyone is dead. I truly lost.
                    if (networkManager != null) networkManager.SendGameOver(enemyPokemon.name);
                    OnGameOver(enemyPokemon.name);
                }
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
        // Disable the main menu buttons, not the hidden move buttons
        if (btnFight) btnFight.interactable = state;
        if (btnBag) btnBag.interactable = state;
        if (btnPokemon) btnPokemon.interactable = state;
        if (btnRun) btnRun.interactable = state;
        
        // If it's NOT our turn, hide the sub-menus to prevent cheating
        if (!state) ShowMainMenu();
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

    public void PerformSwitch(int newIndex)
    {
        if (newIndex < 0 || newIndex >= myParty.Count) return;
        if (myParty[newIndex].hp <= 0) return; // Cannot switch to fainted mon

        // 1. Update Data
        myActiveIndex = newIndex;
        myPokemon = myParty[newIndex]; // Point the shortcut to the new mon

        // 2. Update UI
        UpdateBattleUI();
        
        string log = $"{myUsername} sent out {myPokemon.name}!";
        BroadcastLog(log);

        // 3. Network: Tell Opponent & Spectators
        // We need to add a "SWITCH_ANNOUNCE" packet type.
        if (networkManager != null)
        {
            // Reuse BATTLE_SETUP for now, or create a new one. 
            // Using BATTLE_SETUP is the easiest "lazy fix" because it already updates the enemy view!
            networkManager.SendBattleSetup(myPokemon.name);
            
            // Also update spectators immediately
            SendSpectatorUpdate();
        }
        
        // 4. Enable Buttons (if it's my turn, though usually switching takes a turn)
        SetButtonsInteractable(true); 
        SendSpectatorUpdate();
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
        
        if(btnBagBack) btnBagBack.onClick.AddListener(() => ShowMainMenu());

        // [NEW] Hook up Item Buttons (We will write UseItem later)
        if(btnXAttack)  btnXAttack.onClick.AddListener(() => UseItem("Attack"));
        if(btnXDefense) btnXDefense.onClick.AddListener(() => UseItem("Defense"));
        if(btnXSpAtk)   btnXSpAtk.onClick.AddListener(() => UseItem("SpAttack"));
        if(btnXSpDef)   btnXSpDef.onClick.AddListener(() => UseItem("SpDefense"));

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
        // RefreshPartyUI(); // We will write this in the "Switching" step
    }

    public void OnSurrender()
    {
        networkManager.AddChatMessage("System", "You surrendered!");
        networkManager.SendGameOver(enemyUsername); // Give win to enemy
        OnGameOver(enemyUsername);
    }

    public void UseItem(string statName)
    {
        Debug.Log($"Used X-{statName}! (Logic coming soon)");
        // Logic will be: 
        // 1. Consume turn
        // 2. Add +2 to stat stage
        // 3. Send packet
        
        ShowMainMenu(); // Close bag after use
    }
}