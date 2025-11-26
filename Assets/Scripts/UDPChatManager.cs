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
    private HashSet<int> receivedSequences = new HashSet<int>();

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
            AddChatMessage("System", $"Opponent chose {pokeName}");
            
            // UPDATE THE BATTLE MANAGER
            if (battleManager != null) 
            {
                battleManager.SetOpponentPokemon(pokeName);
            }
        }
        else if (type == "ATTACK_ANNOUNCE")
        {
            string move = ParseValue(rawData, "move_name");
            if (battleManager != null) battleManager.OnOpponentAttackAnnounce(move);
        }
        else if (type == "DEFENSE_ANNOUNCE")
        {
            if (battleManager != null) battleManager.OnDefenseAnnounceReceived();
        }
        else if (type == "CALCULATION_REPORT")
        {
            int dmg = int.Parse(ParseValue(rawData, "damage_dealt"));
            int hp = int.Parse(ParseValue(rawData, "defender_hp_remaining"));
            if (battleManager != null) battleManager.OnCalculationReport(dmg, hp);
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
        AddChatMessage("System", $"GAME OVER! {winnerName} wins!");
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
        if (string.IsNullOrEmpty(targetIP)) return;

        int seq = int.Parse(ParseValue(payload, "sequence_number"));
        
        if (verboseMode) 
        {
            Debug.Log($"<color=orange>[SENDING]</color> to {targetIP}:\n{payload}\n----------------");
        }

        pendingPackets.Add(new PendingPacket 
        { 
            sequenceNumber = seq, 
            payload = payload, 
            timeSinceLastSend = 0f, 
            retryCount = 0 
        });

        SendRawBytes(Encoding.UTF8.GetBytes(payload));
    }

    private void SendAck(int seqToAck)
    {
        // ACK doesn't need a sequence number itself (or it uses a special one)
        // RFC 5.1 says "send an ACK message with the corresponding ack_number"
        string payload = $"message_type: ACK\nack_number: {seqToAck}";
        SendRawBytes(Encoding.UTF8.GetBytes(payload));
    }

    private void SendRawBytes(byte[] bytes)
    {
        try
        {
            if (!string.IsNullOrEmpty(targetIP))
            {
                chatClient.Send(bytes, bytes.Length, targetIP, targetPort);
            }
        }
        catch (System.Exception e) { Debug.LogError(e.Message); }
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
                
                if (verboseMode)
                {
                    Debug.Log($"<color=green>[RECEIVED]</color> from {remoteEP.Address}:\n{text}\n----------------");
                }

                if (isHosting)
                {
                    targetIP = remoteEP.Address.ToString();
                    targetPort = remoteEP.Port; 
                }

                string msgType = ParseValue(text, "message_type");

                if (msgType == "ACK")
                {
                    int ackNum = int.Parse(ParseValue(text, "ack_number"));
                    lock(pendingPackets) 
                    {
                        pendingPackets.RemoveAll(p => p.sequenceNumber == ackNum);
                    }
                    continue; 
                }

                // Handle Normal Messages
                int seq = 0;
                string seqStr = ParseValue(text, "sequence_number");
                if (!string.IsNullOrEmpty(seqStr)) seq = int.Parse(seqStr);

                SendAck(seq); // IMMEDIATE ACK

                if (receivedSequences.Contains(seq)) continue; // Ignore duplicate
                receivedSequences.Add(seq);

                if (msgType == "CHAT_MESSAGE")
                {
                    string sender = ParseValue(text, "sender_name");
                    string contentType = ParseValue(text, "content_type");

                    if (contentType == "STICKER")
                    {
                        string stickerData = ParseValue(text, "sticker_data");
                        // Queue a command to spawn sticker on main thread
                        chatQueue.Enqueue($"STICKER_CMD|{sender}|{stickerData}");
                    }
                    else // It's standard TEXT
                    {
                        string content = ParseValue(text, "message_text");
                        // Queue a command to spawn text on main thread
                        chatQueue.Enqueue($"TEXT_CMD|{sender}|{content}");
                    }
                }
                else 
                {
                    battleEventQueue.Enqueue(text);
                }
            }
            catch (System.Exception) { break; }
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
            try { chatClient = new UdpClient(chatPort); }
            catch { chatClient = new UdpClient(0); } 

            receiveThread = new Thread(ReceiveChatData);
            receiveThread.IsBackground = true;
            receiveThread.Start();
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

    private void AddChatMessage(string sender, string msg)
    {
        chatQueue.Enqueue($"<color=blue>{sender}:</color> {msg}");
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
    }
}