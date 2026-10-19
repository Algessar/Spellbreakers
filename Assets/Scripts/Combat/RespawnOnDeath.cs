using Player;
using UnityEngine;
using UnityEngine.AI;

namespace Spellbreakers.Combat
{
    /// <summary>
    /// Puts a character back on its feet after it dies.
    ///
    /// Death is a state here, not the end of the object: nothing is destroyed or reinstantiated, so
    /// coming back is health plus combat state plus a move to the spawn pose. The delay exists so the
    /// kill is legible before the character stands up again.
    ///
    /// The same component serves the player and every enemy, because <see cref="Damageable"/> is what
    /// reports the death for both.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RespawnOnDeath : MonoBehaviour
    {
        [Tooltip("Seconds the character stays down before it is reset.")]
        [SerializeField, Min(0f)] private float _respawnDelay = 2.5f;

        [Tooltip("Optional marker. When unset, the character returns to the pose it spawned at.")]
        [SerializeField] private Transform _respawnPoint;

        private Damageable _damageable;
        private CombatHandler _combat;
        private Movement _movement;
        private NavMeshAgent _navMeshAgent;

        private Vector3 _spawnPosition;
        private Quaternion _spawnRotation;
        private float _respawnAt = -1f;

        /// <summary>True while the character is down and waiting to be reset.</summary>
        public bool IsAwaitingRespawn => _respawnAt >= 0f;

        private void Awake()
        {
            _damageable = GetComponent<Damageable>() ?? GetComponentInChildren<Damageable>(true);
            _combat = GetComponent<CombatHandler>() ?? GetComponentInChildren<CombatHandler>(true);
            _movement = GetComponent<Movement>() ?? GetComponentInChildren<Movement>(true);
            _navMeshAgent = GetComponent<NavMeshAgent>() ?? GetComponentInChildren<NavMeshAgent>(true);

            // Captured once, so later movement cannot drag the spawn pose along with it.
            _spawnPosition = transform.position;
            _spawnRotation = transform.rotation;
        }

        private void OnEnable()
        {
            if (_damageable != null)
                _damageable.Died += OnDied;
        }

        private void OnDisable()
        {
            if (_damageable != null)
                _damageable.Died -= OnDied;
        }

        private void OnDied(Damageable damageable)
        {
            _respawnAt = Time.time + _respawnDelay;
        }

        private void Update()
        {
            if (_respawnAt < 0f || Time.time < _respawnAt)
                return;

            _respawnAt = -1f;
            Respawn();
        }

        /// <summary>Resets the character to full health in its spawn pose. Public so tooling can force it.</summary>
        public void Respawn()
        {
            var position = _respawnPoint != null ? _respawnPoint.position : _spawnPosition;
            var rotation = _respawnPoint != null ? _respawnPoint.rotation : _spawnRotation;

            // Drop whatever steering the character died holding, or it resumes walking to a stale target.
            _movement?.ClearExternalIntent();

            var agentOnMesh = _navMeshAgent != null && _navMeshAgent.enabled && _navMeshAgent.isOnNavMesh;

            if (agentOnMesh)
            {
                _navMeshAgent.Warp(position);
                _navMeshAgent.ResetPath();
            }
            else
            {
                transform.SetPositionAndRotation(position, rotation);
            }

            transform.rotation = rotation;

            // Combat state first, then health: the agent refuses to leave the Dead reaction for any
            // other reason, and Damageable.IsAlive is what the rest of the game reads afterwards.
            _combat?.Revive();
            _damageable?.ResetHealth();
        }
    }
}
