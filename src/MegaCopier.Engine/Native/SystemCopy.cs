using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MegaCopier.Engine.Native;

/// <summary>
/// Copia con CopyFile2, la API del Explorador, para lo que solo Windows sabe hacer en red: la copia en el servidor
/// (SMB copy offload / ODX: si origen y destino están en el mismo servidor, los datos no pasan por este equipo) y la
/// compresión SMB (COPY_FILE_REQUEST_COMPRESSED_TRAFFIC, Windows 11 y Server 2022), útil en redes lentas.
/// </summary>
internal static unsafe partial class SystemCopy
{
    private const uint CopyFileRequestCompressedTraffic = 0x10000000;
    private const uint CopyFileNoBuffering = 0x00001000;
    private const int ChunkFinished = 2;
    private const int ContinueCopy = 0;
    private const int CancelCopy = 1;
    private static readonly int Cancelled = unchecked((int)0x800704C7); // HRESULT_FROM_WIN32(ERROR_CANCELLED)

    /// <summary>Servidor de una ruta UNC ("\\servidor\recurso\..."), o null si es local.</summary>
    public static string? Server(string path)
    {
        string full = path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase) ? @"\\" + path[8..] : path;
        if (!full.StartsWith(@"\\", StringComparison.Ordinal) || full.StartsWith(@"\\?\", StringComparison.Ordinal) || full.StartsWith(@"\\.\", StringComparison.Ordinal))
        {
            return null;
        }

        int end = full.IndexOf('\\', 2);
        return end > 2 ? full[2..end] : null;
    }

    /// <summary>Origen y destino en el mismo servidor: el servidor puede copiar por su cuenta.</summary>
    public static bool SameServer(string source, string destination) =>
        Server(source) is { } a && Server(destination) is { } b && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    /// <summary>Alguno de los dos está en la red.</summary>
    public static bool OnNetwork(string source, string destination) => Server(source) is not null || Server(destination) is not null;

    /// <summary>Copia el archivo; <paramref name="progress"/> recibe los bytes nuevos de cada bloque terminado.</summary>
    public static void Copy(string source, string destination, bool compress, bool unbuffered, Action<long> progress, CancellationToken cancellationToken)
    {
        var state = new State(progress, cancellationToken);
        var handle = GCHandle.Alloc(state);
        int* cancel = (int*)NativeMemory.AllocZeroed(sizeof(int));
        try
        {
            using var registration = cancellationToken.Register(() => Volatile.Write(ref *cancel, 1));
            var parameters = new ExtendedParameters
            {
                Size = (uint)sizeof(ExtendedParameters),
                CopyFlags = (compress ? CopyFileRequestCompressedTraffic : 0) | (unbuffered ? CopyFileNoBuffering : 0),
                Cancel = cancel,
                ProgressRoutine = &OnProgress,
                CallbackContext = GCHandle.ToIntPtr(handle),
            };

            int hr = CopyFile2(Win32.LongPath(source), Win32.LongPath(destination), &parameters);
            if (hr == Cancelled || cancellationToken.IsCancellationRequested)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            if (hr < 0)
            {
                throw Marshal.GetExceptionForHR(hr) as IOException ?? new IOException(Marshal.GetExceptionForHR(hr)?.Message, hr);
            }
        }
        finally
        {
            NativeMemory.Free(cancel);
            handle.Free();
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int OnProgress(byte* message, nint context)
    {
        var state = (State)GCHandle.FromIntPtr(context).Target!;
        if (*(int*)message == ChunkFinished)
        {
            // COPYFILE2_MESSAGE.Info.ChunkFinished.uliTotalBytesTransferred (x64: desplazamiento 72).
            long total = *(long*)(message + 72);
            long delta = total - state.Reported;
            state.Reported = total;
            if (delta > 0)
            {
                state.Progress(delta);
            }
        }

        return state.Token.IsCancellationRequested ? CancelCopy : ContinueCopy;
    }

    private sealed class State(Action<long> progress, CancellationToken token)
    {
        public Action<long> Progress { get; } = progress;

        public CancellationToken Token { get; } = token;

        public long Reported { get; set; }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ExtendedParameters
    {
        public uint Size;
        public uint CopyFlags;
        public int* Cancel;
        public delegate* unmanaged[Stdcall]<byte*, nint, int> ProgressRoutine;
        public nint CallbackContext;
    }

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int CopyFile2(string existing, string target, ExtendedParameters* parameters);
}
