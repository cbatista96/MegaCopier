// Extensión del Explorador de MegaCopier.
//
//  - Menú contextual (CLSID_ContextMenu): "Pegar con MegaCopier" en el fondo de una carpeta, del Escritorio o de
//    una unidad, y "Pegar en esta carpeta con MegaCopier" sobre una carpeta, si el portapapeles tiene archivos.
//  - Arrastrar con el botón derecho (CLSID_DragDrop): "Copiar aquí con MegaCopier".
//
// No copia nada por sí misma: entrega la petición al proceso residente MegaCopier.exe por la tubería con nombre
// "\\.\pipe\MegaCopier-<SID>" (mensaje = longitud de 4 bytes + JSON UTF-8). Si la app no está abierta, la arranca
// con la copia como argumentos. Así el Explorador nunca se bloquea por una copia.

#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <shlobj.h>
#include <shellapi.h>
#include <sddl.h>
#include <string>
#include <vector>
#include <new>

// {E2B6C3A1-5F47-4C8E-A1D2-7B3F9C0E6A54}
static const CLSID CLSID_ContextMenu = { 0xe2b6c3a1, 0x5f47, 0x4c8e, { 0xa1, 0xd2, 0x7b, 0x3f, 0x9c, 0x0e, 0x6a, 0x54 } };
// {A9D4F1C7-2B6E-4A3F-8C5D-1E7B0F9A3C62}
static const CLSID CLSID_DragDrop = { 0xa9d4f1c7, 0x2b6e, 0x4a3f, { 0x8c, 0x5d, 0x1e, 0x7b, 0x0f, 0x9a, 0x3c, 0x62 } };

static HMODULE g_module = nullptr;
static LONG g_objects = 0;
static LONG g_locks = 0;

namespace {

// ---------------------------------------------------------------------------------------------------------------
// Idioma: el que publica la app en HKCU\Software\MegaCopier\Language, o el de Windows.

// Se lee cada vez (es instantáneo): el Explorador mantiene la DLL cargada y el usuario puede cambiar el idioma.
bool UseSpanish()
{
    wchar_t value[16] = {};
    DWORD size = sizeof(value);
    if (RegGetValueW(HKEY_CURRENT_USER, L"Software\\MegaCopier", L"Language", RRF_RT_REG_SZ, nullptr, value, &size) == ERROR_SUCCESS && value[0] != L'\0')
    {
        return _wcsnicmp(value, L"es", 2) == 0;
    }

    return PRIMARYLANGID(GetUserDefaultUILanguage()) == LANG_SPANISH;
}

const wchar_t* T(const wchar_t* spanish, const wchar_t* english)
{
    return UseSpanish() ? spanish : english;
}

// Interruptor general de la app (doble clic en el icono de la bandeja). Sin valor: activado.
bool MegaCopierEnabled()
{
    DWORD value = 1;
    DWORD size = sizeof(value);
    RegGetValueW(HKEY_CURRENT_USER, L"Software\\MegaCopier", L"Enabled", RRF_RT_REG_DWORD, nullptr, &value, &size);
    return value != 0;
}

// ---------------------------------------------------------------------------------------------------------------
// Utilidades

std::wstring ModuleDirectory()
{
    wchar_t path[MAX_PATH * 4] = {};
    DWORD length = GetModuleFileNameW(g_module, path, ARRAYSIZE(path));
    std::wstring result(path, length);
    size_t slash = result.find_last_of(L'\\');
    return slash == std::wstring::npos ? result : result.substr(0, slash);
}

std::wstring UserSid()
{
    std::wstring sid;
    HANDLE token = nullptr;
    if (OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, &token))
    {
        DWORD size = 0;
        GetTokenInformation(token, TokenUser, nullptr, 0, &size);
        std::vector<BYTE> buffer(size);
        if (size > 0 && GetTokenInformation(token, TokenUser, buffer.data(), size, &size))
        {
            LPWSTR text = nullptr;
            if (ConvertSidToStringSidW(reinterpret_cast<TOKEN_USER*>(buffer.data())->User.Sid, &text))
            {
                sid = text;
                LocalFree(text);
            }
        }

        CloseHandle(token);
    }

