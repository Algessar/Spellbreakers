using System;

namespace Spellbreakers.Combat.Ai
{
    /// <summary>
    /// A response curve that shapes a consideration's raw 0..1 input into a 0..1 score.
    ///
    /// Deliberately not a UnityEngine.AnimationCurve: the decision layer stays free of engine types so
    /// a whole duel's decisions can be replayed in a unit test. Editor assets can be authored as curves
    /// and baked into control points.
    /// </summary>
    public sealed class ResponseCurve
    {
        private readonly float[] _xs;
        private readonly float[] _ys;

        private ResponseCurve(float[] xs, float[] ys, string name)
        {
            _xs = xs;
            _ys = ys;
            Name = name;
        }

        public string Name { get; }

        /// <summary>A straight line: the input is the score.</summary>
        public static ResponseCurve Linear { get; } =
            new ResponseCurve(new[] { 0f, 1f }, new[] { 0f, 1f }, "Linear");

        /// <summary>Low inputs stay low and high inputs climb steeply. Good for "only commit when it is really worth it".</summary>
        public static ResponseCurve EaseIn { get; } =
            new ResponseCurve(new[] { 0f, 0.5f, 1f }, new[] { 0f, 0.25f, 1f }, "EaseIn");

        /// <summary>Low inputs already score well and high inputs flatten. Good for "any opening will do".</summary>
        public static ResponseCurve EaseOut { get; } =
            new ResponseCurve(new[] { 0f, 0.5f, 1f }, new[] { 0f, 0.75f, 1f }, "EaseOut");

        /// <summary>Nothing until the input is nearly certain, then everything.</summary>
        public static ResponseCurve Threshold { get; } =
            new ResponseCurve(new[] { 0f, 0.7f, 0.85f, 1f }, new[] { 0f, 0f, 1f, 1f }, "Threshold");

        /// <summary>Best in the middle: neither too close nor too far.</summary>
        public static ResponseCurve Banded { get; } =
            new ResponseCurve(new[] { 0f, 0.5f, 1f }, new[] { 0f, 1f, 0f }, "Banded");

        /// <summary>Inverted linear: 1 minus the input.</summary>
        public static ResponseCurve Inverted { get; } =
            new ResponseCurve(new[] { 0f, 1f }, new[] { 1f, 0f }, "Inverted");

        /// <summary>Builds a curve from control points. X values must be ascending.</summary>
        public static ResponseCurve FromPoints(float[] xs, float[] ys, string name = "Custom")
        {
            if (xs == null || ys == null || xs.Length < 2 || xs.Length != ys.Length)
                throw new ArgumentException("A response curve needs matching X and Y arrays of at least two points.");

            for (var i = 1; i < xs.Length; i++)
            {
                if (xs[i] <= xs[i - 1])
                    throw new ArgumentException("Response curve X values must be strictly ascending.", nameof(xs));
            }

            return new ResponseCurve((float[])xs.Clone(), (float[])ys.Clone(), name);
        }

        public float Evaluate(float input)
        {
            var x = FencingMath.Clamp01(input);

            if (x <= _xs[0])
                return _ys[0];

            var last = _xs.Length - 1;
            if (x >= _xs[last])
                return _ys[last];

            for (var i = 1; i <= last; i++)
            {
                if (x > _xs[i])
                    continue;

                var span = _xs[i] - _xs[i - 1];
                var t = span <= 1e-6f ? 1f : (x - _xs[i - 1]) / span;
                return FencingMath.Clamp01(FencingMath.Lerp(_ys[i - 1], _ys[i], t));
            }

            return _ys[last];
        }
    }
}
