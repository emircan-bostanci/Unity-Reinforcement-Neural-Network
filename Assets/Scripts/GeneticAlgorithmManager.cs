using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using System.IO;

public class GeneticAlgorithmManager : MonoBehaviour
{
    [Header("Genetic Algorithm Settings")]
    public bool enableGeneticAlgorithm = true;
    public float generationDuration = 10f;
    public float successThreshold = 0.1f;
    public int populationSize = 8;
    public float mutationRate = 0.1f;
    public float elitePercentage = 0.2f;
    
    [Header("Auto Save Settings")]
    public bool enableAutoSave = true;
    public float autoSaveInterval = 3600f;
    public string saveDirectory = "SavedModels";
    
    [Header("Display Settings")]
    public bool showProgressUpdates = true;
    public float progressUpdateInterval = 15f; // Show progress every 15 seconds (reduced frequency)
    
    [Header("References")]
    public Envirovment environment;
    public Agent[] agents;
    
    private float generationTimer = 0f;
    private float autoSaveTimer = 0f;
    private float progressTimer = 0f; // Timer for progress updates
    private int currentGeneration = 0;
    private List<AgentFitness> agentFitnessScores;
    private bool isEvolving = false;
    
    [System.Serializable]
    public class AgentFitness
    {
        public int agentIndex;
        public float fitness;
        public float totalReward;
        public int kills;
        public float survivalTime;
        public bool isElite;
        
        public AgentFitness(int index)
        {
            agentIndex = index;
            fitness = 0f;
            totalReward = 0f;
            kills = 0;
            survivalTime = 0f;
            isElite = false;
        }
    }
    
    void Start()
    {
        if (environment == null)
            environment = FindFirstObjectByType<Envirovment>();
            
        if (agents == null || agents.Length == 0)
        {
            agents = FindObjectsByType<Agent>(FindObjectsSortMode.None);
        }
        
        if (!Directory.Exists(saveDirectory))
        {
            Directory.CreateDirectory(saveDirectory);
        }
        
        InitializeFitnessTracking();
        
        Debug.Log($"Genetic Algorithm Manager initialized - Population: {agents.Length}");
    }
    
    void Update()
    {
        if (!enableGeneticAlgorithm && !enableAutoSave) return;
        
        generationTimer += Time.deltaTime;
        autoSaveTimer += Time.deltaTime;
        progressTimer += Time.deltaTime;
        
        // Show progress updates periodically
        if (showProgressUpdates && progressTimer >= progressUpdateInterval)
        {
            ShowProgressUpdate();
            progressTimer = 0f;
        }
        
        // Check for timeout-based evolution trigger
        bool timeoutTriggered = false;
        bool immediateRestart = false;
        if (environment != null && environment.GetEpisodeEnded())
        {
            timeoutTriggered = true;
            
            // Check if this is due to single agent remaining
            int aliveCount = environment.GetAliveAgentCount();
            if (aliveCount <= 1)
            {
                immediateRestart = true;
                Debug.Log($"🔄 Immediate restart triggered - {aliveCount} agents remaining");
            }
        }
        
        if (enableGeneticAlgorithm && !isEvolving && (generationTimer >= generationDuration || timeoutTriggered))
        {
            if (immediateRestart)
            {
                Debug.Log("🏆 === WINNER DECIDED EVOLUTION ===");
                Debug.Log("Single agent remaining, immediately starting new generation!");
            }
            else if (timeoutTriggered)
            {
                Debug.Log("🚨 === EPISODE ENDED EVOLUTION ===");
                Debug.Log("Episode ended, forcing generation evolution!");
            }
            else
            {
                Debug.Log("⏰ === TIME-BASED EVOLUTION ===");
                Debug.Log($"Generation duration ({generationDuration}s) reached, starting evolution!");
            }
            EvaluateGeneration();
        }
        
        if (enableAutoSave && autoSaveTimer >= autoSaveInterval)
        {
            AutoSaveAllModels();
            autoSaveTimer = 0f;
        }
    }
    
    private void ShowProgressUpdate()
    {
        if (environment == null) return;
        
        TrainingManager trainingManager = FindFirstObjectByType<TrainingManager>();
        
        string progressInfo = $"🎯 Progress Update:";
        
        if (trainingManager != null && trainingManager.IsUsingGeneticAlgorithm())
        {
            progressInfo += $" Generation {currentGeneration}, Episode {trainingManager.GetCurrentEpisode()}";
        }
        else
        {
            progressInfo += $" Generation {currentGeneration}";
        }
        
        progressInfo += $" | Time: {generationTimer:F1}/{generationDuration:F0}s ({GetGenerationProgress()*100:F0}%)";
        progressInfo += $" | Alive: {environment.GetAliveAgentCount()}/{agents.Length}";
        
        if (agentFitnessScores != null && agentFitnessScores.Count > 0)
        {
            float avgFitness = agentFitnessScores.Average(a => a.fitness);
            progressInfo += $" | Avg Fitness: {avgFitness:F2}";
        }
        
        Debug.Log(progressInfo);
    }
    
