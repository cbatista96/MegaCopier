using System.Runtime.InteropServices;

namespace MegaCopier.Engine.Remote;

/// <summary>
/// Un móvil o cámara por Windows Portable Devices (la misma interfaz que usa el Explorador para "Este equipo > Pixel").
/// Llamadas COM por la tabla de métodos (índices y firmas comprobados con PortableDeviceApi.h y PortableDeviceTypes.h
/// del SDK 10.0.26100), así que funciona con Native AOT. Los objetos son "free-threaded": vale cualquier hilo.
/// </summary>
public sealed unsafe partial class WpdDevice : IRemoteStore
{
    private static readonly Guid ClsidManager = new("0AF10CEC-2ECD-4B92-9581-34F6AE0637F3");
    private static readonly Guid IidManager = new("A1567595-4C2F-4574-A6FA-ECEF917B9A40");
    private static readonly Guid ClsidDeviceFtm = new("F7C0039A-4762-488A-B4B3-760EF9A1BA9B");
    private static readonly Guid IidDevice = new("625E2DF8-6392-4CF0-9AD1-3CFA5F17775C");
    private static readonly Guid ClsidValues = new("0C15D503-D017-47CE-9016-7B3F978721CC");
    private static readonly Guid IidValues = new("6848F6F2-3155-4F86-B6F5-263EEEAB3143");
    private static readonly Guid ClsidKeys = new("DE2D022D-2480-43BE-97F0-D1FA2CF98F4F");
    private static readonly Guid IidKeys = new("DADA2357-E0AD-492E-98DB-DD61C53BA353");
    private static readonly Guid ClsidPropVariants = new("08A99E2F-6D6D-4B80-AF5A-BAF2BCBE4CB9");
    private static readonly Guid IidPropVariants = new("89B2E422-4F1B-4316-BCEF-A44AFEA83EB3");
    private static readonly Guid IidDataStream = new("88E04DB3-1012-4D64-9996-F703A950D3F4");

    private static readonly Guid ObjectProperties = new("EF6B490D-5CD8-437A-AFFC-DA8B60EE4A3C");
    private static readonly Guid ClientProperties = new("204D9F0C-2292-4080-9F42-40664E70F859");
    private static readonly Guid FunctionalProperties = new("8F052D93-ABCA-4FC5-A5AC-B01DF4DBE598");
    private static readonly Guid ResourceProperties = new("E81E79BE-34F0-41BF-B53F-F1A06AE87842");

    private static readonly Guid ContentFolder = new("27E2E392-A111-48E0-AB0C-E17705A05F85");
    private static readonly Guid ContentFunctional = new("99ED0160-17FF-4C44-9D98-1D7A6F941921");
    private static readonly Guid ContentGenericFile = new("0085E0A6-8D34-45D7-BC5C-447E59C73D48");
    private static readonly Guid CategoryStorage = new("23F05BBC-15DE-4C2A-A55B-A9AF5CE412EF");
    private static readonly Guid FormatUnspecified = new("30000000-AE6C-4804-98BA-C57B46965FE7");
    private static readonly Guid FormatPropertiesOnly = new("30010000-AE6C-4804-98BA-C57B46965FE7");

    private static readonly PropertyKey KeyParentId = new(ObjectProperties, 3);
    private static readonly PropertyKey KeyName = new(ObjectProperties, 4);
    private static readonly PropertyKey KeyFormat = new(ObjectProperties, 6);
    private static readonly PropertyKey KeyContentType = new(ObjectProperties, 7);
    private static readonly PropertyKey KeySize = new(ObjectProperties, 11);
    private static readonly PropertyKey KeyOriginalFileName = new(ObjectProperties, 12);
    private static readonly PropertyKey KeyDateModified = new(ObjectProperties, 19);
    private static readonly PropertyKey KeyCategory = new(FunctionalProperties, 2);
    private static readonly PropertyKey KeyResourceDefault = new(ResourceProperties, 0);

    private void* _device;
    private void* _content;
    private void* _properties;
    private void* _resources;
    private void* _keys;

