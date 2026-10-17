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
#include <shlwapi.h>
#include <sddl.h>
#include <string>
#include <vector>
#include <new>
#include "Texts.g.h"

// {E2B6C3A1-5F47-4C8E-A1D2-7B3F9C0E6A54}
static const CLSID CLSID_ContextMenu = { 0xe2b6c3a1, 0x5f47, 0x4c8e, { 0xa1, 0xd2, 0x7b, 0x3f, 0x9c, 0x0e, 0x6a, 0x54 } };
// {A9D4F1C7-2B6E-4A3F-8C5D-1E7B0F9A3C62}
static const CLSID CLSID_DragDrop = { 0xa9d4f1c7, 0x2b6e, 0x4a3f, { 0x8c, 0x5d, 0x1e, 0x7b, 0x0f, 0x9a, 0x3c, 0x62 } };
// {6F3C2B71-8E4D-4A5B-9C2E-1D7F3A9B5E40}: el menú corto de Windows 11 (IExplorerCommand, desde el paquete disperso)
static const CLSID CLSID_ExplorerCommand = { 0x6f3c2b71, 0x8e4d, 0x4a5b, { 0x9c, 0x2e, 0x1d, 0x7f, 0x3a, 0x9b, 0x5e, 0x40 } };

static HMODULE g_module = nullptr;
static LONG g_objects = 0;
static LONG g_locks = 0;