    private void InitializeFitnessTracking()
    {
        if (agents == null || agents.Length == 0)
        {
            Debug.LogError("Cannot initialize fitness tracking - no agents found!");
            return;
        }
        
        agentFitnessScores = new List<AgentFitness>();
        
        for (int i = 0; i < agents.Length; i++)
        {
            agentFitnessScores.Add(new AgentFitness(i));
        }
        
        Debug.Log($"Fitness tracking initialized for {agents.Length} agents");
    }
    
    private void EvaluateGeneration()
    {
        isEvolving = true;
        
        // Find TrainingManager for episode display
        TrainingManager trainingManager = FindFirstObjectByType<TrainingManager>();
        string episodeInfo = "";
        if (trainingManager != null)
        {
            episodeInfo = $" Episode {trainingManager.GetCurrentEpisode()}";
        }
        
        Debug.Log($"🧬 === GENERATION {currentGeneration}{episodeInfo} EVALUATION (Duration: {generationTimer:F1}s) ===");
        
        // Calculate fitness for each agent
        CalculateFitnessScores();
        
        // Calculate average fitness
        float averageFitness = agentFitnessScores.Average(a => a.fitness);
        
        Debug.Log($"📊 Generation {currentGeneration} completed - Average Fitness: {averageFitness:F3}");
        
        if (averageFitness < successThreshold)
        {
            Debug.Log($"⚠️ Poor performance detected (avg: {averageFitness:F3} < threshold: {successThreshold}). Evolving generation...");
            EvolvePopulation();
        }
        else
        {
            Debug.Log($"✅ Generation performing well (avg: {averageFitness:F3} >= threshold: {successThreshold}). Continuing without evolution.");
        }
        
        // Reset for next generation
        ResetGenerationTracking();
        
        // Reset the full environment state (positions, deaths, rewards, timeout)
        if (environment != null)
        {
            Debug.Log("🔄 RESETTING WORLD STATE: Reviving dead agents and resetting positions");
            environment.ResetEnvironment(); // This calls the full reset including positions and agent revival
        }
        
        currentGeneration++;
        generationTimer = 0f;
        isEvolving = false;
    }
    
    private void CalculateFitnessScores()
    {
        Debug.Log("=== Calculating Fitness Scores ===");
        
        for (int i = 0; i < agentFitnessScores.Count; i++)
        {
            if (i < agents.Length && environment != null)
            {
                AgentFitness fitness = agentFitnessScores[i];
                
                // Get cumulative reward for this agent
                fitness.totalReward = environment.GetReward(i);
                
                // Get kill count
                fitness.kills = environment.GetKillCount(i);
                
                // Update survival time
                fitness.survivalTime = environment.IsAgentAlive(i) ? generationTimer : fitness.survivalTime;
                
                // Calculate composite fitness
                fitness.fitness = CalculateCompositeFitness(fitness);
                
                string networkType = GetAgentNetworkType(agents[i]);
                Debug.Log($"Agent_{i} ({networkType}): Reward={fitness.totalReward:F1}, Kills={fitness.kills}, Survival={fitness.survivalTime:F1}s, Fitness={fitness.fitness:F3}, Alive={environment.IsAgentAlive(i)}");
            }
        }
        
        // Sort by fitness (best first)
        agentFitnessScores = agentFitnessScores.OrderByDescending(a => a.fitness).ToList();
        
        Debug.Log($"Best Agent: {agentFitnessScores[0].agentIndex} with fitness {agentFitnessScores[0].fitness:F3}");
        Debug.Log($"Worst Agent: {agentFitnessScores[agentFitnessScores.Count-1].agentIndex} with fitness {agentFitnessScores[agentFitnessScores.Count-1].fitness:F3}");
    }
    
