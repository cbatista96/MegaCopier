using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace MegaCopier.App.Services;

/// <summary>
/// Selector de archivos y carpetas a la vez (el de Windows solo deja elegir una de las dos cosas). Es el diálogo
/// «Abrir» de siempre con un botón más, «Seleccionar»: marca una carpeta y la elige entera; entra en ella y marca
/// archivos para elegir solo esos; sin nada marcado, elige la carpeta que estás viendo. «Abrir» sigue entrando en las
/// carpetas y eligiendo archivos como siempre.
/// </summary>
internal static partial class MixedPicker
{
    private const uint SelectButtonId = 1;
    private const int Cancelled = unchecked((int)0x800704C7);

    private static readonly StrategyBasedComWrappers ComWrappers = new();

    /// <summary>Rutas elegidas (archivos y carpetas), o ninguna si se cancela.</summary>
    /// <param name="title">Título del diálogo.</param>
    /// <param name="filters">Tipos de archivo («Sumas», "*.sha256;*.md5"...); sin ellos, todos.</param>
    public static IReadOnlyList<string> Pick(string title, params (string Name, string Pattern)[] filters)
    {
        if (CoCreateInstance(new Guid("DC1C5A9C-E88A-4dde-A5A1-60F82A20AEF7"), 0, 1 /* CLSCTX_INPROC_SERVER */,
            typeof(IFileOpenDialog).GUID, out nint unknown) < 0)
        {
            return [];
        }

        var dialog = (IFileOpenDialog)ComWrappers.GetOrCreateObjectForComInstance(unknown, CreateObjectFlags.None);
        Marshal.Release(unknown);
        var events = new DialogEvents(dialog);
        uint cookie = 0;
        try
        {
            dialog.GetOptions(out uint options);
            dialog.SetOptions(options | 0x200 /* ALLOWMULTISELECT */ | 0x40 /* FORCEFILESYSTEM */ | 0x800 /* PATHMUSTEXIST */ | 0x8 /* NOCHANGEDIR */);
            dialog.SetTitle(title);
            dialog.SetOkButtonLabel(L.Get("Picker_Open"));
            if (filters.Length > 0)
            {
                SetFilters(dialog, [.. filters, (L.Get("Picker_AllFiles"), "*.*")]);
            }

            ((IFileDialogCustomize)dialog).AddPushButton(SelectButtonId, L.Get("Picker_SelectHere"));
            dialog.Advise(events, out cookie);
            int result = dialog.Show(((App)Microsoft.UI.Xaml.Application.Current).Window.Handle);
            if (events.Chosen is { } chosen)
            {
                return chosen; // con el botón «Seleccionar»
            }

            if (result == Cancelled || result < 0)
            {
                return [];
            }

            dialog.GetResults(out var items);
            return Paths(items);
        }
        catch (COMException)
        {
            return [];
        }
        finally
        {
            if (cookie != 0)
            {
                dialog.Unadvise(cookie);
            }
        }
    }

    private static unsafe void SetFilters(IFileOpenDialog dialog, (string Name, string Pattern)[] filters)
    {
        var specs = new FilterSpec[filters.Length];
        var handles = new List<GCHandle>();
        try
        {
            for (int i = 0; i < filters.Length; i++)
            {
                var name = GCHandle.Alloc(filters[i].Name + '\0', GCHandleType.Pinned);
                var pattern = GCHandle.Alloc(filters[i].Pattern + '\0', GCHandleType.Pinned);
                handles.Add(name);
                handles.Add(pattern);
                specs[i] = new FilterSpec { Name = name.AddrOfPinnedObject(), Pattern = pattern.AddrOfPinnedObject() };
            }

            fixed (FilterSpec* pointer = specs)
            {
                dialog.SetFileTypes((uint)specs.Length, (nint)pointer);
            }
        }
        finally
        {
            foreach (var handle in handles)
            {
                handle.Free();
            }
        }
    }

    private static List<string> Paths(IShellItemArray? items)
    {
        var paths = new List<string>();
        if (items is null)
        {
            return paths;
        }

        items.GetCount(out uint count);
        for (uint i = 0; i < count; i++)
        {
            items.GetItemAt(i, out var item);
            if (PathOf(item) is { } path)
            {
                paths.Add(path);
            }
        }

        return paths;
    }

    private static string? PathOf(IShellItem? item)
    {
        try
        {
            if (item is null)
            {
                return null;
            }

            item.GetDisplayName(0x80058000 /* SIGDN_FILESYSPATH */, out string path);
            return path;
        }
        catch (COMException)
        {
            return null; // un sitio virtual (Este equipo, Red...) no es una ruta
        }
    }

    /// <summary>Atiende el botón «Seleccionar» y pone en él cuántos elementos hay marcados.</summary>
    [GeneratedComClass]
    private sealed partial class DialogEvents(IFileOpenDialog dialog) : IFileDialogEvents, IFileDialogControlEvents
    {
        public List<string>? Chosen { get; private set; }

        public int OnFileOk(nint dialogPointer) => 0;

        public int OnFolderChanging(nint dialogPointer, nint folder) => 0;

        public int OnFolderChange(nint dialogPointer)
        {
            UpdateLabel();
            return 0;
        }

        public int OnSelectionChange(nint dialogPointer)
        {
            UpdateLabel();
            return 0;
        }

        public int OnShareViolation(nint dialogPointer, nint item, out int response)
        {
            response = 0;
            return 0;
        }

        public int OnTypeChange(nint dialogPointer) => 0;

        public int OnOverwrite(nint dialogPointer, nint item, out int response)
        {
            response = 0;
            return 0;
        }

        public int OnItemSelected(nint customize, uint control, uint item) => 0;

