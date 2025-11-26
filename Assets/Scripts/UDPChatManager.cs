using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Collections.Concurrent;
using System.Collections.Generic;

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
    public Transform chatContent;    // NEW: The "Content" object in Scroll View
    public GameObject textMessagePrefab;    // NEW: Your Text Prefab
    public GameObject stickerMessagePrefab; // NEW: Your Sticker Prefab

    [Header("Network Settings")]
    public int chatPort = 8000;
    public int broadcastPort = 8001;

    [Header("Debug")]
    public bool verboseMode = true; // Set this to true in Inspector to see logs

    [Header("Spectator Mode")]
    public bool isSpectator = false;
    public List<IPEndPoint> spectators = new List<IPEndPoint>();

    // --- RELIABILITY SETTINGS ---
    private const float RETRY_INTERVAL = 0.5f; // 500ms
    private const int MAX_RETRIES = 5;

    // Internal Network State
    private UdpClient chatClient;
    private UdpClient broadcastClient;
    private Thread receiveThread;
    private bool isAppRunning = true;
    
    private float broadcastTimer = 0f;
    private bool isHosting = false;
    private string myUsername = "Player";
    private string targetIP = "";
    private int targetPort = 8000; 
    
    // [FIX] Monotonically increasing sequence number (RFC Requirement)
    private int localSequenceNumber = 0;

    // Queues
    private ConcurrentQueue<string> chatQueue = new ConcurrentQueue<string>();
    private ConcurrentQueue<string> foundRoomsQueue = new ConcurrentQueue<string>();
    private List<string> knownRooms = new List<string>();
    private ConcurrentQueue<string> battleEventQueue = new ConcurrentQueue<string>(); 

    // --- RELIABILITY STATE ---
    private class PendingPacket
    {
        public int sequenceNumber;
        public string payload;
        public float timeSinceLastSend;
        public int retryCount;
    }
    private List<PendingPacket> pendingPackets = new List<PendingPacket>();
    private Dictionary<string, HashSet<int>> receivedSequencesPerUser = new Dictionary<string, HashSet<int>>();

    void Start()
    {
        // Auto-find BattleManager
        if (battleManager == null) battleManager = GetComponent<BattleManager>();

        panelMenu.SetActive(true);
        panelChat.SetActive(false);
        myUsername = "Player" + Random.Range(100, 999);
        if(inputUsername != null) inputUsername.text = myUsername;

        StartDiscoveryListener();
    }

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

    private void HandleReliability()
    {
        for (int i = pendingPackets.Count - 1; i >= 0; i--)
        {
            var pkt = pendingPackets[i];
            pkt.timeSinceLastSend += Time.deltaTime;

            if (pkt.timeSinceLastSend >= RETRY_INTERVAL)
            {
                if (pkt.retryCount < MAX_RETRIES)
                {
                    Debug.LogWarning($"[Resending] Seq {pkt.sequenceNumber} (Attempt {pkt.retryCount + 1})");
                    SendRawBytes(Encoding.UTF8.GetBytes(pkt.payload));
                    pkt.timeSinceLastSend = 0f;
                    pkt.retryCount++;
                }
                else
                {
                    Debug.LogError($"[Timeout] Gave up on Seq {pkt.sequenceNumber}");
                    pendingPackets.RemoveAt(i);
                    AddChatMessage("System", "Connection Lost (Timeout)");
                }
            }
        }
    }

    private void ProcessQueues()
    {
        while (chatQueue.TryDequeue(out string rawMsg))
        {
            string[] parts = rawMsg.Split('|');

            // If it's a command (has 3 parts: TYPE|SENDER|DATA)
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
                // Fallback for simple system messages
                SpawnText("System", rawMsg);
            }
        }

        while (foundRoomsQueue.TryDequeue(out string roomIP))
        {
            if (!knownRooms.Contains(roomIP))
            {
                knownRooms.Add(roomIP);
                CreateRoomButton(roomIP);
            }
        }
        
        while (battleEventQueue.TryDequeue(out string rawData))
        {
            string type = ParseValue(rawData, "message_type");
            HandleBattleMessage(type, rawData);
        }
    }

    // --- RFC MESSAGE ROUTING ---
    private void HandleBattleMessage(string type, string rawData)
    {
        if (type == "HANDSHAKE_REQUEST")
        {
            SendHandshakeResponse();
            AddChatMessage("System", "Player Connected! Sending Handshake Response...");
            if (battleManager != null) battleManager.SetupBattle(true);
        }
        else if (type == "HANDSHAKE_RESPONSE")
        {
            string seed = ParseValue(rawData, "seed");
            AddChatMessage("System", $"Connected! Seed: {seed}");
            if (battleManager != null) battleManager.SetupBattle(false);
        }
        else if (type == "BATTLE_SETUP")
        {
            string pokeName = ParseValue(rawData, "pokemon_name");
            
            if (battleManager != null) 
            {
                if (isSpectator)
                {
                    // Spectator Logic: First name received goes to Player 1 (Left), Second to Player 2 (Right)
                    // We check if P1 is empty. If yes, fill it. If no, fill P2.
                    if (battleManager.GetMyPokemonName() == "Unknown" || battleManager.GetMyPokemonName() == "Bulbasaur") // Bulbasaur was our dummy default
                    {
                        // Hack: Use "myPokemon" slot for the Host
                        battleManager.SetMyPokemon(pokeName); // Need to add this helper
                        AddChatMessage("System", $"Host is using {pokeName}");
                    }
                    else
                    {
                        // Use "enemyPokemon" slot for the Joiner
                        battleManager.SetOpponentPokemon(pokeName);
                        AddChatMessage("System", $"Player 2 is using {pokeName}");
                    }
                }
                else
                {
                    // Normal Player Logic
                    battleManager.SetOpponentPokemon(pokeName);
                    AddChatMessage("System", $"Opponent chose {pokeName}");
                }
            }
        }
        else if (type == "ATTACK_ANNOUNCE")
        {
            string move = ParseValue(rawData, "move_name");
            
            // [NEW] Log it for everyone (Players + Spectators)
            // This ensures Spectators see the text log

            if (battleManager != null) battleManager.OnOpponentAttackAnnounce(move);
        }
        else if (type == "DEFENSE_ANNOUNCE")
        {
            if (battleManager != null) battleManager.OnDefenseAnnounceReceived();
        }
        else if (type == "CALCULATION_REPORT")
        {
            string attackerName = ParseValue(rawData, "attacker"); // [NEW] Parse Name
            int dmg = int.Parse(ParseValue(rawData, "damage_dealt"));
            int hp = int.Parse(ParseValue(rawData, "defender_hp_remaining"));
            
            // Pass attackerName to the function
            if (battleManager != null) battleManager.OnCalculationReport(attackerName, dmg, hp);
        }
        else if (type == "RESOLUTION_REQUEST")
        {
            int dmg = int.Parse(ParseValue(rawData, "damage_dealt"));
            int hp = int.Parse(ParseValue(rawData, "defender_hp_remaining"));
            if (battleManager != null) battleManager.OnResolutionRequest(dmg, hp);
        }
        else if (type == "GAME_OVER")
        {
            string winner = ParseValue(rawData, "winner");
            if (battleManager != null) battleManager.OnGameOver(winner);
        }
    }

    // --- SENDING FUNCTIONS (Using Reliable Send + Correct Seq) ---

    public void SendHandshakeRequest()
    {
        string payload = $"message_type: HANDSHAKE_REQUEST\nsequence_number: {GetNextSeq()}";
        SendReliablePacket(payload);
    }

    public void SendHandshakeResponse()
    {
        string payload = $"message_type: HANDSHAKE_RESPONSE\n" +
                         $"seed: {Random.Range(1000, 9999)}\n" +
                         $"sequence_number: {GetNextSeq()}";
        SendReliablePacket(payload);
    }

    public void SendBattleSetup(string pokemonName)
    {
        string payload = $"message_type: BATTLE_SETUP\n" +
                         $"communication_mode: P2P\n" +
                         $"pokemon_name: {pokemonName}\n" +
                         $"stat_boosts: {{ \"special_attack_uses\": 5, \"special_defense_uses\": 5 }}\n" +
                         $"sequence_number: {GetNextSeq()}";
        SendReliablePacket(payload);
    }

    public void SendAttackAnnounce(string moveName)
    {
        string payload = $"message_type: ATTACK_ANNOUNCE\n" +
                         $"move_name: {moveName}\n" +
                         $"sequence_number: {GetNextSeq()}";
        SendReliablePacket(payload);
        AddChatMessage("System", $"You used {moveName}!");
    }

    public void SendDefenseAnnounce()
    {
        string payload = $"message_type: DEFENSE_ANNOUNCE\n" +
                         $"sequence_number: {GetNextSeq()}";
        SendReliablePacket(payload);
    }

    public void SendCalculationReport(string attackerName, string moveUsed, int damage, int hpLeft, int attackerHpLeft) // <--- Add argument
    {
        string payload = $"message_type: CALCULATION_REPORT\n" +
                         $"attacker: {attackerName}\n" +
                         $"move_used: {moveUsed}\n" +
                         $"remaining_health: {attackerHpLeft}\n" + // <--- ADDED THIS LINE
                         $"damage_dealt: {damage}\n" +
                         $"defender_hp_remaining: {hpLeft}\n" +
                         $"status_message: Effective\n" +
                         $"sequence_number: {GetNextSeq()}";
        SendReliablePacket(payload);
    }

    public void SendCalculationConfirm()
    {
        string payload = $"message_type: CALCULATION_CONFIRM\n" +
                         $"sequence_number: {GetNextSeq()}";
        SendReliablePacket(payload);
    }
    
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

    public void SendGameOver(string winnerName)
    {
        string payload = $"message_type: GAME_OVER\n" +
                         $"winner: {winnerName}\n" +
                         $"loser: {myUsername}\n" +
                         $"sequence_number: {GetNextSeq()}";
        SendReliablePacket(payload);
    }

    private void SendChatMessage(string messageText)
    {
        // 1. Send Network Packet
        string payload = $"message_type: CHAT_MESSAGE\n" +
                         $"sender_name: {myUsername}\n" +
                         $"content_type: TEXT\n" +
                         $"message_text: {messageText}\n" +
                         $"sequence_number: {GetNextSeq()}";
        SendReliablePacket(payload);

        // 2. Show Locally (Add to Queue as a Command)
        // Format: TEXT_CMD | SenderName | Message
        chatQueue.Enqueue($"TEXT_CMD|Me|{messageText}");
    }

    public void SendStickerMessage(string base64Data)
    {
        string payload = $"message_type: CHAT_MESSAGE\n" +
                         $"sender_name: {myUsername}\n" +
                         $"content_type: STICKER\n" +
                         $"sticker_data: {base64Data}\n" +
                         $"sequence_number: {GetNextSeq()}";
        SendReliablePacket(payload);
        
        // Show Locally
        chatQueue.Enqueue($"STICKER_CMD|Me|{base64Data}");
    }

    // --- LOW LEVEL UDP + ACK LOGIC ---

    // [FIX] Monotonically Increasing Integer
    private int GetNextSeq() 
    { 
        localSequenceNumber++;
        return localSequenceNumber; 
    }

    private void SendReliablePacket(string payload)
    {
        int seq = int.Parse(ParseValue(payload, "sequence_number"));
        byte[] bytes = Encoding.UTF8.GetBytes(payload);

        if (verboseMode) Debug.Log($"[SENDING]:\n{payload}\n----------------");

        // 1. Send to Main Opponent (Player 2)
        if (!string.IsNullOrEmpty(targetIP))
        {
            pendingPackets.Add(new PendingPacket 
            { 
                sequenceNumber = seq, 
                payload = payload, 
                timeSinceLastSend = 0f, 
                retryCount = 0 
            });
            SendRawBytes(bytes, targetIP, targetPort);
        }

        // 2. Forward to ALL Spectators
        foreach (var spec in spectators)
        {
            try { chatClient.Send(bytes, bytes.Length, spec); } catch { }
        }
    }

    private void SendAck(int seqToAck)
    {
        // ACK doesn't need a sequence number itself (or it uses a special one)
        // RFC 5.1 says "send an ACK message with the corresponding ack_number"
        string payload = $"message_type: ACK\nack_number: {seqToAck}";
        SendRawBytes(Encoding.UTF8.GetBytes(payload));
    }

    // Overload SendAck to reply to a specific person (Spectator OR Player)
    private void SendAck(int seqToAck, IPEndPoint target)
    {
        string payload = $"message_type: ACK\nack_number: {seqToAck}";
        byte[] bytes = Encoding.UTF8.GetBytes(payload);
        try { chatClient.Send(bytes, bytes.Length, target); } catch {}
    }

    // Send the handshake response specifically to a new spectator
    private void SendHandshakeResponseTo(IPEndPoint target)
    {
        string payload = $"message_type: HANDSHAKE_RESPONSE\n" +
                         $"seed: {Random.Range(1000, 9999)}\n" +
                         $"sequence_number: {GetNextSeq()}";
        
        byte[] bytes = Encoding.UTF8.GetBytes(payload);
        try { chatClient.Send(bytes, bytes.Length, target); } catch {}
        
        if(verboseMode) Debug.Log($"[SENT SPECTATOR RESPONSE] to {target.Address}");
    }

    private void SendRawBytes(byte[] bytes, string ip, int port)
    {
        try
        {
            if (!string.IsNullOrEmpty(ip))
            {
                IPEndPoint endPoint = new IPEndPoint(IPAddress.Parse(ip), port);
                chatClient.Send(bytes, bytes.Length, endPoint);
            }
        }
        catch (System.Exception e) 
        { 
            Debug.LogError($"Send Error to {ip}: {e.Message}"); 
        }
    }

    // 2. The original version (Keeps other parts of code working)
    private void SendRawBytes(byte[] bytes)
    {
        // Just forward to the main target
        SendRawBytes(bytes, targetIP, targetPort);
    }

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
                string senderID = remoteEP.ToString(); // IP:Port

                // 1. [HOST] Identify Player 2 (Joiner)
                if (isHosting && !spectators.Contains(remoteEP))
                {
                    if (string.IsNullOrEmpty(targetIP) && msgType == "HANDSHAKE_REQUEST") 
                    {
                        targetIP = remoteEP.Address.ToString();
                        targetPort = remoteEP.Port; 
                        AddChatMessage("System", $"Player 2 Connected from {targetIP}");
                    }
                }

                // 2. Handle ACKs
                if (msgType == "ACK")
                {
                    int ackNum = int.Parse(ParseValue(text, "ack_number"));
                    lock(pendingPackets) pendingPackets.RemoveAll(p => p.sequenceNumber == ackNum);
                    continue; 
                }

                // 3. Send Immediate ACK
                string seqStr = ParseValue(text, "sequence_number");
                if (!string.IsNullOrEmpty(seqStr)) SendAck(int.Parse(seqStr), remoteEP);

                // 4. Duplicate Check
                int seq = string.IsNullOrEmpty(seqStr) ? 0 : int.Parse(seqStr);
                if (!receivedSequencesPerUser.ContainsKey(senderID)) receivedSequencesPerUser[senderID] = new HashSet<int>();
                if (receivedSequencesPerUser[senderID].Contains(seq)) continue;
                receivedSequencesPerUser[senderID].Add(seq);

                if (verboseMode) Debug.Log($"<color=green>[RECEIVED]</color> from {senderID}:\n{text}\n----------------");

                // 5. [HOST FIX] Relay Logic for ALL Message Types (except Handshakes)
                // If I am host, and this is NOT a private handshake/spectator request...
                if (isHosting && msgType != "SPECTATOR_REQUEST" && msgType != "HANDSHAKE_REQUEST" && msgType != "HANDSHAKE_RESPONSE")
                {
                    HostRelay(text, senderID);
                }

                // --- HANDLERS ---

                if (msgType == "SPECTATOR_REQUEST")
                {
                    if (!spectators.Contains(remoteEP))
                    {
                        spectators.Add(remoteEP);
                        AddChatMessage("System", $"Spectator joined: {remoteEP.Address}");
                        
                        // Ack Connection
                        string resp = $"message_type: HANDSHAKE_RESPONSE\nseed: {Random.Range(0,999)}\nsequence_number: {GetNextSeq()}";
                        chatClient.Send(Encoding.UTF8.GetBytes(resp), resp.Length, remoteEP);

                        // Sync Names
                        if (battleManager != null)
                        {
                            string p1 = $"message_type: BATTLE_SETUP\ncommunication_mode: P2P\npokemon_name: {battleManager.GetMyPokemonName()}\nsequence_number: {GetNextSeq()}";
                            chatClient.Send(Encoding.UTF8.GetBytes(p1), p1.Length, remoteEP);

                            string enemyName = battleManager.GetEnemyPokemonName();
                            if (enemyName != "Unknown")
                            {
                                string p2 = $"message_type: BATTLE_SETUP\ncommunication_mode: P2P\npokemon_name: {enemyName}\nsequence_number: {GetNextSeq()}";
                                chatClient.Send(Encoding.UTF8.GetBytes(p2), p2.Length, remoteEP);
                            }
                        }
                    }
                    continue;
                }

                if (msgType == "CHAT_MESSAGE")
                {
                    string sender = ParseValue(text, "sender_name");
                    string contentType = ParseValue(text, "content_type");
                    if (contentType == "STICKER") chatQueue.Enqueue($"STICKER_CMD|{sender}|{ParseValue(text, "sticker_data")}");
                    else chatQueue.Enqueue($"TEXT_CMD|{sender}|{ParseValue(text, "message_text")}");
                }
                else 
                {
                    // Process Battle Message locally
                    battleEventQueue.Enqueue(text);
                }
            }
            catch (System.Exception e) { Debug.LogError("UDP Error: " + e.Message); }
        }
    }
    // --- UI & UTILS ---

    public void OnClick_HostGame()
    {
        isHosting = true;
        if(inputUsername != null) myUsername = inputUsername.text;
        SetupChatSocket();
        panelMenu.SetActive(false);
        panelChat.SetActive(true);
        AddChatMessage("System", "Hosting... Waiting for Joiner.");
        if(statusText != null) statusText.text = "Hosting...";
    }

    public void JoinGame(string ip)
    {
        targetIP = ip;
        targetPort = chatPort; 
        if(inputUsername != null) myUsername = inputUsername.text;
        SetupChatSocket();
        panelMenu.SetActive(false);
        panelChat.SetActive(true);
        SendHandshakeRequest();
        AddChatMessage("System", $"Sent Handshake to {ip}...");
    }
    
    public void OnClick_Send()
    {
        if(inputMessage != null) { SendChatMessage(inputMessage.text); inputMessage.text = ""; }
    }

    private void SetupChatSocket()
    {
        try 
        {
            if (chatClient != null) chatClient.Close();

            try 
            {
                // Try to grab the fixed port (Host/Joiner usually get this)
                chatClient = new UdpClient(chatPort); 
            }
            catch (SocketException) 
            { 
                // If 8000 is taken (Localhost testing), grab a random free port
                chatClient = new UdpClient(0); 
                Debug.Log($"Port {chatPort} busy, listening on random port: {((IPEndPoint)chatClient.Client.LocalEndPoint).Port}");
            }

            // Crucial: Allow this socket to send broadcasts (needed for discovery)
            chatClient.EnableBroadcast = true;

            receiveThread = new Thread(ReceiveChatData);
            receiveThread.IsBackground = true;
            receiveThread.Start();
            
            AddChatMessage("System", "Socket Opened.");
        }
        catch (System.Exception e) { AddChatMessage("Error", "Bind failed: " + e.Message); }
    }

    private void BroadcastPresence()
    {
        try
        {
            UdpClient broadcaster = new UdpClient();
            broadcaster.EnableBroadcast = true;
            string payload = $"ROOM:{myUsername}";
            byte[] bytes = Encoding.UTF8.GetBytes(payload);
            broadcaster.Send(bytes, bytes.Length, new IPEndPoint(IPAddress.Broadcast, broadcastPort));
            broadcaster.Close();
        }
        catch (System.Exception) { }
    }

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
                    if (message.StartsWith("ROOM:") && !isHosting)
                        foundRoomsQueue.Enqueue(remoteEP.Address.ToString());
                }
            }
            catch (System.Exception) { }
        });
        discoveryThread.IsBackground = true;
        discoveryThread.Start();
    }

    private void CreateRoomButton(string ip)
    {
        if(roomButtonPrefab == null || roomListContent == null) return;
        GameObject btn = Instantiate(roomButtonPrefab, roomListContent);
        var legacyText = btn.GetComponentInChildren<Text>();
        if(legacyText != null) legacyText.text = $"Join {ip}";
        var tmpText = btn.GetComponentInChildren<TMP_Text>();
        if(tmpText != null) tmpText.text = $"Join {ip}";
        btn.GetComponent<Button>().onClick.AddListener(() => JoinGame(ip));
    }

    public void AddChatMessage(string sender, string msg)
    {
        // Don't bake "Sender: " into the string here.
        // Just send the command so ProcessQueues calls SpawnText(sender, msg)
        chatQueue.Enqueue($"TEXT_CMD|{sender}|{msg}");
    }

    private string ParseValue(string raw, string key)
    {
        foreach(string line in raw.Split('\n'))
        {
            if (line.StartsWith(key + ":")) return line.Substring(key.Length + 1).Trim();
        }
        return "";
    }
    
    private void OnApplicationQuit()
    {
        isAppRunning = false;
        if (chatClient != null) chatClient.Close();
        if (broadcastClient != null) broadcastClient.Close();
        if (receiveThread != null) receiveThread.Abort();
    }

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

    private void SpawnSticker(string sender, string base64Data)
    {
        if (stickerMessagePrefab == null || chatContent == null) return;

        // 1. Decode Base64 string back to Bytes
        byte[] imageBytes = System.Convert.FromBase64String(base64Data);

        // --- NEW: SAVE TO FILE (Rubric Requirement) ---
        // This saves the image to your computer's AppData folder
        string fileName = $"Sticker_{System.DateTime.Now.Ticks}.png";
        string path = System.IO.Path.Combine(Application.persistentDataPath, fileName);
        System.IO.File.WriteAllBytes(path, imageBytes);
        
        if (verboseMode) Debug.Log($"[FILE SAVED] Sticker saved to: {path}");
        // ----------------------------------------------

        // 2. Create a Texture and load bytes
        Texture2D tex = new Texture2D(2, 2);
        tex.LoadImage(imageBytes); 

        // 3. Convert Texture to Sprite
        Sprite sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));

        // 4. Spawn UI
        GameObject msgObj = Instantiate(textMessagePrefab, chatContent);
        msgObj.GetComponent<TMP_Text>().text = $"<color=blue>{sender}</color> sent a sticker:";

        GameObject imgObj = Instantiate(stickerMessagePrefab, chatContent);
        imgObj.GetComponent<Image>().sprite = sprite;

        Canvas.ForceUpdateCanvases();
        ScrollRect scrollRect = chatContent.parent.parent.GetComponent<ScrollRect>();
        if(scrollRect != null) scrollRect.verticalNormalizedPosition = 0f;
    }

    public void JoinAsSpectator(string targetIP)
    {
        Debug.Log($"Attempting to spectate {targetIP}...");
        
        // 1. Setup Socket
        SetupChatSocket();
        isSpectator = true;
        
        // 2. Set Target (Who are we listening to?)
        this.targetIP = targetIP;
        this.targetPort = chatPort; // Assuming default port

        // 3. Send Request
        string payload = $"message_type: SPECTATOR_REQUEST\nsequence_number: {GetNextSeq()}";
        SendReliablePacket(payload);
        
        // 4. UI Update
        panelMenu.SetActive(false);
        panelChat.SetActive(true);
        AddChatMessage("System", "Sent Spectator Request...");
    }

    public TMP_InputField spectateIpInput; // Drag Input Field here

    public void OnClick_Spectate() {
        if(spectateIpInput != null && spectateIpInput.text.Length > 0)
            JoinAsSpectator(spectateIpInput.text);
        else
            JoinAsSpectator("127.0.0.1"); // Default for testing
    }

    private void RelayToSpectators(string rawData)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(rawData);
        foreach (var spec in spectators)
        {
            try { chatClient.Send(bytes, bytes.Length, spec); } catch { }
        }
    }

    // [NEW] Smart Relay that fixes Sequence Number collisions
    private void HostRelay(string originalText, string senderID)
    {
        // 1. Remove the old sequence number
        string cleanMsg = "";
        string[] lines = originalText.Split('\n');
        foreach(var line in lines)
        {
            if (!line.StartsWith("sequence_number") && !string.IsNullOrWhiteSpace(line))
            {
                cleanMsg += line + "\n";
            }
        }

        // 2. Add Host's new sequence number
        string newPayload = cleanMsg + $"sequence_number: {GetNextSeq()}";
        byte[] bytes = Encoding.UTF8.GetBytes(newPayload);

        // 3. Logic: Where to send?
        // If message came from Joiner (TargetIP) -> Send to ALL Spectators
        if (senderID.StartsWith(targetIP))
        {
            foreach (var spec in spectators) 
                try { chatClient.Send(bytes, bytes.Length, spec); } catch {}
        }
        // If message came from a Spectator -> Send to Joiner AND Other Spectators
        else 
        {
            // Send to Joiner
            if (!string.IsNullOrEmpty(targetIP)) SendRawBytes(bytes, targetIP, targetPort);

            // Send to OTHER Spectators
            foreach (var spec in spectators)
            {
                // Don't send back to the sender!
                if (!senderID.Contains(spec.Address.ToString()))
                    try { chatClient.Send(bytes, bytes.Length, spec); } catch {}
            }
        }
    }
}