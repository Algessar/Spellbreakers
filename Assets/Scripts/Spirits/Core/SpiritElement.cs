using System;

namespace Spellbreakers.Spirits
{
    /// <summary>
    /// The four elements a Spirit can be honoured through.
    ///
    /// Spirits.md keeps the original narrow idea - Raine's Wind and Fire - and extends it to four so the
    /// player has a real choice of tree. The order here is the tab order in the tree window and the order
    /// a saved progression is written in, so it must not be reshuffled casually.
    /// </summary>
    public enum SpiritElement
    {
        Fire = 0,
        Wind = 1,
        Earth = 2,
        Water = 3,
    }

    /// <summary>Presentation and identity helpers for <see cref="SpiritElement"/>.</summary>
    public static class SpiritElements
    {
        public static readonly SpiritElement[] All =
        {
            SpiritElement.Fire,
            SpiritElement.Wind,
            SpiritElement.Earth,
            SpiritElement.Water,
        };

        public static string ToDisplayName(SpiritElement element)
        {
            switch (element)
            {
                case SpiritElement.Fire: return "Fire";
                case SpiritElement.Wind: return "Wind";
                case SpiritElement.Earth: return "Earth";
                case SpiritElement.Water: return "Water";
                default: return element.ToString();
            }
        }

        /// <summary>Stable lowercase key, used in save data and boon ids.</summary>
        public static string ToKey(SpiritElement element)
        {
            switch (element)
            {
                case SpiritElement.Fire: return "fire";
                case SpiritElement.Wind: return "wind";
                case SpiritElement.Earth: return "earth";
                case SpiritElement.Water: return "water";
                default: return element.ToString().ToLowerInvariant();
            }
        }

        public static bool TryParseKey(string key, out SpiritElement element)
        {
            foreach (var candidate in All)
            {
                if (string.Equals(ToKey(candidate), key, StringComparison.OrdinalIgnoreCase))
                {
                    element = candidate;
                    return true;
                }
            }

            element = SpiritElement.Fire;
            return false;
        }
    }
}
