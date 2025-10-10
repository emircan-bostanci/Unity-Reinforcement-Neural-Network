# Neural Network Selection System

## Overview
The Agent class now supports both Simple Neural Networks and LSTM Recurrent Neural Networks, selectable via the inspector.

## Key Features

### 🧠 **Dual Network Support**
- **Simple NN**: Fast feedforward network for reactive behavior
- **LSTM RNN**: Memory-based network for strategic/sequence learning

### ⚙️ **Inspector Configuration**
```csharp
[Header("Neural Network Selection")]
public NeuralNetworkType networkType = NeuralNetworkType.SimpleNN;

public enum NeuralNetworkType
{
    SimpleNN,    // Traditional feedforward network
    LSTM_RNN     // Long Short-Term Memory network
}
```

### 🎯 **LSTM-Specific Settings**
```csharp
[Header("LSTM Specific Settings")]
public bool resetLSTMOnDeath = true;      // Reset memory when agent dies
public int lstmSequenceLength = 10;       // Remember last 10 states
public int lstmHiddenSize = 64;           // LSTM hidden layer size
```

## Usage Instructions

### 1. **Inspector Setup**
- Select your Agent GameObject
- In the Agent component, choose "Network Type":
  - **Simple NN**: For faster training, reactive behavior
  - **LSTM RNN**: For strategic learning, memory-based decisions

### 2. **LSTM Configuration**
When using LSTM RNN:
- **Sequence Length**: How many past states to remember (default: 10)
- **Hidden Size**: LSTM memory capacity (default: 64)
- **Reset on Death**: Whether to clear memory when agent dies (recommended: true)

### 3. **Runtime Switching** (Debug)
Use context menu options:
- Right-click Agent → "Switch to Simple NN"
- Right-click Agent → "Switch to LSTM RNN"
- Right-click Agent → "Reset LSTM Memory"

## Expected Learning Differences

### 🔥 **Simple NN (Reactive)**
- **Fast Learning**: Immediate response to current state
- **Good for**: Basic combat, obstacle avoidance
- **Behavior**: "See enemy → shoot", "Hit wall → turn"

### 🧠 **LSTM RNN (Strategic)**
- **Memory-Based**: Learns from sequences and patterns
- **Good for**: Tactical planning, predicting enemy behavior
- **Behavior**: "Enemy moved left 3 times → predict right", "Remember safe paths"

## Technical Implementation

### **Network Interface**
Both networks support:
- `Forward(float[] input)` - Get action output
- `ForwardValue(float[] input)` - Value estimation  
- `SaveModel(filePath)` / `LoadModel(filePath)` - Persistence

### **LSTM Features**
- **Memory States**: Cell state + hidden state for sequence learning
- **Sequence History**: Maintains queue of past observations
- **Memory Reset**: Clears memory on death or manually
- **Training**: Experience replay with sequence awareness

### **Genetic Algorithm Integration**
- Both network types work with existing genetic algorithm
- Model saving/loading adapts to selected network type
- Performance tracking works regardless of network choice

## Recommendations

### **For Beginners**
- Start with **Simple NN** for faster iterations
- Switch to **LSTM RNN** once basic behaviors work

### **For Competition**
- Use **LSTM RNN** for strategic advantage
- Configure 16-20 agents minimum for population diversity
- Enable LSTM memory reset on death for fresh learning

### **Performance Tips**
- **Simple NN**: Lower computational cost, faster training
- **LSTM RNN**: Higher memory usage, slower but smarter behavior
- **Hybrid Approach**: Use Simple NN for exploration, LSTM for exploitation

## Debug Commands

```csharp
[ContextMenu("Log Performance Stats")]   // View training progress
[ContextMenu("Reset LSTM Memory")]       // Clear LSTM memory
[ContextMenu("Switch to Simple NN")]     // Change to feedforward
[ContextMenu("Switch to LSTM RNN")]      // Change to recurrent
```

## Expected Results

With proper population size (16-20 agents):
- **Simple NN**: Fast convergence to basic behaviors
- **LSTM RNN**: Slower initial learning, but superior long-term performance
- **Strategic Behaviors**: Map memory, enemy prediction, tactical planning

The LSTM implementation provides the memory and sequence learning capabilities needed for truly intelligent agent behavior! 🎯🧠