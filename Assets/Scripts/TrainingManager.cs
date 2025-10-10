using UnityEngine;

public class TrainingManager : MonoBehaviour
{
    [Header("References")]
    public Envirovment environment;
    public Agent[] agents; // Array of all agents for multi-agent training
    public GeneticAlgorithmManager geneticManager; // Reference to genetic algorithm

    [Header("Training Settings")]
    public bool isTraining = true;
    public float trainingStepInterval = 0.02f; // 50 Hz training
    public int maxEpisodes = 1000;
    public bool logProgress = true;
    public int logInterval = 10; // Log every 10 episodes
    
    [Header("Episode Management")]
    public bool useGeneticAlgorithm = true; // Use genetic algorithm for episode management
    public bool showEpisodeInfo = true; // Show episode/generation info in logs
    
    [Header("Policy Gradient Settings")]
    public bool useSharedNetwork = false; // Whether both agents share the same network
    public bool trainBothAgents = true;   // Whether to train both player and enemy

    private float lastTrainingStep;
    private int currentEpisode = 0;
    private State currentState;
    private State nextState;
    private Action playerAction;
    private Action enemyAction;
    
    // Training statistics
    private float totalRewardThisEpisode = 0f;
    private float[] recentEpisodeRewards = new float[100]; // Track last 100 episodes
    private int rewardIndex = 0;
    private float episodeStartTime = 0f;
    private int lastGeneticGeneration = -1; // Track genetic algorithm generations

    void Start()
    {
        if (environment == null)
            environment = FindFirstObjectByType<Envirovment>();
        
        // Auto-find genetic manager if using genetic algorithm
        if (useGeneticAlgorithm && geneticManager == null)
        {
            geneticManager = FindFirstObjectByType<GeneticAlgorithmManager>();
            if (geneticManager != null)
            {
                Debug.Log("✅ Found GeneticAlgorithmManager - episodes will be managed by genetic algorithm");
            }
            else
            {
                Debug.LogError("❌ GeneticAlgorithmManager not found! Episodes may not work correctly.");
                useGeneticAlgorithm = false; // Fallback to traditional mode
            }
        }
        
        // Auto-find agents if not assigned
        if (agents == null || agents.Length == 0)
        {
            Agent[] foundAgents = FindObjectsByType<Agent>(FindObjectsSortMode.None);
            if (foundAgents.Length > 0)
            {
                agents = foundAgents;
                Debug.Log($"Auto-found {agents.Length} agents for training");
            }
            else
            {
                Debug.LogError("No agents found for training!");
                return;
            }
        }

        // Only start episodes manually if not using genetic algorithm
        if (!useGeneticAlgorithm)
        {
            currentEpisode = 0; // Will be incremented to 1 in StartNewEpisode
            StartNewEpisode();
        }
        else
        {
            currentEpisode = 1; // Start at episode 1 for genetic algorithm mode
            episodeStartTime = Time.time;
            lastGeneticGeneration = -1; // Ensure we detect the first generation
            Debug.Log($"🧬 Training Manager in Genetic Algorithm mode - Episode {currentEpisode} started");
            Debug.Log($"Waiting for genetic algorithm to begin generation 0...");
        }
        
        // FORCE episode to 1 if it's still 0 (emergency fix)
        if (currentEpisode == 0)
        {
            currentEpisode = 1;
            Debug.Log("🚑 EMERGENCY FIX: Forced episode to 1");
        }
    }

