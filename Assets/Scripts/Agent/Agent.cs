using UnityEngine;

public class Agent : MonoBehaviour, IAgent
{
    [SerializeField]
    State currentState;

    [Header("Agent Settings")]
    public bool useRandomActions = true;  // Set to true for initial testing
    public float explorationNoise = 0.1f;  // For exploration in continuous actions
    
    [Header("Policy Gradient Settings")]
    public float learningRate = 0.001f;
    public float gamma = 0.99f;  // Discount factor
    public float lambda = 0.95f; // GAE lambda
    public int batchSize = 32;   // Training batch size
    
    public SimpleNeuralNetwork policyNetwork;
    
    // Experience buffer for policy gradient
    private System.Collections.Generic.List<Experience> experienceBuffer;
    
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
        if (policyNetwork == null || useRandomActions) return;
        
        // Convert state and action to arrays
        float[] stateArray = state.ToArray();
        float[] actionArray = ActionToArray(action);
        float[] nextStateArray = nextState?.ToArray();
        
        // Get current value estimate if using value network
        float value = 0f;
        if (policyNetwork.useValueNetwork)
        {
            value = policyNetwork.ForwardValue(stateArray);
        }
        
        // Store experience
        Experience experience = new Experience(stateArray, actionArray, reward, nextStateArray, done, value);
        experienceBuffer.Add(experience);
        
