using SlotFramework.Models;

namespace SlotFramework.Interfaces;

public interface ISlotEngine
{
    SpinResult Spin(IRng rng);
    SpinResult FreeSpin(IRng rng, int currentFreeSpinIndex, int totalFreeSpins);
}
