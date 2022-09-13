namespace Arachne;

// A class that represents an arbitrary finite state machine.
public class FSM<TState, TTransition> where TState : Enum where TTransition : Enum
{
    public TState CurrentState { get; private set; }
    private Dictionary<TState, Dictionary<TTransition, TState>> _transitions;

    public event EventHandler<TState>? StateChanged;

    public FSM(TState initialState)
    {
        CurrentState = initialState;
        _transitions = new Dictionary<TState, Dictionary<TTransition, TState>>();
    }

    public void AddTransition(TState fromState, TTransition transition, TState toState)
    {
        if (!_transitions.ContainsKey(fromState))
        {
            _transitions[fromState] = new Dictionary<TTransition, TState>();
        }

        _transitions[fromState][transition] = toState;
    }

    public TState MoveNext(TTransition transition)
    {
        if (!_transitions.ContainsKey(CurrentState))
        {
            throw new InvalidOperationException("No transitions defined for state " + CurrentState);
        }

        if (!_transitions[CurrentState].ContainsKey(transition))
        {
            throw new InvalidOperationException("No transition defined for state " + CurrentState + " and transition " + transition);
        }

        CurrentState = _transitions[CurrentState][transition];
        StateChanged?.Invoke(this, CurrentState);
        return CurrentState;
    }

    public bool TryMoveNext(TTransition transition, out TState newState)
    {
        if (!_transitions.ContainsKey(CurrentState))
        {
            newState = default;
            return false;
        }

        if (!_transitions[CurrentState].ContainsKey(transition))
        {
            newState = default;
            return false;
        }

        newState = _transitions[CurrentState][transition];
        CurrentState = newState;
        StateChanged?.Invoke(this, CurrentState);
        return true;
    }
}