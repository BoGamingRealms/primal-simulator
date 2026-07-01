namespace SlotFramework.Models;

public class ReelSet
{
    public int[][] Reels { get; set; } = System.Array.Empty<int[]>();

    public ReelSet() { }

    public ReelSet(int[][] reels)
    {
        Reels = reels;
    }

    public int GetSymbolAt(int reelIndex, int reelStopIndex, int offset)
    {
        var strip = Reels[reelIndex];
        int len = strip.Length;
        int targetIndex = (reelStopIndex + offset) % len;
        if (targetIndex < 0) targetIndex += len;
        return strip[targetIndex];
    }
}
