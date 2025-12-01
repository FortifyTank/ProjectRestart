using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Collections.Concurrent;
using System.Collections.Generic;

/*
Handles lightweight UDP chat and battle sync: opens sockets, discovers/joins
rooms, sends/receives packets, and keeps a simple reliability layer with
sequence numbers, a resend queue, and ACKs. Also supports a spectator mode
that relays state to extra viewers.
*/
public class UDPChatManager : MonoBehaviour
{
    [Header("References")]
    public BattleManager battleManager;

    [Header("UI References")]
    public GameObject panelMenu;
    public GameObject panelChat;
    public TMP_InputField inputUsername;
    public Transform roomListContent;
    public GameObject roomButtonPrefab;
    public TMP_Text statusText;
    
    [Header("Chat UI")]
    public TMP_InputField inputMessage;
    public Transform chatContent;    
    public GameObject textMessagePrefab;    
    public GameObject stickerMessagePrefab; 

    [Header("Network Settings")]
    public int chatPort = 8000;
    public int broadcastPort = 8001;

    [Header("Debug")]
    public bool verboseMode = true; 

    [Header("Spectator Mode")]
    public bool isSpectator = false;
    public List<IPEndPoint> spectators = new List<IPEndPoint>();

    private const float RETRY_INTERVAL = 0.5f; 
    private const int MAX_RETRIES = 3;

    private UdpClient chatClient;
    private UdpClient broadcastClient;
    private Thread receiveThread;
    private bool isAppRunning = true;
    
    private float broadcastTimer = 0f;
    public bool isHosting = false;
    private string myUsername = "Player";
    private string targetIP = "";
    private int targetPort = 8000; 
    
    private int localSequenceNumber = 0;
    private bool isBattleSetup = false; 

    private ConcurrentQueue<string> chatQueue = new ConcurrentQueue<string>();
    private ConcurrentQueue<string> foundRoomsQueue = new ConcurrentQueue<string>();
    private Dictionary<string, GameObject> activeRoomButtons = new Dictionary<string, GameObject>();
    private Dictionary<string, string> activeRoomStatuses = new Dictionary<string, string>();
    private ConcurrentQueue<string> battleEventQueue = new ConcurrentQueue<string>(); 
    private object socketLock = new object();

    // --- UPDATED RELIABILITY STATE ---
    private class PendingPacket
    {
        public int sequenceNumber;
        public string payload;
        public float timeSinceLastSend;
        public int retryCount;
        public IPEndPoint destination; // Track who this specific packet is for
    }
    private List<PendingPacket> pendingPackets = new List<PendingPacket>();
    private Dictionary<string, HashSet<int>> receivedSequencesPerUser = new Dictionary<string, HashSet<int>>();

    /*
    Sets up initial UI state, generates a default username, and starts the
    discovery listener so joiners can find hosts.
    */
    void Start()
    {
        if (battleManager == null) battleManager = GetComponent<BattleManager>();

        panelMenu.SetActive(true);
        panelChat.SetActive(false);
        if(inputUsername != null) inputUsername.text = "Player" + UnityEngine.Random.Range(100, 999);

        StartDiscoveryListener();
    }

    /*
    Pumps received UI queues, runs the resend timer, and broadcasts room
    presence once per second when hosting.
    */
    void Update()
    {
        ProcessQueues();
        HandleReliability(); 

        if (isHosting)
        {
            broadcastTimer += Time.deltaTime;
            if (broadcastTimer > 1.0f)
            {
                BroadcastPresence();
                broadcastTimer = 0f;
            }
        }
    }

    /*
    Simple resend loop: walks pending packets, resends on a timer until an
    ACK arrives or retries run out. Drops timeouts without killing the app.
    */
    private void HandleReliability()
    {
        lock (pendingPackets) 
        {
            for (int i = pendingPackets.Count - 1; i >= 0; i--)
            {
                var pkt = pendingPackets[i];
                pkt.timeSinceLastSend += Time.deltaTime;

                if (pkt.timeSinceLastSend >= RETRY_INTERVAL)
                {
                    if (pkt.retryCount < MAX_RETRIES)
                    {
                        if(verboseMode) Debug.LogWarning($"[Resending] Seq {pkt.sequenceNumber} to {pkt.destination}");
                        
                        // Resend to the specific destination stored in the packet
                        SendRawBytes(Encoding.UTF8.GetBytes(pkt.payload), pkt.destination); 
                        
                        pkt.timeSinceLastSend = 0f;
                        pkt.retryCount++;
                    }
                    else
                    {
                        // Don't kill connection for spectators, just drop the packet
                        if (verboseMode) Debug.LogError($"[Timeout] Dropped Seq {pkt.sequenceNumber} for {pkt.destination}");
                        pendingPackets.RemoveAt(i);
                    }
                }
            }
        }
    }