    private float CalculateCompositeFitness(AgentFitness fitness)
    {
        float rewardWeight = 0.8f;  // High weight for normalized rewards
        float survivalWeight = 0.2f;
        
        // Normalize rewards for the new [-1, +1] reward system
        // The rewards are already normalized, so we can use them directly
        float normalizedReward = Mathf.Clamp(fitness.totalReward, -1f, 1f);
        
        // Small penalty for excessive negative rewards (likely timeout punishments)
        if (fitness.totalReward < -2f) // Multiple timeout punishments
        {
            normalizedReward = Mathf.Max(normalizedReward, -0.8f); // Cap penalty
            Debug.Log($"Agent_{fitness.agentIndex} has excessive timeout punishment, capping fitness penalty");
        }
        
        // Normalize survival time
        float normalizedSurvival = Mathf.Clamp01(fitness.survivalTime / generationDuration);
        
        float compositeFitness = (normalizedReward * rewardWeight) + (normalizedSurvival * survivalWeight);
        
        // Ensure fitness is within reasonable bounds
        return Mathf.Clamp(compositeFitness, -1f, 1f);
    }
    
    private void EvolvePopulation()
    {
        int eliteCount = Mathf.RoundToInt(agents.Length * elitePercentage);
        for (int i = 0; i < eliteCount; i++)
        {
            agentFitnessScores[i].isElite = true;
        }
        
        Debug.Log($"Evolving population - Elite agents: {eliteCount}");
        
        for (int i = 0; i < eliteCount; i++)
        {
            int agentIndex = agentFitnessScores[i].agentIndex;
            string elitePath = Path.Combine(saveDirectory, $"Elite_Agent_{agentIndex}_Gen_{currentGeneration}.json");
            agents[agentIndex].SaveModel(elitePath);
        }
        
        for (int i = eliteCount; i < agents.Length; i++)
        {
            int agentIndex = agentFitnessScores[i].agentIndex;
            int parentIndex = agentFitnessScores[Random.Range(0, eliteCount)].agentIndex;
            
            CloneAndMutateAgent(parentIndex, agentIndex);
        }
        
        Debug.Log($"Generation {currentGeneration} evolution completed!");
    }
    
    private bool IsAgentNetworkReady(Agent agent)
    {
        if (agent == null) return false;
        
        switch (agent.networkType)
        {
            case Agent.NeuralNetworkType.SimpleNN:
                return agent.simpleNetwork != null;
            case Agent.NeuralNetworkType.LSTM_RNN:
                return agent.lstmNetwork != null;
            default:
                return false;
        }
    }
    
    private void CloneAndMutateAgent(int parentIndex, int childIndex)
    {
        if (parentIndex >= 0 && parentIndex < agents.Length && 
            childIndex >= 0 && childIndex < agents.Length)
        {
            // Get the parent and child agents
            Agent parentAgent = agents[parentIndex];
            Agent childAgent = agents[childIndex];
            
            // Clone the network type from parent
            childAgent.networkType = parentAgent.networkType;
            
            // Initialize the appropriate network for the child
            switch (childAgent.networkType)
            {
                case Agent.NeuralNetworkType.SimpleNN:
                    if (childAgent.simpleNetwork == null)
                    {
                        childAgent.simpleNetwork = new SimpleNeuralNetwork();
                    }
                    childAgent.simpleNetwork.Initialize();
                    Debug.Log($"Agent_{childIndex} evolved from Agent_{parentIndex} using Simple NN");
                    break;
                    
                case Agent.NeuralNetworkType.LSTM_RNN:
                    // LSTM networks are initialized in the Agent's Start() method
                    // We just need to ensure the settings are copied
                    childAgent.lstmSequenceLength = parentAgent.lstmSequenceLength;
                    childAgent.lstmHiddenSize = parentAgent.lstmHiddenSize;
                    childAgent.resetLSTMOnDeath = parentAgent.resetLSTMOnDeath;
                    Debug.Log($"Agent_{childIndex} evolved from Agent_{parentIndex} using LSTM RNN");
                    break;
            }
        }
    }
    
    private string GetAgentNetworkType(Agent agent)
    {
        if (agent == null) return "Unknown";
        return agent.networkType == Agent.NeuralNetworkType.LSTM_RNN ? "LSTM" : "Simple";
    }

    private void ResetGenerationTracking()
    {
        for (int i = 0; i < agentFitnessScores.Count; i++)
        {
            agentFitnessScores[i].fitness = 0f;
            agentFitnessScores[i].totalReward = 0f;
            agentFitnessScores[i].kills = 0;
            agentFitnessScores[i].survivalTime = 0f;
            agentFitnessScores[i].isElite = false;
        }
    }
    