        // Train when episode ends or buffer is full
        if (done || experienceBuffer.Count >= batchSize)
        {
            TrainPolicyGradient();
            experienceBuffer.Clear();
        }
    }
    
    private void TrainPolicyGradient()
    {
        if (experienceBuffer.Count == 0) return;
        
        // Calculate advantages using GAE (Generalized Advantage Estimation)
        CalculateAdvantages();
        
        // Update policy network using policy gradient
        for (int i = 0; i < experienceBuffer.Count; i++)
        {
            Experience exp = experienceBuffer[i];
            
            // Get current policy output for this state
            float[] policyOutput = policyNetwork.Forward(exp.state);
            
            // Calculate policy gradient target (advantage-weighted actions)
            float[] policyTarget = new float[policyOutput.Length];
            for (int j = 0; j < policyTarget.Length; j++)
            {
                // Policy gradient: move actions in direction of advantage
                policyTarget[j] = policyOutput[j] + exp.advantage * learningRate * (exp.action[j] - policyOutput[j]);
                policyTarget[j] = Mathf.Clamp(policyTarget[j], -1f, 1f);
            }
            
            // Update policy network
            policyNetwork.Backward(exp.state, policyTarget, learningRate);
            
            // Train value network if enabled
            if (policyNetwork.useValueNetwork)
            {
                // Calculate discounted return for this experience
                float target = CalculateDiscountedReturn(i);
                float[] valueTarget = new float[] { target };
                
                // Note: This is simplified - in practice you'd train the critic separately
                // For now, we'll skip separate critic training as it requires more complex implementation
            }
        }
        
        Debug.Log($"Trained on batch of {experienceBuffer.Count} experiences");
    }
    
    private void CalculateAdvantages()
    {
        if (experienceBuffer.Count == 0) return;
        
        // Calculate returns and advantages for each experience
        float runningReturn = 0f;
        float runningAdvantage = 0f;
        
        // Work backwards through the buffer
        for (int i = experienceBuffer.Count - 1; i >= 0; i--)
        {
            Experience exp = experienceBuffer[i];
            
            // Calculate discounted return
            if (exp.done)
            {
                runningReturn = exp.reward;
            }
            else
            {
                runningReturn = exp.reward + gamma * runningReturn;
            }
            
            // Calculate advantage (simplified - just return minus value)
            exp.advantage = runningReturn - exp.value;
            
            // Apply GAE smoothing (simplified)
            if (i < experienceBuffer.Count - 1)
            {
                runningAdvantage = exp.advantage + gamma * lambda * runningAdvantage;
                exp.advantage = runningAdvantage;
            }
        }
        
        // Normalize advantages
        NormalizeAdvantages();
    }
    
    private void NormalizeAdvantages()
    {
        if (experienceBuffer.Count == 0) return;
        
        // Calculate mean and standard deviation
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
        float std = Mathf.Sqrt(variance + 1e-8f); // Add small epsilon to avoid division by zero
        
        // Normalize
        foreach (var exp in experienceBuffer)
        {
            exp.advantage = (exp.advantage - mean) / std;
        }
    }
    
    private float CalculateDiscountedReturn(int startIndex)
    {
        float discountedReturn = 0f;
        float discountFactor = 1f;
        
        for (int i = startIndex; i < experienceBuffer.Count; i++)
        {
            discountedReturn += discountFactor * experienceBuffer[i].reward;
            discountFactor *= gamma;
            
            if (experienceBuffer[i].done) break;
        }
        
        return discountedReturn;
    }

    public void LoadModel(string path)
    {
        if (policyNetwork != null)
        {
            policyNetwork.LoadWeights(path);
            Debug.Log($"Loaded model from: {path}");
        }
    }

    public void SaveModel(string path)
    {
        if (policyNetwork != null)
        {
            policyNetwork.SaveWeights(path);
            Debug.Log($"Saved model to: {path}");
        }
    }

    public Action SelectAction(State state)
    {
        if (state == null)
        {
            Debug.LogWarning("State is null, returning random action");
            return GetRandomAction();
        }

        if (useRandomActions || policyNetwork == null)
        {
            Debug.Log($"Using random actions - useRandom: {useRandomActions}, network null: {policyNetwork == null}");
            return GetRandomAction();
        }

        // Convert state to array for neural network
        float[] stateArray = state.ToArray();
        
        // Forward pass through policy network
        float[] policyOutput = policyNetwork.Forward(stateArray);
        
        // Add exploration noise for continuous actions
        for (int i = 0; i < policyOutput.Length; i++)
        {
            policyOutput[i] += Random.Range(-explorationNoise, explorationNoise);
            policyOutput[i] = Mathf.Clamp(policyOutput[i], -1f, 1f);
        }
        
        // Convert network output to Action
        Action action = new Action(
            policyOutput[0],  // lookAngle (-1 to 1)
            policyOutput[1] > 0.1f ? 1f : 0f,  // shoot (threshold at 0.1 - very easy to trigger)
            policyOutput[2],  // moveForward (-1 to 1)
            policyOutput[3],  // moveLeft (-1 to 1)
            policyOutput[4]   // moveRight (-1 to 1)
        );
        
        Debug.Log($"Network Action - Look: {action.lookAngle:F2}, Shoot: {action.shoot}, Forward: {action.moveForward:F2}");
        return action;
    }
    
    private Action GetRandomAction()
    {
        Action action = new Action(
            Random.Range(-0.5f, 0.5f),      // lookAngle (smaller range for less spinning)
            Random.Range(0f, 1f) > 0.2f ? 1f : 0f,  // shoot (80% chance - very aggressive)
            Random.Range(0.5f, 1f),          // moveForward (always move forward)
            Random.Range(-0.3f, 0.3f),       // moveLeft (small strafe)
            Random.Range(-0.3f, 0.3f)        // moveRight (small strafe)
        );
        
        Debug.Log($"Random Action Generated - Look: {action.lookAngle:F2}, Shoot: {action.shoot}, Forward: {action.moveForward:F2}, Left: {action.moveLeft:F2}, Right: {action.moveRight:F2}");
        return action;
    }

    public void Start()
    {
        if (currentState == null)
        {
            currentState = new State();
        }
        
        // Initialize experience buffer
        experienceBuffer = new System.Collections.Generic.List<Experience>();
        
        // Initialize policy network if not assigned
        if (policyNetwork == null)
        {
            policyNetwork = new SimpleNeuralNetwork();
            policyNetwork.useValueNetwork = true; // Enable critic for Actor-Critic
        }
        
        // Ensure network is properly initialized (this will call Initialize() if needed)
        policyNetwork.Initialize();
        
        Debug.Log($"Agent initialized - Random Actions: {useRandomActions}, Network Ready: {policyNetwork != null}");
    }
    
    // Helper methods for action conversion
    private float[] ActionToArray(Action action)
    {
        return new float[] {
            action.lookAngle,
            action.shoot,
            action.moveForward,
            action.moveLeft,
            action.moveRight
        };
    }
}