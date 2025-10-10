using UnityEngine;

// Interface for neural networks to ensure consistency
public interface INetworkInterface
{
    bool UseValueNetwork { get; set; }
    void Initialize();
    float[] Forward(float[] input);
    float ForwardValue(float[] input);
    void SaveModel(string filePath);
    void LoadModel(string filePath);
}