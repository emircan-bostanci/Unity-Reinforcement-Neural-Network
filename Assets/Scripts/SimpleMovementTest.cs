using UnityEngine;

/// <summary>
/// Simple movement test script - attach to any GameObject to make it move around
/// Use this to verify basic movement works before training
/// </summary>
public class SimpleMovementTest : MonoBehaviour
{
    [Header("Movement Test")]
    public float moveSpeed = 5f;
    public bool enableMovement = true;
    public bool circularMotion = false;
    
    private float timer = 0f;
    
    void Update()
    {
        if (!enableMovement) return;
        
        timer += Time.deltaTime;
        
        if (circularMotion)
        {
            // Circular motion for testing
            float radius = 3f;
            float angle = timer * 2f; // 2 radians per second
            Vector3 newPos = new Vector3(
                Mathf.Cos(angle) * radius,
                Mathf.Sin(angle) * radius,
                transform.position.z
            );
            transform.position = newPos;
        }
        else
        {
            // Forward movement with random direction changes
            if (timer > 2f) // Change direction every 2 seconds
            {
                transform.Rotate(0, 0, Random.Range(-45f, 45f));
                timer = 0f;
            }
            
            // Move forward
            transform.position += transform.up * moveSpeed * Time.deltaTime;
        }
        
        Debug.Log($"{gameObject.name} position: {transform.position}");
    }
    
    void OnDrawGizmos()
    {
        // Draw movement direction
        Gizmos.color = Color.red;
        Gizmos.DrawRay(transform.position, transform.up * 2f);
        
        // Draw position marker
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(transform.position, 0.5f);
    }
}