    /*
    Processes queued messages from background threads on the main thread for safe UI updates.
    Handles chat messages, room discoveries, and battle events. Called every frame.
    */
    private void ProcessQueues()
    {
        while (chatQueue.TryDequeue(out string rawMsg))
        {
            string[] parts = rawMsg.Split(new char[]{'|'}, 3);
            if (parts.Length >= 3)
            {
                string type = parts[0];
                string sender = parts[1];
                string data = parts[2];

                if (type == "TEXT_CMD") SpawnText(sender, data);
                else if (type == "STICKER_CMD") SpawnSticker(sender, data);
            }
            else
            {
                SpawnText("System", rawMsg);
            }
        }

        while (foundRoomsQueue.TryDequeue(out string rawEntry))
        {
            string[] parts = rawEntry.Split('|');
            string ip = parts[0];
            string hostName = (parts.Length > 1) ? parts[1] : "Unknown";
            string status = (parts.Length > 2) ? parts[2] : "OPEN";

            // CASE A: It's a brand new room we haven't seen yet
            if (!activeRoomButtons.ContainsKey(ip))
            {
                CreateRoomButton(ip, hostName, status);
            }
            // CASE B: We know this room, BUT the status has changed (e.g. was OPEN, now FULL)
            else if (activeRoomStatuses.ContainsKey(ip) && activeRoomStatuses[ip] != status)
            {
                UpdateRoomButtonVisuals(ip, status); // Call the helper function to update UI
            }
        }
        
        while (battleEventQueue.TryDequeue(out string rawData))
        {
            string type = ParseValue(rawData, "message_type");
            HandleBattleMessage(type, rawData);
        }
    }

    // --- RFC MESSAGE ROUTING ---
    /*
    Routes battle-related messages to `BattleManager`: handshakes, turn commits,
    attack/defense announces, calculation reports/confirmations, resolution
    requests, KO/game-over notices, and spectator state sync.
    */
    private void HandleBattleMessage(string type, string rawData)
    {
        if (type == "HANDSHAKE_REQUEST")
        {
            string joinerName = ParseValue(rawData, "username");
            if (string.IsNullOrEmpty(joinerName)) joinerName = "Unknown Player";

            // Store the name!
            if (battleManager != null) 
            {
                battleManager.enemyUsername = joinerName;
                battleManager.myUsername = myUsername;
            }

            if (!isBattleSetup)
            {
                AddChatMessage("System", $"{joinerName} Connected! Sending Handshake Response...");
            }

            SendHandshakeResponse();

            if (battleManager != null && !isBattleSetup) 
            {
                isBattleSetup = true;
                battleManager.SetWaitingMode(false); //Challenger is here, hide waiting panel
                battleManager.SetupBattle(true);
            }
        }
        else if (type == "HANDSHAKE_RESPONSE")
        {   
            string hostName = ParseValue(rawData, "username"); // Parse Host Name
            if (string.IsNullOrEmpty(hostName)) hostName = "Host";

            // Store the name!
            if (battleManager != null) 
            {
                battleManager.enemyUsername = hostName;
                battleManager.myUsername = myUsername;
            }

            string seed = ParseValue(rawData, "seed");
            
            if (!isBattleSetup)
            {
                AddChatMessage("System", $"Connected! Seed: {seed}");
                isBattleSetup = true;
                if (battleManager != null) battleManager.SetupBattle(false);
            }
        }
        else if (type == "BATTLE_SETUP")
        {
            string pokeName = ParseValue(rawData, "pokemon_name");
            
            if (battleManager != null) 
            {
                if (isSpectator)
                {
                    if (battleManager.GetMyPokemonName() == "Unknown" || battleManager.GetMyPokemonName() == "Bulbasaur")
                    {
                        battleManager.SetMyPokemon(pokeName); 
                    }
                    else
                    {
                        battleManager.SetOpponentPokemon(pokeName);
                    }
                }
                else
                {
                    if (battleManager.GetEnemyPokemonName() != pokeName)
                    {
                        battleManager.SetOpponentPokemon(pokeName);
                    }
                }
                battleManager.OnEnemySwitch(pokeName);
            }
        }
        else if (type == "COMMIT_TURN")
        {
            // 1. Parse Data
            string move = ParseValue(rawData, "move_name");
            int speed = int.Parse(ParseValue(rawData, "speed"));
            bool isSwitch = bool.Parse(ParseValue(rawData, "is_switch"));

            int tieBreaker = 0;
            string tbStr = ParseValue(rawData, "tie_breaker");
            if (!string.IsNullOrEmpty(tbStr)) tieBreaker = int.Parse(tbStr);

            // 2. Tell BattleManager
            if (battleManager != null)
            {
                battleManager.enemyPendingMove = move;
                battleManager.enemyPendingSpeed = speed;
                battleManager.isEnemyActionSwitch = isSwitch;
                battleManager.enemyTieBreaker = tieBreaker; // Save it
                battleManager.hasEnemyCommitted = true;
                
                // 3. Try to Resolve
                battleManager.CheckForResolution();
            }
        }
        // Inside HandleBattleMessage (Add this block)
        else if (type == "TURN_END")
        {
            // The opponent confirms they have finished their action and the turn is over.
            if (battleManager != null)
            {
                battleManager.ForceUnlockAndResetTurn();
            }
        }
        else if (type == "ATTACK_ANNOUNCE")
        {
            // WRAP START
            if (!isSpectator) 
            {
                string move = ParseValue(rawData, "move_name");
                if (battleManager != null) battleManager.OnOpponentAttackAnnounce(move);
            }
            // WRAP END
        }
        else if (type == "DEFENSE_ANNOUNCE")
        {
            // WRAP START
            if (!isSpectator)
            {
                if (battleManager != null) battleManager.OnDefenseAnnounceReceived();
            }
            // WRAP END
        }
        else if (type == "CALCULATION_REPORT")
        {
            // REMOVED "if (!isSpectator)"
            // Spectators MUST run this now, because the logic is inside BattleManager!

            string attackerName = ParseValue(rawData, "attacker"); 
            int dmg = int.Parse(ParseValue(rawData, "damage_dealt"));
            int hp = int.Parse(ParseValue(rawData, "defender_hp_remaining"));
            
            // Parse the 'is_host' boolean
            // We use TryParse to be safe (defaults to false if missing)
            string isHostStr = ParseValue(rawData, "is_host");
            bool isHost = false;
            if (!string.IsNullOrEmpty(isHostStr)) bool.TryParse(isHostStr, out isHost);

            string atkStr = ParseValue(rawData, "atk_stage");
            string defStr = ParseValue(rawData, "def_stage");
            int rAtk = string.IsNullOrEmpty(atkStr) ? 0 : int.Parse(atkStr);
            int rDef = string.IsNullOrEmpty(defStr) ? 0 : int.Parse(defStr);

            if (battleManager != null) 
            {
                // Pass the new ints to the function
                battleManager.OnCalculationReport(attackerName, dmg, hp, isHost, rAtk, rDef);
            }
        }
        else if (type == "CALCULATION_CONFIRM") // 
        {
            // RFC Section 5.2: "turn order reverses, returning to WAITING_FOR_MOVE"
            if (battleManager != null) 
            {
                battleManager.OnCalculationConfirm();
            }
        }
        else if (type == "RESOLUTION_REQUEST")
        {
            // Parse the values from the packet
            int dmg = int.Parse(ParseValue(rawData, "damage_dealt"));
            int hp = int.Parse(ParseValue(rawData, "defender_hp_remaining"));
            
            // Pass to BattleManager
            if (battleManager != null) 
            {
                battleManager.OnResolutionRequest(dmg, hp);
            }
        }
        else if (type == "GAME_OVER")
        {
            string winner = ParseValue(rawData, "winner");
            if (battleManager != null) battleManager.OnGameOver(winner);
        }
        else if (type == "SPECTATOR_SYNC")
        {
            // 1. Parse the Host's State
            string hName = ParseValue(rawData, "host_mon");
            int hHp = int.Parse(ParseValue(rawData, "host_hp"));
            int hMax = int.Parse(ParseValue(rawData, "host_max"));
            
            // 2. Parse the Client's State
            string cName = ParseValue(rawData, "client_mon");
            int cHp = int.Parse(ParseValue(rawData, "client_hp"));
            int cMax = int.Parse(ParseValue(rawData, "client_max"));

            string hUser = ParseValue(rawData, "host_username");
            string cUser = ParseValue(rawData, "client_username");

            string hStats = ParseValue(rawData, "host_stats");
            string cStats = ParseValue(rawData, "client_stats");

            // 3. Force the UI to match
            if (battleManager != null)
            {
                // Pass the names to the function
                battleManager.ForceUpdateSpectatorView(hName, hHp, hMax, cName, cHp, cMax, hUser, cUser, hStats, cStats);
                battleManager.SetWaitingMode(false);
            }
        }
    }

