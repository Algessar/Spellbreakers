using UnityEngine;
using UnityEngine.InputSystem;

namespace Spellbreakers.Spirits
{
    /// <summary>
    /// Owns the player's spirit progression, loads and saves it, and opens the tree window.
    ///
    /// It bootstraps itself on play so the tree is available in every scene without a prefab or a scene
    /// edit, which matters while the system is still a first pass. The progression rules live in
    /// <see cref="SpiritProgression"/> and the window in <see cref="SpiritTreeUI"/>; this is only the
    /// wiring between them, the save file and the Spirits input.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SpiritTreeController : MonoBehaviour
    {
        [Header("Points")]
        [Tooltip("Points granted the first time the game runs with no save file. Where points really come " +
                 "from is still undecided in Spirits.md, so this is the stand-in.")]
        [SerializeField, Min(0)] private int _startingPoints = 5;

        [Header("Persistence")]
        [Tooltip("Save and load the tree between sessions. Off for a throwaway test.")]
        [SerializeField] private bool _persist = true;

        [Header("Debug")]
        [SerializeField] private bool _logProgression;

        private SpiritProgression _progression;
        private SpiritTreeUI _ui;
        private Actions _actions;
        private bool _previousCursorVisible;
        private CursorLockMode _previousCursorLock;

        /// <summary>The single controller in play, or null before it bootstraps.</summary>
        public static SpiritTreeController Instance { get; private set; }

        /// <summary>The live progression, so other systems can read spent ranks and active boons.</summary>
        public SpiritProgression Progression => _progression;

        public bool IsOpen => _ui != null && _ui.IsOpen;

        /// <summary>
        /// Creates the controller in whatever scene starts. A static flag rather than a singleton singleton
        /// pattern: entering play mode twice must still work, and <see cref="Instance"/> is cleared on
        /// destroy.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (Instance != null)
                return;

            var go = new GameObject("Spirit Tree");
            DontDestroyOnLoad(go);
            go.AddComponent<SpiritTreeController>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;

            _progression = new SpiritProgression(SpiritCatalog.Trees);
            LoadOrSeed();
            BuildWindow();

            if (_logProgression)
                Debug.Log($"[Spirits] {_progression.TotalPoints} total, {_progression.SpentPoints} spent.", this);
        }

        private void OnEnable()
        {
            if (_actions != null)
                return;

            _actions = new Actions();
            _actions.Player.Spirits.started += OnSpiritsPressed;
            _actions.Player.Enable();
        }

        private void OnDisable()
        {
            if (_actions == null)
                return;

            _actions.Player.Spirits.started -= OnSpiritsPressed;
            _actions.Disable();
        }

        private void OnDestroy()
        {
            if (_actions != null)
            {
                _actions.Dispose();
                _actions = null;
            }

            if (Instance == this)
                Instance = null;
        }

        private void Update()
        {
            // Escape closes the window, as in the reference UI, without stealing the Spirits button.
            if (_ui != null && _ui.IsOpen)
            {
                var keyboard = Keyboard.current;
                if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
                    Close();
            }
        }

        /// <summary>Adds earned points and saves. The eventual home of this call is whatever rewards them.</summary>
        public void GrantPoints(int amount)
        {
            if (_progression == null || amount <= 0)
                return;

            _progression.GrantPoints(amount);
            Save();
        }

        /// <summary>Wipes every rank and the save file. Bound to nothing in the UI yet, on purpose.</summary>
        public void ResetAllAndDeleteSave()
        {
            _progression?.ResetAll();
            _progression?.SetTotalPoints(0);
            SpiritProgressStore.Delete();
        }

        public void Open()
        {
            if (_ui == null)
                return;

            CaptureCursor();
            _ui.Show();
        }

        public void Close()
        {
            if (_ui == null)
                return;

            _ui.Hide();
            RestoreCursor();
        }

        private void OnSpiritsPressed(InputAction.CallbackContext context)
        {
            if (!context.started)
                return;

            if (_ui == null)
                return;

            if (_ui.IsOpen)
                Close();
            else
                Open();
        }

        private void LoadOrSeed()
        {
            if (_persist && SpiritProgressStore.TryLoad(_progression))
                return;

            // No save yet: this is a fresh run, so seed the starting budget and write it down.
            _progression.GrantPoints(_startingPoints);

            if (_persist)
                SpiritProgressStore.Save(_progression);
        }

        private void BuildWindow()
        {
            _ui = gameObject.AddComponent<SpiritTreeUI>();
            _ui.PersistenceEnabled = _persist;
            _ui.Build(_progression);
        }

        private void Save()
        {
            if (_persist)
                SpiritProgressStore.Save(_progression);
        }

        private void CaptureCursor()
        {
            _previousCursorVisible = Cursor.visible;
            _previousCursorLock = Cursor.lockState;

            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
        }

        private void RestoreCursor()
        {
            Cursor.visible = _previousCursorVisible;
            Cursor.lockState = _previousCursorLock;
        }
    }
}
