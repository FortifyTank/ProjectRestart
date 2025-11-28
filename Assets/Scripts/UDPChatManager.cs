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
    private const int MAX_RETRIES = 5;

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
    private List<string> knownRooms = new List<string>();
    private ConcurrentQueue<string> battleEventQueue = new ConcurrentQueue<string>(); 
    private object socketLock = new object();

    // --- UPDATED RELIABILITY STATE ---
    private class PendingPacket
    {
        public int sequenceNumber;
        public string payload;
        public float timeSinceLastSend;
        public int retryCount;
        public IPEndPoint destination; // [NEW] Track who this specific packet is for
    }
    private List<PendingPacket> pendingPackets = new List<PendingPacket>();
    private Dictionary<string, HashSet<int>> receivedSequencesPerUser = new Dictionary<string, HashSet<int>>();

    void Start()
    {
        if (battleManager == null) battleManager = GetComponent<BattleManager>();

        panelMenu.SetActive(true);
        panelChat.SetActive(false);
        if(inputUsername != null) inputUsername.text = "Player" + UnityEngine.Random.Range(100, 999);

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
                        
                        // [FIX] Resend to the specific destination stored in the packet
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
            string joinerName = ParseValue(rawData, "username");
            if (string.IsNullOrEmpty(joinerName)) joinerName = "Unknown Player";

            if (!isBattleSetup)
            {
                AddChatMessage("System", $"{joinerName} Connected! Sending Handshake Response...");
            }

            SendHandshakeResponse();

            if (battleManager != null && !isBattleSetup) 
            {
                isBattleSetup = true;
                battleManager.SetupBattle(true);
            }
        }
        else if (type == "HANDSHAKE_RESPONSE")
        {
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
                        AddChatMessage("System", $"Host is using {pokeName}");
                    }
                    else
                    {
                        battleManager.SetOpponentPokemon(pokeName);
                        AddChatMessage("System", $"Player 2 is using {pokeName}");
                    }
                }
                else
                {
                    if (battleManager.GetEnemyPokemonName() != pokeName)
                    {
                        battleManager.SetOpponentPokemon(pokeName);
                        AddChatMessage("System", $"Opponent chose {pokeName}");
                    }
                }
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
            // WRAP START
            if (!isSpectator)
            {
                string attackerName = ParseValue(rawData, "attacker"); 
                int dmg = int.Parse(ParseValue(rawData, "damage_dealt"));
                int hp = int.Parse(ParseValue(rawData, "defender_hp_remaining"));
                if (battleManager != null) battleManager.OnCalculationReport(attackerName, dmg, hp);
            }
            // WRAP END
        }
        else if (type == "RESOLUTION_REQUEST")
        {
            // WRAP START
            if (!isSpectator)
            {
                int dmg = int.Parse(ParseValue(rawData, "damage_dealt"));
                int hp = int.Parse(ParseValue(rawData, "defender_hp_remaining"));
                if (battleManager != null) battleManager.OnResolutionRequest(dmg, hp);
            }
            // WRAP END
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

            // 3. Force the UI to match
            if (battleManager != null)
            {
                battleManager.ForceUpdateSpectatorView(hName, hHp, hMax, cName, cHp, cMax);
            }
        }
    }

    // --- SENDING FUNCTIONS ---

    public void SendHandshakeRequest()
    {
        string payload = $"message_type: HANDSHAKE_REQUEST\n" +
                         $"username: {myUsername}\n" +
                         $"sequence_number: {GetNextSeq()}";
        SendReliablePacket(payload);
    }

    public void SendHandshakeResponse()
    {
        string payload = $"message_type: HANDSHAKE_RESPONSE\n" +
                         $"seed: {UnityEngine.Random.Range(1000, 9999)}\n" +
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

    public void SendCalculationReport(string attackerName, string moveUsed, int damage, int hpLeft, int attackerHpLeft) 
    {
        string payload = $"message_type: CALCULATION_REPORT\n" +
                         $"attacker: {attackerName}\n" +
                         $"move_used: {moveUsed}\n" +
                         $"remaining_health: {attackerHpLeft}\n" +
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
        string payload = $"message_type: CHAT_MESSAGE\n" +
                         $"sender_name: {myUsername}\n" +
                         $"content_type: TEXT\n" +
                         $"message_text: {messageText}\n" +
                         $"sequence_number: {GetNextSeq()}";
        SendReliablePacket(payload);
        chatQueue.Enqueue($"TEXT_CMD|Me|{messageText}");
    }

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

    private int GetNextSeq() 
    { 
        localSequenceNumber++;
        return localSequenceNumber; 
    }

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

    private void SendAck(int seqToAck, IPEndPoint target)
    {
        string payload = $"message_type: ACK\nack_number: {seqToAck}";
        byte[] bytes = Encoding.UTF8.GetBytes(payload);
        try { chatClient.Send(bytes, bytes.Length, target); } catch {}
    }

    private void SendHandshakeResponseTo(IPEndPoint target)
    {
        string payload = $"message_type: HANDSHAKE_RESPONSE\n" +
                         $"seed: {UnityEngine.Random.Range(1000, 9999)}\n" +
                         $"sequence_number: {GetNextSeq()}";
        
        // Handshakes to spectators should also be reliable to ensure they connect
        AddToPending(GetNextSeq(), payload, target);
    }

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
    private void SendRawBytes(byte[] bytes, string ip, int port)
    {
        SendRawBytes(bytes, new IPEndPoint(IPAddress.Parse(ip), port));
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
                            string p1 = $"message_type: BATTLE_SETUP\ncommunication_mode: P2P\npokemon_name: {battleManager.GetMyPokemonName()}\nsequence_number: {GetNextSeq()}";
                            AddToPending(GetNextSeq(), p1, remoteEP); // Reliable Setup

                            string enemyName = battleManager.GetEnemyPokemonName();
                            if (enemyName != "Unknown")
                            {
                                string p2 = $"message_type: BATTLE_SETUP\ncommunication_mode: P2P\npokemon_name: {enemyName}\nsequence_number: {GetNextSeq()}";
                                AddToPending(GetNextSeq(), p2, remoteEP); // Reliable Setup
                            }
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
        chatQueue.Enqueue($"TEXT_CMD|{sender}|{msg}");
    }

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

        byte[] imageBytes = System.Convert.FromBase64String(base64Data);
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
    }

    public TMP_InputField spectateIpInput;

    public void OnClick_Spectate() {
        if(spectateIpInput != null && spectateIpInput.text.Length > 0)
            JoinAsSpectator(spectateIpInput.text);
        else
            JoinAsSpectator("127.0.0.1"); 
    }

    private void RelayToSpectators(string rawData)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(rawData);
        foreach (var spec in spectators)
        {
            try { chatClient.Send(bytes, bytes.Length, spec); } catch { }
        }
    }

    private void HostRelay(string originalText, IPEndPoint senderEP)
    {
        string cleanMsg = "";
        string[] lines = originalText.Split('\n');
        foreach (var line in lines)
        {
            if (!line.StartsWith("sequence_number") && !string.IsNullOrWhiteSpace(line))
                cleanMsg += line + "\n";
        }

        // FIXED: Generate ONE sequence number
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
                // FIXED: DO NOT generate a new seq number here
                AddToPending(relaySeq, newPayload, target);
            }
        }
    }

    public void SendSpectatorSync(string hName, int hHp, int hMax, string cName, int cHp, int cMax)
    {
        string payload = $"message_type: SPECTATOR_SYNC\n" +
                        $"host_mon: {hName}\n" +
                        $"host_hp: {hHp}\n" +
                        $"host_max: {hMax}\n" +
                        $"client_mon: {cName}\n" +
                        $"client_hp: {cHp}\n" +
                        $"client_max: {cMax}\n" +
                        $"sequence_number: {GetNextSeq()}";
                        
        // Send to all spectators
        foreach(var spec in spectators)
        {
            SendRawBytes(Encoding.UTF8.GetBytes(payload), spec);
        }
    }
}