    return sid;
}

std::wstring PathFromIdList(PCIDLIST_ABSOLUTE pidl)
{
    if (pidl == nullptr)
    {
        return L"";
    }

    std::vector<wchar_t> buffer(32768);
    return SHGetPathFromIDListEx(pidl, buffer.data(), static_cast<DWORD>(buffer.size()), GPFIDL_DEFAULT) ? std::wstring(buffer.data()) : L"";
}

std::vector<std::wstring> FilesFromDrop(HDROP drop)
{
    std::vector<std::wstring> files;
    UINT count = DragQueryFileW(drop, 0xFFFFFFFF, nullptr, 0);
    for (UINT i = 0; i < count; i++)
    {
        UINT length = DragQueryFileW(drop, i, nullptr, 0);
        std::wstring file(length, L'\0');
        DragQueryFileW(drop, i, file.data(), length + 1);
        files.push_back(file);
    }

    return files;
}

std::vector<std::wstring> FilesFromDataObject(IDataObject* data)
{
    std::vector<std::wstring> files;
    FORMATETC format = { CF_HDROP, nullptr, DVASPECT_CONTENT, -1, TYMED_HGLOBAL };
    STGMEDIUM medium = {};
    if (data != nullptr && SUCCEEDED(data->GetData(&format, &medium)))
    {
        if (HDROP drop = static_cast<HDROP>(GlobalLock(medium.hGlobal)))
        {
            files = FilesFromDrop(drop);
            GlobalUnlock(medium.hGlobal);
        }

        ReleaseStgMedium(&medium);
    }

    return files;
}

struct ClipboardFiles
{
    std::vector<std::wstring> files;
    bool cut = false;
};

ClipboardFiles ReadClipboard()
{
    ClipboardFiles result;
    if (!IsClipboardFormatAvailable(CF_HDROP))
    {
        return result;
    }

    for (int attempt = 0; attempt < 5 && !OpenClipboard(nullptr); attempt++)
    {
        Sleep(15);
    }

    if (HANDLE drop = GetClipboardData(CF_HDROP))
    {
        result.files = FilesFromDrop(static_cast<HDROP>(drop));
    }

    static const UINT preferredEffect = RegisterClipboardFormatW(CFSTR_PREFERREDDROPEFFECT);
    if (HANDLE effect = GetClipboardData(preferredEffect))
    {
        if (auto* value = static_cast<DWORD*>(GlobalLock(effect)))
        {
            result.cut = (*value & DROPEFFECT_MOVE) != 0;
            GlobalUnlock(effect);
        }
    }

    CloseClipboard();
    return result;
}

std::string ToUtf8(const std::wstring& text)
{
    if (text.empty())
    {
        return {};
    }

    int size = WideCharToMultiByte(CP_UTF8, 0, text.data(), static_cast<int>(text.size()), nullptr, 0, nullptr, nullptr);
    std::string result(size, '\0');
    WideCharToMultiByte(CP_UTF8, 0, text.data(), static_cast<int>(text.size()), result.data(), size, nullptr, nullptr);
    return result;
}

std::string JsonString(const std::wstring& text)
{
    std::string out = "\"";
    for (char c : ToUtf8(text))
    {
        switch (c)
        {
        case '"': out += "\\\""; break;
        case '\\': out += "\\\\"; break;
        case '\n': out += "\\n"; break;
        case '\r': out += "\\r"; break;
        case '\t': out += "\\t"; break;
        default:
            if (static_cast<unsigned char>(c) < 0x20)
            {
                char escaped[8];
                wsprintfA(escaped, "\\u%04x", c);
                out += escaped;
            }
            else
            {
                out += c;
            }
        }
    }

    return out + "\"";
}

std::string CopyRequestJson(const std::vector<std::wstring>& sources, const std::wstring& destination, bool move)
{
    std::string json = std::string("{\"Command\":\"copy\",") + (move ? "\"Move\":true," : "") + "\"Sources\":[";
    for (size_t i = 0; i < sources.size(); i++)
    {
        json += (i > 0 ? "," : "") + JsonString(sources[i]);
    }

    return json + "],\"Destination\":" + JsonString(destination) + "}";
}

