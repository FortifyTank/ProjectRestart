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

    [Header("Party Data")]
    public List<Pokemon> myParty = new List<Pokemon>();    // [NEW] The 6 Pokemon
    public List<Pokemon> enemyParty = new List<Pokemon>(); // [NEW] The Opponent's team

    // Computed Properties so the rest of your code doesn't break!
    // These act as "shortcuts" to the active pokemon.
    public Pokemon myPokemon 
    { 
        get { return (myParty.Count > myActiveIndex) ? myParty[myActiveIndex] : null; } 
        set { /* Read-only mostly, used for initialization logic */ } 
    }
    
    public Pokemon enemyPokemon 
    { 
        get { return (enemyParty.Count > enemyActiveIndex) ? enemyParty[enemyActiveIndex] : null; }
        set { /* Read-only */ }
    }
    private bool isGameOver = false;
    
    // Pointers to who is currently fighting
    private int myActiveIndex = 0;
    private int enemyActiveIndex = 0;

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

        // 1. Ensure Data is Loaded
        if (!PokemonDatabase.IsLoaded) PokemonDatabase.LoadData();

        // 2. Clear old data
        myParty.Clear();
        enemyParty.Clear();
        myActiveIndex = 0;
        enemyActiveIndex = 0;

        // --- [NEW] GENERATE 6v6 PARTIES ---
        
        // Slot 1: The User's Choice (The Lead)
        string leadName = PokemonSelector.UserSelection;
        if (string.IsNullOrEmpty(leadName) || PokemonDatabase.GetPokemon(leadName) == null)
            leadName = "Pikachu";

        myParty.Add(PokemonDatabase.GetPokemon(leadName));

        // Slot 2-6: Random Fills (For now, until we build a Party Selector UI)
        FillPartyWithRandoms(myParty, 5);

        // Enemy Party: Fill with Dummy/Randoms initially
        // (We will update their Lead when we get the BATTLE_SETUP network packet)
        FillPartyWithRandoms(enemyParty, 6); 

        // ----------------------------------

        // --- SPECTATOR LOGIC ---
        if (networkManager.isSpectator)
        {
            Debug.Log("Spectator Mode Active...");
            SetButtonsInteractable(false);
            UpdateBattleUI();
            return; 
        }

        if (isHost) SetButtonsInteractable(true);
        else SetButtonsInteractable(false);

        UpdateBattleUI();
        
        // RFC Requirement: Send the name of our LEAD Pokemon
        if (networkManager != null) networkManager.SendBattleSetup(myPokemon.name);
    }

    // Helper to fill empty slots
    private void FillPartyWithRandoms(List<Pokemon> party, int count)
    {
        // Get all possible names from the database keys
        List<string> allNames = new List<string>(PokemonDatabase.AllPokemon.Keys);
        
        for(int i=0; i<count; i++)
        {
            if (allNames.Count == 0) break;
            string randomName = allNames[Random.Range(0, allNames.Count)];
            party.Add(PokemonDatabase.GetPokemon(randomName));
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
                // Host Attacked -> Joiner took damage
                enemyPokemon.hp = hpRemaining;
                if (enemyHpBar != null) enemyHpBar.value = hpRemaining;
            }
            else if (attackerName == enemyPokemon.name)
            {
                // Joiner Attacked -> Host took damage
                myPokemon.hp = hpRemaining;
                if (playerHpBar != null) playerHpBar.value = hpRemaining;
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

            if (networkManager != null)
                networkManager.SendCalculationConfirm();

            pendingMoveName = "";

            // Check faint
            if (myPokemon.hp <= 0)
            {
                // 6v6 CHECK: Do I have other healthy Pokémon?
                bool hasAblePokemon = false;
                foreach (var poke in myParty)
                {
                    if (poke.hp > 0) hasAblePokemon = true;
                }

                if (!hasAblePokemon)
                {
                    // Real Game Over
                    if (networkManager != null)
                        networkManager.SendGameOver(enemyPokemon.name);

                    OnGameOver(enemyPokemon.name);
                }
                else
                {
                    // Only a faint — switch required
                    Debug.Log("My Pokémon fainted! I need to switch!");
                    networkManager.AddChatMessage("Battle", $"{myPokemon.name} fainted!");

                    // Simple auto-switch
                    for (int i = 0; i < myParty.Count; i++)
                    {
                        if (myParty[i].hp > 0)
                        {
                            myActiveIndex = i;   // Ensure this matches your party system
                            UpdateBattleUI();

                            // Re-enable buttons
                            SetButtonsInteractable(true);

                            networkManager.AddChatMessage("Battle",
                                $"Go! {myParty[myActiveIndex].name}!");

                            break;
                        }
                    }
                }
            }
            else
            {
                SetButtonsInteractable(true);
            }
        }
        else
        {
            // Attacker side: just update visuals
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

        // [FIX] Status moves must return 0 damage
        if (move.category == "Status")
        {
            Debug.Log($"[BATTLE] {moveName} is a Status move. No damage dealt.");
            return 0;
        }

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
            // [FIX] Networking says this is their lead.
            // Overwrite Slot 0 with the correct pokemon.
            if (enemyParty.Count > 0)
            {
                enemyParty[0] = p;
            }
            else
            {
                enemyParty.Add(p);
            }
            
            // Reset active index to 0
            enemyActiveIndex = 0;

            UpdateBattleUI();
            Debug.Log($"Opponent sent Lead: {pokemonName}");
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

    public void SetMyPokemon(string pokemonName)
    {
        Pokemon p = PokemonDatabase.GetPokemon(pokemonName);
        if (p != null)
        {
            myPokemon = p;
            UpdateBattleUI();
        }
    }
}