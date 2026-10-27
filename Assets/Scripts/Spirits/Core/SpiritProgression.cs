using System;
using System.Collections.Generic;

namespace Spellbreakers.Spirits
{
    /// <summary>Why a node can or cannot be clicked, and how the tree window should draw it.</summary>
    public enum SpiritTalentState
    {
        /// <summary>Tier or prerequisite not met. Drawn greyed out.</summary>
        Locked = 0,

        /// <summary>Reachable and not maxed. Clickable, whether or not a point is spare.</summary>
        Available = 1,

        /// <summary>All ranks bought. Drawn complete.</summary>
        Maxed = 2,
    }

    /// <summary>
    /// The player's spirit points and the ranks they have bought with them.
    ///
    /// This is the whole rules engine for the tree: Classic WoW's shape - five points in a tree to open
    /// the next tier, prerequisites that must be ranked first, a rank that can be bought and refunded
    /// one at a time, and a full respec that gives everything back. It owns no UI and no persistence, so
    /// both can change without touching the rules.
    /// </summary>
    public sealed class SpiritProgression
    {
        private readonly Dictionary<string, int> _ranks = new Dictionary<string, int>(StringComparer.Ordinal);

        public SpiritProgression(IReadOnlyList<SpiritTree> trees = null, int startingPoints = 0)
        {
            Trees = trees ?? SpiritCatalog.Trees;
            TotalPoints = Math.Max(0, startingPoints);
        }

        /// <summary>The trees this progression was built against.</summary>
        public IReadOnlyList<SpiritTree> Trees { get; }

        /// <summary>Total points earned, spent or not. The spending budget is this minus <see cref="SpentPoints"/>.</summary>
        public int TotalPoints { get; private set; }

        /// <summary>Points already committed to ranks.</summary>
        public int SpentPoints
        {
            get
            {
                var spent = 0;
                foreach (var pair in _ranks)
                {
                    var talent = Find(pair.Key);
                    if (talent != null)
                        spent += pair.Value * talent.CostPerRank;
                }

                return spent;
            }
        }

        public int AvailablePoints => TotalPoints - SpentPoints;

        /// <summary>Raised whenever a rank, a respec or the point total changes.</summary>
        public event Action Changed;

        /// <summary>How many points are committed inside one element's tree.</summary>
        public int SpentIn(SpiritElement element)
        {
            var spent = 0;
            foreach (var pair in _ranks)
            {
                var talent = Find(pair.Key);
                if (talent != null && talent.Element == element)
                    spent += pair.Value * talent.CostPerRank;
            }

            return spent;
        }

        /// <summary>Ranks bought in a talent, 0 when none.</summary>
        public int RankOf(string talentId)
        {
            if (string.IsNullOrEmpty(talentId))
                return 0;

            return _ranks.TryGetValue(talentId, out var rank) ? rank : 0;
        }

        public bool IsMaxed(string talentId)
        {
            var talent = Find(talentId);
            return talent != null && RankOf(talentId) >= talent.MaxRank;
        }

        /// <summary>Which tree owns a talent id, or null.</summary>
        public SpiritTree TreeOf(string talentId)
        {
            foreach (var tree in Trees)
            {
                if (tree.Contains(talentId))
                    return tree;
            }

            return null;
        }

        /// <summary>Whether the tier and prerequisite requirements are met, ignoring points available.</summary>
        public bool IsReachable(string talentId)
        {
            var talent = Find(talentId);
            if (talent == null)
                return false;

            var tree = TreeOf(talentId);
            if (tree == null)
                return false;

            if (SpentIn(talent.Element) < SpiritTree.PointsRequiredForTier(talent.Tier))
                return false;

            if (!talent.HasPrerequisite)
                return true;

            return RankOf(talent.RequiresId) >= talent.RequiresRank;
        }

        public SpiritTalentState StateOf(string talentId)
        {
            var talent = Find(talentId);
            if (talent == null)
                return SpiritTalentState.Locked;

            if (RankOf(talentId) >= talent.MaxRank)
                return SpiritTalentState.Maxed;

            return IsReachable(talentId) ? SpiritTalentState.Available : SpiritTalentState.Locked;
        }