    private WpdDevice(void* device)
    {
        _device = device;
        Check(Call(_device, 5, out var content), "Content");
        _content = content;
        Check(Call(_content, 4, out var properties), "Properties");
        _properties = properties;
        Check(Call(_content, 5, out var resources), "Transfer");
        _resources = resources;

        _keys = Create(ClsidKeys, IidKeys);
        foreach (var key in new[] { KeyName, KeyOriginalFileName, KeyContentType, KeySize, KeyDateModified, KeyCategory })
        {
            var copy = key;
            // IPortableDeviceKeyCollection::Add (índice 5)
            ((delegate* unmanaged[Stdcall]<void*, PropertyKey*, int>)Slot(_keys, 5))(_keys, &copy);
        }
    }

    public string RootId => "DEVICE";

    /// <summary>Móviles y cámaras conectados ahora mismo.</summary>
    public static IReadOnlyList<MtpDeviceInfo> List()
    {
        EnsureCom();
        void* manager = null;
        try
        {
            manager = Create(ClsidManager, IidManager);
            uint count = 0;
            // IPortableDeviceManager::RefreshDeviceList (4) y GetDevices (3): primero el número, luego los ids.
            ((delegate* unmanaged[Stdcall]<void*, int>)Slot(manager, 4))(manager);
            if (((delegate* unmanaged[Stdcall]<void*, char**, uint*, int>)Slot(manager, 3))(manager, null, &count) < 0 || count == 0)
            {
                return [];
            }

            var ids = new nint[count];
            fixed (nint* buffer = ids)
            {
                if (((delegate* unmanaged[Stdcall]<void*, char**, uint*, int>)Slot(manager, 3))(manager, (char**)buffer, &count) < 0)
                {
                    return [];
                }
            }

            var result = new List<MtpDeviceInfo>();
            for (int i = 0; i < count; i++)
            {
                string id = new((char*)ids[i]);
                Marshal.FreeCoTaskMem(ids[i]);
                result.Add(new MtpDeviceInfo(id, FriendlyName(manager, id)));
            }

            return result;
        }
        catch (COMException)
        {
            return [];
        }
        finally
        {
            Release(manager);
        }
    }

    private static string FriendlyName(void* manager, string id)
    {
        uint length = 0;
        fixed (char* idPointer = id)
        {
            // IPortableDeviceManager::GetDeviceFriendlyName (índice 5): primero la longitud.
            var call = (delegate* unmanaged[Stdcall]<void*, char*, char*, uint*, int>)Slot(manager, 5);
            if (call(manager, idPointer, null, &length) < 0 || length == 0)
            {
                return id;
            }

            char* buffer = stackalloc char[(int)Math.Min(length, 1024)];
            length = Math.Min(length, 1024);
            return call(manager, idPointer, buffer, &length) < 0 ? id : new string(buffer).Trim();
        }
    }

    /// <summary>Abre el dispositivo (para leer y escribir; si no deja escribir, solo para leer).</summary>
    public static WpdDevice Open(string deviceId)
    {
        EnsureCom();
        foreach (uint access in new[] { 0xC0000000u /* GENERIC_READ | GENERIC_WRITE */, 0x80000000u /* GENERIC_READ */ })
        {
            void* device = Create(ClsidDeviceFtm, IidDevice);
            void* client = Create(ClsidValues, IidValues);
            try
            {
                SetString(client, new PropertyKey(ClientProperties, 2), "MegaCopier");
                SetUInt(client, new PropertyKey(ClientProperties, 3), 1);
                SetUInt(client, new PropertyKey(ClientProperties, 4), 0);
                SetUInt(client, new PropertyKey(ClientProperties, 5), 0);
                SetUInt(client, new PropertyKey(ClientProperties, 8), 0x00020000 /* SECURITY_IMPERSONATION */);
                SetUInt(client, new PropertyKey(ClientProperties, 9), access);

                int hr;
                fixed (char* id = deviceId)
                {
                    // IPortableDevice::Open (índice 3)
                    hr = ((delegate* unmanaged[Stdcall]<void*, char*, void*, int>)Slot(device, 3))(device, id, client);
                }

                if (hr >= 0)
                {
                    return new WpdDevice(device);
                }

                Release(device);
                if (hr != unchecked((int)0x80070005) /* E_ACCESSDENIED: se reintenta solo lectura */ || access == 0x80000000u)
                {
                    Check(hr, "Open");
                }
            }
            finally
            {
                Release(client);
            }
        }

        throw new IOException("No se pudo abrir el dispositivo.");
    }

