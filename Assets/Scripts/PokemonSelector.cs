using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using System.Linq;

public class PokemonSelector : MonoBehaviour
{
    [Header("UI References")]
    public GameObject panelPokedex;      // The new panel we will make
    public GameObject panelMainMenu;     // The existing menu to go back to
    public Transform listContent;        // The content area of the ScrollView
    public GameObject pokemonButtonPrefab; // A prefab for the list item
    public TMP_Text selectedPokemonText; // To show user what they picked on Main Menu

    // Global variable to store the user's choice
    public static string UserSelection = "Pikachu"; // Default safety

    private bool isListPopulated = false;

    void Start()
    {
        // Ensure Database is loaded immediately
        PokemonDatabase.LoadData();
        
        // Hide Pokedex at start
        panelPokedex.SetActive(false);
    }

    public void OpenPokedex()
    {
        panelMainMenu.SetActive(false);
        panelPokedex.SetActive(true);

        if (!isListPopulated)
        {
            PopulateList();
            isListPopulated = true;
        }
    }

    public void ClosePokedex()
    {
        panelPokedex.SetActive(false);
        panelMainMenu.SetActive(true);
    }

    private void PopulateList()
    {
        // clear existing children if any
        foreach (Transform child in listContent) Destroy(child.gameObject);

        // Sort alphabetically for easier finding
        var sortedList = PokemonDatabase.AllPokemon.Keys.OrderBy(n => n).ToList();

        foreach (string pokeName in sortedList)
        {
            GameObject btnObj = Instantiate(pokemonButtonPrefab, listContent);
            
            // Setup Label
            TMP_Text label = btnObj.GetComponentInChildren<TMP_Text>();
            if (label != null) label.text = pokeName;

            // Setup Click Event
            Button btn = btnObj.GetComponent<Button>();
            btn.onClick.AddListener(() => OnPokemonClicked(pokeName));
        }
    }

    private void OnPokemonClicked(string name)
    {
        UserSelection = name;
        Debug.Log($"Selected: {name}");
        
        // Update Main Menu Text
        if (selectedPokemonText != null) 
            selectedPokemonText.text = $"Selected: {name}";

        ClosePokedex();
    }
}