    // --- SENDING FUNCTIONS ---

    /*
    Ask a host to connect: sends a handshake with username and a fresh sequence.
    */
    public void SendHandshakeRequest()
    {
        string payload = $"message_type: HANDSHAKE_REQUEST\n" +
                         $"username: {myUsername}\n" +
                         $"sequence_number: {GetNextSeq()}";
        SendReliablePacket(payload);
    }

    /*
    Reply to a joiner: includes username, a random seed, and sequence.
    */
    public void SendHandshakeResponse()
    {
        // [FIX] Added 'username' so the Joiner knows my name!
        string payload = $"message_type: HANDSHAKE_RESPONSE\n" +
                        $"username: {myUsername}\n" + 
                        $"seed: {UnityEngine.Random.Range(1000, 9999)}\n" +
                        $"sequence_number: {GetNextSeq()}";
        SendReliablePacket(payload);
    }

    /*
    Announce the active Pokémon to the other side so the UI can sync.
    */
    public void SendBattleSetup(string pokemonName)
    {
        string payload = $"message_type: BATTLE_SETUP\n" +
                         $"communication_mode: P2P\n" +
                         $"pokemon_name: {pokemonName}\n" +
                         $"stat_boosts: {{ \"special_attack_uses\": 5, \"special_defense_uses\": 5 }}\n" +
                         $"sequence_number: {GetNextSeq()}";
        SendReliablePacket(payload);
    }

    /*
    Tell the opponent which move is being used.
    */
    public void SendAttackAnnounce(string moveName)
    {
        string payload = $"message_type: ATTACK_ANNOUNCE\n" +
                         $"move_name: {moveName}\n" +
                         $"sequence_number: {GetNextSeq()}";
        SendReliablePacket(payload);;
    }

    /*
    Signal readiness to defend so the attacker can resolve damage.
    */
    public void SendDefenseAnnounce()
    {
        string payload = $"message_type: DEFENSE_ANNOUNCE\n" +
                         $"sequence_number: {GetNextSeq()}";
        SendReliablePacket(payload);
    }

    /*
    Send the official damage math: attacker, move, damage dealt, HP left, and
    the relevant stat stages used in the calculation.
    */
    public void SendCalculationReport(string attacker, string move, int dmg, int defHp, int attHp, bool isHost, int relevantAtkStage, int relevantDefStage)
    {
        string payload = $"message_type: CALCULATION_REPORT\n" +
                        $"attacker: {attacker}\n" +
                        $"move_name: {move}\n" +
                        $"damage_dealt: {dmg}\n" +
                        $"defender_hp_remaining: {defHp}\n" +
                        $"attacker_hp_remaining: {attHp}\n" +
                        $"is_host: {isHost}\n" + 
                        // These are the stages actually used in the math
                        $"atk_stage: {relevantAtkStage}\n" + 
                        $"def_stage: {relevantDefStage}\n" + 
                        $"sequence_number: {GetNextSeq()}";

        SendReliablePacket(payload);
    }

