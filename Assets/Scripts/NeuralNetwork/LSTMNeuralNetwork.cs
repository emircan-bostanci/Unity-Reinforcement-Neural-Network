using UnityEngine;
using System;
using System.Collections.Generic;

[System.Serializable]
public class LSTMNeuralNetwork : MonoBehaviour
{
    [Header("LSTM Network Architecture")]
    public int inputSize = 38;
    public int lstmHiddenSize = 64;
    public int outputSize = 5;
    public int numLayers = 2;
    public float learningRate = 0.001f;
    
    [Header("Memory Settings")]
    public int sequenceLength = 10; // Remember last 10 states
    public bool resetMemoryOnDeath = true;
    
    [Header("Training Settings")]
    public float gradientClipping = 1.0f;
    public float dropoutRate = 0.1f;
    public bool enableTraining = true;
    
    // LSTM Cell state and hidden state
    private float[] cellState;
    private float[] hiddenState;
    private Queue<float[]> stateHistory;
    
    // Network weights (simplified LSTM implementation)
    private float[,] weightsInputGate;
    private float[,] weightsForgetGate; 
    private float[,] weightsOutputGate;
    private float[,] weightsCandidateGate;
    private float[,] weightsHiddenToOutput;
    private float[] biases;
    
    // Training data buffers
    private List<float[]> experienceStates;
    private List<float[]> experienceActions;
    private List<float> experienceRewards;
    private List<bool> experienceDones;
    
    // Network state
    private bool isInitialized = false;
    private System.Random random;
    private int trainingSteps = 0;
    private float? gaussianSpare = null; // For Box-Muller transform
    
    public bool IsInitialized => isInitialized;
    public bool UseValueNetwork { get; set; } = true;

    public void Initialize()
    {
        if (isInitialized) return;
        
        random = new System.Random(GetInstanceID()); // Unique seed per instance
        
        // Initialize LSTM states
        cellState = new float[lstmHiddenSize];
        hiddenState = new float[lstmHiddenSize];
        stateHistory = new Queue<float[]>();
        
        // Initialize weights with Xavier/Glorot initialization
        InitializeWeights();
        
        // Initialize experience buffers
        experienceStates = new List<float[]>();
        experienceActions = new List<float[]>();
        experienceRewards = new List<float>();
        experienceDones = new List<bool>();
        
        isInitialized = true;
        Debug.Log($"🧠 LSTM Neural Network initialized: Input={inputSize}, LSTM={lstmHiddenSize}, Output={outputSize}, Layers={numLayers}");
    }
    
    private void InitializeWeights()
    {
        // Xavier/Glorot initialization for better gradient flow
        float inputScale = Mathf.Sqrt(2.0f / (inputSize + lstmHiddenSize));
        float hiddenScale = Mathf.Sqrt(2.0f / (lstmHiddenSize * 2));
        float outputScale = Mathf.Sqrt(2.0f / (lstmHiddenSize + outputSize));
        
        int totalInputSize = inputSize + lstmHiddenSize; // Concatenated input
        
        weightsInputGate = InitializeMatrix(totalInputSize, lstmHiddenSize, inputScale);
        weightsForgetGate = InitializeMatrix(totalInputSize, lstmHiddenSize, inputScale);
        weightsOutputGate = InitializeMatrix(totalInputSize, lstmHiddenSize, inputScale);
        weightsCandidateGate = InitializeMatrix(totalInputSize, lstmHiddenSize, inputScale);
        weightsHiddenToOutput = InitializeMatrix(lstmHiddenSize, outputSize, outputScale);
        
        // Initialize biases (forget gate bias = 1.0 for better gradient flow)
        biases = new float[lstmHiddenSize * 4 + outputSize];
        for (int i = lstmHiddenSize; i < lstmHiddenSize * 2; i++) // Forget gate biases
        {
            biases[i] = 1.0f; // Initialize forget gate bias to 1
        }
        
        Debug.Log("✅ LSTM weights initialized with Xavier initialization");
    }
    
