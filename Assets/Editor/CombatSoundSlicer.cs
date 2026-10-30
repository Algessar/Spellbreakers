using System;
using System.Collections.Generic;
using Spellbreakers.Combat;
using UnityEngine;

namespace Spellbreakers.EditorTools
{
    /// <summary>
    /// Finds the individual sounds inside a compilation file.
    ///
    /// Sound packs are sold as one long file holding thirty hits, and until they are separated there is no
    /// way to use thirteen of them. Re-cutting the file into assets would drag the importer settings, the
    /// licence file and the version history along with it, so instead this measures the decoded samples and
    /// reports where the sounds are - which the profile then stores as <see cref="CombatSoundSlice"/>
    /// windows, leaving the file untouched.
    ///
    /// The detection is deliberately simple and inspectable: peak level per 5 ms frame, gated against the
    /// file's own peak, with silence gaps deciding where one sound ends and the next begins. Nothing here
    /// guesses what a sound *is*, only where it stops being silence.
    /// </summary>
    internal static class CombatSoundSlicer
    {
        /// <summary>Analysis resolution. 5 ms is finer than any gap between two sword sounds.</summary>
        private const float FrameSeconds = 0.005f;

        /// <summary>Columns in the waveform preview sent to the window.</summary>
        private const int EnvelopeColumns = 1100;

        public sealed class Settings
        {
            public float ThresholdDb = -30f;

            /// <summary>
            /// How far below the start gate a sound has to fall before it counts as finished.
            ///
            /// Without this a percussive hit - which is most of what a sword pack contains - is cut at the
            /// moment it passes the start gate, which is only a few milliseconds into its decay, and the
            /// excerpt ends on a click instead of on the sound. The gate starts a sound; this lets it finish.
            /// </summary>
            public float EndHysteresisDb = 15f;

            public float MinGapSeconds = 0.08f;
            public float MinLengthSeconds = 0.04f;
            public float PaddingSeconds = 0.01f;
            public int MaxSegments = 32;
        }

        public struct Segment
        {
            public float Start;
            public float Length;

            /// <summary>Peak level of the segment in dBFS, so one loud hit in a compilation stands out.</summary>
            public float PeakDb;

            public float End => Start + Length;
        }

        public sealed class Analysis
        {
            public AudioClip Clip;
            public float ClipLength;
            public float ClipPeak;

            /// <summary>Per-column peak of the whole file, 0..1, for drawing the waveform.</summary>
            public float[] Envelope = Array.Empty<float>();

            public List<Segment> Segments = new List<Segment>();
        }