namespace {

// ---------------------------------------------------------------------------------------------------------------
// Idioma: el que publica la app en HKCU\Software\MegaCopier\Language, o el de Windows.

// La traducción exacta ("pt-BR") o la primera del mismo idioma ("pt-PT" → "pt-BR"); si no hay, nullptr.
const TextLanguage* FindLanguage(const wchar_t* name)
{
    for (const auto& language : TextLanguages)
    {
        if (_wcsicmp(language.code, name) == 0)
        {
            return &language;
        }
    }

    const wchar_t* dash = wcschr(name, L'-');
    size_t length = dash ? static_cast<size_t>(dash - name) : wcslen(name);
    for (const auto& language : TextLanguages)
    {
        if (length > 0 && _wcsnicmp(language.code, name, length) == 0 && language.code[length] == L'-')
        {
            return &language;
        }
    }

    return nullptr;
}

// Se lee cada vez (es instantáneo): el Explorador mantiene la DLL cargada y el usuario puede cambiar el idioma.
const TextLanguage& CurrentLanguage()
{
    wchar_t value[LOCALE_NAME_MAX_LENGTH] = {};
    DWORD size = sizeof(value);
    if (RegGetValueW(HKEY_CURRENT_USER, L"Software\\MegaCopier", L"Language", RRF_RT_REG_SZ, nullptr, value, &size) == ERROR_SUCCESS && value[0] != L'\0')
    {
        if (const TextLanguage* language = FindLanguage(value))
        {
            return *language;
        }
    }

    wchar_t ui[LOCALE_NAME_MAX_LENGTH] = {};
    if (LCIDToLocaleName(MAKELCID(GetUserDefaultUILanguage(), SORT_DEFAULT), ui, LOCALE_NAME_MAX_LENGTH, 0) > 0)
    {
        if (const TextLanguage* language = FindLanguage(ui))
        {
            return *language;
        }
    }

    return *FindLanguage(L"en-US");
}

const wchar_t* T(Text text)
{
    return CurrentLanguage().texts[static_cast<int>(text)];
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

/// Comprobar sumas, crear sumas o borrar de forma segura: lo que se eligió en el submenú MegaCopier.
std::string ToolRequestJson(const char* command, const char* operation, const std::vector<std::wstring>& sources)
{
    std::string json = std::string("{\"Command\":\"") + command + "\",";
    if (operation != nullptr)
    {
        json += std::string("\"Operation\":\"") + operation + "\",";
    }

    json += "\"SecureDelete\":true,\"Sources\":[";
    for (size_t i = 0; i < sources.size(); i++)
    {
        json += (i > 0 ? "," : "") + JsonString(sources[i]);
    }

    return json + "]}";
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

    std::wstring message = T(Text::DeliverFailed);
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

/// Con la app cerrada: "MegaCopier.exe test|hash|wipe <rutas>".
bool LaunchTool(const wchar_t* verb, const std::vector<std::wstring>& sources)
{
    std::wstring exe = ModuleDirectory() + L"\\MegaCopier.exe";
    std::wstring commandLine = QuoteArgument(exe) + L" " + verb;
    for (const auto& source : sources)
    {
        commandLine += L" " + QuoteArgument(source);
    }

    STARTUPINFOW startup = { sizeof(startup) };
    PROCESS_INFORMATION process = {};
    if (commandLine.size() >= 32000 || !CreateProcessW(exe.c_str(), commandLine.data(), nullptr, nullptr, FALSE, 0, nullptr, nullptr, &startup, &process))
    {
        return false;
    }

    AllowSetForegroundWindow(process.dwProcessId);
    CloseHandle(process.hThread);
    CloseHandle(process.hProcess);
    return true;
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
        m_tools.clear();
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
            m_commands.push_back({ T(Text::CopyHere), false });
            m_commands.push_back({ T(Text::MoveHere), true });
        }
        else
        {
            // Menú contextual: pegar lo que hay en el portapapeles (copiado con Ctrl+C o cortado con Ctrl+X).
            ClipboardFiles clipboard = ReadClipboard();
            DWORD attributes = m_items.size() == 1 ? GetFileAttributesW(m_items[0].c_str()) : INVALID_FILE_ATTRIBUTES;
            if (clipboard.files.empty())
            {
                // Nada que pegar.
            }
            else if (m_items.empty())
            {
                m_destination = m_folder; // fondo de una carpeta o del Escritorio
                m_commands.push_back({ clipboard.cut ? T(Text::MoveHere)
                                                     : T(Text::Paste), clipboard.cut });
            }
            else if (attributes != INVALID_FILE_ATTRIBUTES && (attributes & FILE_ATTRIBUTE_DIRECTORY))
            {
                m_destination = m_items[0]; // sobre una carpeta o una unidad
                m_commands.push_back({ clipboard.cut ? T(Text::MoveIntoFolder)
                                                     : T(Text::PasteIntoFolder), clipboard.cut });
            }

            if (!clipboard.files.empty())
            {
                m_sources = clipboard.files;
                m_fromClipboard = true;
            }

            // Sobre archivos o carpetas: el submenú MegaCopier (comprobar sumas, crear sumas, borrar de forma segura).
            if (!m_items.empty())
            {
                m_tools.push_back({ T(Text::TestChecksums), Action::Test });
                m_tools.push_back({ T(Text::CreateChecksums), Action::Hash });
                m_tools.push_back({ T(Text::SecureDelete), Action::Wipe });
            }
        }

        return S_OK;
    }

    // IContextMenu
    IFACEMETHODIMP QueryContextMenu(HMENU menu, UINT index, UINT firstCommand, UINT lastCommand, UINT flags) override
    {
        // Pegar (si hay algo que pegar) y, debajo, el submenú MegaCopier con las herramientas.
        bool paste = !m_sources.empty() && !m_destination.empty() && !m_commands.empty();
        UINT pasteCount = paste ? static_cast<UINT>(m_commands.size()) : 0;
        UINT total = pasteCount + static_cast<UINT>(m_tools.size());
        if ((flags & CMF_DEFAULTONLY) || total == 0 || firstCommand + total - (m_tools.empty() ? 1 : 0) > lastCommand)
        {
            return MAKE_HRESULT(SEVERITY_SUCCESS, 0, 0);
        }

        m_pasteCount = pasteCount;
        UINT position = index;
        for (UINT i = 0; i < pasteCount; i++)
        {
            MENUITEMINFOW item = { sizeof(item) };
            item.fMask = MIIM_STRING | MIIM_ID | MIIM_BITMAP;
            item.wID = firstCommand + i;
            item.dwTypeData = m_commands[i].text.data();
            item.hbmpItem = MenuBitmap();
            InsertMenuItemW(menu, position++, TRUE, &item);
        }

        if (!m_tools.empty())
        {
            HMENU submenu = CreatePopupMenu();
            for (UINT i = 0; i < m_tools.size(); i++)
            {
                MENUITEMINFOW item = { sizeof(item) };
                item.fMask = MIIM_STRING | MIIM_ID;
                item.wID = firstCommand + pasteCount + i;
                item.dwTypeData = m_tools[i].text.data();
                InsertMenuItemW(submenu, i, TRUE, &item);
            }

            static std::wstring title = L"MegaCopier";
            MENUITEMINFOW item = { sizeof(item) };
            item.fMask = MIIM_STRING | MIIM_SUBMENU | MIIM_BITMAP | MIIM_ID;
            item.wID = firstCommand + total; // no se invoca: solo abre el submenú
            item.hSubMenu = submenu;
            item.dwTypeData = title.data();
            item.hbmpItem = MenuBitmap();
            InsertMenuItemW(menu, position++, TRUE, &item);
            return MAKE_HRESULT(SEVERITY_SUCCESS, 0, static_cast<USHORT>(total + 1));
        }

        return MAKE_HRESULT(SEVERITY_SUCCESS, 0, static_cast<USHORT>(total));
    }

    IFACEMETHODIMP InvokeCommand(CMINVOKECOMMANDINFO* info) override
    {
        size_t index;
        if (HIWORD(info->lpVerb) != 0)
        {
            // Invocación por verbo de texto.
            if (lstrcmpiA(info->lpVerb, "megacopier") == 0 && m_pasteCount > 0)
            {
                index = 0;
            }
            else if (lstrcmpiA(info->lpVerb, "megacopier-move") == 0 && m_pasteCount > 1)
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

        if (index < m_pasteCount)
        {
            bool move = m_commands[index].move;
            if (StartCopy(info->hwnd, m_sources, m_destination, move) && move && m_fromClipboard)
            {
                ClearClipboard();
            }

            return S_OK;
        }

        index -= m_pasteCount;
        if (index >= m_tools.size())
        {
            return E_FAIL;
        }

        // Borrar siempre lo confirma la app (con su aviso de que no se puede deshacer).
        Action action = m_tools[index].action;
        const char* command = action == Action::Wipe ? "confirm-delete" : "copy";
        const char* operation = action == Action::Test ? "Test" : action == Action::Hash ? "Hash" : nullptr;
        const wchar_t* verb = action == Action::Test ? L"test" : action == Action::Hash ? L"hash" : L"wipe";
        std::string error;
        if (!SendToApp(ToolRequestJson(command, operation, m_items), error) && !LaunchTool(verb, m_items))
        {
            MessageBoxW(info->hwnd, T(Text::OpenFailed), L"MegaCopier", MB_OK | MB_ICONWARNING);
        }

        return S_OK;
    }

    IFACEMETHODIMP GetCommandString(UINT_PTR command, UINT type, UINT*, CHAR* name, UINT maxLength) override
    {
        if (command >= m_pasteCount + m_tools.size())
        {
            return E_INVALIDARG;
        }

        bool tool = command >= m_pasteCount;
        switch (type)
        {
        case GCS_VERBW:
            lstrcpynW(reinterpret_cast<LPWSTR>(name),
                      tool ? (m_tools[command - m_pasteCount].action == Action::Test ? L"megacopier-test"
                              : m_tools[command - m_pasteCount].action == Action::Hash ? L"megacopier-hash" : L"megacopier-wipe")
                           : command == 1 ? L"megacopier-move" : L"megacopier",
                      maxLength);
            return S_OK;
        case GCS_HELPTEXTW:
            lstrcpynW(reinterpret_cast<LPWSTR>(name),
                      tool ? m_tools[command - m_pasteCount].text.c_str()
                           : T(Text::HelpText),
                      maxLength);
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

    enum class Action
    {
        Test,
        Hash,
        Wipe,
    };

    struct Tool
    {
        std::wstring text;
        Action action;
    };

    std::vector<std::wstring> m_sources;
    std::wstring m_destination;
    std::vector<Command> m_commands;
    std::vector<Tool> m_tools;
    UINT m_pasteCount = 0;
    bool m_fromClipboard = false;
};

// ---------------------------------------------------------------------------------------------------------------
// Menú corto de Windows 11 (IExplorerCommand): "Pegar con MegaCopier" sin pasar por "Mostrar más opciones".
// Windows solo lo muestra si la DLL está registrada en un paquete con identidad (el paquete "disperso" que instala
// MegaCopier si está firmado). Sobre una carpeta o en su fondo, el elemento que llega es la propia carpeta.

class ExplorerCommand final : public IExplorerCommand
{
public:
    ExplorerCommand() { InterlockedIncrement(&g_objects); }
    ~ExplorerCommand() { InterlockedDecrement(&g_objects); }

    IFACEMETHODIMP QueryInterface(REFIID riid, void** result) override
    {
        if (riid == IID_IUnknown || riid == __uuidof(IExplorerCommand))
        {
            *result = static_cast<IExplorerCommand*>(this);
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

    IFACEMETHODIMP GetTitle(IShellItemArray*, LPWSTR* name) override
    {
        return SHStrDupW(ReadClipboard().cut ? T(Text::MoveHere) : T(Text::Paste), name);
    }

    IFACEMETHODIMP GetIcon(IShellItemArray*, LPWSTR* icon) override
    {
        std::wstring exe = ModuleDirectory() + L"\\MegaCopier.exe,0";
        return SHStrDupW(exe.c_str(), icon);
    }

    IFACEMETHODIMP GetToolTip(IShellItemArray*, LPWSTR* tip) override
    {
        *tip = nullptr;
        return E_NOTIMPL;
    }

    IFACEMETHODIMP GetCanonicalName(GUID* name) override
    {
        *name = CLSID_ExplorerCommand;
        return S_OK;
    }

    IFACEMETHODIMP GetState(IShellItemArray* items, BOOL, EXPCMDSTATE* state) override
    {
        // Visible solo si hay algo que pegar, MegaCopier está activado y el destino es una carpeta del disco.
        *state = MegaCopierEnabled() && IsClipboardFormatAvailable(CF_HDROP) && !Destination(items).empty() ? ECS_ENABLED : ECS_HIDDEN;
        return S_OK;
    }

    IFACEMETHODIMP Invoke(IShellItemArray* items, IBindCtx*) override
    {
        std::wstring destination = Destination(items);
        ClipboardFiles clipboard = ReadClipboard();
        if (destination.empty() || clipboard.files.empty())
        {
            return S_OK;
        }

        if (StartCopy(nullptr, clipboard.files, destination, clipboard.cut) && clipboard.cut)
        {
            ClearClipboard(); // como el Explorador: lo cortado ya no está en su sitio
        }

        return S_OK;
    }

    IFACEMETHODIMP GetFlags(EXPCMDFLAGS* flags) override
    {
        *flags = ECF_DEFAULT;
        return S_OK;
    }

    IFACEMETHODIMP EnumSubCommands(IEnumExplorerCommand** commands) override
    {
        *commands = nullptr;
        return E_NOTIMPL;
    }

private:
    /// La carpeta del disco sobre la que se pulsó (o la del fondo); vacía si no es una carpeta del disco.
    static std::wstring Destination(IShellItemArray* items)
    {
        IShellItem* item = nullptr;
        DWORD count = 0;
        if (items == nullptr || FAILED(items->GetCount(&count)) || count != 1 || FAILED(items->GetItemAt(0, &item)))
        {
            return {};
        }

        std::wstring path;
        LPWSTR name = nullptr;
        if (SUCCEEDED(item->GetDisplayName(SIGDN_FILESYSPATH, &name)))
        {
            DWORD attributes = GetFileAttributesW(name);
            if (attributes != INVALID_FILE_ATTRIBUTES && (attributes & FILE_ATTRIBUTE_DIRECTORY))
            {
                path = name;
            }

            CoTaskMemFree(name);
        }

        item->Release();
        return path;
    }

    LONG m_refs = 1;
};

class CommandFactory final : public IClassFactory
{
public:
    CommandFactory() { InterlockedIncrement(&g_objects); }
    ~CommandFactory() { InterlockedDecrement(&g_objects); }

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

        auto* command = new (std::nothrow) ExplorerCommand();
        if (command == nullptr)
        {
            return E_OUTOFMEMORY;
        }

        HRESULT hr = command->QueryInterface(riid, result);
        command->Release();
        return hr;
    }

    IFACEMETHODIMP LockServer(BOOL lock) override
    {
        lock ? InterlockedIncrement(&g_locks) : InterlockedDecrement(&g_locks);
        return S_OK;
    }

private:
    LONG m_refs = 1;
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
    if (clsid == CLSID_ExplorerCommand)
    {
        auto* commands = new (std::nothrow) CommandFactory();
        if (commands == nullptr)
        {
            return E_OUTOFMEMORY;
        }

        HRESULT created = commands->QueryInterface(riid, result);
        commands->Release();
        return created;
    }

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
