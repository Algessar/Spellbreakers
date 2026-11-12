using System;
using System.Collections.Generic;

namespace Spellbreakers.World
{
    /// <summary>
    /// The stable names scenes are referred to by in code, in saves and in inspector fields.
    ///
    /// Save data records one of these strings, so they must never change once a player's save could
    /// contain them. They are also what a designer picks in the inspector instead of a build index,
    /// which is what the teleporters used until now: an index moves whenever a scene is added, removed
    /// or reordered, and a save made before the move then resumes in the wrong place.
    /// </summary>
    public static class SceneIds
    {
        /// <summary>The bootstrap scene. Authored in the vertical-slice pass; nothing loads it yet.</summary>
        public const string Boot = "boot";

        /// <summary>The village hub. The only scene with authored content today.</summary>
        public const string HomeBase = "home-base";

        /// <summary>The combat sandbox. Development scene, but reachable so a tester can jump to it.</summary>
        public const string FencingLab = "fencing-lab";

        /// <summary>Open stub, pending content.</summary>
        public const string TempleValley = "temple-valley";

        /// <summary>Open stub, pending content.</summary>
        public const string WindsweptMountains = "windswept-mountains";
    }

    /// <summary>
    /// One addressable scene: a stable <see cref="Id"/>, the actual scene name Unity loads, and an
    /// optional label for a UI that has to show the player where they are going.
    ///
    /// <see cref="SceneName"/> is intentionally separate from <see cref="Id"/>. The id is for saves and
    /// is frozen; the scene name is a file and is allowed to be renamed with the editor's own scene
    /// renaming, which rewrites the reference here rather than every save file.
    /// </summary>
    public sealed class SceneEntry
    {
        public SceneEntry(string id, string sceneName, string displayName = null)
        {
            Id = id?.Trim() ?? string.Empty;
            SceneName = sceneName?.Trim() ?? string.Empty;
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? SceneName : displayName.Trim();
        }

        /// <summary>Stable save/inspector key. Never derived from the scene name.</summary>
        public string Id { get; }

        /// <summary>The scene file name Unity loads, without the extension.</summary>
        public string SceneName { get; }

        /// <summary>What a menu may show. Falls back to the scene name when no label was authored.</summary>
        public string DisplayName { get; }

        public bool IsValid => Id.Length > 0 && SceneName.Length > 0;

        public override string ToString() => $"{Id} -> {SceneName}";
    }

    /// <summary>
    /// The scenes the game is allowed to travel to, authored in code the same way
    /// <c>SpiritCatalog</c> authors its trees: the system runs with no assets to wire, and
    /// <see cref="Override"/> replaces the whole thing for a test.
    ///
    /// Validation is split by what can actually be checked at each layer. This catalog can check its own
    /// shape - duplicate ids, empty names - and nothing else. Whether the named scene really exists and
    /// is in the build settings is a question only the engine can answer, so that check lives in the
    /// editor validator and in <c>SceneFlow</c>, which fails loudly at load time.
    /// </summary>
    public sealed class SceneCatalog
    {
        private static SceneCatalog _override;

        /// <summary>The shipped list of scenes.</summary>
        public static SceneCatalog Default { get; } = new SceneCatalog(DefaultEntries());

        /// <summary>Replaces <see cref="Default"/> wholesale, for tests and for tuning without a rebuild.</summary>
        public static SceneCatalog Override
        {
            get => _override;
            set => _override = value;
        }

        /// <summary>What the game should use: the override if one is installed, otherwise the default.</summary>
        public static SceneCatalog Active => _override ?? Default;

        private readonly List<SceneEntry> _entries = new List<SceneEntry>();
        private readonly Dictionary<string, SceneEntry> _byId = new Dictionary<string, SceneEntry>(StringComparer.Ordinal);
        private readonly List<string> _problems = new List<string>();

        /// <summary>
        /// Indexes the given entries. Malformed or duplicate entries are skipped and recorded rather than
        /// thrown on: a typo in one row should not make the other scenes unreachable, and the reason
        /// belongs in <see cref="Validate"/> where the editor can show it.
        /// </summary>
        public SceneCatalog(IEnumerable<SceneEntry> entries)
        {
            if (entries == null)
            {
                _problems.Add("Scene catalog was built from a null entry list.");
                return;
            }

            var index = 0;
            foreach (var entry in entries)
            {
                if (entry == null)
                {
                    _problems.Add($"Entry {index} is null.");
                    index++;
                    continue;
                }

                if (!entry.IsValid)
                {
                    _problems.Add($"Entry {index} ('{entry}') is missing its id or its scene name.");
                    index++;
                    continue;
                }

                if (_byId.ContainsKey(entry.Id))
                {
                    _problems.Add($"Duplicate scene id '{entry.Id}'; the later entry was ignored.");
                    index++;
                    continue;
                }

                _byId.Add(entry.Id, entry);
                _entries.Add(entry);
                index++;
            }
        }

        /// <summary>The usable entries, in authored order.</summary>
        public IReadOnlyList<SceneEntry> Entries => _entries;

        public int Count => _entries.Count;

        public bool Contains(string id) => !string.IsNullOrEmpty(id) && _byId.ContainsKey(id);

        public bool TryGet(string id, out SceneEntry entry)
        {
            entry = null;
            return !string.IsNullOrEmpty(id) && _byId.TryGetValue(id, out entry);
        }

        /// <summary>
        /// The entry for <paramref name="id"/>, or an exception that names what was asked for and what
        /// exists. A missing scene here is a programming error, not player input, so it fails immediately
        /// instead of returning null into a load call that would fail far less legibly.
        /// </summary>
        public SceneEntry Get(string id)
        {
            if (TryGet(id, out var entry))
                return entry;

            throw new InvalidOperationException(
                $"No scene is registered under id '{id}'. Known ids: {string.Join(", ", KnownIds())}.");
        }

        /// <summary>Everything wrong with the catalog's own shape. Empty means it is internally sound.</summary>
        public IReadOnlyList<string> Validate() => _problems;

        /// <summary>
        /// The scenes that exist and can be travelled to today.
        ///
        /// <see cref="SceneIds.Boot"/> is deliberately absent: the constant is forward-declared for saves
        /// and for the eventual start-up path, but the scene itself has not been authored yet. Listing a
        /// scene file that does not exist would put a row in front of a designer that can only fail at
        /// load time, so the entry is added together with the scene.
        /// </summary>
        public static List<SceneEntry> DefaultEntries()
        {
            return new List<SceneEntry>
            {
                new SceneEntry(SceneIds.HomeBase, "SCENE_HomeBase", "Home"),
                new SceneEntry(SceneIds.FencingLab, "SCENE_FencingLab", "Fencing Lab"),
                new SceneEntry(SceneIds.TempleValley, "SCENE_TempleValley", "Temple Valley"),
                new SceneEntry(SceneIds.WindsweptMountains, "SCENE_WindsweptMountains", "Windswept Mountains"),
            };
        }

        private IEnumerable<string> KnownIds() =>
            _entries.Count == 0 ? new[] { "(none)" } : _entries.ConvertAll(e => e.Id);
    }
}