    /*
    Confirm agreement on the calculation; lets turn flow continue.
    */
    public void SendCalculationConfirm()
    {
        string payload = $"message_type: CALCULATION_CONFIRM\n" +
                         $"sequence_number: {GetNextSeq()}";
        SendReliablePacket(payload);
    }
    
    /*
    Request a correction when local math disagrees; includes the values
    calculated locally for comparison.
    */
    public void SendResolutionRequest(string attacker, string move, int myCalcDamage, int myCalcHp)
    {
        string payload = $"message_type: RESOLUTION_REQUEST\n" +
                         $"attacker: {attacker}\n" +
                         $"move_used: {move}\n" +
                         $"damage_dealt: {myCalcDamage}\n" +
                         $"defender_hp_remaining: {myCalcHp}\n" +
                         $"sequence_number: {GetNextSeq()}";
        SendReliablePacket(payload);
        AddChatMessage("System", "Discrepancy! Sending Resolution Request...");
    }

    /*
    Announce the winner and close out the match.
    */
    public void SendGameOver(string winnerName)
    {
        string payload = $"message_type: GAME_OVER\n" +
                         $"winner: {winnerName}\n" +
                         $"loser: {myUsername}\n" +
                         $"sequence_number: {GetNextSeq()}";
        SendReliablePacket(payload);
    }

    /*
    Send a normal text chat message and queue it locally so the UI updates.
    */
    private void SendChatMessage(string messageText)
    {
        string payload = $"message_type: CHAT_MESSAGE\n" +
                         $"sender_name: {myUsername}\n" +
                         $"content_type: TEXT\n" +
                         $"message_text: {messageText}\n" +
                         $"sequence_number: {GetNextSeq()}";
        SendReliablePacket(payload);
        chatQueue.Enqueue($"TEXT_CMD|Me|{messageText}");
    }

    /*
    Send a system line (non-user) and echo it into the local chat.
    */
    private void SendSystemMessage(string messageText)
    {
        string payload = $"message_type: CHAT_MESSAGE\n" +
                         $"sender_name: System\n" +
                         $"content_type: TEXT\n" +
                         $"message_text: {messageText}\n" +
                         $"sequence_number: {GetNextSeq()}";
        SendReliablePacket(payload);
        chatQueue.Enqueue($"TEXT_CMD|System|{messageText}"); 
    }

    /*
    Broadcast a system chat message to peers without queuing it locally.
    */
    public void SendSystemMessagePacket(string text)
    {
        // Sends a chat message labeled as "System" to everyone
        string payload = $"message_type: CHAT_MESSAGE\n" +
                         $"sender_name: System\n" +
                         $"content_type: TEXT\n" +
                         $"message_text: {text}\n" +
                         $"sequence_number: {GetNextSeq()}";
        SendReliablePacket(payload);
    }

    /*
    Send a sticker payload (base64 PNG) and add it to the local chat queue.
    */
    public void SendStickerMessage(string base64Data)
    {
        string payload = $"message_type: CHAT_MESSAGE\n" +
                         $"sender_name: {myUsername}\n" +
                         $"content_type: STICKER\n" +
                         $"sticker_data: {base64Data}\n" +
                         $"sequence_number: {GetNextSeq()}";
        SendReliablePacket(payload);
        chatQueue.Enqueue($"STICKER_CMD|Me|{base64Data}");
    }

    // --- LOW LEVEL UDP + ACK LOGIC ---

    /*
    Increment and return the next sequence number for reliability.
    */
    private int GetNextSeq() 
    { 
        localSequenceNumber++;
        return localSequenceNumber; 
    }

    /*
    Adds the payload to the resend list for the main peer and any spectators,
    then sends immediately. ACKs remove entries.
    */
    private void SendReliablePacket(string payload)
    {
        int seq = int.Parse(ParseValue(payload, "sequence_number"));
        
        if (verboseMode) Debug.Log($"[SENDING]:\n{payload}\n----------------");

        // [FIX] Add to pending list for MAIN TARGET
        if (!string.IsNullOrEmpty(targetIP))
        {
            AddToPending(seq, payload, new IPEndPoint(IPAddress.Parse(targetIP), targetPort));
        }

        // [FIX] Add to pending list for SPECTATORS (Now reliable!)
        foreach (var spec in spectators)
        {
            AddToPending(seq, payload, spec);
        }
    }

    /*
    Track a packet for resends and fire the first send. Stores destination
    to scope ACK removal per peer.
    */
    private void AddToPending(int seq, string payload, IPEndPoint dest)
    {
        lock (pendingPackets)
        {
            pendingPackets.Add(new PendingPacket 
            { 
                sequenceNumber = seq, 
                payload = payload, 
                timeSinceLastSend = 0f, 
                retryCount = 0,
                destination = dest // [NEW] We track the destination
            });
        }
        // Send immediately first time
        SendRawBytes(Encoding.UTF8.GetBytes(payload), dest);
    }

