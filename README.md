
# Pokemon Battle Game

A multiplayer turn-based Pokemon battle game built with Unity, featuring UDP networking, spectator mode, and authentic Pokemon battle mechanics.

## Overview

This project is a real-time multiplayer Pokemon battle simulator where players can battle each other over a local network with up to 6 Pokemon on their team. The game implements core Pokemon mechanics including type effectiveness, stat stages, status conditions, and turn-based combat.

## AI Disclaimer

The game logic, protocol architecture, and UI were designed by our group with the assistance of AI tools, including image generators and Gemini. We utilized these tools for technical guidance on Unity UI development and for sourcing CSV data. Furthermore, AI was heavily employed for debugging during our rigorous playtesting phase to ensure stability. Additional features, such as the Pokédex system and complex Status Effects, were implemented at our own discretion to enhance gameplay depth. While AI assisted in generating code documentation, we maintain full understanding and ownership of our codebase, particularly the custom UDP protocol and its reliability layer.

## Features

### Core Protocol Implementation 

- **UDP Sockets & Message Handling** 
  - Correct implementation of UDP socket setup, binding, and efficient datagram transmission
  - Non-blocking receive operations with threaded message processing
  - Socket synchronization using thread-safe queues

- **Handshake Process** 
  - Full implementation of Host, Joiner, and Spectator roles
  - Secure seed exchange for deterministic random number generation
  - Room discovery via UDP broadcast messages
  - Automatic peer connection establishment

- **Message Serialization** 
  - All protocol messages formatted as `key:value` pairs with newline delimiters
  - Structured packet types for all game events
  - Reliable parsing and validation of incoming messages

### Game Logic & State Management 

- **Turn-Based Flow** 
  - Complete 4-step turn flow implementation:
    1. Both players commit actions simultaneously
    2. Turn order resolution (Switch > Speed > Tiebreaker)
    3. Sequential move execution
    4. State synchronization
  - Correct reversal of turn order based on speed comparison
  - Tie-breaking using synchronized random values

- **Win/Loss Condition** 
  - Automatic detection when a Pokemon faints (HP ≤ 0)
  - Proper `GAME_OVER` message transmission
  - End-game state handling with winner announcement

- **Battle State Synchronization** 
  - Perfect synchronization of HP values across both peers
  - Status effect tracking (Burn, Poison, Paralysis, Sleep, Freeze)
  - Stat stage modifications synchronized in real-time
  - Calculation verification to prevent desync

### Reliability & Error Handling 

- **Sequence Numbers & ACKs** 
  - Robust sequence numbering for all critical battle messages
  - Acknowledgement system for reliable delivery
  - Per-user received sequence tracking to prevent duplicates

- **Retransmission Logic** 
  - Automatic retransmission upon timeout (0.5s intervals)
  - Retry counter implementation (max 3 retries)
  - Pending packet queue management
  - Packet loss simulation support for testing

- **Discrepancy Resolution** 
  - Detection of calculation mismatches in damage reports
  - `RESOLUTION_REQUEST` protocol for conflict resolution
  - Automatic correction acceptance mechanism
  - Prevention of state divergence between clients

### Additional Features

- **Damage Calculation** 
  - Accurate implementation of Pokemon damage formula:

    ```text
    Damage = (Move Power × Attack × Type Effectiveness) / Defense
    ```

  - Separate Special Attack and Special Defense stats
  - Stat stage multipliers (±6 stages: 0.25× to 4×)
  - Type effectiveness system (0×, 0.25×, 0.5×, 1×, 2×, 4×)
  - Burn status halving physical attack damage

- **Chat Functionality (Text)** 
  - Plain-text chat system running asynchronously
  - Non-disruptive to battle state machine
  - System messages for game events
  - User-to-user messaging

- **Chat Functionality (Stickers)** 
  - Base64 encoded sticker message handling
  - Sticker decoding and display
  - Save sticker data to file functionality
  - Integration with StickerManager component

- **Verbose Mode** 
  - Detailed message logging in delimited format
  - Toggle between verbose and normal modes
  - Hidden reliability/error messages in normal mode
  - Error messages always visible regardless of mode


### Bonus Features 

- **Spectator Mode** 
  - Full spectator UI implementation
  - Real-time battle viewing without participation
  - Spectator-specific packet handling and updates
  - Read-only interface with disabled controls

- **Graphical Battle Interface** 
  - Unity-based visual battle system
  - Pokemon sprite rendering from Resources
  - Animated HP bars with smooth transitions
  - Visual stat stage indicators with color coding
  - Team selection GUI with party management
  - Bag interface for item usage

