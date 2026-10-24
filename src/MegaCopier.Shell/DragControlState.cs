namespace MegaCopier.Shell;

/// <summary>Estado real de Ctrl mientras se envía Esc para cancelar un arrastre OLE.</summary>
internal sealed class DragControlState
{
    private int _held;
    public volatile bool Suspended;

    public void Reset(bool left, bool right) => Volatile.Write(ref _held, (left ? 1 : 0) | (right ? 2 : 0));

    // Solo se observan las dos teclas Ctrl. Las pulsaciones de restauración nunca pueden
    // volver a bajar una tecla que el usuario ya soltó mientras se cancelaba el arrastre.
    public bool Block(int key, bool up, bool ownInput)
    {
        int bit = key == 0xA2 ? 1 : key == 0xA3 ? 2 : 0;
        if (bit == 0) return false;
        if (ownInput) return !up && (Volatile.Read(ref _held) & bit) == 0;
        if (up) Interlocked.And(ref _held, ~bit);
        else Interlocked.Or(ref _held, bit);
        return Suspended;
    }
}
