using NUnit.Framework;
using Spellbreakers.World;

namespace Spellbreakers.Tests
{
    /// <summary>
    /// The state machine is what decides whether a pause press, a death and a scene load can interleave
    /// without leaving the game in a state nobody can leave. Before it existed, the only thing ordered was
    /// whatever <c>Time.timeScale</c> happened to be, set from nowhere and read by everybody.
    ///
    /// These tests are about the table, not about Unity. Nothing here needs a scene.
    /// </summary>
    [TestFixture]
    public sealed class GameStateMachineTests
    {
        [Test]
        public void ABootedGameCanEnterPlay()
        {
            var machine = new GameStateMachine();

            Assert.That(machine.Current, Is.EqualTo(GameState.Boot));
            Assert.That(machine.TrySet(GameState.Playing), Is.True);
            Assert.That(machine.Current, Is.EqualTo(GameState.Playing));
        }

        [Test]
        public void ABootedGameCanEnterTheTitle()
        {
            var machine = new GameStateMachine();

            Assert.That(machine.TrySet(GameState.Title), Is.True);
        }

        [Test]
        public void ABootedGameCannotPauseOrDie()
        {
            var machine = new GameStateMachine();

            Assert.That(machine.TrySet(GameState.Paused), Is.False);
            Assert.That(machine.TrySet(GameState.Dead), Is.False);
            Assert.That(machine.TrySet(GameState.Transition), Is.False);
            Assert.That(machine.Current, Is.EqualTo(GameState.Boot));
        }

        [Test]
        public void PlayAndPauseAreReversible()
        {
            var machine = new GameStateMachine();
            machine.Reset(GameState.Playing);

            Assert.That(machine.TrySet(GameState.Paused), Is.True);
            Assert.That(machine.TrySet(GameState.Playing), Is.True);
            Assert.That(machine.TrySet(GameState.Paused), Is.True);
        }

        [Test]
        public void APausedGameCannotDie()
        {
            // Dying behind an open menu would leave the death beat playing under the pause overlay.
            var machine = new GameStateMachine();
            machine.Reset(GameState.Paused);

            Assert.That(machine.TrySet(GameState.Dead), Is.False);
            Assert.That(machine.Current, Is.EqualTo(GameState.Paused));
        }

        [Test]
        public void APausedGameCanOnlyResumeOrQuitToTheTitle()
        {
            var machine = new GameStateMachine();
            machine.Reset(GameState.Paused);

            Assert.That(GameStateMachine.CanTransition(GameState.Paused, GameState.Playing), Is.True);
            Assert.That(GameStateMachine.CanTransition(GameState.Paused, GameState.Title), Is.True);
            Assert.That(GameStateMachine.CanTransition(GameState.Paused, GameState.Transition), Is.False);
            Assert.That(GameStateMachine.CanTransition(GameState.Paused, GameState.Boot), Is.False);
        }

        [Test]
        public void TheTitleCannotEnterAnyGameplayStateButPlay()
        {
            Assert.That(GameStateMachine.CanTransition(GameState.Title, GameState.Playing), Is.True);

            Assert.That(GameStateMachine.CanTransition(GameState.Title, GameState.Paused), Is.False);
            Assert.That(GameStateMachine.CanTransition(GameState.Title, GameState.Transition), Is.False);
            Assert.That(GameStateMachine.CanTransition(GameState.Title, GameState.Dead), Is.False);
        }

        [Test]
        public void DeathReturnsToPlayThroughTheCheckpoint()
        {
            var machine = new GameStateMachine();
            machine.Reset(GameState.Playing);
            machine.TrySet(GameState.Dead);

            Assert.That(machine.Current, Is.EqualTo(GameState.Dead));
            Assert.That(machine.TrySet(GameState.Playing), Is.True);
        }

        [Test]
        public void ATransitionEndsInPlayOrIsAbandonedToTheTitle()
        {
            Assert.That(GameStateMachine.CanTransition(GameState.Transition, GameState.Playing), Is.True);
            Assert.That(GameStateMachine.CanTransition(GameState.Transition, GameState.Title), Is.True);
            Assert.That(GameStateMachine.CanTransition(GameState.Transition, GameState.Paused), Is.False);
        }

        [Test]
        public void NothingEverReturnsToBoot()
        {
            foreach (GameState from in System.Enum.GetValues(typeof(GameState)))
            {
                if (from == GameState.Boot)
                    continue;

                Assert.That(GameStateMachine.CanTransition(from, GameState.Boot), Is.False,
                    $"{from} must not return to Boot.");
            }
        }

        [Test]
        public void AskingForTheCurrentStateIsRefusedAndRaisesNothing()
        {
            var machine = new GameStateMachine();
            machine.Reset(GameState.Playing);

            var raised = 0;
            machine.Changed += (_, _) => raised++;

            Assert.That(machine.TrySet(GameState.Playing), Is.False);
            Assert.That(raised, Is.Zero);
        }

