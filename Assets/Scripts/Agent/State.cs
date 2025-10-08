using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class State
{
    public Transform Position;
    public Transform EnemyPosition;
    public RaycastHit2D[] RaycastHits { get; set; }

    public State()
    {
        RaycastHits = new RaycastHit2D[32]; // 32 rays as specified
    }

    public State(Transform position, Transform enemyPosition, RaycastHit2D[] raycastHits)
    {
        Position = position;
        EnemyPosition = enemyPosition;
        RaycastHits = raycastHits;
    }

    // Convert state to float array for neural network input
    public float[] ToArray(float mapWidth = 100f, float mapHeight = 100f, float maxRayDistance = 20f)
    {
        List<float> stateArray = new List<float>();

        // Add ray distances (normalized)
        if (RaycastHits != null)
        {
            for (int i = 0; i < RaycastHits.Length; i++)
            {
                if (RaycastHits[i].collider != null)
                {
                    stateArray.Add(RaycastHits[i].distance / maxRayDistance);
                }
                else
                {
                    stateArray.Add(1.0f); // Max distance if no hit
                }
            }
        }

        // Add agent positions (normalized)
        if (Position != null)
        {
            stateArray.Add(Position.position.x / (mapWidth / 2f));
            stateArray.Add(Position.position.y / (mapHeight / 2f));
            stateArray.Add(Position.eulerAngles.z / 360f);
        }
        else
        {
            stateArray.Add(0f);
            stateArray.Add(0f);
            stateArray.Add(0f);
        }

        if (EnemyPosition != null)
        {
            stateArray.Add(EnemyPosition.position.x / (mapWidth / 2f));
            stateArray.Add(EnemyPosition.position.y / (mapHeight / 2f));
            stateArray.Add(EnemyPosition.eulerAngles.z / 360f);
        }
        else
        {
            stateArray.Add(0f);
            stateArray.Add(0f);
            stateArray.Add(0f);
        }

        return stateArray.ToArray();
    }
}