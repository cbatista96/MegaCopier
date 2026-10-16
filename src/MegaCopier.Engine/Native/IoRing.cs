using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace MegaCopier.Engine.Native;

/// <summary>
/// IoRing (Windows 11): muchas lecturas en una sola petición al sistema, como io_uring en Linux. Aquí se usa para leer
/// muchos archivos pequeños a la vez (calcular o comprobar las sumas de una biblioteca de fotos, por ejemplo). Si el
/// sistema no lo tiene, <see cref="IsAvailable"/> es false. Los archivos se abren en modo normal (no superpuesto): con
/// FILE_FLAG_OVERLAPPED cada lectura termina con E_INVALIDARG.
/// </summary>
internal static unsafe partial class IoRing
{
    private const int Sector = 4096;
    private static readonly Lazy<bool> Available = new(Probe);

    public static bool IsAvailable => Available.Value;

    /// <summary>
    /// Lee enteros los archivos (cada uno de hasta <paramref name="maxLength"/> bytes) con todas las lecturas en vuelo a la
    /// vez, y entrega cada contenido (o su error) a <paramref name="onFile"/>, en el orden de la lista.
    /// </summary>
    public static void ReadAll(IReadOnlyList<string> paths, int maxLength, bool unbuffered, Action<int, ReadOnlySpan<byte>, Exception?> onFile)
    {
        int count = paths.Count;
        int slot = (maxLength + Sector - 1) / Sector * Sector;
        byte* memory = (byte*)NativeMemory.AlignedAlloc((nuint)(slot * count), Sector);
        var handles = new SafeFileHandle?[count];
        var errors = new Exception?[count];
        var lengths = new int[count];
        nint ring = 0;
        try
        {
            var flags = new CreateFlags();
            Check(CreateIoRing(1 /* IORING_VERSION_1 */, flags, (uint)Math.Max(8, count), (uint)Math.Max(16, count * 2), &ring));

            // Abrir cada archivo cuesta más que leerlo: se abren varios a la vez.
            Parallel.For(0, count, new ParallelOptions { MaxDegreeOfParallelism = Math.Min(4, Environment.ProcessorCount) }, i =>
            {
                try
                {
                    handles[i] = File.OpenHandle(paths[i], FileMode.Open, FileAccess.Read, FileShare.Read, unbuffered ? Win32.NoBuffering : FileOptions.None);
                    long length = RandomAccess.GetLength(handles[i]!);
                    lengths[i] = length <= maxLength ? (int)length : throw new IOException("Archivo demasiado grande para la lectura en lote.");
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    errors[i] = ex;
                }
            });

            int queued = 0;
            for (int i = 0; i < count; i++)
            {
                if (errors[i] is not null || lengths[i] == 0)
                {
                    continue;
                }

                try
                {
                    long length = lengths[i];
                    var file = new HandleRef { Kind = 0, Handle = handles[i]!.DangerousGetHandle() };
                    var buffer = new BufferRef { Kind = 0, Address = memory + ((long)slot * i) };
                    uint toRead = unbuffered ? (uint)((length + Sector - 1) / Sector * Sector) : (uint)length; // sin caché: sectores enteros
                    Check(BuildIoRingReadFile(ring, file, buffer, toRead, 0, (nuint)i, 0));
                    queued++;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    errors[i] = ex;
                }
            }

            uint submitted;
            Check(SubmitIoRing(ring, (uint)queued, uint.MaxValue, &submitted));
            for (int done = 0; done < queued;)
            {
                Completion completion;
                int hr = PopIoRingCompletion(ring, &completion);
                if (hr == 1 /* S_FALSE: aún nada */)
                {
                    Check(SubmitIoRing(ring, 1, uint.MaxValue, &submitted));
                    continue;
                }

                Check(hr);
                int index = (int)completion.UserData;
                if (completion.ResultCode < 0)
                {
                    errors[index] = Marshal.GetExceptionForHR(completion.ResultCode) as IOException ?? new IOException($"Error 0x{completion.ResultCode:X8}");
                }
                else
                {
                    // Lo leído de verdad (el archivo pudo encoger entre medias).
                    lengths[index] = (int)Math.Min((ulong)lengths[index], completion.Information);
                }

                done++;
            }

            for (int i = 0; i < count; i++)
            {
                onFile(i, errors[i] is null ? new ReadOnlySpan<byte>(memory + ((long)slot * i), lengths[i]) : default, errors[i]);
            }
        }
        finally
        {
            if (ring != 0)
            {
                CloseIoRing(ring);
            }

            foreach (var handle in handles)
            {
                handle?.Dispose();
            }

            NativeMemory.AlignedFree(memory);
        }
    }

    private static bool Probe()
    {
        try
        {
            Capabilities capabilities;
            return QueryIoRingCapabilities(&capabilities) >= 0 && capabilities.MaxVersion >= 1;
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException)
        {
            return false;
        }
    }

    private static void Check(int hr)
    {
        if (hr < 0)
        {
            throw Marshal.GetExceptionForHR(hr) as IOException ?? new IOException($"IoRing: 0x{hr:X8}", hr);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Capabilities
    {
        public int MaxVersion;
        public uint MaxSubmissionQueueSize;
        public uint MaxCompletionQueueSize;
        public int FeatureFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CreateFlags
    {
        public int Required;
        public int Advisory;
    }

    [StructLayout(LayoutKind.Explicit, Size = 16)]
    private struct HandleRef
    {
        [FieldOffset(0)]
        public int Kind;

        [FieldOffset(8)]
        public nint Handle;
    }

    [StructLayout(LayoutKind.Explicit, Size = 16)]
    private struct BufferRef
    {
        [FieldOffset(0)]
        public int Kind;

        [FieldOffset(8)]
        public byte* Address;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Completion
    {
        public nuint UserData;
        public int ResultCode;
        public nuint Information;
    }

    [LibraryImport("kernelbase.dll")]
    private static partial int QueryIoRingCapabilities(Capabilities* capabilities);

    [LibraryImport("kernelbase.dll")]
    private static partial int CreateIoRing(int version, CreateFlags flags, uint submissionQueueSize, uint completionQueueSize, nint* ring);

    [LibraryImport("kernelbase.dll")]
    private static partial int BuildIoRingReadFile(nint ring, HandleRef file, BufferRef buffer, uint numberOfBytesToRead, ulong fileOffset, nuint userData, int sqeFlags);

    [LibraryImport("kernelbase.dll")]
    private static partial int SubmitIoRing(nint ring, uint waitOperations, uint milliseconds, uint* submittedEntries);

    [LibraryImport("kernelbase.dll")]
    private static partial int PopIoRingCompletion(nint ring, Completion* completion);

    [LibraryImport("kernelbase.dll")]
    private static partial int CloseIoRing(nint ring);
}
