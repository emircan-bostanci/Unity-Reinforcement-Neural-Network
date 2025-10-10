using UnityEngine;
using System.Collections.Generic;

public class Agent : MonoBehaviour, IAgent
{
    [SerializeField]
    State currentState;

    [Header("Agent Settings")]
    public bool useRandomActions = true;  // Set to true for initial testing
    public float explorationNoise = 0.1f;  // For exploration in continuous actions
    
    [Header("Neural Network Selection")]
    public NeuralNetworkType networkType = NeuralNetworkType.LSTM_RNN;
    
    [Header("Policy Gradient Settings")]
    public float learningRate = 0.001f;
    public float gamma = 0.99f;  // Discount factor
    public float lambda = 0.95f; // GAE lambda
    public int batchSize = 32;   // Training batch size
    
    [Header("LSTM Specific Settings")]
    public bool resetLSTMOnDeath = true;
    public int lstmSequenceLength = 10;
    public int lstmHiddenSize = 64;
    
    // Neural network references
    public SimpleNeuralNetwork simpleNetwork;
    [System.NonSerialized]
    public LSTMNeuralNetwork lstmNetwork;
    
    // Experience buffer for policy gradient
    private List<Experience> experienceBuffer;
    
    // Performance tracking
    private float totalReward = 0f;
    private int episodeCount = 0;
    private float timeAlive = 0f;
    
    [System.Serializable]
    public enum NeuralNetworkType
    {
        SimpleNN,
        LSTM_RNN
    }
    
    [System.Serializable]
    public class Experience
    {
        public float[] state;
        public float[] action;
        public float reward;
        public float[] nextState;
        public bool done;
        public float value;
        public float advantage;
        
        public Experience(float[] state, float[] action, float reward, float[] nextState, bool done, float value = 0f)
        {
            this.state = (float[])state.Clone();
            this.action = (float[])action.Clone();
            this.reward = reward;
            this.nextState = nextState != null ? (float[])nextState.Clone() : null;
            this.done = done;
            this.value = value;
            this.advantage = 0f; // Will be calculated during training
        }
    }

    public void Learn(State state, Action action, float reward, State nextState, bool done)
    {
        // Convert state and action to arrays
        float[] stateArray = state.ToArray();
        float[] actionArray = ActionToArray(action);
        float[] nextStateArray = nextState?.ToArray();
        
        float value = GetNetworkValue(stateArray);
        
        Experience experience = new Experience(stateArray, actionArray, reward, nextStateArray, done, value);
        experienceBuffer.Add(experience);
        
        // Update performance tracking
        totalReward += reward;
        timeAlive += Time.deltaTime;
        
        // Store experience in the appropriate network
        if (networkType == NeuralNetworkType.LSTM_RNN && lstmNetwork != null)
        {
            lstmNetwork.StoreExperience(stateArray, actionArray, reward, done);
            
            // Reset LSTM memory on death if enabled
            if (done && resetLSTMOnDeath)
            {
                lstmNetwork.ResetMemory();
                OnAgentDeath();
            }
        }
        
        // Train periodically
        if (experienceBuffer.Count >= batchSize)
        {
            TrainNetwork();
        }
        
        // Log significant events
        if (Mathf.Abs(reward) > 0.1f)
        {
            string networkName = networkType == NeuralNetworkType.LSTM_RNN ? "LSTM" : "Simple";
            
            // Get current episode from TrainingManager
            TrainingManager trainingManager = FindFirstObjectByType<TrainingManager>();
            int currentEpisode = trainingManager != null ? trainingManager.GetCurrentEpisode() : episodeCount;
            
            Debug.Log($"🎯 {networkName} Agent reward: {reward:F3} (Total: {totalReward:F1}, Episodes: {currentEpisode})");
        }
    }