bool WriteAll(HANDLE pipe, const void* data, DWORD size)
{
    DWORD written = 0;
    return WriteFile(pipe, data, size, &written, nullptr) && written == size;
}

bool ReadAll(HANDLE pipe, void* data, DWORD size)
{
    auto* bytes = static_cast<BYTE*>(data);
    while (size > 0)
    {
        DWORD read = 0;
        if (!ReadFile(pipe, bytes, size, &read, nullptr) || read == 0)
        {
            return false;
        }

        bytes += read;
        size -= read;
    }

    return true;
}

/// Entrega la petición a la app residente. false si no está abierta o no respondió bien.
bool SendToApp(const std::string& json, std::string& error)
{
    std::wstring sid = UserSid();
    HANDLE mutex = OpenMutexW(SYNCHRONIZE, FALSE, (L"Local\\MegaCopier-" + sid).c_str());
    if (mutex == nullptr)
    {
        return false; // la app no está abierta: no tiene sentido esperar a la tubería
    }

    CloseHandle(mutex);

    std::wstring pipeName = L"\\\\.\\pipe\\MegaCopier-" + sid;
    HANDLE pipe = INVALID_HANDLE_VALUE;
    for (int attempt = 0; attempt < 30 && pipe == INVALID_HANDLE_VALUE; attempt++)
    {
        pipe = CreateFileW(pipeName.c_str(), GENERIC_READ | GENERIC_WRITE, 0, nullptr, OPEN_EXISTING, 0, nullptr);
        if (pipe == INVALID_HANDLE_VALUE)
        {
            if (GetLastError() == ERROR_PIPE_BUSY)
            {
                WaitNamedPipeW(pipeName.c_str(), 500);
            }
            else
            {
                Sleep(100); // la app está arrancando y aún no abrió la tubería
            }
        }
    }

    if (pipe == INVALID_HANDLE_VALUE)
    {
        return false;
    }

    // El Explorador tiene el primer plano: cede el permiso para que la ventana de MegaCopier pueda mostrarse delante.
    ULONG serverPid = 0;
    if (GetNamedPipeServerProcessId(pipe, &serverPid))
    {
        AllowSetForegroundWindow(serverPid);
    }

    bool ok = false;
    DWORD length = static_cast<DWORD>(json.size());
    if (WriteAll(pipe, &length, sizeof(length)) && WriteAll(pipe, json.data(), length))
    {
        DWORD responseLength = 0;
        if (ReadAll(pipe, &responseLength, sizeof(responseLength)) && responseLength < 1 << 20)
        {
            std::string response(responseLength, '\0');
            if (ReadAll(pipe, response.data(), responseLength))
            {
                ok = response.find("\"Ok\":true") != std::string::npos;
                if (!ok)
                {
                    error = response;
                }
            }
        }
    }

    CloseHandle(pipe);
    return ok;
}

std::wstring QuoteArgument(const std::wstring& argument)
{
    // Una barra final antes de la comilla de cierre la escaparía ("D:\" → "D:\\").
    std::wstring quoted = L"\"" + argument;
    if (!argument.empty() && argument.back() == L'\\')
    {
        quoted += L'\\';
    }

    return quoted + L"\"";
}

bool LaunchApp(const std::vector<std::wstring>& sources, const std::wstring& destination, bool move)
{
    std::wstring exe = ModuleDirectory() + L"\\MegaCopier.exe";
    std::wstring commandLine = QuoteArgument(exe) + L" copy " + (move ? L"--move " : L"") + L"--dest " + QuoteArgument(destination);
    for (const auto& source : sources)
    {
        commandLine += L" " + QuoteArgument(source);
    }

    if (commandLine.size() >= 32000)
    {
        return false;
    }

    STARTUPINFOW startup = { sizeof(startup) };
    PROCESS_INFORMATION process = {};
    if (!CreateProcessW(exe.c_str(), commandLine.data(), nullptr, nullptr, FALSE, 0, nullptr, nullptr, &startup, &process))
    {
        return false;
    }

    AllowSetForegroundWindow(process.dwProcessId);
    CloseHandle(process.hThread);
    CloseHandle(process.hProcess);
    return true;
}

