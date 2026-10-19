using System.Collections.Generic;
using UnityEngine;

namespace Spellbreakers.Combat
{
    /// <summary>
    /// Finds combatants without searching the scene.
    ///
    /// This replaces the per-frame <c>FindObjectsByType&lt;Damageable&gt;</c> and
    /// <c>GetComponentsInChildren&lt;Collider&gt;</c> calls the previous targeting code made, which were
    /// the most expensive thing in the combat loop and got worse the larger a level became.
    /// </summary>
    public static class CombatantRegistry
    {
        /// <summary>Every live combatant hostile to <paramref name="selfTeam"/>, within range.</summary>
        public static int CollectHostiles(
            Vector3 origin,
            CombatTeam selfTeam,
            float maxDistance,
            List<Damageable> results)
        {
            if (results == null)
                return 0;

            results.Clear();
            var maxSqr = maxDistance * maxDistance;

            foreach (var candidate in Damageable.All)
            {
                if (candidate == null || !candidate.IsAlive)
                    continue;

                if (!CombatTeams.AreHostile(selfTeam, candidate.Team))
                    continue;

                if ((candidate.AimPosition - origin).sqrMagnitude > maxSqr)
                    continue;

                results.Add(candidate);
            }

            return results.Count;
        }

        /// <summary>
        /// The best hostile to engage: nearest, with a bias towards whatever is roughly ahead so an
        /// enemy does not turn its back on the player to chase something behind it.
        /// </summary>
        public static Damageable FindBestHostile(
            Vector3 origin,
            Vector3 forward,
            CombatTeam selfTeam,
            float maxDistance,
            float maxAngleDegrees,
            List<Damageable> buffer)
        {
            if (CollectHostiles(origin, selfTeam, maxDistance, buffer) == 0)
                return null;

            Damageable best = null;
            var bestScore = float.PositiveInfinity;
            var hasForward = forward.sqrMagnitude > 0.0001f;

            for (var i = 0; i < buffer.Count; i++)
            {
                var candidate = buffer[i];
                var offset = candidate.AimPosition - origin;
                var distance = offset.magnitude;

                if (distance <= 0.01f)
                    continue;

                if (hasForward)
                {
                    var angle = Vector3.Angle(forward, offset);
                    if (angle > maxAngleDegrees * 0.5f)
                        continue;
                }

                var score = distance / Mathf.Max(0.01f, maxDistance);
                if (score < bestScore)
                {
                    bestScore = score;
                    best = candidate;
                }
            }

            // Nothing in front: fall back to the nearest hostile rather than standing around.
            if (best != null)
                return best;

            var nearest = float.PositiveInfinity;
            for (var i = 0; i < buffer.Count; i++)
            {
                var distance = Vector3.Distance(origin, buffer[i].AimPosition);
                if (distance >= nearest)
                    continue;

                nearest = distance;
                best = buffer[i];
            }

            return best;
        }

        /// <summary>The combat handler that owns a damageable, if any. Cached lookups live in callers.</summary>
        public static T FindOwnerComponent<T>(Damageable damageable) where T : Component
        {
            if (damageable == null)
                return null;

            var direct = damageable.GetComponent<T>();
            if (direct != null)
                return direct;

            var root = damageable.transform.root;
            return root.GetComponentInChildren<T>(true);
        }
    }
}
