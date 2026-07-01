using System.Collections.Generic;

namespace SlotFramework.Models;

public class Paytable
{
    private readonly Dictionary<int, long[]> _payouts = new();

    public void AddPayout(int symbolId, int matchCount, long payout)
    {
        if (!_payouts.TryGetValue(symbolId, out var array))
        {
            array = new long[6]; // standard 5 reels max match is 5
            _payouts[symbolId] = array;
        }

        if (matchCount >= array.Length)
        {
            var newArray = new long[matchCount + 1];
            System.Array.Copy(array, newArray, array.Length);
            _payouts[symbolId] = newArray;
            array = newArray;
        }

        array[matchCount] = payout;
    }

    public long GetPayout(int symbolId, int matchCount)
    {
        if (_payouts.TryGetValue(symbolId, out var array))
        {
            if (matchCount < array.Length)
            {
                return array[matchCount];
            }
        }
        return 0;
    }

    public IReadOnlyDictionary<int, long[]> RawPayouts => _payouts;
}
