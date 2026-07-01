using System;
using SlotFramework.Interfaces;

namespace SlotFramework.Utilities;

/// <summary>
/// A high-performance, non-thread-safe pseudo-random number generator.
/// Implementation of xoshiro256** 1.0.
/// Extremely fast, suitable for slot math simulations.
/// For multi-threading, each thread should have its own instance.
/// </summary>
public class FastRandom : IRng
{
    private ulong _s0;
    private ulong _s1;
    private ulong _s2;
    private ulong _s3;

    public FastRandom() : this((ulong)Guid.NewGuid().GetHashCode())
    {
    }

    public FastRandom(ulong seed)
    {
        ulong state = seed;
        _s0 = SplitMix64(ref state);
        _s1 = SplitMix64(ref state);
        _s2 = SplitMix64(ref state);
        _s3 = SplitMix64(ref state);
    }

    private static ulong SplitMix64(ref ulong state)
    {
        state += 0x9e3779b97f4a7c15;
        ulong z = state;
        z = (z ^ (z >> 30)) * 0xbf58476d1ce4e5b9;
        z = (z ^ (z >> 27)) * 0x94d049bb133111eb;
        return z ^ (z >> 31);
    }

    private static ulong Rotl(ulong x, int k)
    {
        return (x << k) | (x >> (64 - k));
    }

    public ulong Next64()
    {
        ulong result = Rotl(_s1 * 5, 7) * 9;
        ulong t = _s1 << 17;

        _s2 ^= _s0;
        _s3 ^= _s1;
        _s1 ^= _s2;
        _s0 ^= _s3;

        _s2 ^= t;
        _s3 = Rotl(_s3, 45);

        return result;
    }

    public int Next(int maxValue)
    {
        if (maxValue <= 0) throw new ArgumentOutOfRangeException(nameof(maxValue));
        return (int)(Next64() % (ulong)maxValue);
    }

    public double NextDouble()
    {
        return (Next64() >> 11) * (1.0 / 9007199254740992.0);
    }
}
