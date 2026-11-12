using System;
using System.Collections;
using Spellbreakers.World;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Spellbreakers.Gameplay
{
    /// <summary>
    /// Moves the game between the scenes named in <see cref="SceneCatalog"/>.
    ///
    /// This replaces <c>SceneManager.LoadScene(buildIndex)</c>, which is what the teleporters used and
    /// what made a scene's position in the build settings silently decide where a door went. Scene ids are
    /// stable strings, so reordering the build list no longer changes anything, and a save can record
    /// where the player was without recording a number that will drift.
    ///
    /// A failed load is reported, not thrown. The three ways to get here with a bad target - a typo'd id, a
    /// scene left out of the build settings, and a travel requested mid-travel - all end in a log line that
    /// names the fix, because a half-loaded game is worse than a refused one.
    /// </summary>
    [DefaultExecutionOrder(-800)]
    [DisallowMultipleComponent]
    public sealed class SceneFlow : MonoBehaviour
    {
        /// <summary>The flow in play, or null before <see cref="GameRoot"/> has built one.</summary>
        public static SceneFlow Instance { get; private set; }

        [Header("Diagnostics")]
        [Tooltip("Log each load and each refusal. Refusals are always logged regardless of this.")]
        [SerializeField] private bool _logLoads = true;

        /// <summary>True from the moment a travel is accepted until the destination has finished loading.</summary>
        public bool IsLoading { get; private set; }

        /// <summary>The catalog entry for the scene currently loaded, or null when it is not catalogued.</summary>
        public SceneEntry CurrentScene { get; private set; }

        /// <summary>The current scene's id, or empty when the active scene is not in the catalog.</summary>
        public string CurrentSceneId => CurrentScene?.Id ?? string.Empty;

        /// <summary>Raised when a travel is accepted, before the load starts. The hook a fade hangs on.</summary>
        public event Action<SceneEntry> LoadStarted;

        /// <summary>Raised when the destination is loaded and the game has returned to play.</summary>
        public event Action<SceneEntry> LoadCompleted;

        /// <summary>Raised with the target entry (possibly null) and a reason when a travel is refused.</summary>
        public event Action<SceneEntry, string> LoadFailed;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning($"[SceneFlow] '{name}' is a second SceneFlow; '{Instance.name}' already owns " +
                                 "scene travel, so this one is removed.", this);
                Destroy(gameObject);
                return;
            }

            Instance = this;
        }

        private void Start()
        {
            // The first scene already exists by the time this runs; later ones are tracked on arrival.
            ResolveCurrentScene();
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        /// <summary>
        /// Travels to the scene registered under <paramref name="sceneId"/>. Returns false when the request
        /// was refused; in that case the game is left exactly where it was, still playing.
        /// </summary>
        public bool Travel(string sceneId)
        {
            if (IsLoading)
            {
                Refuse(null, $"a travel to '{sceneId}' was requested while already travelling");
                return false;
            }

            if (!SceneCatalog.Active.TryGet(sceneId, out var entry))
            {
                Refuse(null, $"no scene is registered under id '{sceneId}'");
                return false;
            }

            if (!CanTravelTo(entry, out var reason))
            {
                Refuse(entry, reason);
                return false;
            }

            StartCoroutine(TravelRoutine(entry));
            return true;
        }

        /// <summary>
        /// Whether the destination can actually be loaded, with a reason when it cannot. Public so an
        /// interaction prompt can grey itself out instead of offering a door that will refuse.
        /// </summary>
        public bool CanTravelTo(string sceneId, out string reason)
        {
            if (!SceneCatalog.Active.TryGet(sceneId, out var entry))
            {
                reason = $"no scene is registered under id '{sceneId}'";
                return false;
            }

            return CanTravelTo(entry, out reason);
        }

        private bool CanTravelTo(SceneEntry entry, out string reason)
        {
            // The single most likely misconfiguration in this project right now: a scene that exists but
            // was never added to the build settings, which fails at load time with a far less useful error.
            if (!Application.CanStreamedLevelBeLoaded(entry.SceneName))
            {
                reason = $"'{entry.SceneName}' is not in the build settings (or is disabled there); " +
                         "add it in File > Build Profiles > Scene List";
                return false;
            }

            reason = null;
            return true;
        }

        private IEnumerator TravelRoutine(SceneEntry entry)
        {
            IsLoading = true;

            var state = GameStateService.Instance;
            state?.BeginTransition();

            if (_logLoads)
                Debug.Log($"[SceneFlow] Loading '{entry.Id}' ({entry.SceneName}).", this);

            LoadStarted?.Invoke(entry);

            AsyncOperation operation;
            try
            {
                operation = SceneManager.LoadSceneAsync(entry.SceneName, LoadSceneMode.Single);
            }
            catch (Exception exception)
            {
                IsLoading = false;
                state?.EndTransition();
                Refuse(entry, $"the engine could not start loading '{entry.SceneName}': {exception.Message}");
                yield break;
            }

            if (operation == null)
            {
                IsLoading = false;
                state?.EndTransition();
                Refuse(entry, $"the engine returned no load operation for '{entry.SceneName}'");
                yield break;
            }

            while (!operation.isDone)
                yield return null;

            CurrentScene = entry;
            IsLoading = false;

            state?.EndTransition();

            if (_logLoads)
                Debug.Log($"[SceneFlow] Arrived in '{entry.Id}'.", this);

            LoadCompleted?.Invoke(entry);
        }

        /// <summary>Matches the active scene against the catalog, so the current id survives a load.</summary>
        private void ResolveCurrentScene()
        {
            var activeName = SceneManager.GetActiveScene().name;

            foreach (var entry in SceneCatalog.Active.Entries)
            {
                if (string.Equals(entry.SceneName, activeName, StringComparison.Ordinal))
                {
                    CurrentScene = entry;
                    return;
                }
            }

            // A development scene the catalog does not list. Not an error: the fencing lab and the scale
            // tests are meant to be played directly from the editor.
            CurrentScene = null;

            if (_logLoads)
                Debug.Log($"[SceneFlow] Active scene '{activeName}' is not in the catalog; no id is recorded " +
                          "for it, so it cannot be saved into.", this);
        }

        private void Refuse(SceneEntry entry, string reason)
        {
            Debug.LogError($"[SceneFlow] Travel refused: {reason}.", this);
            LoadFailed?.Invoke(entry, reason);
        }
    }
}