bool StartCopy(HWND owner, const std::vector<std::wstring>& sources, const std::wstring& destination, bool move)
{
    std::string error;
    if (SendToApp(CopyRequestJson(sources, destination, move), error) || LaunchApp(sources, destination, move))
    {
        return true;
    }

    std::wstring message = T(L"No se pudo entregar la copia a MegaCopier.", L"Couldn't hand the transfer over to MegaCopier.");
    if (!error.empty())
    {
        int size = MultiByteToWideChar(CP_UTF8, 0, error.data(), static_cast<int>(error.size()), nullptr, 0);
        std::wstring wide(size, L'\0');
        MultiByteToWideChar(CP_UTF8, 0, error.data(), static_cast<int>(error.size()), wide.data(), size);
        message += L"\n\n" + wide;
    }

    MessageBoxW(owner, message.c_str(), L"MegaCopier", MB_OK | MB_ICONWARNING);
    return false;
}

// Tras pegar algo cortado, el portapapeles se vacía (como hace el Explorador).
void ClearClipboard()
{
    if (OpenClipboard(nullptr))
    {
        EmptyClipboard();
        CloseClipboard();
    }
}

/// Icono de 16 px del menú (mapa de bits ARGB, como piden los menús de Windows).
HBITMAP MenuBitmap()
{
    static HBITMAP bitmap = [] {
        int size = GetSystemMetrics(SM_CXSMICON);
        std::wstring icoPath = ModuleDirectory() + L"\\Assets\\MegaCopier.ico";
        HICON icon = static_cast<HICON>(LoadImageW(nullptr, icoPath.c_str(), IMAGE_ICON, size, size, LR_LOADFROMFILE));
        if (icon == nullptr)
        {
            return static_cast<HBITMAP>(nullptr);
        }

        BITMAPINFO info = {};
        info.bmiHeader.biSize = sizeof(BITMAPINFOHEADER);
        info.bmiHeader.biWidth = size;
        info.bmiHeader.biHeight = -size;
        info.bmiHeader.biPlanes = 1;
        info.bmiHeader.biBitCount = 32;
        void* bits = nullptr;
        HDC screen = GetDC(nullptr);
        HBITMAP result = CreateDIBSection(screen, &info, DIB_RGB_COLORS, &bits, nullptr, 0);
        HDC dc = CreateCompatibleDC(screen);
        HGDIOBJ previous = SelectObject(dc, result);
        DrawIconEx(dc, 0, 0, icon, size, size, 0, nullptr, DI_NORMAL);
        SelectObject(dc, previous);
        DeleteDC(dc);
        ReleaseDC(nullptr, screen);
        DestroyIcon(icon);
        return result;
    }();
    return bitmap;
}

// ---------------------------------------------------------------------------------------------------------------
// Extensión

enum class Mode
{
    ContextMenu,
    DragDrop,
};

class ShellExtension final : public IShellExtInit, public IContextMenu
{
public:
    explicit ShellExtension(Mode mode) : m_mode(mode) { InterlockedIncrement(&g_objects); }
    ~ShellExtension() { InterlockedDecrement(&g_objects); }

    // IUnknown
    IFACEMETHODIMP QueryInterface(REFIID riid, void** result) override
    {
        if (result == nullptr)
        {
            return E_POINTER;
        }

        if (riid == IID_IUnknown || riid == IID_IContextMenu)
        {
            *result = static_cast<IContextMenu*>(this);
        }
        else if (riid == IID_IShellExtInit)
        {
            *result = static_cast<IShellExtInit*>(this);
        }
        else
        {
            *result = nullptr;
            return E_NOINTERFACE;
        }

        AddRef();
        return S_OK;
    }

    IFACEMETHODIMP_(ULONG) AddRef() override { return InterlockedIncrement(&m_refs); }

    IFACEMETHODIMP_(ULONG) Release() override
    {
        ULONG refs = InterlockedDecrement(&m_refs);
        if (refs == 0)
        {
            delete this;
        }

        return refs;
    }