    public Action SelectAction(State state)
    {
        if (useRandomActions)
        {
            return GenerateRandomAction();
        }
        
        // Convert state to array for neural network
        float[] stateArray = state.ToArray();
        
        // Get network output
        float[] policyOutput = GetNetworkOutput(stateArray);
        
        if (policyOutput == null)
        {
            Debug.LogWarning($"Network output is null for {networkType}, using random action");
            return GenerateRandomAction();
        }
        
        // Add exploration noise if enabled
        if (explorationNoise > 0f)
        {
            for (int i = 0; i < policyOutput.Length; i++)
            {
                policyOutput[i] += Random.Range(-explorationNoise, explorationNoise);
            }
        }
        
        // Convert network output to action
        Action action = new Action
        {
            lookAngle = Mathf.Clamp(policyOutput[0], -1f, 1f),
            shoot = policyOutput[1] > 0.1f ? 1f : 0f, // Lower threshold for easier shooting
            moveForward = Mathf.Clamp(policyOutput[2], 0f, 1f),
            moveLeft = Mathf.Clamp(policyOutput[3], -1f, 1f),
            moveRight = Mathf.Clamp(policyOutput[4], -1f, 1f)
        };
        
        // Enhanced shooting behavior - 80% random shooting chance
        if (Random.value < 0.8f)
        {
            action.shoot = Random.value > 0.7f ? 1f : 0f;
        }
        
        return action;
    }
    
    private object GetCurrentNetwork()
    {
        switch (networkType)
        {
            case NeuralNetworkType.SimpleNN:
                return simpleNetwork;
            case NeuralNetworkType.LSTM_RNN:
                return lstmNetwork;
            default:
                return simpleNetwork;
        }
    }
    
    private float[] GetNetworkOutput(float[] stateArray)
    {
        switch (networkType)
        {
            case NeuralNetworkType.SimpleNN:
                return simpleNetwork?.Forward(stateArray);
            case NeuralNetworkType.LSTM_RNN:
                return lstmNetwork?.Forward(stateArray);
            default:
                return simpleNetwork?.Forward(stateArray);
        }
    }
    
    private float GetNetworkValue(float[] stateArray)
    {
        switch (networkType)
        {
            case NeuralNetworkType.SimpleNN:
                return simpleNetwork?.useValueNetwork == true ? simpleNetwork.ForwardValue(stateArray) : 0f;
            case NeuralNetworkType.LSTM_RNN:
                return lstmNetwork?.UseValueNetwork == true ? lstmNetwork.ForwardValue(stateArray) : 0f;
            default:
                return 0f;
        }
    }
    
    private Action GenerateRandomAction()
    {
        return new Action
        {
            lookAngle = Random.Range(-1f, 1f),
            shoot = Random.value > 0.7f ? 1f : 0f,
            moveForward = Random.Range(0f, 1f),
            moveLeft = Random.Range(-1f, 1f),
            moveRight = Random.Range(-1f, 1f)
        };
    }
    
    private void TrainNetwork()
    {
        if (networkType == NeuralNetworkType.LSTM_RNN && lstmNetwork != null)
        {
            lstmNetwork.TrainOnBatch(batchSize);
        }
        else if (networkType == NeuralNetworkType.SimpleNN && simpleNetwork != null)
        {
            TrainPolicyGradient();
        }
        
        // Clear some experiences to prevent memory buildup
        if (experienceBuffer.Count > 500)
        {
            experienceBuffer.RemoveRange(0, 100);
        }
    }
    
    private void TrainPolicyGradient()
    {
        if (experienceBuffer.Count == 0) return;

        // Calculate advantages
        CalculateAdvantages();

        // Train on all experiences
        for (int i = 0; i < experienceBuffer.Count; i++)
        {
            Experience exp = experienceBuffer[i];
            
            // Forward pass to get current policy output
            float[] policyOutput = simpleNetwork.Forward(exp.state);
            
            // Create target based on advantage
            float[] policyTarget = (float[])exp.action.Clone();
            for (int j = 0; j < policyTarget.Length; j++)
            {
                policyTarget[j] += exp.advantage * 0.1f; // Scale the advantage
            }
            
            // Backward pass
            simpleNetwork.Backward(exp.state, policyTarget, learningRate);
            
            // Train value network if enabled
            if (simpleNetwork.useValueNetwork)
            {
                // Value target is the discounted future reward
                float valueTarget = exp.reward + (exp.done ? 0 : gamma * 0.5f); // Simplified
                // Note: TrainValue method needs to be implemented in SimpleNeuralNetwork
                // simpleNetwork.TrainValue(exp.state, valueTarget, learningRate);
            }
        }

        Debug.Log($"Trained on batch of {experienceBuffer.Count} experiences");
    }

