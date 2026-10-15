using System.Buffers;
using System.Runtime.InteropServices;

namespace MegaCopier.Engine.IO;

/// <summary>
/// Memoria nativa alineada a 4096 bytes. La E/S sin caché (FILE_FLAG_NO_BUFFERING) exige que
/// la dirección del búfer, el desplazamiento y el tamaño sean múltiplos del tamaño de sector.
/// Hereda de <see cref="MemoryManager{T}"/> para poder usarse en E/S asíncrona superpuesta.
/// </summary>
internal sealed unsafe class AlignedBuffer : MemoryManager<byte>
{
    public const int Alignment = 4096;

    private void* _pointer;

    public AlignedBuffer(int capacity)
    {
        if (capacity <= 0 || capacity % Alignment != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), "El tamaño debe ser múltiplo de 4096.");
        }

        Capacity = capacity;
        _pointer = NativeMemory.AlignedAlloc((nuint)capacity, Alignment);
    }

    public int Capacity { get; }

    public Span<byte> Span => GetSpan();

    public override Span<byte> GetSpan()
    {
        ObjectDisposedException.ThrowIf(_pointer is null, this);
        return new Span<byte>(_pointer, Capacity);
    }

    // La memoria nativa no se mueve: "fijarla" es gratis.
    public override MemoryHandle Pin(int elementIndex = 0) => new((byte*)_pointer + elementIndex);

    public override void Unpin()
    {
    }

    protected override void Dispose(bool disposing)
    {
        if (_pointer is not null)
        {
            NativeMemory.AlignedFree(_pointer);
            _pointer = null;
        }
    }
}

/// <summary>
/// Conjunto fijo de búferes reutilizados durante todo un trabajo, sin asignaciones por archivo.
/// Solo lo usa una transferencia a la vez, así que no necesita sincronización.
/// </summary>
internal sealed class BufferPool : IDisposable
{
    private readonly AlignedBuffer[] _all;
    private readonly Stack<AlignedBuffer> _free = new();

    public BufferPool(int bufferSize, int count)
    {
        BufferSize = bufferSize;
        _all = new AlignedBuffer[count];
        for (int i = 0; i < count; i++)
        {
            _all[i] = new AlignedBuffer(bufferSize);
            _free.Push(_all[i]);
        }
    }

    public int BufferSize { get; }

    public int Available
    {
        get
        {
            lock (_free)
            {
                return _free.Count;
            }
        }
    }

    public AlignedBuffer Rent()
    {
        lock (_free)
        {
            return _free.Count > 0
                ? _free.Pop()
                : throw new InvalidOperationException("No quedan búferes libres: la canalización retuvo más de los previstos.");
        }
    }

    public bool TryRent(out AlignedBuffer buffer)
    {
        lock (_free)
        {
            return _free.TryPop(out buffer!);
        }
    }

    public void Return(AlignedBuffer buffer)
    {
        lock (_free)
        {
            _free.Push(buffer);
        }
    }

    public void Dispose()
    {
        foreach (IDisposable buffer in _all)
        {
            buffer.Dispose();
        }
    }
}