        /// <summary>
        /// Reads the clip's decoded samples and finds the sounds in it. Returns false with a reason rather
        /// than throwing: this runs from a window button, and a menu action must never break the editor.
        /// </summary>
        public static bool TryAnalyze(AudioClip clip, Settings settings, out Analysis analysis, out string error)
        {
            analysis = null;
            error = null;

            if (clip == null)
            {
                error = "No clip selected.";
                return false;
            }

            if (clip.channels <= 0 || clip.frequency <= 0 || clip.samples <= 0)
            {
                error = $"'{clip.name}' reports no audio data.";
                return false;
            }

            // Compressed clips must be decompressed to be read, and an imported file is not necessarily
            // resident yet.
            if (clip.loadState != AudioDataLoadState.Loaded)
                clip.LoadAudioData();

            var totalSamples = (long)clip.samples * clip.channels;
            if (totalSamples <= 0 || totalSamples > int.MaxValue)
            {
                error = $"'{clip.name}' is too large to analyse in one pass.";
                return false;
            }

            var interleaved = new float[totalSamples];
            if (!clip.GetData(interleaved, 0))
            {
                error = $"'{clip.name}' could not be read. Check that its import settings use Decompress On " +
                        "Load rather than Streaming, then analyse again.";
                return false;
            }

            var frameSamples = Mathf.Max(1, Mathf.RoundToInt(clip.frequency * FrameSeconds));
            var frameCount = Mathf.CeilToInt(clip.samples / (float)frameSamples);
            var secondsPerFrame = frameSamples / (float)clip.frequency;

            var frames = new float[frameCount];
            var peak = 0f;

            for (var frame = 0; frame < frameCount; frame++)
            {
                var firstSample = frame * frameSamples;
                var lastSample = Mathf.Min(firstSample + frameSamples, clip.samples);
                var framePeak = 0f;

                for (var sample = firstSample; sample < lastSample; sample++)
                {
                    var baseIndex = sample * clip.channels;
                    for (var channel = 0; channel < clip.channels; channel++)
                    {
                        var value = Mathf.Abs(interleaved[baseIndex + channel]);
                        if (value > framePeak)
                            framePeak = value;
                    }
                }

                frames[frame] = framePeak;
                if (framePeak > peak)
                    peak = framePeak;
            }

            if (peak <= 0.00001f)
            {
                error = $"'{clip.name}' is silent.";
                return false;
            }

            // The gate is relative to the file's own peak, so it works the same on a quiet recording and a
            // hot one without retuning.
            var startGate = peak * Mathf.Pow(10f, Mathf.Clamp(settings.ThresholdDb, -90f, 0f) / 20f);
            var endGate = peak * Mathf.Pow(
                10f, Mathf.Clamp(settings.ThresholdDb - Mathf.Max(0f, settings.EndHysteresisDb), -90f, 0f) / 20f);

            var runs = FindRuns(frames, startGate, endGate);
            runs = MergeRuns(runs, secondsPerFrame, Mathf.Max(0f, settings.MinGapSeconds));

            var segments = BuildSegments(runs, frames, secondsPerFrame, clip.length, settings);
            if (segments.Count == 0)
            {
                error = $"No sounds found above {settings.ThresholdDb:0} dB. Lower the threshold, or reduce " +
                        "the minimum length.";
                return false;
            }

            analysis = new Analysis
            {
                Clip = clip,
                ClipLength = clip.length,
                ClipPeak = peak,
                Envelope = BuildEnvelope(frames),
                Segments = segments,
            };

            return true;
        }

        /// <summary>
        /// Contiguous runs of frames above the gate, in frame indices. A run starts on <paramref
        /// name="startGate"/> and only ends once the level has fallen below <paramref name="endGate"/>, so a
        /// hit's decay stays inside its excerpt instead of being cut a few milliseconds in.
        /// </summary>
        private static List<Vector2Int> FindRuns(float[] frames, float startGate, float endGate)
        {
            var runs = new List<Vector2Int>();
            var start = -1;

            for (var i = 0; i < frames.Length; i++)
            {
                if (start < 0)
                {
                    if (frames[i] >= startGate)
                        start = i;

                    continue;
                }

                if (frames[i] < endGate)
                {
                    runs.Add(new Vector2Int(start, i));
                    start = -1;
                }
            }

            if (start >= 0)
                runs.Add(new Vector2Int(start, frames.Length));

            return runs;
        }

        /// <summary>Joins runs separated by less than the minimum gap: a decay dip is not a new sound.</summary>
        private static List<Vector2Int> MergeRuns(List<Vector2Int> runs, float secondsPerFrame, float minGapSeconds)
        {
            var merged = new List<Vector2Int>();
            if (runs.Count == 0)
                return merged;

            var current = runs[0];

            for (var i = 1; i < runs.Count; i++)
            {
                var gap = (runs[i].x - current.y) * secondsPerFrame;
                if (gap < minGapSeconds)
                {
                    current.y = runs[i].y;
                    continue;
                }

                merged.Add(current);
                current = runs[i];
            }

            merged.Add(current);
            return merged;
        }