    private void CalculateAdvantages()
    {
        if (experienceBuffer.Count == 0) return;

        // Calculate returns and advantages using GAE
        float runningReturn = 0f;
        float runningAdvantage = 0f;

        for (int i = experienceBuffer.Count - 1; i >= 0; i--)
        {
            Experience exp = experienceBuffer[i];
            
            if (exp.done)
            {
                runningReturn = exp.reward;
            }
            else
            {
                runningReturn = exp.reward + gamma * runningReturn;
            }
            
            float delta = exp.reward - exp.value;
            if (i < experienceBuffer.Count - 1)
            {
                delta += gamma * experienceBuffer[i + 1].value;
            }
            
            runningAdvantage = delta + gamma * lambda * runningAdvantage;
            exp.advantage = runningAdvantage;
        }
    }

    private void NormalizeAdvantages()
    {
        if (experienceBuffer.Count == 0) return;

        // Calculate mean and std
        float mean = 0f;
        foreach (var exp in experienceBuffer)
        {
            mean += exp.advantage;
        }
        mean /= experienceBuffer.Count;

        float variance = 0f;
        foreach (var exp in experienceBuffer)
        {
            variance += (exp.advantage - mean) * (exp.advantage - mean);
        }
        variance /= experienceBuffer.Count;
        float std = Mathf.Sqrt(variance + 1e-8f);

        // Normalize
        foreach (var exp in experienceBuffer)
        {
            exp.advantage = (exp.advantage - mean) / std;
        }
    }
    
    private void OnAgentDeath()
    {
        episodeCount++;
        
        string networkName = networkType == NeuralNetworkType.LSTM_RNN ? "LSTM" : "Simple";
        Debug.Log($"💀 {networkName} Agent died - Episode {episodeCount}, Total reward: {totalReward:F1}, Time alive: {timeAlive:F1}s");
        
        // Reset tracking
        timeAlive = 0f;
    }

    public void Start()
    {
        if (currentState == null)
        {
            currentState = new State();
        }
        
        // Initialize experience buffer
        experienceBuffer = new List<Experience>();
        
        // Initialize the selected neural network type
        InitializeSelectedNetwork();
        
        string networkName = networkType == NeuralNetworkType.LSTM_RNN ? "LSTM RNN" : "Simple NN";
        Debug.Log($"🤖 Agent initialized - Network: {networkName}, Random Actions: {useRandomActions}");
    }
    
    private void InitializeSelectedNetwork()
    {
        switch (networkType)
        {
            case NeuralNetworkType.SimpleNN:
                InitializeSimpleNetwork();
                break;
            case NeuralNetworkType.LSTM_RNN:
                InitializeLSTMNetwork();
                break;
        }
    }
    
    private void InitializeSimpleNetwork()
    {
        if (simpleNetwork == null)
        {
            simpleNetwork = new SimpleNeuralNetwork();
            simpleNetwork.useValueNetwork = true;
        }
        simpleNetwork.Initialize();
        Debug.Log($"✅ Simple Neural Network initialized");
    }
    
    private void InitializeLSTMNetwork()
    {
        if (lstmNetwork == null)
        {
            lstmNetwork = gameObject.GetComponent<LSTMNeuralNetwork>();
            if (lstmNetwork == null)
            {
                lstmNetwork = gameObject.AddComponent<LSTMNeuralNetwork>();
            }
        }
        
        // Configure LSTM settings
        lstmNetwork.sequenceLength = lstmSequenceLength;
        lstmNetwork.lstmHiddenSize = lstmHiddenSize;
        lstmNetwork.resetMemoryOnDeath = resetLSTMOnDeath;
        lstmNetwork.UseValueNetwork = true;
        
        lstmNetwork.Initialize();
        Debug.Log($"✅ LSTM Neural Network initialized (Hidden: {lstmHiddenSize}, Sequence: {lstmSequenceLength})");
    }

