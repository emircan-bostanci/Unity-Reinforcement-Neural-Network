using UnityEngine;

/// <summary>
/// Instructions for setting up the Deep Reinforcement Learning environment
/// </summary>
public class SetupInstructions : MonoBehaviour
{
    [Header("Setup Instructions")]
    [TextArea(20, 30)]
    public string instructions = @"
DEEP REINFORCEMENT LEARNING ENVIRONMENT SETUP

1. SCENE SETUP:
   - Create an empty GameObject and attach the 'Envirovment' script
   - Create another empty GameObject and attach the 'TrainingManager' script

2. PLAYER AGENT:
   - Create a GameObject for the player (e.g., a capsule or sprite)
   - Add the 'Agent' script to it
   - Set up a Rigidbody2D (optional, for physics)
   - Add a Collider2D for collision detection
   - Set the layer to 'Agent' or create a custom layer

3. ENEMY AGENT:
   - Create another GameObject for the enemy (similar to player)
   - Add the 'Agent' script to it
   - Same setup as player agent

4. WALLS:
   - Create wall GameObjects (e.g., using Tilemap, sprites, or primitives)
   - Add Collider2D components to all walls
   - Set the layer to 'Wall' or create a custom layer

5. SPAWN POINTS:
   - Create empty GameObjects as spawn points for player
   - Create empty GameObjects as spawn points for enemy
   - Position them appropriately in your level

6. ENVIRONMENT CONFIGURATION:
   - Assign Player Agent and Enemy Agent transforms to the Environment
   - Set up Wall Layer Mask (select the wall layer)
   - Set up Agent Layer Mask (select the agent layer)
   - Assign Player Spawn Points array
   - Assign Enemy Spawn Points array
   - Adjust other parameters as needed:
     * Max Episode Length (e.g., 1000)
     * Shoot Range (e.g., 10)
     * Move Speed (e.g., 5)
     * Rotation Speed (e.g., 90)

7. TRAINING MANAGER CONFIGURATION:
   - Assign the Environment reference
   - Assign Player Agent and Enemy Agent references
   - Set training parameters:
     * Training Step Interval (e.g., 0.02 for 50Hz)
     * Max Episodes (e.g., 1000)

8. LAYER SETUP:
   - Create layers: 'Wall', 'Agent'
   - Assign appropriate GameObjects to these layers
   - Configure Layer Masks in the Environment script

STATES (32 + 6 = 38 total):
- 32 ray distances (normalized 0-1)
- Player position X, Y (normalized)
- Enemy position X, Y (normalized)  
- Player rotation (normalized 0-1)
- Enemy rotation (normalized 0-1)

ACTIONS (5 total):
- Look angle (-1 to 1)
- Shoot (0 to 1, >0.5 = shoot)
- Move forward (-1 to 1)
- Move left (-1 to 1)
- Move right (-1 to 1)

REWARDS:
- Shoot enemy: +50 (episode ends)
- Shoot nothing: -10
- Hit wall: -20
- Do nothing: -5

NEXT STEPS:
1. Implement neural network in SimpleNeuralNetwork.cs
2. Connect neural network to Agent.SelectAction()
3. Implement learning algorithm in Agent.Learn()
4. Test and tune hyperparameters
";

    void Start()
    {
        Debug.Log(instructions);
    }
}

// Additional helper component for quick setup
[System.Serializable]
public class EnvironmentSetupHelper
{
    [Header("Quick Setup")]
    public bool autoSetupLayers = true;
    public bool createSpawnPoints = true;
    public Vector2[] playerSpawnPositions = new Vector2[] { new Vector2(-10, 0), new Vector2(-10, 5) };
    public Vector2[] enemySpawnPositions = new Vector2[] { new Vector2(10, 0), new Vector2(10, 5) };
}