    private float[,] InitializeMatrix(int rows, int cols, float scale)
    {
        float[,] matrix = new float[rows, cols];
        for (int i = 0; i < rows; i++)
        {
            for (int j = 0; j < cols; j++)
            {
                // Xavier initialization: random normal * sqrt(2 / (fan_in + fan_out))
                matrix[i, j] = GaussianRandom() * scale;
            }
        }
        return matrix;
    }
    
    private float GaussianRandom()
    {
        // Box-Muller transform for Gaussian random numbers
        if (gaussianSpare.HasValue)
        {
            float temp = gaussianSpare.Value;
            gaussianSpare = null;
            return temp;
        }
        
        float u1 = (float)random.NextDouble();
        float u2 = (float)random.NextDouble();
        float mag = Mathf.Sqrt(-2.0f * Mathf.Log(u1));
        gaussianSpare = mag * Mathf.Sin(2.0f * Mathf.PI * u2);
        return mag * Mathf.Cos(2.0f * Mathf.PI * u2);
    }
    
    public float[] Forward(float[] input)
    {
        if (!isInitialized) Initialize();
        
        // Validate input
        if (input.Length != inputSize)
        {
            Debug.LogError($"Input size mismatch: expected {inputSize}, got {input.Length}");
            return new float[outputSize];
        }
        
        // Add current state to history for sequence learning
        stateHistory.Enqueue((float[])input.Clone());
        if (stateHistory.Count > sequenceLength)
        {
            stateHistory.Dequeue();
        }
        
        // Create concatenated input [current_input, previous_hidden_state]
        float[] lstmInput = new float[inputSize + lstmHiddenSize];
        Array.Copy(input, 0, lstmInput, 0, inputSize);
        Array.Copy(hiddenState, 0, lstmInput, inputSize, lstmHiddenSize);
        
        // LSTM Forward Pass
        ForwardLSTMCell(lstmInput);
        
        // Output layer (policy network)
        float[] output = new float[outputSize];
        for (int i = 0; i < outputSize; i++)
        {
            output[i] = biases[lstmHiddenSize * 4 + i]; // Output bias
            for (int j = 0; j < lstmHiddenSize; j++)
            {
                output[i] += hiddenState[j] * weightsHiddenToOutput[j, i];
            }
            
            // Apply appropriate activation for each output
            if (i == 0) // lookAngle: [-1, 1]
            {
                output[i] = Tanh(output[i]);
            }
            else if (i == 1) // shoot: [0, 1]
            {
                output[i] = Sigmoid(output[i]);
            }
            else // movement: [0, 1] or [-1, 1] depending on usage
            {
                output[i] = Tanh(output[i]);
            }
        }
        
        return output;
    }
    
    public float ForwardValue(float[] input)
    {
        if (!UseValueNetwork) return 0f;
        
        // Simple value estimation from LSTM hidden state
        float value = 0f;
        float[] output = Forward(input);
        
        // Estimate value based on output confidence and hidden state activation
        for (int i = 0; i < lstmHiddenSize; i++)
        {
            value += hiddenState[i] * 0.1f; // Simple value approximation
        }
        
        return Mathf.Clamp(value, -1f, 1f); // Normalized value estimate
    }
    
