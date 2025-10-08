using UnityEngine;

public class TrainingManager : MonoBehaviour
{
    [Header("References")]
    public Envirovment environment;
    public Agent[] agents; // Array of all agents for multi-agent training

    [Header("Training Settings")]
    public bool isTraining = true;
    public float trainingStepInterval = 0.02f; // 50 Hz training
    public int maxEpisodes = 1000;
    public bool logProgress = true;
    public int logInterval = 10; // Log every 10 episodes
    
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

    void Start()
    {
        if (environment == null)
            environment = FindFirstObjectByType<Envirovment>();
        
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

        StartNewEpisode();
    }

    void Update()
    {
        if (!isTraining)
        {
            Debug.LogWarning("Training is disabled!");
            return;
        }

        if (Time.time - lastTrainingStep >= trainingStepInterval)
        {
            Debug.Log($"Running training step at time {Time.time:F2}");
            TrainingStep();
            lastTrainingStep = Time.time;
        }
    }

    void TrainingStep()
    {
        if (environment.IsEpisodeFinished())
        {
            EndEpisode();
            return;
        }

        // Get current state
        currentState = environment.GetCurrentState();
        
        if (currentState == null)
        {
            Debug.LogError("Current state is null!");
            return;
        }

        Debug.Log("Getting actions from all agents...");

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

        Debug.Log("Applying actions to environment...");
        
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

        Debug.Log($"Step completed - {environment.GetAliveAgentCount()} agents alive");

        // Train all alive agents
        for (int i = 0; i < agents.Length; i++)
        {
            if (environment.IsAgentAlive(i))
            {
                try
                {
                    float agentReward = environment.GetReward(i);
                    agents[i].Learn(currentState, agentActions[i], agentReward, nextState, environment.IsEpisodeFinished());
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
        
        Debug.Log($"Starting Episode {currentEpisode}");
        
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
        StartNewEpisode();
    }
}