    /*
    Send an ACK back to the sender for the given sequence number.
    */
    private void SendAck(int seqToAck, IPEndPoint target)
    {
        string payload = $"message_type: ACK\nack_number: {seqToAck}";
        byte[] bytes = Encoding.UTF8.GetBytes(payload);
        try { chatClient.Send(bytes, bytes.Length, target); } catch {}
    }

    /*
    Reply to a spectator or joiner directly with a handshake response.
    */
    private void SendHandshakeResponseTo(IPEndPoint target)
    {
        string payload = $"message_type: HANDSHAKE_RESPONSE\n" +
                        $"username: {myUsername}\n" + // [FIX] Added username
                        $"seed: {UnityEngine.Random.Range(1000, 9999)}\n" +
                        $"sequence_number: {GetNextSeq()}";
        AddToPending(GetNextSeq(), payload, target);
    }

    /*
    Low-level send with a socket lock to avoid thread contention.
    */
    private void SendRawBytes(byte[] bytes, IPEndPoint endPoint)
    {
        try
        {
            if (endPoint != null)
            {
                // [FIX] CRITICAL: Lock the socket so Background Relay and Main Thread don't fight
                lock (socketLock) 
                {
                    chatClient.Send(bytes, bytes.Length, endPoint);
                }
            }
        }
        catch (System.Exception e) { Debug.LogError($"Send Error: {e.Message}"); }
    }

    // Helper for old calls
    /*
    Helper overload to send to an IP/port pair.
    */
    private void SendRawBytes(byte[] bytes, string ip, int port)
    {
        SendRawBytes(bytes, new IPEndPoint(IPAddress.Parse(ip), port));
    }

    /*
    Background receive loop: handles ACKs, relays when hosting, queues chat,
    registers spectators, and forwards battle packets to the main thread.
    */
    private void ReceiveChatData()
    {
        IPEndPoint remoteEP = new IPEndPoint(IPAddress.Any, 0);
        while (isAppRunning)
        {
            try
            {
                byte[] data = chatClient.Receive(ref remoteEP);
                string text = Encoding.UTF8.GetString(data);
                string msgType = ParseValue(text, "message_type");
                string senderID = remoteEP.ToString(); 

                if (isHosting && !spectators.Contains(remoteEP))
                {
                    if (string.IsNullOrEmpty(targetIP) && msgType == "HANDSHAKE_REQUEST") 
                    {
                        targetIP = remoteEP.Address.ToString();
                        targetPort = remoteEP.Port; 
                        
                        string joinerName = ParseValue(text, "username");
                        if (string.IsNullOrEmpty(joinerName)) joinerName = "Player 2";
                        
                        SendSystemMessage($"{joinerName} has challenged {myUsername} into a battle!");
                    }
                }

                if (msgType == "ACK")
                {
                    int ackNum = int.Parse(ParseValue(text, "ack_number"));
                    
                    // [FIX] Remove packet only for THIS specific sender
                    lock(pendingPackets) 
                    {
                        pendingPackets.RemoveAll(p => p.sequenceNumber == ackNum && p.destination.ToString() == remoteEP.ToString());
                    }
                    continue; 
                }

                string seqStr = ParseValue(text, "sequence_number");
                if (!string.IsNullOrEmpty(seqStr)) SendAck(int.Parse(seqStr), remoteEP);

                int seq = string.IsNullOrEmpty(seqStr) ? 0 : int.Parse(seqStr);
                if (!receivedSequencesPerUser.ContainsKey(senderID)) receivedSequencesPerUser[senderID] = new HashSet<int>();
                if (receivedSequencesPerUser[senderID].Contains(seq)) continue;
                receivedSequencesPerUser[senderID].Add(seq);

                if (verboseMode) Debug.Log($"<color=green>[RECEIVED]</color> from {senderID}:\n{text}\n----------------");

                if (isHosting && msgType != "SPECTATOR_REQUEST" && msgType != "HANDSHAKE_REQUEST" && msgType != "HANDSHAKE_RESPONSE")
                {
                    HostRelay(text, remoteEP);
                }

                if (msgType == "CHAT_MESSAGE")
                {
                    string sender = ParseValue(text, "sender_name");
                    string contentType = ParseValue(text, "content_type");
                    
                    if (contentType == "STICKER") chatQueue.Enqueue($"STICKER_CMD|{sender}|{ParseValue(text, "sticker_data")}");
                    else chatQueue.Enqueue($"TEXT_CMD|{sender}|{ParseValue(text, "message_text")}");
                    continue; 
                }

                if (msgType == "SPECTATOR_REQUEST")
                {
                    if (!spectators.Contains(remoteEP))
                    {
                        spectators.Add(remoteEP);
                        
                        string specName = ParseValue(text, "username");
                        if (string.IsNullOrEmpty(specName)) specName = "A Spectator";

                        SendSystemMessage($"{specName} has joined as spectator.");
                        SendHandshakeResponseTo(remoteEP);

                        if (battleManager != null)
                        {   
                            // This SYNC function now handles loading the sprites AND setting the correct HP.
                            // We do NOT need to send BATTLE_SETUP anymore.
                            battleManager.SendSpectatorUpdate();
                        }
                    }
                    continue;
                }

                battleEventQueue.Enqueue(text);
            }
            catch (System.Exception e) { Debug.LogError("UDP Error: " + e.Message); }
        }
    }

    // --- UI & UTILS ---

