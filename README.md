# Unity Reinforcement Learning Environment

![Unity](https://img.shields.io/badge/Unity-6000.2.2f1+-000000.svg?style=flat&logo=unity)
![C#](https://img.shields.io/badge/C%23-8.0+-239120.svg?style=flat&logo=c-sharp)
![License](https://img.shields.io/badge/License-MIT-green.svg?style=flat)

## Project Overview

This is a Unity-based reinforcement learning environment where multiple AI agents learn to compete in a battle arena. The project implements neural networks that can be trained using genetic algorithms to develop combat behaviors.

### Key Features

- **Dual Neural Network Support**: Simple feedforward networks and LSTM recurrent networks
- **Genetic Algorithm Training**: Population-based evolution with fitness evaluation
- **Multi-Agent Environment**: Supports multiple agents training simultaneously
- **Raycast Vision System**: 32-ray perception system for environment awareness
- **Model Persistence**: Save and load trained neural network weights
- **Real-time Training**: Watch agents learn and improve their behaviors

## Technical Implementation

### Neural Network Architecture

#### Simple Neural Network
- **Layers**: 35 inputs → 128 hidden → 64 hidden → 5 outputs
- **Activation Functions**: ReLU for hidden layers, Tanh for output
- **Training Method**: Policy gradient with advantage estimation

#### LSTM Neural Network  
- **Architecture**: 35 inputs → LSTM(64 hidden units) → 5 outputs
- **Sequence Length**: 10 timesteps
- **Memory**: Maintains cell state and hidden state between timesteps

### State Representation (35 dimensions)
```csharp
State = {
    raycastDistances[32],    // 32 normalized distance values (0-1)
    agentPosition.x,         // Agent X position (normalized)
    agentPosition.y,         // Agent Y position (normalized)  
    agentRotation           // Agent rotation (normalized 0-1)
}
```

### Action Space (5 continuous values)
```csharp
Action = {
    lookAngle,     // Rotation input (-1 to 1)
    shoot,         // Shoot action (threshold > 0.1)
    moveForward,   // Forward movement (-1 to 1)
    moveLeft,      // Left strafe (-1 to 1)
    moveRight      // Right strafe (-1 to 1)
}
```

### Genetic Algorithm
- **Population**: Configurable agent count (default: 8)
- **Selection**: Elite preservation (top 20% by default)
- **Mutation**: Gaussian noise applied to neural network weights
- **Fitness**: Based on kills, survival time, and reward accumulation

## Environment Features

### Vision System
Each agent has 32 raycasts in a forward-facing arc that detect:
- Walls (collision detection)
- Other agents (enemy detection)
- Empty space

### Reward System
- **Kill Enemy**: +200 points
- **Spot Enemy**: +5 points  
- **Miss Shot**: -5 points
- **Hit Wall**: -20 points
- **Survival**: Small time-based bonus

### Training Managers
- **TrainingManager**: Standard reinforcement learning loop
- **GeneticAlgorithmManager**: Handles population evolution
- Multiple specialized training managers for different approaches

## Getting Started

### Requirements
- Unity 6000.2.2f1 or newer
- Windows/Mac/Linux
- Minimum 4GB RAM

### Installation
1. Clone or download this repository
2. Open the project in Unity
3. Open the scene: `Assets/Scenes/SampleScene.unity`
4. Press Play to start training

### Configuration
Key settings can be adjusted in the Unity Inspector:

```csharp
// Agent Settings
[Header("Neural Network Selection")]
public NeuralNetworkType networkType = NeuralNetworkType.LSTM_RNN;

[Header("Training Parameters")]
public float learningRate = 0.001f;
public float gamma = 0.99f;
public int batchSize = 32;

// Genetic Algorithm Settings  
[Header("Genetic Algorithm")]
public float generationDuration = 10f;
public float mutationRate = 0.1f;
public float elitePercentage = 0.2f;
```

## Project Structure

```
Assets/Scripts/
├── Agent/
│   └── Agent.cs                 # Main agent implementation
├── NeuralNetwork/
│   ├── SimpleNeuralNetwork.cs   # Feedforward network
│   ├── LSTMNeuralNetwork.cs     # LSTM implementation
│   └── INeuralNetwork.cs        # Network interface
├── Environment/
│   └── Environment.cs           # Training environment
├── TrainingManager.cs           # Training loop management
└── GeneticAlgorithmManager.cs   # Population evolution
```

## How It Works

1. **Initialization**: Agents spawn with random neural network weights
2. **Perception**: Each agent uses 32 raycasts to sense the environment
3. **Decision**: Neural networks process sensory input to produce actions
4. **Action**: Agents move, rotate, and shoot based on network outputs
5. **Evaluation**: Agents receive rewards/penalties based on performance
6. **Evolution**: After each generation, successful agents reproduce with mutations

## Debug Features

The project includes several context menu options for debugging:
- `[ContextMenu("Show Agent Performance")]` - Display agent statistics
- `[ContextMenu("Switch to LSTM RNN")]` - Change network type at runtime
- `[ContextMenu("Load Best Model")]` - Load previously saved models
- `[ContextMenu("Force Evolution")]` - Manually trigger genetic algorithm

## License

MIT License - see LICENSE file for details.

## Contributing

Contributions are welcome! Please feel free to submit pull requests or open issues for bugs and feature requests.

## Author

Emircan Bostanci
Beyhan Meyrali
