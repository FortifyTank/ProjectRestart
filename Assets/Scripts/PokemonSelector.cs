using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using System.Linq;

public class PokemonSelector : MonoBehaviour
{
    [Header("Panels")]
    public GameObject panelMainMenu;     
    public GameObject panelParty;        
    public GameObject panelPokedex;      

    [Header("Pokedex UI")]
    public Transform listContent;        
    public GameObject pokemonButtonPrefab; 
    public TMP_InputField searchInput;   

    [Header("Party Slots")]
    public Button[] slotButtons;         
    
    // We store the actual names here
    public static string[] PartyData = new string[6]; 

    // Which slot are we currently changing?
    // [FIX] Made public so you can see it in Inspector for debugging
    public int currentEditingSlotIndex = -1; 
    private List<string> allPokemonNames = new List<string>();

    void Start()
    {
        PokemonDatabase.LoadData();
        allPokemonNames = PokemonDatabase.AllPokemon.Keys.OrderBy(n => n).ToList();

        // Start State
        panelMainMenu.SetActive(true);
        panelParty.SetActive(false);
        panelPokedex.SetActive(false);
        
        UpdateSlotUI(); // Ensure UI matches empty data at start

        // Hook up Slot Buttons dynamically
        for (int i = 0; i < slotButtons.Length; i++)
        {
            int x = i; // Capture index
            slotButtons[i].onClick.RemoveAllListeners(); // [FIX] Clear old listeners
            slotButtons[i].onClick.AddListener(() => OnSlotClicked(x));
        }

        if (searchInput != null) searchInput.onValueChanged.AddListener(OnSearchValueChanged);
    }

    // --- NAVIGATION ---

    public void GoToPartyScreen()
    {
        panelMainMenu.SetActive(false);
        panelParty.SetActive(true);
        panelPokedex.SetActive(false);
        UpdateSlotUI();
    }

    public void BackToMainMenu()
    {
        panelParty.SetActive(false);
        panelMainMenu.SetActive(true);
    }

    private void OnSlotClicked(int index)
    {
        currentEditingSlotIndex = index;
        Debug.Log($"[DEBUG] Editing Slot: {index}"); // Check console for this!
        
        // Go to List
        panelParty.SetActive(false);
        panelPokedex.SetActive(true);
        
        if (searchInput != null) searchInput.text = "";
        RefreshList("");
    }

    public void BackToPartyScreen()
    {
        panelPokedex.SetActive(false);
        panelParty.SetActive(true);
        
        // [FIX] Force UI update whenever we return to this screen
        UpdateSlotUI(); 
    }

    // --- UI UPDATES ---

    private void UpdateSlotUI()
    {
        for (int i = 0; i < slotButtons.Length; i++)
        {
            TMP_Text txt = slotButtons[i].GetComponentInChildren<TMP_Text>();
            
            // Safety check
            if (txt == null) 
            {
                Debug.LogError($"Slot Button {i} is missing a TextMeshPro component!");
                continue;
            }

            if (string.IsNullOrEmpty(PartyData[i]))
            {
                txt.text = $"Slot {i+1}\n(Empty)";
            }
            else
            {
                // [FIX] Use the exact string from array
                txt.text = PartyData[i]; 
            }
        }
    }

    // --- SEARCH & SELECTION ---

    private void OnSearchValueChanged(string query) { RefreshList(query); }

    private void RefreshList(string searchFilter)
    {
        foreach (Transform child in listContent) Destroy(child.gameObject);
        searchFilter = searchFilter.ToLower();
        
        List<string> filteredList = allPokemonNames
            .Where(name => name.ToLower().Contains(searchFilter))
            .ToList();

        foreach (string pokeName in filteredList)
        {
            GameObject btnObj = Instantiate(pokemonButtonPrefab, listContent);
            
            // Set Text
            TMP_Text label = btnObj.GetComponentInChildren<TMP_Text>();
            if (label != null) label.text = pokeName;

            // Set Button Click
            Button btn = btnObj.GetComponent<Button>();
            string capturedName = pokeName;
            
            btn.onClick.RemoveAllListeners();
            btn.onClick.AddListener(() => OnPokemonSelected(capturedName));
        }
    }

    private void OnPokemonSelected(string pokemonName)
    {
        Debug.Log($"[DEBUG] Selected: {pokemonName} for Slot {currentEditingSlotIndex}");

        if (currentEditingSlotIndex != -1)
        {
            // [FIX] Ensure we write to the array
            PartyData[currentEditingSlotIndex] = pokemonName;
        }
        else
        {
            Debug.LogError("Error: currentEditingSlotIndex was -1 when selecting pokemon!");
        }

        BackToPartyScreen(); 
    }
}