    /*
    Host a room: bind the socket, flip UI to chat, and start broadcasting.
    */
    public void OnClick_HostGame()
    {
        isHosting = true;
        if(inputUsername != null && inputUsername.text.Length > 0) myUsername = inputUsername.text;
        SetupChatSocket();
        panelMenu.SetActive(false);
        panelChat.SetActive(true);
        AddChatMessage("System", "Hosting... Waiting for Joiner.");
        if(statusText != null) statusText.text = "Hosting...";
    }

    /*
    Join a room: set the target IP/port, bind, send handshake, and switch UI.
    */
    public void JoinGame(string ip)
    {
        targetIP = ip;
        targetPort = chatPort; 
        if(inputUsername != null && inputUsername.text.Length > 0) myUsername = inputUsername.text;
        SetupChatSocket();
        panelMenu.SetActive(false);
        panelChat.SetActive(true);
        SendHandshakeRequest();
        AddChatMessage("System", $"Sent Handshake to {ip}...");
        if(statusText != null) statusText.text = "Joining...";
    }
    
    /*
    Send the text typed into the input field.
    */
    public void OnClick_Send()
    {
        if(inputMessage != null) { SendChatMessage(inputMessage.text); inputMessage.text = ""; }
    }

    /*
    Create and bind the UDP socket, enlarge buffers, and start the receive
    thread. Uses a random port if the preferred one is busy.
    */
    private void SetupChatSocket()
    {
        try 
        {
            if (chatClient != null) chatClient.Close();

            try { chatClient = new UdpClient(chatPort); }
            catch (SocketException) { chatClient = new UdpClient(0); }

            chatClient.Client.ReceiveBufferSize = 1024 * 1024;
            chatClient.EnableBroadcast = true;

            receiveThread = new Thread(ReceiveChatData);
            receiveThread.IsBackground = true;
            receiveThread.Start();
            
            AddChatMessage("System", "Socket Opened.");
        }
        catch (System.Exception e) { AddChatMessage("Error", "Bind failed: " + e.Message); }
    }

    /*
    Broadcasts the host's room info (username and status: OPEN/FULL) to the local
    network via UDP. Called every second when hosting to advertise room availability.
    */
    private void BroadcastPresence()
    {
        try
        {
            UdpClient broadcaster = new UdpClient();
            broadcaster.EnableBroadcast = true;

            // LOGIC: If we have a targetIP, we are in battle.
            string status = string.IsNullOrEmpty(targetIP) ? "OPEN" : "FULL";

            // Send: ROOM:Name:Status
            string payload = $"ROOM:{myUsername}:{status}";
            
            byte[] bytes = Encoding.UTF8.GetBytes(payload);
            broadcaster.Send(bytes, bytes.Length, new IPEndPoint(IPAddress.Broadcast, broadcastPort));
            broadcaster.Close();
        }
        catch (System.Exception) { }
    }

    /*
    Starts a background thread to listen for room broadcast packets on the network.
    Discovered rooms (IP, host name, status) are queued for main thread UI updates.
    Only processes broadcasts when not hosting to avoid self-discovery.
    */
    private void StartDiscoveryListener()
    {
        Thread discoveryThread = new Thread(() => 
        {
            try
            {
                broadcastClient = new UdpClient();
                broadcastClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                broadcastClient.Client.Bind(new IPEndPoint(IPAddress.Any, broadcastPort));
                IPEndPoint remoteEP = new IPEndPoint(IPAddress.Any, 0);

                while (isAppRunning)
                {
                    byte[] data = broadcastClient.Receive(ref remoteEP);
                    string message = Encoding.UTF8.GetString(data);
                    
                    // CHANGE STARTS HERE
                    if (message.StartsWith("ROOM:") && !isHosting)
                    {
                        // Format is "ROOM:Name:Status"
                        string[] parts = message.Split(':');
                        
                        // Safety check on array length
                        string hostName = (parts.Length > 1) ? parts[1] : "Unknown";
                        string status = (parts.Length > 2) ? parts[2] : "OPEN"; // Default to OPEN
                        
                        string ip = remoteEP.Address.ToString();

                        // Queue: IP | Name | Status
                        foundRoomsQueue.Enqueue($"{ip}|{hostName}|{status}");
                    }
                    // CHANGE ENDS HERE
                }
            }
            catch (System.Exception) { }
        });
        discoveryThread.IsBackground = true;
        discoveryThread.Start();
    }

    /*
    Creates a room button in the lobby UI showing the host's name and status.
    Registers the button for future updates and wires up Join/Spectate functionality.
    Called when a new room is discovered via broadcast.
    */
    private void CreateRoomButton(string ip, string hostName, string status)
    {
        if(roomButtonPrefab == null || roomListContent == null) return;
        
        GameObject roomObj = Instantiate(roomButtonPrefab, roomListContent);

        // 1. REGISTER THE BUTTON (So we can find it later!)
        if (activeRoomButtons.ContainsKey(ip)) activeRoomButtons.Remove(ip);
        activeRoomButtons.Add(ip, roomObj);

        if (activeRoomStatuses.ContainsKey(ip)) activeRoomStatuses.Remove(ip);
        activeRoomStatuses.Add(ip, status);

        // 2. Set Name
        TMP_Text nameLabel = null;
        Transform textTrans = roomObj.transform.Find("UsernameText"); 
        if (textTrans != null) nameLabel = textTrans.GetComponent<TMP_Text>();
        if (nameLabel != null) nameLabel.text = $"Hoster: {hostName}";

        // 3. Set Visuals (Use the helper function so we don't duplicate code)
        UpdateRoomButtonVisuals(ip, status);
    }