    void Update()
    {
        if (!isTraining)
        {
            return;
        }
        
        // Emergency checks and resets
        if (Time.time - episodeStartTime > 30f) // If episode runs longer than 30 seconds
        {
            Debug.Log("🚑 EMERGENCY: Episode running too long, forcing reset!");
            OnManualEpisodeEnd();
            return;
        }
        
        // Check for excessive reward accumulation
        if (environment != null)
        {
            for (int i = 0; i < environment.GetAgentCount(); i++)
            {
                float agentReward = environment.GetReward(i);
                if (Mathf.Abs(agentReward) > 100f) // Reward too high/low
                {
                    Debug.Log($"🚑 EMERGENCY: Agent_{i} reward too high ({agentReward:F1}), forcing reset!");
                    OnManualEpisodeEnd();
                    return;
                }
            }
        }
        
        // Emergency checks and resets
        if (Time.time - episodeStartTime > 30f) // If episode runs longer than 30 seconds
        {
            Debug.Log("🚑 EMERGENCY: Episode running too long, forcing reset!");
            OnManualEpisodeEnd();
            return;
        }
        
        // Check for excessive reward accumulation
        if (environment != null)
        {
            for (int i = 0; i < environment.GetAgentCount(); i++)
            {
                float agentReward = environment.GetReward(i);
                if (Mathf.Abs(agentReward) > 100f) // Reward too high/low
                {
                    Debug.Log($"🚑 EMERGENCY: Agent_{i} reward too high ({agentReward:F1}), forcing reset!");
                    OnManualEpisodeEnd();
                    return;
                }
            }
        }

        // Check if genetic algorithm triggered a new generation (episode reset)
        if (useGeneticAlgorithm && geneticManager != null)
        {
            int currentGeneration = geneticManager.GetCurrentGeneration();
            
            // Debug logging to track generation changes (less frequent)
            if (Time.time - lastTrainingStep >= 2f) // Log every 2 seconds to avoid spam
            {
                Debug.Log($"GA Status: Gen={currentGeneration}, LastGen={lastGeneticGeneration}, Episode={currentEpisode}, GAEnabled={geneticManager.enableGeneticAlgorithm}");
            }
            
            if (currentGeneration != lastGeneticGeneration)
            {
                Debug.Log($"🔄 Generation change detected: {lastGeneticGeneration} -> {currentGeneration}");
                
                // Genetic algorithm started a new generation
                if (lastGeneticGeneration >= 0) // Only increment after first generation
                {
                    OnGeneticGenerationComplete();
                }
                else
                {
                    // First generation starting
                    currentEpisode = 1;
                    Debug.Log($"🧬 First Generation Started - Episode {currentEpisode}");
                }
                lastGeneticGeneration = currentGeneration;
                episodeStartTime = Time.time;
            }
        }
        else if (useGeneticAlgorithm && geneticManager == null)
        {
            // Genetic algorithm is enabled but manager not found - switch to manual mode
            if (Time.time - lastTrainingStep >= 10f) // Log every 10 seconds
            {
                Debug.LogWarning("Genetic Algorithm enabled but manager not found - consider disabling useGeneticAlgorithm");
            }
        }

        if (Time.time - lastTrainingStep >= trainingStepInterval)
        {
            TrainingStep();
            lastTrainingStep = Time.time;
        }
    }

    void TrainingStep()
    {
        // Check for episode end conditions
        bool shouldEndEpisode = false;
        
        if (!useGeneticAlgorithm)
        {
            // Traditional episode management
            if (environment.IsEpisodeFinished())
            {
                shouldEndEpisode = true;
            }
        }
        
        // Check if only one agent remains alive (restart condition)
        if (environment != null && environment.GetAliveAgentCount() <= 1)
        {
            Debug.Log($"Only {environment.GetAliveAgentCount()} agent(s) remaining - restarting episode!");
            shouldEndEpisode = true;
        }
        
        if (shouldEndEpisode)
        {
            if (useGeneticAlgorithm)
            {
                // Force genetic algorithm to evaluate and reset
                if (geneticManager != null)
                {
                    Debug.Log("Forcing genetic algorithm evaluation due to episode end condition");
                    geneticManager.ForceEvolution();
                }
                else
                {
                    // Fallback: manual reset
                    OnManualEpisodeEnd();
                }
            }
            else
            {
                // Traditional episode end
                EndEpisode();
            }
            return;
        }

        // Get current state
        currentState = environment.GetCurrentState();
        
        if (currentState == null)
        {
            Debug.LogError("Current state is null!");
            return;
        }

        // Get actions from all alive agents
        Action[] agentActions = new Action[agents.Length];
        for (int i = 0; i < agents.Length; i++)
        {
            if (environment.IsAgentAlive(i))
            {
                try
                {
                    agentActions[i] = agents[i].SelectAction(currentState);
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"Error getting action from agent {i}: {e.Message}");
                    agentActions[i] = GetRandomAction();
                }
            }
        }
        
        // Apply actions for all alive agents
        for (int i = 0; i < agents.Length; i++)
        {
            if (environment.IsAgentAlive(i) && agentActions[i] != null)
            {
                environment.ApplyAction(agentActions[i], i);
            }
        }

        // Step environment
        environment.Step();

        // Get next state
        nextState = environment.GetCurrentState();

