using System.IO.Pipes;
using System.Runtime.InteropServices;

namespace MegaCopier.Engine.Native;

/// <summary>
/// Copia de archivos cifrados con EFS sin descifrarlos (como robocopy /EFSRAW): se exporta el formato de copia de
/// seguridad de EFS (ReadEncryptedFileRaw) y se importa en el destino (WriteEncryptedFileRaw). El destino queda cifrado
/// con las mismas claves, y no hace falta poder abrir el contenido: basta con tener acceso al archivo.
/// </summary>
internal static unsafe partial class EfsRaw
{
    private const uint CreateForImport = 1;

    /// <summary>
    /// Exporta <paramref name="source"/> y lo importa en <paramref name="destination"/>. <paramref name="onBlock"/> recibe
    /// cada bloque del formato exportado (para el progreso y el hash) y puede lanzar una excepción para cancelar.
    /// </summary>
    public static void Copy(string source, string destination, Action<ReadOnlySpan<byte>> onBlock)
    {
        // La exportación entrega los datos a una función y la importación los pide a otra: se unen con una tubería, la
        // exportación en otro hilo. Así no hace falta guardar el archivo entero en memoria.
        using var pipeOut = new AnonymousPipeServerStream(PipeDirection.Out);
        using var pipeIn = new AnonymousPipeClientStream(PipeDirection.In, pipeOut.ClientSafePipeHandle);
        Exception? exportError = null;
        var export = new Thread(() =>
        {
            try
            {
                Export(source, block =>
                {
                    onBlock(block);
                    pipeOut.Write(block);
                });
            }
            catch (Exception ex)
            {
                exportError = ex;
            }
            finally
            {
                pipeOut.Dispose(); // fin de los datos para la importación
            }
        })
        { IsBackground = true, Name = "EFS raw export" };
        export.Start();
        try
        {
            Import(destination, pipeIn);
        }
        catch
        {
            pipeIn.Dispose(); // la exportación se queda sin lector y termina con error
            export.Join();
            if (exportError is not null and not IOException { HResult: unchecked((int)0x8007006D) })
            {
                throw exportError; // falló al leer el origen: la importación solo se quedó sin datos
            }

            throw;
        }

        export.Join();
        if (exportError is not null)
        {
            throw exportError;
        }
    }

    /// <summary>Recorre el formato exportado de un archivo (para verificar la copia).</summary>
    public static void Export(string path, Action<ReadOnlySpan<byte>> onBlock)
    {
        Check(OpenEncryptedFileRawW(LongPath(path), 0, out nint context), path);
        var state = new ExportState(onBlock);
        var handle = GCHandle.Alloc(state);
        try
        {
            uint error = ReadEncryptedFileRaw(&ExportCallback, GCHandle.ToIntPtr(handle), context);
            if (state.Error is not null)
            {
                throw state.Error;
            }

            Check(error, path);
        }
        finally
        {
            handle.Free();
            CloseEncryptedFileRaw(context);
        }
    }

    private static void Import(string path, Stream data)
    {
        Check(OpenEncryptedFileRawW(LongPath(path), CreateForImport, out nint context), path);
        var state = new ImportState(data);
        var handle = GCHandle.Alloc(state);
        try
        {
            uint error = WriteEncryptedFileRaw(&ImportCallback, GCHandle.ToIntPtr(handle), context);
            if (state.Error is not null)
            {
                throw state.Error;
            }

            Check(error, path);
        }
        finally
        {
            handle.Free();
            CloseEncryptedFileRaw(context);
        }
    }

    private const uint ErrorSuccess = 0;
    private const uint ErrorCancelled = 1223;

    [UnmanagedCallersOnly]
    private static uint ExportCallback(byte* data, nint context, uint length)
    {
        var state = (ExportState)GCHandle.FromIntPtr(context).Target!;
        try
        {
            state.OnBlock(new ReadOnlySpan<byte>(data, (int)length));
            return ErrorSuccess;
        }
        catch (Exception ex)
        {
            state.Error = ex;
            return ErrorCancelled;
        }
    }

    [UnmanagedCallersOnly]
    private static uint ImportCallback(byte* data, nint context, uint* length)
    {
        var state = (ImportState)GCHandle.FromIntPtr(context).Target!;
        try
        {
            // Se rellena lo que se pueda; 0 bytes indica a Windows que no hay más.
            *length = (uint)state.Data.Read(new Span<byte>(data, (int)*length));
            return ErrorSuccess;
        }
        catch (Exception ex)
        {
            state.Error = ex;
            *length = 0;
            return ErrorCancelled;
        }
    }

    private static void Check(uint error, string path)
    {
        if (error != ErrorSuccess)
        {
            int code = (int)error;
            throw code == 5 ? new UnauthorizedAccessException(path) : new IOException($"{Marshal.GetPInvokeErrorMessage(code)} ({path})", unchecked((int)0x80070000 | code));
        }
    }

    private static string LongPath(string path) =>
        path.StartsWith(@"\\?\", StringComparison.Ordinal) ? path
        : path.StartsWith(@"\\", StringComparison.Ordinal) ? @"\\?\UNC\" + path[2..]
        : @"\\?\" + Path.GetFullPath(path);

    private sealed class ExportState(Action<ReadOnlySpan<byte>> onBlock)
    {
        public Action<ReadOnlySpan<byte>> OnBlock { get; } = onBlock;

        public Exception? Error { get; set; }
    }

    private sealed class ImportState(Stream data)
    {
        public Stream Data { get; } = data;

        public Exception? Error { get; set; }
    }

    [LibraryImport("advapi32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint OpenEncryptedFileRawW(string fileName, uint flags, out nint context);

    [LibraryImport("advapi32.dll")]
    private static partial uint ReadEncryptedFileRaw(delegate* unmanaged<byte*, nint, uint, uint> export, nint callbackContext, nint context);

    [LibraryImport("advapi32.dll")]
    private static partial uint WriteEncryptedFileRaw(delegate* unmanaged<byte*, nint, uint*, uint> import, nint callbackContext, nint context);

    [LibraryImport("advapi32.dll")]
    private static partial void CloseEncryptedFileRaw(nint context);
}