    /*
    Updates a room button's appearance based on status. Join button disabled when FULL,
    enabled when OPEN. Spectate button always enabled. Called when room status changes.
    */
    private void UpdateRoomButtonVisuals(string ip, string status)
    {
        // Safety check: do we actually have a button for this IP?
        if (!activeRoomButtons.ContainsKey(ip)) return;

        GameObject roomObj = activeRoomButtons[ip];
        activeRoomStatuses[ip] = status; // Update our memory of the status

        // 1. GET REFERENCES
        Button joinBtn = null;
        Transform joinTrans = roomObj.transform.Find("JoinButton");
        if (joinTrans != null) joinBtn = joinTrans.GetComponent<Button>();

        Button specBtn = null;
        Transform specTrans = roomObj.transform.Find("SpectateButton");
        if (specTrans != null) specBtn = specTrans.GetComponent<Button>();

        // 2. UPDATE JOIN BUTTON
        if (joinBtn != null)
        {
            // Clear old clicks so we don't stack them
            joinBtn.onClick.RemoveAllListeners(); 

            TMP_Text joinLabel = joinBtn.GetComponentInChildren<TMP_Text>();

            if (status == "FULL")
            {
                joinBtn.interactable = false; // Disable click
                if (joinLabel != null) joinLabel.text = "(IN BATTLE)";
            }
            else
            {
                joinBtn.interactable = true; // Enable click
                if (joinLabel != null) joinLabel.text = "JOIN";
                joinBtn.onClick.AddListener(() => JoinGame(ip));
            }
        }

        // 3. UPDATE SPECTATE BUTTON
        if (specBtn != null)
        {
            specBtn.onClick.RemoveAllListeners();
            specBtn.interactable = true; // Always clickable
            specBtn.onClick.AddListener(() => JoinAsSpectator(ip));
        }
    }

    /*
    Queue a chat line for the UI thread to display.
    */
    public void AddChatMessage(string sender, string msg)
    {
        chatQueue.Enqueue($"TEXT_CMD|{sender}|{msg}");
    }

    /*
    Extracts a value by key from the newline-delimited payload format.
    Accepts optional spaces after the colon.
    */
    private string ParseValue(string raw, string key)
{
    foreach (string line in raw.Split('\n'))
    {
        string trimmed = line.Trim();

        if (trimmed.StartsWith(key + ":"))
            return trimmed.Substring(key.Length + 1).Trim();

        if (trimmed.StartsWith(key + " :"))
            return trimmed.Substring(key.Length + 2).Trim();

        if (trimmed.StartsWith(key + ":"))
            return trimmed.Substring(key.Length + 1).Trim();
    }
    return "";
}
    
    /*
    Cleanly shut down sockets and background threads when exiting.
    */
    private void OnApplicationQuit()
    {
        isAppRunning = false;
        if (chatClient != null) chatClient.Close();
        if (broadcastClient != null) broadcastClient.Close();
        if (receiveThread != null) receiveThread.Abort();
    }

    /*
    Instantiate a text chat line and scroll the view to the bottom.
    */
    public void SpawnText(string sender, string message)
    {
        if (textMessagePrefab == null || chatContent == null) return;

        GameObject obj = Instantiate(textMessagePrefab, chatContent);
        TMP_Text label = obj.GetComponent<TMP_Text>();
        if (label != null) label.text = $"<color=blue>{sender}:</color> {message}";
        Canvas.ForceUpdateCanvases();
        ScrollRect scrollRect = chatContent.parent.parent.GetComponent<ScrollRect>();
        if(scrollRect != null) scrollRect.verticalNormalizedPosition = 0f;
    }