    private void ForwardLSTMCell(float[] input)
    {
        // Calculate LSTM gates
        float[] forgetGate = CalculateGate(input, weightsForgetGate, lstmHiddenSize, "sigmoid");
        float[] inputGate = CalculateGate(input, weightsInputGate, 0, "sigmoid");
        float[] candidateGate = CalculateGate(input, weightsCandidateGate, lstmHiddenSize * 2, "tanh");
        float[] outputGate = CalculateGate(input, weightsOutputGate, lstmHiddenSize * 3, "sigmoid");
        
        // Update cell state: C_t = f_t ⊙ C_{t-1} + i_t ⊙ C̃_t
        for (int i = 0; i < lstmHiddenSize; i++)
        {
            cellState[i] = forgetGate[i] * cellState[i] + inputGate[i] * candidateGate[i];
            
            // Gradient clipping for cell state
            cellState[i] = Mathf.Clamp(cellState[i], -gradientClipping, gradientClipping);
        }
        
        // Update hidden state: h_t = o_t ⊙ tanh(C_t)
        for (int i = 0; i < lstmHiddenSize; i++)
        {
            hiddenState[i] = outputGate[i] * Tanh(cellState[i]);
            
            // Apply dropout during training (simplified)
            if (enableTraining && random.NextDouble() < dropoutRate)
            {
                hiddenState[i] = 0f;
            }
        }
    }
    
    private float[] CalculateGate(float[] input, float[,] weights, int biasOffset, string activation)
    {
        float[] gate = new float[lstmHiddenSize];
        
        for (int i = 0; i < lstmHiddenSize; i++)
        {
            gate[i] = biases[biasOffset + i]; // Add bias
            
            // Matrix multiplication
            for (int j = 0; j < input.Length; j++)
            {
                gate[i] += input[j] * weights[j, i];
            }
            
            // Apply activation function
            switch (activation)
            {
                case "sigmoid":
                    gate[i] = Sigmoid(gate[i]);
                    break;
                case "tanh":
                    gate[i] = Tanh(gate[i]);
                    break;
                default:
                    gate[i] = Tanh(gate[i]);
                    break;
            }
        }
        
        return gate;
    }
    
    public void ResetMemory()
    {
        if (!isInitialized) return;
        
        // Clear LSTM memory states
        for (int i = 0; i < lstmHiddenSize; i++)
        {
            cellState[i] = 0f;
            hiddenState[i] = 0f;
        }
        
        stateHistory.Clear();
        Debug.Log("🧠 LSTM memory states reset");
    }
    
    public void StoreExperience(float[] state, float[] action, float reward, bool done)
    {
        if (!enableTraining) return;
        
        experienceStates.Add((float[])state.Clone());
        experienceActions.Add((float[])action.Clone());
        experienceRewards.Add(reward);
        experienceDones.Add(done);
        
        // Reset memory if agent died
        if (done && resetMemoryOnDeath)
        {
            ResetMemory();
        }
        
        // Limit experience buffer size to prevent memory issues
        const int maxBufferSize = 2000;
        if (experienceStates.Count > maxBufferSize)
        {
            experienceStates.RemoveAt(0);
            experienceActions.RemoveAt(0);
            experienceRewards.RemoveAt(0);
            experienceDones.RemoveAt(0);
        }
    }
    
    public void TrainOnBatch(int batchSize = 32)
    {
        if (!enableTraining || experienceStates.Count < batchSize) return;
        
        trainingSteps++;
        
        // Simple training approach (in a full implementation, this would be proper BPTT)
        for (int batch = 0; batch < batchSize; batch++)
        {
            int randomIndex = random.Next(experienceStates.Count);
            
            float[] state = experienceStates[randomIndex];
            float[] action = experienceActions[randomIndex];
            float reward = experienceRewards[randomIndex];
            
            // Forward pass
            float[] predicted = Forward(state);
            
            // Simple reward-based learning (placeholder for proper policy gradient)
            UpdateWeightsBasedOnReward(predicted, action, reward);
        }
        
        // Log training progress occasionally
        if (trainingSteps % 100 == 0)
        {
            Debug.Log($"🎓 LSTM Training Step {trainingSteps} - Experience buffer: {experienceStates.Count}");
        }
    }
    
    private void UpdateWeightsBasedOnReward(float[] predicted, float[] target, float reward)
    {
        // Simplified weight update (in practice, this would be proper backpropagation through time)
        float learningMultiplier = reward * learningRate;
        
        // Update output weights based on reward signal
        for (int i = 0; i < outputSize; i++)
        {
            float error = target[i] - predicted[i];
            
            for (int j = 0; j < lstmHiddenSize; j++)
            {
                weightsHiddenToOutput[j, i] += learningMultiplier * error * hiddenState[j];
            }
            
            // Update output bias
            biases[lstmHiddenSize * 4 + i] += learningMultiplier * error;
        }
    }
    