        [Test]
        public void ARefusedTransitionLeavesTheStateAloneAndRaisesNothing()
        {
            var machine = new GameStateMachine();
            machine.Reset(GameState.Title);

            var raised = 0;
            machine.Changed += (_, _) => raised++;

            Assert.That(machine.TrySet(GameState.Dead), Is.False);
            Assert.That(raised, Is.Zero);
            Assert.That(machine.Current, Is.EqualTo(GameState.Title));
        }

        [Test]
        public void ChangedReportsTheTransitionItJustMade()
        {
            var machine = new GameStateMachine();
            machine.Reset(GameState.Playing);

            GameState reportedFrom = GameState.Boot;
            GameState reportedTo = GameState.Boot;
            machine.Changed += (from, to) =>
            {
                reportedFrom = from;
                reportedTo = to;
            };

            machine.TrySet(GameState.Paused);

            Assert.That(reportedFrom, Is.EqualTo(GameState.Playing));
            Assert.That(reportedTo, Is.EqualTo(GameState.Paused));
        }

        [Test]
        public void ResetForcesAStateTheTableWouldRefuse()
        {
            // Loading a save straight into play is a real case the table cannot describe.
            var machine = new GameStateMachine();

            machine.Reset(GameState.Dead);

            Assert.That(machine.Current, Is.EqualTo(GameState.Dead));
        }

        [Test]
        public void ResetToTheCurrentStateRaisesNothing()
        {
            var machine = new GameStateMachine();
            machine.Reset(GameState.Playing);

            var raised = 0;
            machine.Changed += (_, _) => raised++;

            machine.Reset(GameState.Playing);

            Assert.That(raised, Is.Zero);
        }

        [Test]
        public void OnlyPauseFreezesTime()
        {
            Assert.That(GameStateMachine.FreezesTime(GameState.Paused), Is.True);

            foreach (GameState state in System.Enum.GetValues(typeof(GameState)))
            {
                if (state == GameState.Paused)
                    continue;

                Assert.That(GameStateMachine.FreezesTime(state), Is.False,
                    $"{state} must not freeze time; a transition needs frames to fade and load.");
            }
        }

        [Test]
        public void OnlyPlayingHonoursPlayerInput()
        {
            Assert.That(GameStateMachine.BlocksPlayerInput(GameState.Playing), Is.False);

            foreach (GameState state in System.Enum.GetValues(typeof(GameState)))
            {
                if (state == GameState.Playing)
                    continue;

                Assert.That(GameStateMachine.BlocksPlayerInput(state), Is.True,
                    $"{state} must block player input.");
            }
        }

        [Test]
        public void NoStateTransitionsToItself()
        {
            foreach (GameState state in System.Enum.GetValues(typeof(GameState)))
            {
                Assert.That(GameStateMachine.CanTransition(state, state), Is.False,
                    $"{state} to itself is a no-op, not a transition.");
            }
        }

        [Test]
        public void EveryStateCanBeLeft()
        {
            // A state with no outgoing edge is a soft lock: the game would sit in it with no way back.
            var deadEnds = new System.Collections.Generic.List<GameState>();

            foreach (GameState from in System.Enum.GetValues(typeof(GameState)))
            {
                var canLeave = false;

                foreach (GameState to in System.Enum.GetValues(typeof(GameState)))
                {
                    if (GameStateMachine.CanTransition(from, to))
                    {
                        canLeave = true;
                        break;
                    }
                }

                if (!canLeave)
                    deadEnds.Add(from);
            }

            Assert.That(deadEnds, Is.Empty, "These states cannot be left: " + string.Join(", ", deadEnds));
        }

        [Test]
        public void EveryStateIsReachableFromBoot()
        {
            // Guards against a state that is written into the enum and the service but that nothing can
            // actually enter, which would make its tests pass while the game can never get there.
            var reachable = new System.Collections.Generic.HashSet<GameState> { GameState.Boot };
            var queue = new System.Collections.Generic.Queue<GameState>();
            queue.Enqueue(GameState.Boot);

            while (queue.Count > 0)
            {
                var from = queue.Dequeue();

                foreach (GameState to in System.Enum.GetValues(typeof(GameState)))
                {
                    if (GameStateMachine.CanTransition(from, to) && reachable.Add(to))
                        queue.Enqueue(to);
                }
            }

            var unreachable = new System.Collections.Generic.List<GameState>();

            foreach (GameState state in System.Enum.GetValues(typeof(GameState)))
            {
                if (!reachable.Contains(state))
                    unreachable.Add(state);
            }

            Assert.That(unreachable, Is.Empty, "These states cannot be reached: " + string.Join(", ", unreachable));
        }
    }
}