        /// <summary>
        /// Whether a point can be spent right now, and if not why. The reason is written for the player:
        /// it goes straight into the node tooltip rather than being turned into a message at the call site.
        /// </summary>
        public bool CanRankUp(string talentId, out string reason)
        {
            reason = string.Empty;

            var talent = Find(talentId);
            if (talent == null)
            {
                reason = "Unknown talent.";
                return false;
            }

            if (RankOf(talentId) >= talent.MaxRank)
            {
                reason = "Already at maximum rank.";
                return false;
            }

            var tree = TreeOf(talentId);
            if (tree == null)
            {
                reason = "This talent is not part of any tree.";
                return false;
            }

            var requiredForTier = SpiritTree.PointsRequiredForTier(talent.Tier);
            var spentInTree = SpentIn(talent.Element);
            if (spentInTree < requiredForTier)
            {
                reason = $"Requires {requiredForTier} points spent in the " +
                         $"{SpiritElements.ToDisplayName(talent.Element)} tree " +
                         $"({spentInTree} spent).";
                return false;
            }

            if (talent.HasPrerequisite && RankOf(talent.RequiresId) < talent.RequiresRank)
            {
                var prerequisite = tree.Find(talent.RequiresId);
                var prerequisiteName = prerequisite != null ? prerequisite.Name : talent.RequiresId;
                reason = $"Requires {prerequisiteName} rank {talent.RequiresRank}.";
                return false;
            }

            if (AvailablePoints < talent.CostPerRank)
            {
                reason = $"Requires {talent.CostPerRank} spirit point" +
                         $"{(talent.CostPerRank == 1 ? string.Empty : "s")}.";
                return false;
            }

            return true;
        }

        /// <summary>Spends one rank on a talent. Returns false and changes nothing when it is not legal.</summary>
        public bool RankUp(string talentId)
        {
            if (!CanRankUp(talentId, out _))
                return false;

            var talent = Find(talentId);
            _ranks[talentId] = RankOf(talentId) + 1;
            Changed?.Invoke();
            return true;
        }

        /// <summary>
        /// Gives back the most recent rank of a talent. Classic WoW only ever refunds the last point, and
        /// keeping that restriction means a tree can never be left in a state its own rules could not
        /// have produced.
        /// </summary>
        public bool Refund(string talentId)
        {
            var rank = RankOf(talentId);
            if (rank <= 0)
                return false;

            if (rank == 1)
                _ranks.Remove(talentId);
            else
                _ranks[talentId] = rank - 1;

            Changed?.Invoke();
            return true;
        }

        /// <summary>Refunds every point spent in one element's tree.</summary>
        public int ResetElement(SpiritElement element)
        {
            var refunded = 0;

            foreach (var tree in Trees)
            {
                if (tree.Element != element)
                    continue;

                foreach (var talent in tree.Talents)
                {
                    if (_ranks.Remove(talent.Id))
                        refunded += talent.CostPerRank;
                }
            }

            if (refunded > 0)
                Changed?.Invoke();

            return refunded;
        }

        /// <summary>Refunds every point spent anywhere.</summary>
        public int ResetAll()
        {
            var refunded = SpentPoints;
            if (refunded <= 0)
                return 0;

            _ranks.Clear();
            Changed?.Invoke();
            return refunded;
        }

        /// <summary>Adds earned points. The real source of these is still undecided in Spirits.md.</summary>
        public void GrantPoints(int amount)
        {
            if (amount <= 0)
                return;

            TotalPoints += amount;
            Changed?.Invoke();
        }

        /// <summary>Rebuilds the total from save data, keeping spent ranks where they are.</summary>
        public void SetTotalPoints(int totalPoints)
        {
            var clamped = Math.Max(0, totalPoints);
            if (clamped == TotalPoints)
                return;

            TotalPoints = clamped;
            Changed?.Invoke();
        }

        /// <summary>
        /// Sets a rank directly, for loading a save. Validation is deliberately skipped: a saved rank was
        /// legal when it was written, and re-validating it against a tree that has since been rebalanced
        /// would silently delete the player's progress.
        /// </summary>
        public bool RestoreRank(string talentId, int rank)
        {
            var talent = Find(talentId);
            if (talent == null || rank <= 0)
                return false;

            var clamped = Math.Min(rank, talent.MaxRank);
            if (clamped == RankOf(talentId))
                return false;

            _ranks[talentId] = clamped;
            return true;
        }

        /// <summary>Every talent with at least one rank, for saving.</summary>
        public IEnumerable<KeyValuePair<string, int>> SpentRanks => _ranks;

        /// <summary>
        /// The boon ids currently active at their purchased rank. Nothing consumes these yet - this is the
        /// seam where the Spirits' actual effects will plug in once Spirits.md settles what an upgrade does.
        /// </summary>
        public IEnumerable<string> ActiveBoonIds
        {
            get
            {
                foreach (var pair in _ranks)
                {
                    var talent = Find(pair.Key);
                    if (talent == null || talent.BoonId == SpiritTalent.NoBoon)
                        continue;

                    yield return talent.BoonId;
                }
            }
        }

        private SpiritTalent Find(string talentId)
        {
            if (string.IsNullOrEmpty(talentId))
                return null;

            foreach (var tree in Trees)
            {
                var talent = tree.Find(talentId);
                if (talent != null)
                    return talent;
            }

            return null;
        }
    }
}