    public IReadOnlyList<RemoteItem> Children(string parentId)
    {
        var ids = new List<string>();
        void* enumerator = null;
        try
        {
            fixed (char* parent = parentId)
            {
                // IPortableDeviceContent::EnumObjects (índice 3)
                Check(((delegate* unmanaged[Stdcall]<void*, uint, char*, void*, void**, int>)Slot(_content, 3))(_content, 0, parent, null, &enumerator), "EnumObjects");
            }

            var batch = new nint[64];
            while (true)
            {
                uint fetched = 0;
                int hr;
                fixed (nint* buffer = batch)
                {
                    // IEnumPortableDeviceObjectIDs::Next (índice 3); S_FALSE (1) = no quedan más
                    hr = ((delegate* unmanaged[Stdcall]<void*, uint, char**, uint*, int>)Slot(enumerator, 3))(enumerator, (uint)batch.Length, (char**)buffer, &fetched);
                }

                for (int i = 0; i < fetched; i++)
                {
                    ids.Add(new string((char*)batch[i]));
                    Marshal.FreeCoTaskMem(batch[i]);
                }

                if (hr != 0 || fetched == 0)
                {
                    break;
                }
            }
        }
        finally
        {
            Release(enumerator);
        }

        var result = new List<RemoteItem>(ids.Count);
        foreach (string id in ids)
        {
            if (Describe(id) is { } item)
            {
                result.Add(item);
            }
        }

        return result;
    }

    /// <summary>Nombre, tipo, tamaño y fecha de un objeto; null si es un objeto funcional que no es un almacenamiento.</summary>
    private RemoteItem? Describe(string id)
    {
        void* values = null;
        try
        {
            fixed (char* idPointer = id)
            {
                // IPortableDeviceProperties::GetValues (índice 5)
                if (((delegate* unmanaged[Stdcall]<void*, char*, void*, void**, int>)Slot(_properties, 5))(_properties, idPointer, _keys, &values) < 0 || values == null)
                {
                    return null;
                }
            }

            Guid type = GetGuid(values, KeyContentType);
            if (type == ContentFunctional && GetGuid(values, KeyCategory) != CategoryStorage)
            {
                return null; // información del dispositivo, servicios...: no son carpetas con archivos
            }

            bool folder = type == ContentFolder || type == ContentFunctional;
            string name = (folder ? null : GetString(values, KeyOriginalFileName)) ?? GetString(values, KeyName) ?? id;
            long size = folder ? 0 : (long)GetULong(values, KeySize);
            return new RemoteItem(id, name, folder, size, GetDate(values, KeyDateModified));
        }
        finally
        {
            Release(values);
        }
    }

    public Stream OpenRead(string objectId)
    {
        void* stream = null;
        uint optimal = 0;
        var key = KeyResourceDefault;
        fixed (char* id = objectId)
        {
            // IPortableDeviceResources::GetStream (índice 5), STGM_READ
            Check(((delegate* unmanaged[Stdcall]<void*, char*, PropertyKey*, uint, uint*, void**, int>)Slot(_resources, 5))(_resources, id, &key, 0, &optimal, &stream), "GetStream");
        }

        return new ComStream(stream, writable: false);
    }

    public string Upload(string parentId, string name, Stream data, long length, CancellationToken cancellationToken)
    {
        void* values = Create(ClsidValues, IidValues);
        void* stream = null;
        bool committed = false;
        try
        {
            SetString(values, KeyParentId, parentId);
            SetString(values, KeyName, name);
            SetString(values, KeyOriginalFileName, name);
            SetULong(values, KeySize, (ulong)length);
            SetGuid(values, KeyContentType, ContentGenericFile);
            SetGuid(values, KeyFormat, FormatUnspecified);

            uint optimal = 0;
            // IPortableDeviceContent::CreateObjectWithPropertiesAndData (índice 7)
            Check(((delegate* unmanaged[Stdcall]<void*, void*, void**, uint*, char**, int>)Slot(_content, 7))(_content, values, &stream, &optimal, null), "CreateObjectWithPropertiesAndData");

            byte[] buffer = new byte[Math.Clamp((int)optimal, 64 * 1024, 4 << 20)];
            int read;
            while ((read = data.Read(buffer, 0, buffer.Length)) > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                uint written = 0;
                fixed (byte* pointer = buffer)
                {
                    // IStream::Write (índice 4)
                    Check(((delegate* unmanaged[Stdcall]<void*, byte*, uint, uint*, int>)Slot(stream, 4))(stream, pointer, (uint)read, &written), "Write");
                }
            }

            // IStream::Commit (índice 8): hasta aquí el archivo no existe en el dispositivo
            Check(((delegate* unmanaged[Stdcall]<void*, uint, int>)Slot(stream, 8))(stream, 0), "Commit");
            committed = true;

            void* dataStream = null;
            Guid iid = IidDataStream;
            char* newId = null;
            if (QueryInterface(stream, &iid, &dataStream) >= 0)
            {
                // IPortableDeviceDataStream::GetObjectID (índice 14)
                ((delegate* unmanaged[Stdcall]<void*, char**, int>)Slot(dataStream, 14))(dataStream, &newId);
                Release(dataStream);
            }

            return TakeString(newId) ?? "";
        }
        finally
        {
            if (!committed && stream != null)
            {
                void* dataStream = null;
                Guid iid = IidDataStream;
                if (QueryInterface(stream, &iid, &dataStream) >= 0)
                {
                    ((delegate* unmanaged[Stdcall]<void*, int>)Slot(dataStream, 15))(dataStream); // Cancel: no deja medio archivo
                    Release(dataStream);
                }
            }

            Release(stream);
            Release(values);
        }
    }

