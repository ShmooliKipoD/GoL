using System;

namespace GoL.Sim.Core;

/// <summary>
/// PCG32 pseudo-random generator, as a mutable struct.
/// <para>
/// A struct rather than a class because the simulation holds one of these per
/// creature: they must not allocate, and a creature's whole random state has to
/// serialize with the creature. Sixteen bytes, no heap, good statistical quality.
/// </para>
/// <para>
/// Every draw is a pure function of the state, so a run is reproducible from its
/// seed. Never replace this with <see cref="System.Random"/> - its algorithm is
/// not contractually stable across .NET versions, which would silently break
/// replay of saved worlds.
/// </para>
/// </summary>
public struct Pcg32
{
    private const ulong Multiplier = 6364136223846793005UL;

    private ulong _state;
    private readonly ulong _increment;

    // Box-Muller produces two normal deviates per pair of uniforms; the spare is
    // kept here so half the Gaussian draws cost nothing.
    private float _spareGauss;
    private bool _hasSpareGauss;

    public Pcg32(ulong seed, ulong sequence = 1UL)
    {
        _increment = (sequence << 1) | 1UL;   // must be odd for a full period
        _state = 0UL;
        _spareGauss = 0f;
        _hasSpareGauss = false;

        NextUInt();
        _state += seed;
        NextUInt();
    }

    public uint NextUInt()
    {
        ulong old = _state;
        _state = old * Multiplier + _increment;

        uint xorshifted = (uint)(((old >> 18) ^ old) >> 27);
        int rot = (int)(old >> 59);
        return (xorshifted >> rot) | (xorshifted << ((-rot) & 31));
    }

    /// <summary>Uniform in [0, 1). Uses 24 bits, which is float's mantissa - taking
    /// more would add bits the type cannot represent.</summary>
    public float NextFloat() => (NextUInt() >> 8) * (1.0f / 16777216.0f);

    /// <summary>Uniform in [min, max).</summary>
    public float NextFloat(float min, float max) => min + NextFloat() * (max - min);

    /// <summary>Uniform in [0, exclusiveMax). Rejection-sampled, so it is unbiased -
    /// a plain modulo would skew the low values.</summary>
    public int NextInt(int exclusiveMax)
    {
        if (exclusiveMax <= 0) throw new ArgumentOutOfRangeException(nameof(exclusiveMax));

        uint bound = (uint)exclusiveMax;
        uint threshold = (uint)(-(int)bound) % bound;

        while (true)
        {
            uint r = NextUInt();
            if (r >= threshold) return (int)(r % bound);
        }
    }

    /// <summary>Uniform in [min, exclusiveMax).</summary>
    public int NextInt(int min, int exclusiveMax) => min + NextInt(exclusiveMax - min);

    /// <summary>True with the given probability.</summary>
    public bool Chance(float probability) => NextFloat() < probability;

    /// <summary>Standard normal deviate (mean 0, sigma 1), via Box-Muller.</summary>
    public float NextGauss()
    {
        if (_hasSpareGauss)
        {
            _hasSpareGauss = false;
            return _spareGauss;
        }

        // Reject the degenerate u == 0 case, where Log would be -infinity.
        float u1;
        do { u1 = NextFloat(); } while (u1 <= 1e-7f);

        float u2 = NextFloat();
        float magnitude = MathF.Sqrt(-2f * MathF.Log(u1));
        float angle = 2f * MathF.PI * u2;

        _spareGauss = magnitude * MathF.Sin(angle);
        _hasSpareGauss = true;
        return magnitude * MathF.Cos(angle);
    }

    /// <summary>Normal deviate with the given mean and standard deviation.</summary>
    public float NextGauss(float mean, float sigma) => mean + NextGauss() * sigma;

    /// <summary>
    /// Mixes values into a 64-bit seed (SplitMix64 finalizer). Used to derive a
    /// creature's stream from (worldSeed, creatureId, birthTick) so that its
    /// random sequence does not depend on the order creatures are updated in.
    /// </summary>
    public static ulong Mix(ulong a, ulong b, ulong c = 0UL)
    {
        ulong z = a;
        z ^= b + 0x9E3779B97F4A7C15UL + (z << 6) + (z >> 2);
        z ^= c + 0x9E3779B97F4A7C15UL + (z << 6) + (z >> 2);

        z += 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }
}
