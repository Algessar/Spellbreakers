namespace Spellbreakers.Combat.Ai
{
    /// <summary>
    /// A small, seedable random source. The AI must be reproducible - a duel that behaves differently
    /// on every run cannot be tested, and a duel that cannot be tested cannot be tuned.
    /// </summary>
    public struct DeterministicRandom
    {
        private uint _state;

        public DeterministicRandom(int seed)
        {
            // Zero is a fixed point of a plain LCG, so map it away.
            _state = seed == 0 ? 0x9E3779B9u : unchecked((uint)seed);
        }

        public uint State => _state;

        /// <summary>Xorshift32: cheap, and well distributed enough for behaviour jitter.</summary>
        public uint NextUInt()
        {
            var x = _state;
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            _state = x;
            return x;
        }

        public float Next01()
        {
            return (NextUInt() >> 8) * (1f / 16777216f);
        }

        public float Range(float min, float max)
        {
            return min + (max - min) * Next01();
        }

        public int Range(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive)
                return minInclusive;

            return minInclusive + (int)(NextUInt() % (uint)(maxExclusive - minInclusive));
        }
    }
}
