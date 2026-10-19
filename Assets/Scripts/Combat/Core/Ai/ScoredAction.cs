using System.Collections.Generic;
using System.Text;

namespace Spellbreakers.Combat.Ai
{
    /// <summary>One consideration's contribution to a candidate's score, kept for the debug overlay.</summary>
    public readonly struct ConsiderationScore
    {
        public ConsiderationScore(string name, float weight, float score)
        {
            Name = name;
            Weight = weight;
            Score = score;
        }

        public string Name { get; }

        public float Weight { get; }

        public float Score { get; }

        /// <summary>What this consideration actually contributed to the final, normalised score.</summary>
        public float Contribution => Weight * Score;
    }

    /// <summary>
    /// A candidate action with its final score and the full breakdown. When an enemy does something
    /// that looks wrong, this is what says which consideration made it look that way.
    /// </summary>
    public sealed class ScoredAction
    {
        public ScoredAction(FencingAction action, float score, ConsiderationScore[] breakdown)
        {
            Action = action;
            Score = score;
            Breakdown = breakdown;
        }

        public FencingAction Action { get; }

        public float Score { get; }

        public ConsiderationScore[] Breakdown { get; }

        /// <summary>Single-line summary for logs and the overlay.</summary>
        public string Explain()
        {
            var builder = new StringBuilder();
            builder.Append(Action.Name);
            builder.Append(" = ");
            builder.Append(Score.ToString("0.000"));

            if (Breakdown == null)
                return builder.ToString();

            builder.Append("  [");
            for (var i = 0; i < Breakdown.Length; i++)
            {
                if (i > 0)
                    builder.Append(", ");

                builder.Append(Breakdown[i].Name);
                builder.Append(' ');
                builder.Append(Breakdown[i].Score.ToString("0.00"));
                builder.Append('x');
                builder.Append(Breakdown[i].Weight.ToString("0.0"));
            }

            builder.Append(']');
            return builder.ToString();
        }

        public override string ToString()
        {
            return Explain();
        }
    }

    /// <summary>Convenience helpers for reading a score list.</summary>
    public static class ScoredActionExtensions
    {
        public static ScoredAction Best(this IReadOnlyList<ScoredAction> scores)
        {
            if (scores == null || scores.Count == 0)
                return null;

            var best = scores[0];
            for (var i = 1; i < scores.Count; i++)
            {
                if (scores[i].Score > best.Score)
                    best = scores[i];
            }

            return best;
        }
    }
}