        // Train all alive agents
        for (int i = 0; i < agents.Length; i++)
        {
            if (environment.IsAgentAlive(i))
            {
                try
                {
                    float stepReward = environment.GetLastStepReward(i);  // Get STEP reward, not cumulative
                    float totalReward = environment.GetReward(i);         // Get cumulative for logging
                    totalRewardThisEpisode += stepReward; // Accumulate episode reward from step rewards
                    
                    // Force episode number update for agent learning log
                    if (agents[i] != null)
                    {
                        // Update agent with current episode (if agent has this property)
                        Debug.Log($"🎯 Episode {currentEpisode} - Agent_{i} Step Reward: {stepReward:F3} (Total: {totalReward:F1})");
                    }
                    
                    agents[i].Learn(currentState, agentActions[i], stepReward, nextState, environment.IsEpisodeFinished());
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning($"Learning not implemented for agent {i} or error: {e.Message}");
                }
            }
        }
    }

    void StartNewEpisode()
    {
        currentEpisode++;
        environment.Reset();
        episodeStartTime = Time.time;
        totalRewardThisEpisode = 0f;
        
        if (showEpisodeInfo)
        {
            if (useGeneticAlgorithm && geneticManager != null)
            {
                Debug.Log($"🧬 Generation {geneticManager.GetCurrentGeneration()} Episode {currentEpisode} Started (Manual Mode)");
            }
            else
            {
                Debug.Log($"📈 Episode {currentEpisode} Started (Traditional Mode)");
            }
        }
        
        if (currentEpisode > maxEpisodes)
        {
            Debug.Log("Training completed!");
            isTraining = false;
        }
    }

    void EndEpisode()
    {
        float episodeReward = environment.GetReward();
        totalRewardThisEpisode += episodeReward;
        
        // Determine episode end reason
        string endReason = "";
        if (!environment.resetOnTimeout && environment.timeSinceLastReward >= environment.noRewardTimeoutDuration && !environment.hasEarnedRewardThisEpisode)
        {
            endReason = " (TIMEOUT PUNISHMENT APPLIED)";
        }
        else if (environment.resetOnTimeout && environment.timeSinceLastReward >= environment.noRewardTimeoutDuration && !environment.hasEarnedRewardThisEpisode)
        {
            endReason = " (TIMEOUT - Episode reset)";
        }
        else if (episodeReward >= 40f) // Close to the +50 reward for shooting enemy
        {
            endReason = " (SUCCESS - Enemy defeated)";
        }
        else if (environment.currentEpisodeTime >= environment.maxEpisodeLength)
        {
            endReason = " (TIME LIMIT)";
        }
        else
        {
            endReason = " (OTHER)";
        }
        
        // Store episode reward for averaging
        recentEpisodeRewards[rewardIndex] = totalRewardThisEpisode;
        rewardIndex = (rewardIndex + 1) % recentEpisodeRewards.Length;
        
        if (logProgress && currentEpisode % logInterval == 0)
        {
            float averageReward = CalculateAverageReward();
            Debug.Log($"Episode {currentEpisode} finished{endReason}. Episode Reward: {totalRewardThisEpisode:F2}, Average Reward (last 100): {averageReward:F2}");
        }
        
        // Reset episode tracking
        totalRewardThisEpisode = 0f;
        
        StartNewEpisode();
    }
    
    private float CalculateAverageReward()
    {
        float sum = 0f;
        int count = Mathf.Min(currentEpisode, recentEpisodeRewards.Length);
        
        for (int i = 0; i < count; i++)
        {
            sum += recentEpisodeRewards[i];
        }
        
        return count > 0 ? sum / count : 0f;
    }
    
    // Handle genetic algorithm generation completion
    private void OnGeneticGenerationComplete()
    {
        Debug.Log($"📋 Before increment: Episode={currentEpisode}");
        currentEpisode++; // Increment episode when genetic algorithm resets
        totalRewardThisEpisode = 0f; // Reset episode reward tracking
        
        // Force reset all cumulative rewards in environment
        if (environment != null)
        {
            environment.ResetCumulativeRewards();
            Debug.Log("📋 Environment rewards reset");
        }
        
        if (showEpisodeInfo && geneticManager != null)
        {
            float episodeDuration = Time.time - episodeStartTime;
            Debug.Log($"🏆 Generation {geneticManager.GetCurrentGeneration()} Complete - Starting Episode {currentEpisode} (Previous Duration: {episodeDuration:F1}s)");
        }
        
        Debug.Log($"� After increment: Episode={currentEpisode}");
    }
    
    // Handle manual episode end when genetic algorithm isn't working
    private void OnManualEpisodeEnd()
    {
        Debug.Log($"🔄 Manual episode end - Episode {currentEpisode} finished");
        currentEpisode++;
        totalRewardThisEpisode = 0f;
        
        // Force reset all cumulative rewards in environment
        if (environment != null)
        {
            environment.Reset(); // This should reset everything
            environment.ResetCumulativeRewards();
            environment.ResetTimeoutStatus();
            Debug.Log("📋 Environment COMPLETELY reset");
        }
        
        // Reset all agents if possible
        if (agents != null)
        {
            for (int i = 0; i < agents.Length; i++)
            {
                if (agents[i] != null)
                {
                    // Reset agent's total reward tracking
                    agents[i].ResetTotalReward();
                    
                    // Reset LSTM memory if agent has LSTM
                    if (agents[i].networkType == Agent.NeuralNetworkType.LSTM_RNN && agents[i].lstmNetwork != null)
                    {
                        agents[i].lstmNetwork.ResetMemory();
                    }
                }
            }
        }
        
        episodeStartTime = Time.time;
        Debug.Log($"🆕 Starting Episode {currentEpisode} (Manual Mode) - All rewards reset to 0");
    }
    
    // Public access methods
    public int GetCurrentEpisode() { return currentEpisode; }
    public int GetCurrentGeneration() { return useGeneticAlgorithm && geneticManager != null ? geneticManager.GetCurrentGeneration() : 0; }
    public float GetEpisodeDuration() { return Time.time - episodeStartTime; }
    public bool IsUsingGeneticAlgorithm() { return useGeneticAlgorithm && geneticManager != null; }

    Action GetRandomAction()
    {
        return new Action(
            Random.Range(-1f, 1f),      // lookAngle
            Random.Range(0f, 1f),       // shoot
            Random.Range(-1f, 1f),      // moveForward
            Random.Range(-1f, 1f),      // moveLeft
            Random.Range(-1f, 1f)       // moveRight
        );
    }

    Action GetSimpleEnemyAction()
    {
        // Simple AI: move forward and shoot occasionally for any agent
        return new Action(
            Random.Range(-0.2f, 0.2f),  // Small random rotation
            Random.Range(0f, 1f) > 0.9f ? 1f : 0f,  // 10% chance to shoot
            0.5f,                        // Move forward
            0f,                          // No left movement
            0f                           // No right movement
        );
    }

    // Public methods for external control
    public void StartTraining()
    {
        isTraining = true;
        currentEpisode = 0;
        StartNewEpisode();
    }

    public void StopTraining()
    {
        isTraining = false;
    }

    public void ResetTraining()
    {
        currentEpisode = 0;
        lastGeneticGeneration = -1;
        episodeStartTime = Time.time;
        totalRewardThisEpisode = 0f;
        
        if (!useGeneticAlgorithm)
        {
            StartNewEpisode();
        }
        else
        {
            Debug.Log("🔄 Training reset - waiting for genetic algorithm");
        }
    }
    
    // Debug method to check current status
    [ContextMenu("Show Training Status")]
    public void ShowTrainingStatus()
    {
        Debug.Log("=== TRAINING STATUS ===");
        Debug.Log($"Current Episode: {currentEpisode}");
        Debug.Log($"Using Genetic Algorithm: {useGeneticAlgorithm}");
        Debug.Log($"Is Training: {isTraining}");
        Debug.Log($"Genetic Manager Found: {geneticManager != null}");
        Debug.Log($"Total Episode Reward: {totalRewardThisEpisode}");
        
        if (geneticManager != null)
        {
            Debug.Log($"Current Generation: {geneticManager.GetCurrentGeneration()}");
            Debug.Log($"Last Genetic Generation: {lastGeneticGeneration}");
            Debug.Log($"Generation Progress: {geneticManager.GetGenerationProgress()*100:F1}%");
            Debug.Log($"GA Enabled: {geneticManager.enableGeneticAlgorithm}");
        }
        
        Debug.Log($"Episode Duration: {Time.time - episodeStartTime:F1}s");
        Debug.Log($"Environment: {environment != null}");
        
        if (environment != null)
        {
            Debug.Log($"Agent Count: {environment.GetAgentCount()}");
            Debug.Log($"Alive Agents: {environment.GetAliveAgentCount()}");
            
            // Show cumulative rewards for each agent
            for (int i = 0; i < environment.GetAgentCount(); i++)
            {
                Debug.Log($"Agent_{i} Total Reward: {environment.GetReward(i):F2}");
            }
        }
        
        if (agents != null)
        {
            Debug.Log($"Agents Array: {agents.Length}");
        }
    }
    
    // Debug method to toggle genetic algorithm
    [ContextMenu("Toggle Genetic Algorithm")]
    public void ToggleGeneticAlgorithm()
    {
        useGeneticAlgorithm = !useGeneticAlgorithm;
        Debug.Log($"Genetic Algorithm: {(useGeneticAlgorithm ? "ENABLED" : "DISABLED")}");
        
        if (!useGeneticAlgorithm)
        {
            Debug.Log("Switched to traditional episode management");
            // Reset episode counter for traditional mode
            currentEpisode = 0;
            episodeStartTime = Time.time;
        }
        else
        {
            Debug.Log("Switched to genetic algorithm episode management");
            lastGeneticGeneration = -1; // Reset to detect first generation
        }
    }
    
    // Debug method to force episode restart
    [ContextMenu("Force Episode Restart")]
    public void ForceEpisodeRestart()
    {
        Debug.Log("🔄 Forcing episode restart...");
        
        if (useGeneticAlgorithm && geneticManager != null)
        {
            geneticManager.ForceEvolution();
        }
        else
        {
            OnManualEpisodeEnd();
        }
    }
}