    private void AutoSaveAllModels()
    {
        string timestamp = System.DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        string autoSaveFolder = Path.Combine(saveDirectory, $"AutoSave_{timestamp}");
        
        if (!Directory.Exists(autoSaveFolder))
        {
            Directory.CreateDirectory(autoSaveFolder);
        }
        
        int savedCount = 0;
        for (int i = 0; i < agents.Length; i++)
        {
            try
            {
                string savePath = Path.Combine(autoSaveFolder, $"Agent_{i}.json");
                agents[i].SaveModel(savePath);
                savedCount++;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Failed to auto-save Agent_{i}: {e.Message}");
            }
        }
        
        Debug.Log($"Auto-saved {savedCount}/{agents.Length} agent models to {autoSaveFolder}");
        
        SaveGenerationStats(autoSaveFolder);
    }
    
    private void SaveGenerationStats(string folder)
    {
        try
        {
            string statsPath = Path.Combine(folder, "generation_stats.txt");
            List<string> stats = new List<string>
            {
                $"Generation: {currentGeneration}",
                $"Timestamp: {System.DateTime.Now}",
                $"Population Size: {agents.Length}",
                $"Generation Duration: {generationDuration}s",
                "",
                "Agent Fitness Scores:"
            };
            
            if (agentFitnessScores != null)
            {
                foreach (var fitness in agentFitnessScores)
                {
                    stats.Add($"Agent_{fitness.agentIndex}: Fitness={fitness.fitness:F3}, Reward={fitness.totalReward:F1}, Elite={fitness.isElite}");
                }
            }
            
            File.WriteAllLines(statsPath, stats);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Failed to save generation stats: {e.Message}");
        }
    }
    
    public void ForceEvolution()
    {
        if (!isEvolving)
        {
            Debug.Log("Forcing evolution...");
            EvaluateGeneration();
        }
    }
    
    public void ForceSave()
    {
        Debug.Log("Forcing auto-save...");
        AutoSaveAllModels();
    }
    
    public float GetAverageFitness()
    {
        if (agentFitnessScores == null || agentFitnessScores.Count == 0) return 0f;
        return agentFitnessScores.Average(a => a.fitness);
    }
    
    public int GetCurrentGeneration()
    {
        return currentGeneration;
    }
    
    public float GetGenerationProgress()
    {
        return generationTimer / generationDuration;
    }
    
    // Debug method to test fitness calculation
    [ContextMenu("Test Fitness Calculation")]
    public void TestFitnessCalculation()
    {
        if (environment == null || agents == null)
        {
            Debug.LogError("Environment or agents not set!");
            return;
        }
        
        Debug.Log("=== MANUAL FITNESS TEST ===");
        for (int i = 0; i < agents.Length; i++)
        {
            float cumulativeReward = environment.GetReward(i);
            bool isAlive = environment.IsAgentAlive(i);
            Debug.Log($"Agent_{i}: Cumulative Reward = {cumulativeReward:F1}, Alive = {isAlive}, Timer = {generationTimer:F1}s");
        }
    }
    
    // Debug method to force generation evaluation
    [ContextMenu("Force Generation Evaluation")]
    public void TestGenerationEvaluation()
    {
        if (!isEvolving)
        {
            Debug.Log("=== FORCED GENERATION EVALUATION ===");
            EvaluateGeneration();
        }
        else
        {
            Debug.Log("Already evaluating generation!");
        }
    }
    
    // Debug method to check agent network status
    [ContextMenu("Check Agent Networks")]
    public void CheckAgentNetworks()
    {
        if (agents == null)
        {
            Debug.LogError("No agents array assigned!");
            return;
        }
        
        Debug.Log("=== AGENT NETWORK STATUS ===");
        for (int i = 0; i < agents.Length; i++)
        {
            if (agents[i] != null)
            {
                string networkType = GetAgentNetworkType(agents[i]);
                bool isReady = IsAgentNetworkReady(agents[i]);
                Debug.Log($"Agent_{i}: Type={networkType}, Ready={isReady}");
                
                if (agents[i].networkType == Agent.NeuralNetworkType.LSTM_RNN)
                {
                    Debug.Log($"   LSTM Settings: Hidden={agents[i].lstmHiddenSize}, Sequence={agents[i].lstmSequenceLength}, ResetOnDeath={agents[i].resetLSTMOnDeath}");
                }
            }
            else
            {
                Debug.LogError($"Agent_{i} is null!");
            }
        }
    }
    public void CheckTimeoutStatus()
    {
        if (environment != null)
        {
            Debug.Log($"=== ENVIRONMENT STATUS ===");
            Debug.Log($"Episode ended: {environment.GetEpisodeEnded()}");
            Debug.Log($"Alive agents: {environment.GetAliveAgentCount()}");
            Debug.Log($"Generation timer: {generationTimer:F1}s / {generationDuration}s");
            
            bool willTimeout = environment.GetEpisodeEnded();
            Debug.Log($"Will trigger evolution: {willTimeout}");
        }
    }
}