    // IShellExtInit
    IFACEMETHODIMP Initialize(PCIDLIST_ABSOLUTE folder, IDataObject* data, HKEY) override
    {
        m_folder = PathFromIdList(folder);
        m_items = FilesFromDataObject(data);
        m_sources.clear();
        m_destination.clear();
        m_commands.clear();
        m_fromClipboard = false;

        if (!MegaCopierEnabled())
        {
            return S_OK; // desactivado desde la bandeja: el Explorador trabaja solo
        }

        if (m_mode == Mode::DragDrop)
        {
            // Arrastre con el botón derecho: lo arrastrado va a la carpeta destino.
            m_sources = m_items;
            m_destination = m_folder;
            m_commands.push_back({ T(L"Copiar aquí con MegaCopier", L"Copy here with MegaCopier"), false });
            m_commands.push_back({ T(L"Mover aquí con MegaCopier", L"Move here with MegaCopier"), true });
        }
        else
        {
            // Menú contextual: pegar lo que hay en el portapapeles (copiado con Ctrl+C o cortado con Ctrl+X).
            ClipboardFiles clipboard = ReadClipboard();
            if (clipboard.files.empty())
            {
                return S_OK;
            }

            DWORD attributes = m_items.size() == 1 ? GetFileAttributesW(m_items[0].c_str()) : INVALID_FILE_ATTRIBUTES;
            if (m_items.empty())
            {
                m_destination = m_folder; // fondo de una carpeta o del Escritorio
                m_commands.push_back({ clipboard.cut ? T(L"Mover aquí con MegaCopier", L"Move here with MegaCopier")
                                                     : T(L"Pegar con MegaCopier", L"Paste with MegaCopier"), clipboard.cut });
            }
            else if (attributes != INVALID_FILE_ATTRIBUTES && (attributes & FILE_ATTRIBUTE_DIRECTORY))
            {
                m_destination = m_items[0]; // sobre una carpeta o una unidad
                m_commands.push_back({ clipboard.cut ? T(L"Mover a esta carpeta con MegaCopier", L"Move into this folder with MegaCopier")
                                                     : T(L"Pegar en esta carpeta con MegaCopier", L"Paste into this folder with MegaCopier"), clipboard.cut });
            }

            m_sources = clipboard.files;
            m_fromClipboard = true;
        }

        return S_OK;
    }

    // IContextMenu
    IFACEMETHODIMP QueryContextMenu(HMENU menu, UINT index, UINT firstCommand, UINT lastCommand, UINT flags) override
    {
        if ((flags & CMF_DEFAULTONLY) || m_sources.empty() || m_destination.empty() || m_commands.empty() ||
            firstCommand + m_commands.size() - 1 > lastCommand)
        {
            return MAKE_HRESULT(SEVERITY_SUCCESS, 0, 0);
        }

        for (UINT i = 0; i < m_commands.size(); i++)
        {
            MENUITEMINFOW item = { sizeof(item) };
            item.fMask = MIIM_STRING | MIIM_ID | MIIM_BITMAP;
            item.wID = firstCommand + i;
            item.dwTypeData = m_commands[i].text.data();
            item.hbmpItem = MenuBitmap();
            InsertMenuItemW(menu, index + i, TRUE, &item);
        }

        return MAKE_HRESULT(SEVERITY_SUCCESS, 0, static_cast<USHORT>(m_commands.size()));
    }

    IFACEMETHODIMP InvokeCommand(CMINVOKECOMMANDINFO* info) override
    {
        size_t index;
        if (HIWORD(info->lpVerb) != 0)
        {
            // Invocación por verbo de texto.
            if (lstrcmpiA(info->lpVerb, "megacopier") == 0)
            {
                index = 0;
            }
            else if (lstrcmpiA(info->lpVerb, "megacopier-move") == 0 && m_commands.size() > 1)
            {
                index = 1;
            }
            else
            {
                return E_FAIL;
            }
        }
        else
        {
            index = LOWORD(info->lpVerb);
        }

        if (index >= m_commands.size())
        {
            return E_FAIL;
        }

        bool move = m_commands[index].move;
        if (StartCopy(info->hwnd, m_sources, m_destination, move) && move && m_fromClipboard)
        {
            ClearClipboard();
        }

        return S_OK;
    }

