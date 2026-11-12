using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Spellbreakers.World;
using UnityEngine;

namespace Spellbreakers.Tests
{
    /// <summary>
    /// The catalog is the replacement for referring to scenes by build index, which is what the
    /// teleporters did and what made a scene reorder silently change where a door went.
    ///
    /// Two kinds of failure are covered here. The shape of the table - duplicate ids, half-filled rows -
    /// is checkable without Unity at all. The second kind is the one that used to cost the most time: the
    /// table naming a scene file that is not where it claims to be, which should be a failing test and not
    /// a null reference in play mode.
    /// </summary>
    [TestFixture]
    public sealed class SceneCatalogTests
    {
        private const string SceneFolder = "World/Scenes";

        [TearDown]
        public void ClearOverride()
        {
            // A leaked override would make every later test in the session see a catalog it never asked for.
            SceneCatalog.Override = null;
        }

        [Test]
        public void TheShippedCatalogIsInternallySound()
        {
            Assert.That(SceneCatalog.Default.Validate(), Is.Empty,
                "The shipped scene catalog has malformed or duplicate rows.");
        }

        [Test]
        public void TheShippedCatalogIsNotEmpty()
        {
            Assert.That(SceneCatalog.Default.Count, Is.GreaterThan(0));
        }

        [Test]
        public void EveryShippedSceneExistsOnDisk()
        {
            var missing = new List<string>();

            foreach (var entry in SceneCatalog.Default.Entries)
            {
                var path = Path.Combine(Application.dataPath, SceneFolder, entry.SceneName + ".unity");
                if (!File.Exists(path))
                    missing.Add($"{entry.Id} -> {entry.SceneName} (looked for {path})");
            }

            Assert.That(missing, Is.Empty,
                "The scene catalog names scene files that do not exist:\n" + string.Join("\n", missing));
        }

        [Test]
        public void TheHubIsTravelable()
        {
            Assert.That(SceneCatalog.Default.TryGet(SceneIds.HomeBase, out var entry), Is.True);
            Assert.That(entry.SceneName, Is.EqualTo("SCENE_HomeBase"));
        }

        [Test]
        public void TheBootstrapSceneIsNotOfferedUntilItIsAuthored()
        {
            // The id is forward-declared for saves, but a row that cannot load is worse than no row.
            Assert.That(SceneCatalog.Default.Contains(SceneIds.Boot), Is.False);
        }

        [Test]
        public void LookingUpAnUnknownIdFails()
        {
            Assert.That(SceneCatalog.Default.TryGet("no-such-scene", out var entry), Is.False);
            Assert.That(entry, Is.Null);
        }

        [Test]
        public void LookingUpNothingFails()
        {
            Assert.That(SceneCatalog.Default.TryGet(null, out _), Is.False);
            Assert.That(SceneCatalog.Default.TryGet(string.Empty, out _), Is.False);
        }

        [Test]
        public void GettingAnUnknownIdThrowsAndSaysWhatExists()
        {
            var exception = Assert.Throws<System.InvalidOperationException>(
                () => SceneCatalog.Default.Get("no-such-scene"));

            Assert.That(exception.Message, Does.Contain("no-such-scene"));
            Assert.That(exception.Message, Does.Contain(SceneIds.HomeBase),
                "The message should list the ids that do exist, so the typo is obvious.");
        }

        [Test]
        public void ASceneEntryFallsBackToItsSceneNameWhenNoLabelIsAuthored()
        {
            var entry = new SceneEntry("x", "SCENE_X");

            Assert.That(entry.DisplayName, Is.EqualTo("SCENE_X"));
        }

        [Test]
        public void ASceneEntryTrimsWhatItIsGiven()
        {
            var entry = new SceneEntry("  home-base  ", "  SCENE_HomeBase  ", "  Home  ");

            Assert.That(entry.Id, Is.EqualTo("home-base"));
            Assert.That(entry.SceneName, Is.EqualTo("SCENE_HomeBase"));
            Assert.That(entry.DisplayName, Is.EqualTo("Home"));
        }

        [Test]
        public void AMissingIdOrSceneNameIsARecordedProblemNotACrash()
        {
            // One bad row must not make the other scenes unreachable.
            var catalog = new SceneCatalog(new[]
            {
                new SceneEntry("good", "SCENE_Good"),
                new SceneEntry("no-scene", "   "),
                new SceneEntry("", "SCENE_Orphan"),
            });

            Assert.That(catalog.Count, Is.EqualTo(1));
            Assert.That(catalog.Validate().Count, Is.EqualTo(2));
            Assert.That(catalog.TryGet("good", out _), Is.True);
        }

        [Test]
        public void ADuplicateIdKeepsTheFirstAndRecordsTheClash()
        {
            var catalog = new SceneCatalog(new[]
            {
                new SceneEntry("home", "SCENE_First"),
                new SceneEntry("home", "SCENE_Second"),
            });

            Assert.That(catalog.Count, Is.EqualTo(1));
            Assert.That(catalog.Get("home").SceneName, Is.EqualTo("SCENE_First"));
            Assert.That(catalog.Validate().Count, Is.EqualTo(1));
            Assert.That(catalog.Validate()[0], Does.Contain("home"));
        }

        [Test]
        public void ANullEntryListIsARecordedProblem()
        {
            var catalog = new SceneCatalog(null);

            Assert.That(catalog.Count, Is.Zero);
            Assert.That(catalog.Validate(), Is.Not.Empty);
        }

        [Test]
        public void ANullRowIsSkippedRatherThanThrownOn()
        {
            var catalog = new SceneCatalog(new SceneEntry[] { null, new SceneEntry("home", "SCENE_HomeBase") });

            Assert.That(catalog.Count, Is.EqualTo(1));
            Assert.That(catalog.Validate(), Is.Not.Empty);
        }

        [Test]
        public void AnOverrideReplacesTheShippedCatalog()
        {
            var replacement = new SceneCatalog(new[] { new SceneEntry("only", "SCENE_Only") });
            SceneCatalog.Override = replacement;

            Assert.That(SceneCatalog.Active, Is.SameAs(replacement));
            Assert.That(SceneCatalog.Active.TryGet(SceneIds.HomeBase, out _), Is.False);
        }

        [Test]
        public void ClearingTheOverrideRestoresTheShippedCatalog()
        {
            SceneCatalog.Override = new SceneCatalog(new[] { new SceneEntry("only", "SCENE_Only") });
            SceneCatalog.Override = null;

            Assert.That(SceneCatalog.Active, Is.SameAs(SceneCatalog.Default));
        }
    }
}
