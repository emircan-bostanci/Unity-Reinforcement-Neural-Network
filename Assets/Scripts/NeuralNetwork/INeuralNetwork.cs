public interface INeuralNetwork
{
    float[] Forward(float[] input);
    void Backward(float[] input, float[] targetOutput, float learningRate);
    void BackwardGradient(float[] input, float[] targetOutput, float learningRate);
    void SaveWeights(string path);
    void LoadWeights(string path);
    INeuralNetwork Clone(); // For creating target networks or actor-critic
}