    private float[] ActionToArray(Action action)
    {
        return new float[]
        {
            action.lookAngle,
            action.shoot,
            action.moveForward,
            action.moveLeft,
            action.moveRight
        };
    }

    // Model persistence methods for genetic algorithm
    public void SaveModel(string filePath)
    {
        switch (networkType)
        {
            case NeuralNetworkType.SimpleNN:
                if (simpleNetwork != null)
                {
                    // SimpleNeuralNetwork uses SaveWeights method
                    simpleNetwork.SaveWeights(filePath);
                    Debug.Log($"💾 Simple NN weights saved to {filePath}");
                }
                break;
            case NeuralNetworkType.LSTM_RNN:
                if (lstmNetwork != null)
                {
                    lstmNetwork.SaveModel(filePath);
                    Debug.Log($"💾 LSTM model saved to {filePath}");
                }
                break;
        }
    }
    
    public void LoadModel(string filePath)
    {
        switch (networkType)
        {
            case NeuralNetworkType.SimpleNN:
                if (simpleNetwork != null)
                {
                    // SimpleNeuralNetwork uses LoadWeights method  
                    simpleNetwork.LoadWeights(filePath);
                    Debug.Log($"📁 Simple NN weights loaded from {filePath}");
                }
                break;
            case NeuralNetworkType.LSTM_RNN:
                if (lstmNetwork != null)
                {
                    lstmNetwork.LoadModel(filePath);
                    Debug.Log($"📁 LSTM model loaded from {filePath}");
                }
                break;
        }
    }
    
    // Context menu debug methods
    [ContextMenu("Switch to Simple NN")]
    public void SwitchToSimpleNN()
    {
        networkType = NeuralNetworkType.SimpleNN;
        InitializeSelectedNetwork();
        Debug.Log("🔄 Switched to Simple Neural Network");
    }
    
    [ContextMenu("Switch to LSTM RNN")]
    public void SwitchToLSTM()
    {
        networkType = NeuralNetworkType.LSTM_RNN;
        InitializeSelectedNetwork();
        Debug.Log("🔄 Switched to LSTM RNN");
    }
    
    [ContextMenu("Reset LSTM Memory")]
    public void ResetLSTMMemory()
    {
        if (networkType == NeuralNetworkType.LSTM_RNN && lstmNetwork != null)
        {
            lstmNetwork.ResetMemory();
            Debug.Log("🧠 LSTM memory reset");
        }
        else
        {
            Debug.Log("❌ Not using LSTM or LSTM not available");
        }
    }
    
    [ContextMenu("Log Performance Stats")]
    public void LogPerformanceStats()
    {
        string networkName = networkType == NeuralNetworkType.LSTM_RNN ? "LSTM RNN" : "Simple NN";
        Debug.Log($"📊 Agent Performance ({networkName}):");
        Debug.Log($"   Episodes: {episodeCount}");
        Debug.Log($"   Total Reward: {totalReward:F1}");
        Debug.Log($"   Average Reward: {(episodeCount > 0 ? totalReward / episodeCount : 0):F2}");
        Debug.Log($"   Current Time Alive: {timeAlive:F1}s");
        Debug.Log($"   Experience Buffer: {experienceBuffer.Count}");
        
        if (networkType == NeuralNetworkType.LSTM_RNN && lstmNetwork != null)
        {
            lstmNetwork.LogMemoryState();
        }
    }
    
    // Method to reset agent's cumulative reward (called by TrainingManager)
    public void ResetTotalReward()
    {
        totalReward = 0f;
        episodeCount = 0;
        Debug.Log($"Agent {gameObject.name} total reward reset to 0");
    }
    
    // Method to increment episode count
    public void IncrementEpisode()
    {
        episodeCount++;
    }
}