    public string CreateFolder(string parentId, string name)
    {
        void* values = Create(ClsidValues, IidValues);
        try
        {
            SetString(values, KeyParentId, parentId);
            SetString(values, KeyName, name);
            SetString(values, KeyOriginalFileName, name);
            SetGuid(values, KeyContentType, ContentFolder);
            SetGuid(values, KeyFormat, FormatPropertiesOnly);
            char* id = null;
            // IPortableDeviceContent::CreateObjectWithPropertiesOnly (índice 6)
            Check(((delegate* unmanaged[Stdcall]<void*, void*, char**, int>)Slot(_content, 6))(_content, values, &id), "CreateObjectWithPropertiesOnly");
            return TakeString(id) ?? throw new IOException("El dispositivo no devolvió la carpeta creada.");
        }
        finally
        {
            Release(values);
        }
    }

    public void Delete(string objectId, bool recursive)
    {
        void* collection = Create(ClsidPropVariants, IidPropVariants);
        try
        {
            fixed (char* id = objectId)
            {
                var value = new PropVariant { vt = 31 /* VT_LPWSTR */, pointer = (nint)id };
                // IPortableDevicePropVariantCollection::Add (índice 5): copia el valor
                Check(((delegate* unmanaged[Stdcall]<void*, PropVariant*, int>)Slot(collection, 5))(collection, &value), "Add");
            }

            // IPortableDeviceContent::Delete (índice 8)
            Check(((delegate* unmanaged[Stdcall]<void*, uint, void*, void**, int>)Slot(_content, 8))(_content, recursive ? 1u : 0u, collection, null), "Delete");
        }
        finally
        {
            Release(collection);
        }
    }

    public void Dispose()
    {
        if (_device != null)
        {
            ((delegate* unmanaged[Stdcall]<void*, int>)Slot(_device, 8))(_device); // IPortableDevice::Close
        }

        Release(_keys);
        Release(_resources);
        Release(_properties);
        Release(_content);
        Release(_device);
        _keys = _resources = _properties = _content = _device = null;
    }

    // --- Valores (IPortableDeviceValues) ---

    private static void SetString(void* values, PropertyKey key, string value)
    {
        fixed (char* text = value)
        {
            Check(((delegate* unmanaged[Stdcall]<void*, PropertyKey*, char*, int>)Slot(values, 7))(values, &key, text), "SetStringValue");
        }
    }

    private static void SetUInt(void* values, PropertyKey key, uint value) =>
        Check(((delegate* unmanaged[Stdcall]<void*, PropertyKey*, uint, int>)Slot(values, 9))(values, &key, value), "SetUnsignedIntegerValue");

    private static void SetULong(void* values, PropertyKey key, ulong value) =>
        Check(((delegate* unmanaged[Stdcall]<void*, PropertyKey*, ulong, int>)Slot(values, 13))(values, &key, value), "SetUnsignedLargeIntegerValue");

    private static void SetGuid(void* values, PropertyKey key, Guid value) =>
        Check(((delegate* unmanaged[Stdcall]<void*, PropertyKey*, Guid*, int>)Slot(values, 27))(values, &key, &value), "SetGuidValue");