    /*
    Instantiate a sticker message from base64 data and size it nicely.
    */
    private void SpawnSticker(string sender, string base64Data)
    {
        if (stickerMessagePrefab == null || chatContent == null) return;

        byte[] imageBytes = System.Convert.FromBase64String(base64Data);

        //save sticker locally logic
        try 
        {
            // 1. Create Folder
            string folder = Application.persistentDataPath + "/SavedStickers";
            if (!System.IO.Directory.Exists(folder)) System.IO.Directory.CreateDirectory(folder);

            // 2. Create Filename with format = Sticker_Sender_Timestamp.png
            string filename = $"Sticker_{sender}_{System.DateTime.Now:yyyyMMdd_HHmmss}.png";
            string path = System.IO.Path.Combine(folder, filename);

            // 3. Write File
            System.IO.File.WriteAllBytes(path, imageBytes);
            Debug.Log($"[Sticker] Saved to: {path}");
        }
        catch (System.Exception e) 
        {
            Debug.LogError($"Failed to save sticker: {e.Message}");
        }

        Texture2D tex = new Texture2D(2, 2);
        tex.LoadImage(imageBytes); 
        Sprite sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));

        // Text above sticker
        GameObject msgObj = Instantiate(textMessagePrefab, chatContent);
        TMP_Text tmp = msgObj.GetComponent<TMP_Text>();
        if (tmp != null) tmp.text = $"<color=blue>{sender}:</color> sent a sticker!";

        // Sticker image
        GameObject imgObj = Instantiate(stickerMessagePrefab, chatContent);
        Image img = imgObj.GetComponent<Image>();
        if (img != null) img.sprite = sprite;

        // Force size to fit nicely
        LayoutElement layout = imgObj.GetComponent<LayoutElement>();
        if (layout == null) layout = imgObj.AddComponent<LayoutElement>();
        layout.preferredWidth = 150;
        layout.preferredHeight = 150;

        // Scroll down
        Canvas.ForceUpdateCanvases();
        ScrollRect scrollRect = chatContent.parent.parent.GetComponent<ScrollRect>();
        if (scrollRect != null) scrollRect.verticalNormalizedPosition = 0f;
    }

    /*
    Enter spectator mode for a given IP: bind, send a request, flip UI, and
    wait for sync packets.
    */
    public void JoinAsSpectator(string targetIP)
    {
        if(inputUsername != null && inputUsername.text.Length > 0) myUsername = inputUsername.text; 
        
        SetupChatSocket();
        isSpectator = true;
        this.targetIP = targetIP;
        this.targetPort = chatPort; 

        string payload = $"message_type: SPECTATOR_REQUEST\n" +
                         $"username: {myUsername}\n" +
                         $"sequence_number: {GetNextSeq()}";
        SendReliablePacket(payload);
        
        panelMenu.SetActive(false);
        panelChat.SetActive(true);
        AddChatMessage("System", "Sent Spectator Request...");

        if(statusText != null) statusText.text = "Spectating...";
    }

    public TMP_InputField spectateIpInput;

    /*
    UI hook to join as a spectator using the input IP or localhost.
    */
    public void OnClick_Spectate() {
        if(spectateIpInput != null && spectateIpInput.text.Length > 0)
            JoinAsSpectator(spectateIpInput.text);
        else
            JoinAsSpectator("127.0.0.1"); 
    }

    /*
    Send raw data to all registered spectators.
    */
    private void RelayToSpectators(string rawData)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(rawData);
        foreach (var spec in spectators)
        {
            try { chatClient.Send(bytes, bytes.Length, spec); } catch { }
        }
    }

    /*
    Repackage an incoming message with a fresh sequence and forward it to
    everyone except the original sender.
    */
    private void HostRelay(string originalText, IPEndPoint senderEP)
    {
        string cleanMsg = "";
        string[] lines = originalText.Split('\n');
        foreach (var line in lines)
        {
            if (!line.StartsWith("sequence_number") && !string.IsNullOrWhiteSpace(line))
                cleanMsg += line + "\n";
        }

        // Generate ONE sequence number
        int relaySeq = GetNextSeq();
        string newPayload = cleanMsg + $"sequence_number: {relaySeq}";

        List<IPEndPoint> allTargets = new List<IPEndPoint>(spectators);
        if (!string.IsNullOrEmpty(targetIP))
        {
            try
            {
                IPEndPoint joinerEP = new IPEndPoint(IPAddress.Parse(targetIP), targetPort);
                if (!allTargets.Contains(joinerEP)) allTargets.Add(joinerEP);
            }
            catch { }
        }

        foreach (IPEndPoint target in allTargets)
        {
            if (!target.Equals(senderEP))
            {
                // DO NOT generate a new seq number here
                AddToPending(relaySeq, newPayload, target);
            }
        }
    }

    /*
    Snapshot host/client Pokémon names and HP values to keep spectators in sync.
    */
    public void SendSpectatorSync(string hName, int hHp, int hMax, string cName, int cHp, int cMax, string hUser, string cUser, string hStats, string cStats)
    {
        string payload = $"message_type: SPECTATOR_SYNC\n" +
                        $"host_mon: {hName}\n" +
                        $"host_hp: {hHp}\n" +
                        $"host_max: {hMax}\n" +
                        $"client_mon: {cName}\n" +
                        $"client_hp: {cHp}\n" +
                        $"client_max: {cMax}\n" +
                        $"host_username: {hUser}\n" +
                        $"client_username: {cUser}\n" +
                        $"host_stats: {hStats}\n" +
                        $"client_stats: {cStats}\n" +
                        $"sequence_number: {GetNextSeq()}";
                        
        // Send to all spectators
        foreach(var spec in spectators)
        {
            SendRawBytes(Encoding.UTF8.GetBytes(payload), spec);
        }
    }

    /*
    Send the turn commit payload: chosen move/switch, speed, and tie-breaker.
    */
    public void SendCommitPacket(string move, int speed, bool isSwitch, int tieBreaker)
    {
        string payload = $"message_type: COMMIT_TURN\n" +
                         $"move_name: {move}\n" +
                         $"speed: {speed}\n" +
                         $"is_switch: {isSwitch}\n" +
                         $"tie_breaker: {tieBreaker}\n" + // [NEW]
                         $"sequence_number: {GetNextSeq()}";
        SendReliablePacket(payload);
    }

    // Inside UDPChatManager.cs

    // New Sending Function
    /*
    Notify the peer that the turn is over.
    */
    public void SendTurnEndPacket()
    {
        string payload = $"message_type: TURN_END\n" +
                        $"sequence_number: {GetNextSeq()}";
        SendReliablePacket(payload);
    }

    /*
    Shuts down all network components: stops threads, closes UDP sockets, and clears
    lobby data. Called when returning to lobby or exiting to prevent resource leaks.
    */
    public void Shutdown()
    {
        isAppRunning = false; // Stop the threads
        
        // Close Sockets
        if (chatClient != null) { chatClient.Close(); chatClient = null; }
        if (broadcastClient != null) { broadcastClient.Close(); broadcastClient = null; }
        
        // Abort Threads
        if (receiveThread != null && receiveThread.IsAlive) receiveThread.Abort();

        // Clear Lobby Data
        activeRoomButtons.Clear();
        activeRoomStatuses.Clear();
    }
}