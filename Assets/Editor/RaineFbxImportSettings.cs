using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Spellbreakers.EditorTools
{
    /// <summary>
    /// Puts the project's import conventions onto every FBX whose file name contains "Raine", so the rig
    /// cannot drift into a different avatar or a different root-motion mode just because someone dragged a
    /// new export in by hand.
    ///
    /// Three things are enforced:
    ///
    ///  * <b>Avatar.</b> Every Raine model is Humanoid and copies the canonical avatar that lives on
    ///    <see cref="AvatarSourcePath"/>. That model itself is the exception: it keeps creating its own
    ///    avatar, because it is the one everybody else copies from.
    ///
    ///  * <b>Root motion.</b> Guard animations (the "G_" family: GUARD_G_Alber-Right, NEW_G_Pflug,
    ///    B_G_Ochs ...) bake every axis, so they play perfectly in place and contribute no delta.
    ///    Correction animations ("Corr-") bake rotation and Y but leave root position XZ free, because the
    ///    correction is what actually slides the fighter into the new measure. Transition poses follow the
    ///    corrections: they bake rotation and Y and leave XZ alone.
    ///
    ///  * <b>"Based Upon".</b> Original on every axis for all of the above. The root node of these exports
    ///    carries the Blender rest offset, so measuring against it would tilt and drag every clip.
    ///
    /// Clips matching none of those families (idles, walks, base poses, the Langort passes) are left exactly
    /// as authored; this tool only owns the three families it names.
    ///
    /// Run via menu: Spellbreakers / FBX Import / Apply Raine Import Settings
    /// New imports are handled automatically by <see cref="RaineFbxImportPostprocessor"/>.
    /// </summary>
    public static class RaineFbxImportSettings
    {
        /// <summary>Prefix for every log line this tool emits, so a build log stays greppable.</summary>
        public const string LogTag = "[RaineFbx]";

        /// <summary>The model that owns the canonical Avatar every other Raine model copies.</summary>
        public const string AvatarSourcePath = "Assets/Animation/HumanoidAvatar/Raine_Humanoid.fbx";

        /// <summary>Fragment a model's file name has to contain for this tool to claim it.</summary>
        public const string ModelNameFragment = "Raine";

        /// <summary>
        /// When true, <see cref="RaineFbxImportPostprocessor"/> applies the conventions during import, so a
        /// freshly dropped export is already correct. Turn it off if you want import settings to stay purely
        /// manual; the menu item below works either way.
        ///
        /// Deliberately "static readonly" rather than "const": a compile-time constant would make the guard
        /// in the postprocessor dead code and earn an unreachable-code warning.
        /// </summary>
        public static readonly bool AutoApplyOnImport = true;

        private const string MenuRoot = "Spellbreakers/FBX Import/";

        // ------------------------------------------------------------------ menu

        [MenuItem(MenuRoot + "Apply Raine Import Settings", false, 100)]
        public static void ApplyToAllRaineModels()
        {
            var paths = new List<string>();

            foreach (var guid in AssetDatabase.FindAssets("t:Model"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (IsRaineModel(path))
                    paths.Add(path);
            }

            if (paths.Count == 0)
            {
                Debug.LogWarning($"{LogTag} Found no FBX named like a '{ModelNameFragment}' model; nothing to do.");
                return;
            }

            paths.Sort(StringComparer.OrdinalIgnoreCase);
            ApplyTo(paths);
        }

        [MenuItem(MenuRoot + "Apply Raine Import Settings to Selection", false, 101)]
        public static void ApplyToSelectedRaineModels()
        {
            var paths = new List<string>();

            foreach (var selected in Selection.objects)
            {
                // Selecting the GameObject inside a model hands back a sub-asset, so go through its path
                // rather than assuming the selection is the model itself.
                string path = AssetDatabase.GetAssetPath(selected);
                if (IsRaineModel(path) && !paths.Contains(path))
                    paths.Add(path);
            }

            if (paths.Count == 0)
            {
                Debug.LogWarning($"{LogTag} The selection holds no Raine FBX; nothing to do.");
                return;
            }

            paths.Sort(StringComparer.OrdinalIgnoreCase);
            ApplyTo(paths);
        }

        // --------------------------------------------------------------- applies

        /// <summary>Applies the conventions to a set of model paths, reimporting only what changed.</summary>
        public static void ApplyTo(IReadOnlyList<string> assetPaths)
        {
            var changedPaths = new List<string>();
            var skippedPaths = new List<string>();

            int index = 0;
            try
            {
                foreach (var path in assetPaths)
                {
                    index++;
                    EditorUtility.DisplayProgressBar(
                        "Raine FBX import settings", path, (float)index / Mathf.Max(1, assetPaths.Count));

                    if (!(AssetImporter.GetAtPath(path) is ModelImporter importer))
                    {
                        Debug.LogWarning($"{LogTag} '{path}' has no ModelImporter; skipped.");
                        skippedPaths.Add(path);
                        continue;
                    }

                    if (!Apply(importer, path))
                        continue;

                    // Reimporting these files is expensive (Raine_Humanoid.fbx alone is ~59 MB), so it only
                    // happens when something actually differed.
                    importer.SaveAndReimport();
                    changedPaths.Add(path);
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            Report(assetPaths.Count, changedPaths, skippedPaths);
        }

        /// <summary>
        /// Writes the conventions onto <paramref name="importer"/> in memory. Returns true when something
        /// actually changed, which is what lets a caller skip an otherwise unavoidable reimport.
        /// </summary>
        public static bool Apply(ModelImporter importer, string assetPath)
        {
            if (importer == null || string.IsNullOrEmpty(assetPath))
                return false;

            bool changed = ApplyAvatar(importer, assetPath);

            var clips = importer.clipAnimations;
            if (clips == null || clips.Length == 0)
            {
                // Nothing has been authored per clip yet, so the importer has no clip list to hand back.
                // Defaults are the same takes, and assigning them makes the edits below persist.
                clips = importer.defaultClipAnimations;
            }

            if (clips == null || clips.Length == 0)
                return changed;

            bool clipsChanged = false;
            foreach (var clip in clips)
            {
                if (clip == null)
                    continue;

                var policy = ResolvePolicy(clip);
                if (policy == RootMotionPolicy.Untouched)
                    continue;

                clipsChanged |= ApplyRootMotion(clip, policy);
            }

            if (clipsChanged)
            {
                importer.clipAnimations = clips;
                changed = true;
            }

            return changed;
        }

        // ---------------------------------------------------------------- avatar

        private static bool ApplyAvatar(ModelImporter importer, string assetPath)
        {
            bool changed = false;

            // Unity 6.4 spells the humanoid member "Human" (it was "Humanoid" in older editors).
            if (importer.animationType != ModelImporterAnimationType.Human)
            {
                importer.animationType = ModelImporterAnimationType.Human;
                changed = true;
            }

            // The canonical avatar lives on this model: it has to keep generating its own, otherwise every
            // other Raine model would end up copying from nothing.
            if (SamePath(assetPath, AvatarSourcePath))
            {
                if (importer.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel)
                {
                    importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                    changed = true;
                }

                return changed;
            }

            var avatar = LoadCanonicalAvatar();
            if (avatar == null)
            {
                Debug.LogWarning($"{LogTag} '{assetPath}': no Avatar found on '{AvatarSourcePath}', so the " +
                                 "avatar was not copied. Import that model first, then re-run the tool.");
                return changed;
            }

            if (importer.avatarSetup != ModelImporterAvatarSetup.CopyFromOther)
            {
                importer.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
                changed = true;
            }

            if (importer.sourceAvatar != avatar)
            {
                importer.sourceAvatar = avatar;
                changed = true;
            }

            return changed;
        }

        private static Avatar LoadCanonicalAvatar()
        {
            // AssetDatabase is queried during OnPreprocessModel too, where the source model may not be
            // imported yet. A miss is reported by the caller; never let it throw out of an import callback.
            try
            {
                foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(AvatarSourcePath))
                {
                    if (asset is Avatar avatar && !string.IsNullOrEmpty(avatar.name))
                        return avatar;
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"{LogTag} Reading the canonical avatar from '{AvatarSourcePath}' failed: " +
                                 $"{exception.Message}");
            }

            return null;
        }

        // --------------------------------------------------------- root motion

        private enum RootMotionPolicy
        {
            /// <summary>Not one of the families this tool owns; leave the clip exactly as authored.</summary>
            Untouched,

            /// <summary>Guard clips: nothing may escape into the world transform.</summary>
            NoRootMotion,

            /// <summary>Corrections and transition poses: bake rotation and Y, keep XZ travelling.</summary>
            BakeRotationAndHeight,
        }

        private static RootMotionPolicy ResolvePolicy(ModelImporterClipAnimation clip)
        {
            // Renaming a clip in the importer leaves takeName pointing at the original FBX take, and the
            // project uses both spellings, so either one may carry the family marker.
            var policy = ResolvePolicy(clip.name);
            return policy != RootMotionPolicy.Untouched ? policy : ResolvePolicy(clip.takeName);
        }

        private static RootMotionPolicy ResolvePolicy(string clipName)
        {
            if (string.IsNullOrEmpty(clipName))
                return RootMotionPolicy.Untouched;

            if (clipName.IndexOf("TransitionPose", StringComparison.OrdinalIgnoreCase) >= 0)
                return RootMotionPolicy.BakeRotationAndHeight;

            // "GUARD_Corr-Pflug-Langort-Right" and friends. The underscore variant is accepted so a future
            // rename cannot silently drop a clip out of the family.
            if (clipName.IndexOf("Corr-", StringComparison.OrdinalIgnoreCase) >= 0 ||
                clipName.IndexOf("Corr_", StringComparison.OrdinalIgnoreCase) >= 0)
                return RootMotionPolicy.BakeRotationAndHeight;

            if (HasGuardMarker(clipName))
                return RootMotionPolicy.NoRootMotion;

            return RootMotionPolicy.Untouched;
        }

        /// <summary>
        /// True for the "G_" guard family (GUARD_G_Alber-Right, NEW_G_Pflug, B_G_Ochs). The marker is matched
        /// on a token boundary so that a word merely starting with a capital G cannot be mistaken for one.
        /// </summary>
        private static bool HasGuardMarker(string clipName)
        {
            int cursor = 0;

            while (cursor < clipName.Length - 1)
            {
                int at = clipName.IndexOf("G_", cursor, StringComparison.OrdinalIgnoreCase);
                if (at < 0)
                    return false;

                if (at == 0)
                    return true;

                switch (clipName[at - 1])
                {
                    case '_':
                    case '-':
                    case '.':
                    case ' ':
                        return true;
                }

                cursor = at + 1;
            }

            return false;
        }

        private static bool ApplyRootMotion(ModelImporterClipAnimation clip, RootMotionPolicy policy)
        {
            bool bakeRotation = true;
            bool bakeHeight = true;
            bool bakeXZ = policy == RootMotionPolicy.NoRootMotion;

            bool changed = false;

            if (clip.lockRootRotation != bakeRotation)
            {
                clip.lockRootRotation = bakeRotation;
                changed = true;
            }

            if (clip.lockRootHeightY != bakeHeight)
            {
                clip.lockRootHeightY = bakeHeight;
                changed = true;
            }

            if (clip.lockRootPositionXZ != bakeXZ)
            {
                clip.lockRootPositionXZ = bakeXZ;
                changed = true;
            }

            // "Based Upon" is Original on every axis. heightFromFeet is the only way to say anything else
            // for Y, so it has to be cleared explicitly rather than left at whatever was imported.
            if (!clip.keepOriginalOrientation)
            {
                clip.keepOriginalOrientation = true;
                changed = true;
            }

            if (!clip.keepOriginalPositionY)
            {
                clip.keepOriginalPositionY = true;
                changed = true;
            }

            if (!clip.keepOriginalPositionXZ)
            {
                clip.keepOriginalPositionXZ = true;
                changed = true;
            }

            if (clip.heightFromFeet)
            {
                clip.heightFromFeet = false;
                changed = true;
            }

            return changed;
        }

        // ----------------------------------------------------------------- misc

        /// <summary>True when <paramref name="assetPath"/> is an FBX this tool should claim.</summary>
        public static bool IsRaineModel(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
                return false;

            if (!assetPath.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase))
                return false;

            string fileName = Path.GetFileNameWithoutExtension(assetPath);
            return fileName.IndexOf(ModelNameFragment, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static bool SamePath(string left, string right)
        {
            return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }

        private static void Report(int total, List<string> changedPaths, List<string> skippedPaths)
        {
            if (changedPaths.Count == 0)
            {
                Debug.Log($"{LogTag} Checked {total} Raine FBX; all already match the import conventions.");
                return;
            }

            var builder = new StringBuilder();
            builder.Append($"{LogTag} Updated {changedPaths.Count} of {total} Raine FBX:");

            foreach (var path in changedPaths)
                builder.Append("\n  ").Append(path);

            if (skippedPaths.Count > 0)
            {
                builder.Append($"\nSkipped {skippedPaths.Count}:");
                foreach (var path in skippedPaths)
                    builder.Append("\n  ").Append(path);
            }

            Debug.Log(builder.ToString());
        }
    }

    /// <summary>
    /// Keeps freshly imported Raine models correct without anyone having to remember the menu item.
    ///
    /// This runs in two stages on purpose. <see cref="OnPreprocessModel"/> writes everything it can before
    /// Unity bakes the clips, so the very first import is already right and no second import is needed for
    /// the clip and animation-type settings.
    ///
    /// The avatar cannot be resolved reliably at that point, though: the model that owns it may not be
    /// imported yet, and AssetDatabase is not dependable from inside another asset's import. That is not a
    /// hypothetical - it left three models Humanoid with no copied avatar. <see cref="OnPostprocessAllAssets"/>
    /// therefore re-runs the same pass once the whole batch has settled, when the canonical avatar is
    /// definitely loadable, and reimports only what is still wrong.
    /// </summary>
    internal sealed class RaineFbxImportPostprocessor : AssetPostprocessor
    {
        private static bool _finishing;

        private void OnPreprocessModel()
        {
            if (!RaineFbxImportSettings.AutoApplyOnImport)
                return;

            if (!(assetImporter is ModelImporter importer))
                return;

            if (!RaineFbxImportSettings.IsRaineModel(assetPath))
                return;

            RaineFbxImportSettings.Apply(importer, assetPath);
        }

        /// <summary>
        /// Finishes anything the preprocess pass could not: now that the batch has settled, the canonical
        /// avatar is loadable and the copy can actually be made.
        ///
        /// This converges by construction - <see cref="RaineFbxImportSettings.Apply"/> reports a change only
        /// when something genuinely differs, so the reimport it triggers finds nothing left to do on the
        /// next pass and the chain stops.
        /// </summary>
        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            if (!RaineFbxImportSettings.AutoApplyOnImport || _finishing)
                return;

            List<string> pending = null;
            var avatarMissing = false;

            foreach (var path in importedAssets)
            {
                if (!RaineFbxImportSettings.IsRaineModel(path))
                    continue;

                if (!(AssetImporter.GetAtPath(path) is ModelImporter importer))
                    continue;

                if (!RaineFbxImportSettings.Apply(importer, path))
                    continue;

                // A model that is not the avatar source but still has no avatar means the canonical one is
                // genuinely unreadable. Report that once instead of retrying forever.
                if (importer.sourceAvatar == null &&
                    !RaineFbxImportSettings.SamePath(path, RaineFbxImportSettings.AvatarSourcePath))
                    avatarMissing = true;

                (pending ?? (pending = new List<string>())).Add(path);
            }

            if (pending == null)
                return;

            if (avatarMissing)
            {
                Debug.LogWarning($"{RaineFbxImportSettings.LogTag} '{RaineFbxImportSettings.AvatarSourcePath}' " +
                                 "exposes no Avatar, so copies could not be made. Import that model, then run " +
                                 "Spellbreakers / FBX Import / Apply Raine Import Settings.");
            }

            _finishing = true;
            try
            {
                foreach (var path in pending)
                {
                    if (AssetImporter.GetAtPath(path) is ModelImporter importer)
                        importer.SaveAndReimport();
                }
            }
            finally
            {
                _finishing = false;
            }
        }
    }
}
