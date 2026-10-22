namespace MegaCopier.Engine;

/// <summary>Historial acotado de pantallas: una navegación nueva descarta solo la rama de avance.</summary>
public sealed class NavigationHistory<T>(T initial)
{
    private readonly List<T> _entries = [initial];
    private int _index;
    public T Current => _entries[_index];
    public bool CanBack => _index > 0;
    public bool CanForward => _index + 1 < _entries.Count;
    public void Visit(T state)
    {
        if (EqualityComparer<T>.Default.Equals(Current, state)) return;
        if (CanForward) _entries.RemoveRange(_index + 1, _entries.Count - _index - 1);
        _entries.Add(state); _index++;
        if (_entries.Count > 128) { _entries.RemoveAt(0); _index--; }
    }
    public bool Move(bool forward, out T state)
    {
        bool allowed = forward ? CanForward : CanBack;
        if (allowed) _index += forward ? 1 : -1;
        state = Current; return allowed;
    }
}