## Project Structure

```
ProjectRestart/
├── Assets/
│   ├── Scripts/
│   │   ├── BattleManager.cs          # Core battle logic and turn resolution
│   │   ├── UDPChatManager.cs         # Networking and packet handling
│   │   ├── PokemonData.cs            # Pokemon data model
│   │   ├── PokemonDatabase.cs        # Pokemon data loading from CSV
│   │   ├── PokemonSelector.cs        # Team selection UI
│   │   ├── MoveLoader.cs             # Move data loading from CSV
│   │   ├── GameConstants.cs          # Enums and shared data structures
│   │   ├── StickerManager.cs         # Chat sticker support
│   │   ├── LoadSprites.cs            # Sprite loading utilities
│   │   ├── SpriteMap.cs              # Sprite mapping utilities
│   │   ├── SpriteTester.cs           # Sprite testing utilities
│   │   └── WindowSetup.cs            # Window configuration
│   ├── Resources/
│   │   └── Sprites/                  # Pokemon sprite images
│   └── TextMesh Pro/                 # UI text rendering
├── ProjectSettings/                   # Unity project configuration
└── README.md                          # This file
```

## Getting Started

### Prerequisites
- Unity 2020.3 or later
- .NET Framework 4.x
- Windows, macOS, or Linux

### Installation

1. Clone or download this repository
2. Open the project in Unity Hub
3. Open the main scene in Unity Editor
4. Ensure Pokemon data CSV files are in the Resources folder
5. Press Play to test in the Unity Editor

### Building the Game

1. Go to `File > Build Settings`
2. Select your target platform (Windows, macOS, Linux)
3. Click `Build` and choose an output directory
4. Run the executable to start the game

## How to Play

### Starting a Battle

**As Host:**
1. Enter your username
2. Click "Host Game"
3. Wait for an opponent to join
4. Select your team of Pokemon
5. Battle begins automatically

**As Joiner:**
1. Enter your username
2. Wait for available rooms to appear
3. Click on a room to join
4. Select your team of Pokemon
5. Battle begins automatically

**As Spectator:**
1. Enter your username
2. Enable "Spectator Mode" checkbox
3. Join an existing room
4. Watch the battle unfold

### Battle Controls

- **Fight**: Choose one of your Pokemon's 4 moves
- **Bag**: Use consumable items (5 uses each)
- **Pokemon**: Switch to a different party member
- **Run**: Surrender the match
- **Stats**: View current stat stage modifications

### Battle Mechanics

#### Turn Order
1. Both players select an action simultaneously
2. Turn order is determined by:
   - Switching always goes first
   - Higher Speed stat moves next
   - Random tiebreaker if speeds are equal

#### Damage Calculation
```
Damage = (Move Power × Attack Stat × Type Effectiveness) / Defense Stat
```
- Attack/Defense affected by stat stages (±6 stages max)
- Burn halves physical attack damage
- Type effectiveness ranges from 0× to 4×

#### Status Conditions
- **Burn**: Takes 1/8 max HP damage per turn, halves physical attack
- **Poison**: Takes 1/8 max HP damage per turn
- **Paralysis**: 25% chance to be unable to move
- **Sleep**: Can't move for 1-3 turns
- **Freeze**: Currently implemented but not used

#### Stat Stages
- Range: -6 to +6
- Each stage multiplies the stat by 1.5× (positive) or 0.67× (negative)
- Reset when switching out

## Network Architecture

### UDP Communication
- **Chat Port**: 8000 (default)
- **Broadcast Port**: 8001 (default)
- **Packet Types**:
  - Room discovery broadcasts
  - Battle setup and handshake
  - Move commitments
  - Damage calculations
  - Switch notifications
  - Spectator updates

### Reliability Layer
- Sequence numbers for packet ordering
- ACK/Retry system for critical packets
- Maximum 3 retries with 0.5s intervals
- Duplicate detection per user

## Data Files

The game requires CSV data files for Pokemon and moves:

### Pokemon Data
Expected fields: ID, Name, Types, HP, Attack, Defense, Sp. Attack, Sp. Defense, Speed, Type Effectiveness

### Move Data
Expected fields: ID, Name, Type, Category, Power, Damage Class, Ailment ID, Ailment Chance, Stat Changes

## Known Limitations

- LAN-only multiplayer (no internet play)
- Limited to Pokemon available in data files
- No animations or sound effects
- Basic sprite rendering
- Windows-optimized (may require adjustments for other platforms)


