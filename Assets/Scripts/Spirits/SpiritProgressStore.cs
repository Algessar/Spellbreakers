using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Spellbreakers.Spirits
{
    /// <summary>
    /// Saves and loads the player's spirit progression as a small JSON file under the persistent data path.
    ///
    /// Kept out of <see cref="SpiritProgression"/> on purpose: the rules are engine-free and unit-tested,
    /// while this has to touch the disk. Loading is forgiving - a missing, empty or corrupt file leaves a
    /// fresh progression rather than throwing during startup - because a save file is the last thing that
    /// should be able to stop the game from running.
    /// </summary>
    public static class SpiritProgressStore
    {
        private const string FileName = "spirits.json";

        [Serializable]
        private sealed class SaveData
        {
            public int totalPoints;

            // Parallel lists rather than a dictionary: Unity's JsonUtility cannot serialise a dictionary
            // without a wrapper type per entry, and the pairing is trivial to keep intact.
            public List<string> talentIds = new List<string>();
            public List<int> ranks = new List<int>();
        }

        public static string FilePath => Path.Combine(Application.persistentDataPath, FileName);

        public static bool Exists()
        {
            return File.Exists(FilePath);
        }

        /// <summary>Writes the progression. Returns false when the write failed; the game keeps running.</summary>
        public static bool Save(SpiritProgression progression)
        {
            if (progression == null)
                return false;

            var data = new SaveData { totalPoints = progression.TotalPoints };

            foreach (var pair in progression.SpentRanks)
            {
                if (pair.Value <= 0)
                    continue;

                data.talentIds.Add(pair.Key);
                data.ranks.Add(pair.Value);
            }

            try
            {
                var directory = Path.GetDirectoryName(FilePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                    Directory.CreateDirectory(directory);

                File.WriteAllText(FilePath, JsonUtility.ToJson(data, true));
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogError($"[Spirits] Could not save the spirit tree to '{FilePath}': {exception.Message}");
                return false;
            }
        }

        /// <summary>
        /// Fills <paramref name="progression"/> from disk. Returns false when there was nothing usable, in
        /// which case the progression is left exactly as it was.
        /// </summary>
        public static bool TryLoad(SpiritProgression progression)
        {
            if (progression == null || !File.Exists(FilePath))
                return false;

            SaveData data;
            try
            {
                var json = File.ReadAllText(FilePath);
                if (string.IsNullOrWhiteSpace(json))
                    return false;

                data = JsonUtility.FromJson<SaveData>(json);
            }
            catch (Exception exception)
            {
                Debug.LogError($"[Spirits] Could not read '{FilePath}': {exception.Message}");
                return false;
            }

            if (data == null)
                return false;

            progression.SetTotalPoints(data.totalPoints);

            if (data.talentIds == null || data.ranks == null)
                return true;

            var count = Mathf.Min(data.talentIds.Count, data.ranks.Count);
            var restored = 0;

            for (var i = 0; i < count; i++)
            {
                // RestoreRank ignores ids that no longer exist, so a talent removed in a later pass simply
                // drops out instead of corrupting the whole load.
                if (progression.RestoreRank(data.talentIds[i], data.ranks[i]))
                    restored++;
            }

            // A save that referenced nothing this build understands is not worth trusting.
            if (restored == 0 && data.talentIds.Count > 0 && progression.TotalPoints > 0)
            {
                Debug.LogWarning($"[Spirits] '{FilePath}' has no talents this build recognises; treating it as empty.");
                progression.ResetAll();
                progression.SetTotalPoints(0);
                return false;
            }

            return true;
        }

        /// <summary>Deletes the save. Used by the debug reset, and by tests.</summary>
        public static bool Delete()
        {
            try
            {
                if (!File.Exists(FilePath))
                    return true;

                File.Delete(FilePath);
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogError($"[Spirits] Could not delete '{FilePath}': {exception.Message}");
                return false;
            }
        }
    }
}
