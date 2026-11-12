using Spellbreakers.Combat;
using Spellbreakers.Spirits;
using Spellbreakers.World;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Spellbreakers.Gameplay
{
    /// <summary>
    /// The one object that outlives a scene, and the one place services are built.
    ///
    /// Before this existed, each service installed itself: the spirit window and the combat sound manager
    /// both used <c>RuntimeInitializeOnLoadMethod</c>, neither knew about the other, and the order they
    /// happened in was whatever the runtime chose. Two of them is survivable. The HUD, the save service,
    /// the pause menu, the fade and the checkpoint system would make eight, and the first symptom of a
    /// wrong order is a null reference in an unrelated system several frames later.
    ///
    /// Construction order is therefore explicit and lives here:
    ///
    ///   1. <see cref="GameStateService"/> - everything else asks it whether input counts.
    ///   2. <see cref="SceneFlow"/> - needs the state service to bracket a load.
    ///   3. Scene-scoped services, re-ensured on every arrival.
    ///
    /// The save service, the world flags and the UI root join this list in their own passes, after the
    /// state service because they will want to pause and the scale to report through it.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    [DisallowMultipleComponent]
    public sealed class GameRoot : MonoBehaviour
    {
        /// <summary>The root in play, or null before boot.</summary>
        public static GameRoot Instance { get; private set; }

        [Header("Diagnostics")]
        [Tooltip("Log the services that were built, and the scene each arrival brings.")]
        [SerializeField] private bool _logBoot = true;

        /// <summary>The single owner of <see cref="GameState"/>.</summary>
        public GameStateService State { get; private set; }

        /// <summary>The single owner of scene travel.</summary>
        public SceneFlow Flow { get; private set; }

        /// <summary>
        /// Runs before the first scene is loaded, so the services exist before any scene object wakes up.
        /// That ordering is the point: a component that finds no state service in its Awake is now a bug in
        /// that component, not an unavoidable race.
        ///
        /// The bootstrap scene in the vertical-slice plan will hold this object rather than relying on the
        /// hook, but the hook stays so that a developer pressing Play in any existing scene still gets a
        /// working game. It is idempotent, so both paths cannot produce two roots.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            if (Instance != null)
                return;

            var host = new GameObject("Game Root");
            host.AddComponent<GameRoot>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning($"[GameRoot] '{name}' is a second root; '{Instance.name}' already owns the " +
                                 "services, so this one is removed.", this);
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);

            BuildServices();

            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        /// <summary>
        /// Covers the first scene. Whether <c>sceneLoaded</c> also fires for it has varied across Unity
        /// versions, and both entry points are idempotent, so this does not have to be decided.
        /// </summary>
        private void Start()
        {
            EnsureSceneScopedServices();
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;

            if (Instance == this)
                Instance = null;
        }

        private void BuildServices()
        {
            State = AddService<GameStateService>("Game State");
            Flow = AddService<SceneFlow>("Scene Flow");

            // A play session starts in play until the title screen is authored. Boot is a state the game
            // defines itself out of on the first frame, not one it should ever sit in.
            State.ResetTo(GameState.Playing);

            if (_logBoot)
                Debug.Log("[GameRoot] Built: GameStateService, SceneFlow.", this);
        }

        /// <summary>
        /// Creates a child object for a service. Each service gets its own object so that its duplicate
        /// guard can remove itself without taking the root - and every other service - with it.
        /// </summary>
        private T AddService<T>(string serviceName) where T : Component
        {
            var host = new GameObject(serviceName);
            host.transform.SetParent(transform, false);
            return host.AddComponent<T>();
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (_logBoot)
                Debug.Log($"[GameRoot] Arrived in '{scene.name}'.", this);

            EnsureSceneScopedServices();
        }

        /// <summary>
        /// Services whose lifetime is the scene, not the session.
        ///
        /// Combat audio is the current example: it is deliberately destroyed with the scene it belongs to
        /// and rebuilt for the next one, so it is re-ensured rather than parented under the root. Both calls
        /// are no-ops once the service exists, which is what makes running them from two places safe.
        /// </summary>
        private void EnsureSceneScopedServices()
        {
            CombatSoundManager.EnsureInstance();
            SpiritTreeController.EnsureInstance();
        }
    }
}
