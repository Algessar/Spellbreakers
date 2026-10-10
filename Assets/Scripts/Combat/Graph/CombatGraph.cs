/// <summary>
/// A guard change always follows the same route: current guard -> Langort -> destination guard.
/// The graph owns logical combat state; CombatHandler plays the corresponding Animator states.
/// </summary>
public sealed class CombatGraph
{
    public const string PassingGuard = "Langort";

    public string CurrentGuard { get; private set; }
    public bool IsTransitioning { get; private set; }

    public CombatGraph(string initialGuard)
    {
        CurrentGuard = initialGuard;
    }

    public bool TryBeginTransition(string destination, out GuardTransition transition)
    {
        if (IsTransitioning || string.IsNullOrWhiteSpace(destination) || destination == CurrentGuard)
        {
            transition = default;
            return false;
        }

        IsTransitioning = true;
        transition = new GuardTransition(CurrentGuard, PassingGuard, destination);
        return true;
    }

    public bool CompleteTransition(GuardTransition transition)
    {
        if (!IsTransitioning || transition.From != CurrentGuard)
            return false;

        CurrentGuard = transition.To;
        IsTransitioning = false;
        return true;
    }

    public void CancelTransition()
    {
        IsTransitioning = false;
    }
}

public readonly struct GuardTransition
{
    public string From { get; }
    public string PassThrough { get; }
    public string To { get; }

    public GuardTransition(string from, string passThrough, string to)
    {
        From = from;
        PassThrough = passThrough;
        To = to;
    }
}