    IFACEMETHODIMP GetCommandString(UINT_PTR command, UINT type, UINT*, CHAR* name, UINT maxLength) override
    {
        if (command >= m_commands.size())
        {
            return E_INVALIDARG;
        }

        switch (type)
        {
        case GCS_VERBW:
            lstrcpynW(reinterpret_cast<LPWSTR>(name), command == 1 ? L"megacopier-move" : L"megacopier", maxLength);
            return S_OK;
        case GCS_HELPTEXTW:
            lstrcpynW(reinterpret_cast<LPWSTR>(name), T(L"Copia con MegaCopier: reanudable tras un corte de luz", L"Copy with MegaCopier: resumes after a power cut"), maxLength);
            return S_OK;
        default:
            return E_NOTIMPL;
        }
    }

private:
    LONG m_refs = 1;
    Mode m_mode;
    std::wstring m_folder;
    std::vector<std::wstring> m_items;
    struct Command
    {
        std::wstring text;
        bool move;
    };

    std::vector<std::wstring> m_sources;
    std::wstring m_destination;
    std::vector<Command> m_commands;
    bool m_fromClipboard = false;
};

class ClassFactory final : public IClassFactory
{
public:
    explicit ClassFactory(Mode mode) : m_mode(mode) { InterlockedIncrement(&g_objects); }
    ~ClassFactory() { InterlockedDecrement(&g_objects); }

    IFACEMETHODIMP QueryInterface(REFIID riid, void** result) override
    {
        if (riid == IID_IUnknown || riid == IID_IClassFactory)
        {
            *result = static_cast<IClassFactory*>(this);
            AddRef();
            return S_OK;
        }

        *result = nullptr;
        return E_NOINTERFACE;
    }

    IFACEMETHODIMP_(ULONG) AddRef() override { return InterlockedIncrement(&m_refs); }

    IFACEMETHODIMP_(ULONG) Release() override
    {
        ULONG refs = InterlockedDecrement(&m_refs);
        if (refs == 0)
        {
            delete this;
        }

        return refs;
    }

    IFACEMETHODIMP CreateInstance(IUnknown* outer, REFIID riid, void** result) override
    {
        *result = nullptr;
        if (outer != nullptr)
        {
            return CLASS_E_NOAGGREGATION;
        }

        auto* extension = new (std::nothrow) ShellExtension(m_mode);
        if (extension == nullptr)
        {
            return E_OUTOFMEMORY;
        }

        HRESULT hr = extension->QueryInterface(riid, result);
        extension->Release();
        return hr;
    }

    IFACEMETHODIMP LockServer(BOOL lock) override
    {
        lock ? InterlockedIncrement(&g_locks) : InterlockedDecrement(&g_locks);
        return S_OK;
    }

private:
    LONG m_refs = 1;
    Mode m_mode;
};

} // namespace

// ---------------------------------------------------------------------------------------------------------------
// Exportaciones COM

BOOL APIENTRY DllMain(HMODULE module, DWORD reason, LPVOID)
{
    if (reason == DLL_PROCESS_ATTACH)
    {
        g_module = module;
        DisableThreadLibraryCalls(module);
    }

    return TRUE;
}

STDAPI DllGetClassObject(REFCLSID clsid, REFIID riid, void** result)
{
    Mode mode;
    if (clsid == CLSID_ContextMenu)
    {
        mode = Mode::ContextMenu;
    }
    else if (clsid == CLSID_DragDrop)
    {
        mode = Mode::DragDrop;
    }
    else
    {
        *result = nullptr;
        return CLASS_E_CLASSNOTAVAILABLE;
    }

    auto* factory = new (std::nothrow) ClassFactory(mode);
    if (factory == nullptr)
    {
        return E_OUTOFMEMORY;
    }

    HRESULT hr = factory->QueryInterface(riid, result);
    factory->Release();
    return hr;
}

STDAPI DllCanUnloadNow()
{
    return g_objects == 0 && g_locks == 0 ? S_OK : S_FALSE;
}