        private static List<Segment> BuildSegments(
            List<Vector2Int> runs,
            float[] frames,
            float secondsPerFrame,
            float clipLength,
            Settings settings)
        {
            var padding = Mathf.Max(0f, settings.PaddingSeconds);
            var minLength = Mathf.Max(0.005f, settings.MinLengthSeconds);
            var candidates = new List<Segment>(runs.Count);

            for (var i = 0; i < runs.Count; i++)
            {
                var start = Mathf.Max(0f, runs[i].x * secondsPerFrame - padding);
                var end = Mathf.Min(clipLength, runs[i].y * secondsPerFrame + padding);
                var length = end - start;

                if (length < minLength)
                    continue;

                var localPeak = 0f;
                for (var frame = runs[i].x; frame < runs[i].y; frame++)
                {
                    if (frames[frame] > localPeak)
                        localPeak = frames[frame];
                }

                candidates.Add(new Segment
                {
                    Start = start,
                    Length = length,
                    PeakDb = Mathf.Max(-96f, 20f * Mathf.Log10(Mathf.Max(localPeak, 0.00001f))),
                });
            }

            var limit = Mathf.Max(1, settings.MaxSegments);
            if (candidates.Count <= limit)
                return candidates;

            // Keep the loudest, then restore time order. Keeping the first N would systematically throw away
            // the best hits in a compilation that builds up to them.
            candidates.Sort((a, b) => b.PeakDb.CompareTo(a.PeakDb));
            candidates.RemoveRange(limit, candidates.Count - limit);
            candidates.Sort((a, b) => a.Start.CompareTo(b.Start));

            return candidates;
        }

        private static float[] BuildEnvelope(float[] frames)
        {
            var columns = Mathf.Clamp(EnvelopeColumns, 8, Mathf.Max(8, frames.Length));
            var envelope = new float[columns];
            var peak = 0f;

            for (var column = 0; column < columns; column++)
            {
                var first = Mathf.FloorToInt(column * frames.Length / (float)columns);
                var last = Mathf.Max(first + 1, Mathf.FloorToInt((column + 1) * frames.Length / (float)columns));
                var value = 0f;

                for (var frame = first; frame < last && frame < frames.Length; frame++)
                {
                    if (frames[frame] > value)
                        value = frames[frame];
                }

                envelope[column] = value;
                if (value > peak)
                    peak = value;
            }

            if (peak > 0f)
            {
                for (var column = 0; column < columns; column++)
                    envelope[column] /= peak;
            }

            return envelope;
        }

        /// <summary>Appends detected segments to a cue as excerpts, leaving everything else alone.</summary>
        public static int AppendToCue(CombatSoundProfile profile, CombatSoundCue cue, AudioClip clip,
            IReadOnlyList<Segment> segments, float fadeIn, float fadeOut)
        {
            if (profile == null || clip == null || segments == null || segments.Count == 0)
                return 0;

            var target = FindCue(profile, cue);
            if (target == null)
                return 0;

            var added = new List<CombatSoundSlice>(segments.Count);
            for (var i = 0; i < segments.Count; i++)
            {
                added.Add(CombatSoundSlice.CreateTransient(clip, segments[i].Start, segments[i].Length,
                    fadeIn, fadeOut));
            }

            target.AddSlices(added);
            return added.Count;
        }

        /// <summary>Empties one cue row, for when an experiment needs undoing.</summary>
        public static bool ClearCue(CombatSoundProfile profile, CombatSoundCue cue)
        {
            var target = FindCue(profile, cue);
            if (target == null || target.SliceCount == 0)
                return false;

            target.SetSlices(Array.Empty<CombatSoundSlice>());
            return true;
        }

        /// <summary>The cue's excerpt set, created if the row exists but has never been touched.</summary>
        private static CombatSoundClips FindCue(CombatSoundProfile profile, CombatSoundCue cue)
        {
            if (profile == null)
                return null;

            var entries = profile.GetEntries();
            for (var i = 0; i < entries.Length; i++)
            {
                if (entries[i].Cue != cue)
                    continue;

                if (entries[i].Clips == null)
                    entries[i].Clips = new CombatSoundClips();

                return entries[i].Clips;
            }

            return null;
        }
    }
}