        public int OnButtonClicked(nint customize, uint control)
        {
            if (control != SelectButtonId)
            {
                return 0;
            }

            var chosen = Selection();
            if (chosen.Count == 0)
            {
                // Nada marcado: la carpeta que se está viendo.
                dialog.GetFolder(out var folder);
                if (PathOf(folder) is { } path)
                {
                    chosen.Add(path);
                }
            }

            if (chosen.Count > 0)
            {
                Chosen = chosen;
                dialog.Close(0);
            }

            return 0;
        }

        public int OnCheckButtonToggled(nint customize, uint control, int isChecked) => 0;

        public int OnControlActivating(nint customize, uint control) => 0;

        private List<string> Selection()
        {
            try
            {
                dialog.GetSelectedItems(out var items);
                return Paths(items);
            }
            catch (COMException)
            {
                return [];
            }
        }

        private void UpdateLabel()
        {
            try
            {
                int count = Selection().Count;
                ((IFileDialogCustomize)dialog).SetControlLabel(SelectButtonId, count switch
                {
                    0 => L.Get("Picker_SelectHere"),
                    1 => L.Get("Picker_SelectOne"),
                    _ => L.F("Picker_SelectMany", count),
                });
            }
            catch (COMException)
            {
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FilterSpec
    {
        public nint Name;
        public nint Pattern;
    }

    [LibraryImport("ole32.dll")]
    private static partial int CoCreateInstance(in Guid clsid, nint outer, uint context, in Guid iid, out nint instance);

    [GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
    [Guid("42f85136-db7e-439c-85f1-e4075d135fc8")]
    internal partial interface IFileDialog
    {
        // IModalWindow
        [PreserveSig]
        int Show(nint owner);

        void SetFileTypes(uint count, nint specs);

        void SetFileTypeIndex(uint index);

        void GetFileTypeIndex(out uint index);

        void Advise(IFileDialogEvents events, out uint cookie);

        void Unadvise(uint cookie);

        void SetOptions(uint options);

        void GetOptions(out uint options);

        void SetDefaultFolder(IShellItem folder);

        void SetFolder(IShellItem folder);

        void GetFolder(out IShellItem folder);

        void GetCurrentSelection(out IShellItem item);

        void SetFileName(string name);

        void GetFileName(out string name);

        void SetTitle(string title);

        void SetOkButtonLabel(string text);

        void SetFileNameLabel(string label);

        void GetResult(out IShellItem item);

        void AddPlace(IShellItem item, int place);

        void SetDefaultExtension(string extension);

        void Close(int result);

        void SetClientGuid(in Guid guid);

        void ClearClientData();

        void SetFilter(nint filter);
    }

    [GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
    [Guid("d57c7288-d4ad-4768-be02-9d969532d960")]
    internal partial interface IFileOpenDialog : IFileDialog
    {
        void GetResults(out IShellItemArray items);

        void GetSelectedItems(out IShellItemArray items);
    }

    [GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
    [Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe")]
    internal partial interface IShellItem
    {
        void BindToHandler(nint context, in Guid handler, in Guid iid, out nint result);

        void GetParent(out IShellItem parent);

        void GetDisplayName(uint form, out string name);

        void GetAttributes(uint mask, out uint attributes);

        void Compare(IShellItem other, uint hint, out int order);
    }

    [GeneratedComInterface]
    [Guid("b63ea76d-1f85-456f-a19c-48159efa858b")]
    internal partial interface IShellItemArray
    {
        void BindToHandler(nint context, in Guid handler, in Guid iid, out nint result);

        void GetPropertyStore(int flags, in Guid iid, out nint store);

        void GetPropertyDescriptionList(nint keyType, in Guid iid, out nint list);

        void GetAttributes(int flags, uint mask, out uint attributes);

        void GetCount(out uint count);

        void GetItemAt(uint index, out IShellItem item);

        void EnumItems(out nint items);
    }

    [GeneratedComInterface]
    [Guid("973510db-7d7f-452b-8975-74a85828d354")]
    internal partial interface IFileDialogEvents
    {
        [PreserveSig]
        int OnFileOk(nint dialog);

        [PreserveSig]
        int OnFolderChanging(nint dialog, nint folder);

        [PreserveSig]
        int OnFolderChange(nint dialog);

        [PreserveSig]
        int OnSelectionChange(nint dialog);

        [PreserveSig]
        int OnShareViolation(nint dialog, nint item, out int response);

        [PreserveSig]
        int OnTypeChange(nint dialog);

        [PreserveSig]
        int OnOverwrite(nint dialog, nint item, out int response);
    }

    [GeneratedComInterface]
    [Guid("36116642-d713-4b97-9b83-7484a9d00433")]
    internal partial interface IFileDialogControlEvents
    {
        [PreserveSig]
        int OnItemSelected(nint customize, uint control, uint item);

        [PreserveSig]
        int OnButtonClicked(nint customize, uint control);

        [PreserveSig]
        int OnCheckButtonToggled(nint customize, uint control, int isChecked);

        [PreserveSig]
        int OnControlActivating(nint customize, uint control);
    }

    [GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
    [Guid("e6fdd21a-163f-4975-9c8c-a69f1ba37034")]
    internal partial interface IFileDialogCustomize
    {
        void EnableOpenDropDown(uint control);

        void AddMenu(uint control, string label);

        void AddPushButton(uint control, string label);

        void AddComboBox(uint control);

        void AddRadioButtonList(uint control);

        void AddCheckButton(uint control, string label, int isChecked);

        void AddEditBox(uint control, string text);

        void AddSeparator(uint control);

        void AddText(uint control, string text);

        void SetControlLabel(uint control, string label);
    }
}
