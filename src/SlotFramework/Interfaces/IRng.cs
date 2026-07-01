namespace SlotFramework.Interfaces;

public interface IRng
{
    int Next(int maxValue); // Returns 0 <= x < maxValue
    double NextDouble();    // Returns 0.0 <= x < 1.0
}