    private static string? GetString(void* values, PropertyKey key)
    {
        char* text = null;
        return ((delegate* unmanaged[Stdcall]<void*, PropertyKey*, char**, int>)Slot(values, 8))(values, &key, &text) < 0 ? null : TakeString(text);
    }

    private static ulong GetULong(void* values, PropertyKey key)
    {
        ulong value = 0;
        ((delegate* unmanaged[Stdcall]<void*, PropertyKey*, ulong*, int>)Slot(values, 14))(values, &key, &value);
        return value;
    }

    private static Guid GetGuid(void* values, PropertyKey key)
    {
        Guid value = Guid.Empty;
        ((delegate* unmanaged[Stdcall]<void*, PropertyKey*, Guid*, int>)Slot(values, 28))(values, &key, &value);
        return value;
    }

    private static DateTime? GetDate(void* values, PropertyKey key)
    {
        var value = default(PropVariant);
        // IPortableDeviceValues::GetValue (índice 6): VT_DATE en hora local
        if (((delegate* unmanaged[Stdcall]<void*, PropertyKey*, PropVariant*, int>)Slot(values, 6))(values, &key, &value) < 0)
        {
            return null;
        }

        try
        {
            return value.vt == 7 /* VT_DATE */ && value.date > 0
                ? DateTime.SpecifyKind(DateTime.FromOADate(value.date), DateTimeKind.Local).ToUniversalTime()
                : null;
        }
        finally
        {
            PropVariantClear(&value);
        }
    }

    private static string? TakeString(char* text)
    {
        if (text == null)
        {
            return null;
        }

        string result = new(text);
        Marshal.FreeCoTaskMem((nint)text);
        return result;
    }

    // --- COM ---

    private static void EnsureCom() => CoInitializeEx(null, 0 /* COINIT_MULTITHREADED */); // S_FALSE o "otro modo" también valen

    private static void* Create(Guid clsid, Guid iid)
    {
        void* result = null;
        Check(CoCreateInstance(&clsid, null, 1 /* CLSCTX_INPROC_SERVER */, &iid, &result), "CoCreateInstance");
        return result;
    }

    private static void* Slot(void* com, int index) => (*(void***)com)[index];

    private static int Call(void* com, int index, out void* result)
    {
        void* value = null;
        int hr = ((delegate* unmanaged[Stdcall]<void*, void**, int>)Slot(com, index))(com, &value);
        result = value;
        return hr;
    }

    private static int QueryInterface(void* com, Guid* iid, void** result) =>
        ((delegate* unmanaged[Stdcall]<void*, Guid*, void**, int>)Slot(com, 0))(com, iid, result);

    private static void Release(void* com)
    {
        if (com != null)
        {
            ((delegate* unmanaged[Stdcall]<void*, uint>)Slot(com, 2))(com);
        }
    }

    private static void Check(int hr, string what)
    {
        if (hr < 0)
        {
            throw new IOException($"{what}: 0x{hr:X8}", hr);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct PropertyKey(Guid fmtid, uint pid)
    {
        public readonly Guid FormatId = fmtid;
        public readonly uint PropertyId = pid;
    }

    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct PropVariant
    {
        [FieldOffset(0)]
        public ushort vt;

        [FieldOffset(8)]
        public double date;

        [FieldOffset(8)]
        public nint pointer;
    }

    /// <summary>Un IStream de COM visto como Stream de .NET (solo lectura secuencial).</summary>
    private sealed class ComStream(void* stream, bool writable) : Stream
    {
        private void* _stream = stream;

        public override bool CanRead => !writable;

        public override bool CanSeek => false;

        public override bool CanWrite => writable;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            uint read = 0;
            fixed (byte* pointer = buffer)
            {
                // IStream::Read (índice 3); S_FALSE con menos bytes = final
                Check(((delegate* unmanaged[Stdcall]<void*, byte*, uint, uint*, int>)Slot(_stream, 3))(_stream, pointer, (uint)buffer.Length, &read), "Read");
            }

            return (int)read;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            Release(_stream);
            _stream = null;
            base.Dispose(disposing);
        }
    }

    [LibraryImport("ole32.dll")]
    private static partial int CoInitializeEx(void* reserved, uint model);

    [LibraryImport("ole32.dll")]
    private static partial int CoCreateInstance(Guid* clsid, void* outer, uint context, Guid* iid, void** result);

    [LibraryImport("ole32.dll")]
    private static partial int PropVariantClear(PropVariant* value);
}