    // Activation functions
    private float Sigmoid(float x)
    {
        return 1f / (1f + Mathf.Exp(-Mathf.Clamp(x, -500f, 500f))); // Prevent overflow
    }
    
    private float Tanh(float x)
    {
        x = Mathf.Clamp(x, -500f, 500f); // Prevent overflow
        return (Mathf.Exp(x) - Mathf.Exp(-x)) / (Mathf.Exp(x) + Mathf.Exp(-x));
    }
    
    // Diagnostics and monitoring
    public void LogMemoryState()
    {
        if (!isInitialized) return;
        
        float avgHidden = 0f;
        float avgCell = 0f;
        float maxHidden = 0f;
        float maxCell = 0f;
        
        for (int i = 0; i < lstmHiddenSize; i++)
        {
            float absHidden = Mathf.Abs(hiddenState[i]);
            float absCell = Mathf.Abs(cellState[i]);
            
            avgHidden += absHidden;
            avgCell += absCell;
            
            maxHidden = Mathf.Max(maxHidden, absHidden);
            maxCell = Mathf.Max(maxCell, absCell);
        }
        
        avgHidden /= lstmHiddenSize;
        avgCell /= lstmHiddenSize;
        
        Debug.Log($"🧠 LSTM Memory - Hidden: avg={avgHidden:F3}, max={maxHidden:F3} | Cell: avg={avgCell:F3}, max={maxCell:F3} | History: {stateHistory.Count}/{sequenceLength}");
    }
    
    public void LogNetworkStats()
    {
        Debug.Log($"📊 LSTM Network Stats:");
        Debug.Log($"   Training Steps: {trainingSteps}");
        Debug.Log($"   Experience Buffer: {experienceStates.Count}");
        Debug.Log($"   Memory Sequences: {stateHistory.Count}/{sequenceLength}");
        Debug.Log($"   Learning Rate: {learningRate}");
        Debug.Log($"   Dropout Rate: {dropoutRate}");
        Debug.Log($"   Reset on Death: {resetMemoryOnDeath}");
    }
    
    // Model persistence (simplified)
    public void SaveModel(string filePath)
    {
        // In a full implementation, this would serialize all weights and biases
        Debug.Log($"💾 LSTM model save requested: {filePath}");
        Debug.Log("Note: Full model serialization not implemented in this simplified version");
    }
    
    public void LoadModel(string filePath)
    {
        // In a full implementation, this would deserialize weights and biases
        Debug.Log($"📁 LSTM model load requested: {filePath}");
        Debug.Log("Note: Full model serialization not implemented in this simplified version");
        
        // Reinitialize the network
        Initialize();
    }
    
    // Context menu debug methods
    [ContextMenu("Reset LSTM Memory")]
    public void DebugResetMemory()
    {
        ResetMemory();
    }
    
    [ContextMenu("Log Memory State")]
    public void DebugLogMemoryState()
    {
        LogMemoryState();
    }
    
    [ContextMenu("Log Network Stats")]
    public void DebugLogNetworkStats()
    {
        LogNetworkStats();
    }
    
    [ContextMenu("Test Forward Pass")]
    public void DebugTestForwardPass()
    {
        if (!isInitialized) Initialize();
        
        float[] testInput = new float[inputSize];
        for (int i = 0; i < inputSize; i++)
        {
            testInput[i] = (float)random.NextDouble();
        }
        
        float[] output = Forward(testInput);
        Debug.Log($"🧪 LSTM Test - Input[0]: {testInput[0]:F3}, Output: [{string.Join(", ", System.Array.ConvertAll(output, x => x.ToString("F3")))}]");
    }
}