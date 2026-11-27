#define UNICODE
#define _UNICODE
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <shlobj.h>
#include <shlguid.h>
#include <shellapi.h>
#include <strsafe.h>
#include <string>
#include <vector>
#include <new>
#include <wchar.h>

// El icono del menú necesita GDI (CreateDIBSection, SelectObject, DeleteDC…). Se declara
// aquí además de en la línea de enlazado para que cualquier compilación local funcione.
#pragma comment(lib, "gdi32.lib")
#pragma comment(lib, "user32.lib")
#pragma comment(lib, "shell32.lib")

// Classic Windows Explorer context-menu handler. It contributes exactly one verb,
// captioned "Pegar con XoPy" with its own icon, and forwards the selected folder to XoPy.
static const CLSID CLSID_XoPyPaste = { 0x3b1e92a7, 0x7ad6, 0x4e71, {0xa2,0x18,0x75,0x44,0x9c,0xe1,0x30,0x61} };
static HINSTANCE g_module = nullptr;
static long g_objects = 0;

static std::wstring ReadAppPath()
{
    wchar_t value[MAX_PATH] = {};
    DWORD bytes = sizeof(value);
    if (RegGetValueW(HKEY_CURRENT_USER, L"Software\\Microsoft\\Windows\\CurrentVersion\\App Paths\\XoPy.exe",
                     nullptr, RRF_RT_REG_SZ, nullptr, value, &bytes) == ERROR_SUCCESS && value[0]) return value;
    if (RegGetValueW(HKEY_LOCAL_MACHINE, L"Software\\Microsoft\\Windows\\CurrentVersion\\App Paths\\XoPy.exe",
                     nullptr, RRF_RT_REG_SZ, nullptr, value, &bytes) == ERROR_SUCCESS && value[0]) return value;
    wchar_t programFiles[MAX_PATH] = {};
    if (GetEnvironmentVariableW(L"ProgramFiles", programFiles, MAX_PATH)) {
        std::wstring candidate = programFiles;
        candidate += L"\\XoPy\\XoPy.exe";
        if (GetFileAttributesW(candidate.c_str()) != INVALID_FILE_ATTRIBUTES) return candidate;
    }
    return L"";
}

// Convierte un HICON en HBITMAP de 32 bits con alfa: es lo que acepta el menú contextual.
static HBITMAP IconToBitmap(HICON icon, int size)
{
    BITMAPINFO info = {};
    info.bmiHeader.biSize = sizeof(BITMAPINFOHEADER);
    info.bmiHeader.biWidth = size;
    info.bmiHeader.biHeight = -size; // negativo: de arriba abajo
    info.bmiHeader.biPlanes = 1;
    info.bmiHeader.biBitCount = 32;
    info.bmiHeader.biCompression = BI_RGB;
    void* bits = nullptr;
    HDC screen = GetDC(nullptr);
    HDC memory = CreateCompatibleDC(screen);
    HBITMAP bitmap = CreateDIBSection(screen, &info, DIB_RGB_COLORS, &bits, nullptr, 0);
    if (bitmap && memory && bits)
    {
        HGDIOBJ previous = SelectObject(memory, bitmap);
        DrawIconEx(memory, 0, 0, icon, size, size, 0, nullptr, DI_NORMAL);
        SelectObject(memory, previous);
    }
    else if (bitmap) { DeleteObject(bitmap); bitmap = nullptr; }
    if (memory) DeleteDC(memory);
    if (screen) ReleaseDC(nullptr, screen);
    return bitmap;
}

// ¿Está XoPy puesto como gestor de copias predeterminado? Lo escribe la propia aplicación
// al guardar las preferencias. Si no se puede leer, se responde que NO: nunca se toca el
// menú de Windows «por si acaso».
static bool IsDefaultCopier()
{
    DWORD value = 0;
    DWORD size = sizeof(value);
    if (RegGetValueW(HKEY_CURRENT_USER, L"Software\\XoPy", L"DefaultCopier",
                     RRF_RT_REG_DWORD, nullptr, &value, &size) == ERROR_SUCCESS) return value != 0;
    return false;
}

// Busca el «Pegar» que pone Windows en el menú. Se compara el texto sin el & del acelerador
// y sin puntos suspensivos, y se descartan «Pegar acceso directo» y los de otros programas.
static int FindNativePaste(HMENU menu)
{
    const int count = GetMenuItemCount(menu);
    for (int i = 0; i < count; ++i)
    {
        wchar_t text[128] = {};
        if (GetMenuStringW(menu, static_cast<UINT>(i), text, 128, MF_BYPOSITION) <= 0) continue;
        std::wstring clean;
        for (wchar_t c : std::wstring(text))
        {
            if (c == L'&') continue;
            if (c == L'\t') break;   // el atajo («Ctrl+V») va detrás de una tabulación
            clean += c;
        }
        while (!clean.empty() && (clean.back() == L' ' || clean.back() == L'.')) clean.pop_back();
        if (_wcsicmp(clean.c_str(), L"Pegar") == 0 || _wcsicmp(clean.c_str(), L"Paste") == 0) return i;
    }
    return -1;
}

static std::wstring Quote(const std::wstring& value)
{
    std::wstring out = L"\"";
    for (wchar_t c : value) { if (c == L'\"') out += L'\\'; out += c; }
    out += L'\"';
    return out;
}

class XoPyPasteHandler final : public IShellExtInit, public IContextMenu
{
public:
    XoPyPasteHandler() { InterlockedIncrement(&g_objects); }
    ~XoPyPasteHandler() { InterlockedDecrement(&g_objects); }

    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID iid, void** out) override
    {
        if (!out) return E_POINTER;
        *out = nullptr;
        if (IsEqualIID(iid, IID_IUnknown) || IsEqualIID(iid, IID_IShellExtInit)) *out = static_cast<IShellExtInit*>(this);
        else if (IsEqualIID(iid, IID_IContextMenu)) *out = static_cast<IContextMenu*>(this);
        else return E_NOINTERFACE;
        AddRef();
        return S_OK;
    }
    ULONG STDMETHODCALLTYPE AddRef() override { return static_cast<ULONG>(InterlockedIncrement(&refs_)); }
    ULONG STDMETHODCALLTYPE Release() override
    {
        const ULONG left = static_cast<ULONG>(InterlockedDecrement(&refs_));
        if (!left) delete this;
        return left;
    }

    HRESULT STDMETHODCALLTYPE Initialize(PCIDLIST_ABSOLUTE folder, IDataObject* selection, HKEY) override
    {
        wchar_t path[MAX_PATH] = {};
        destination_.clear();
        // OJO con esta comprobación: antes decía «if (!folder || !path[0])» y path acababa
        // de inicializarse a ceros, así que SIEMPRE salía por aquí con E_INVALIDARG. Es
        // decir: la entrada «Pegar con XoPy» no llegaba a aparecer nunca y el usuario
        // acababa pulsando el «Pegar» de Windows. Ese era el fallo de «al tocar pegar lo
        // hace el copiador de Windows».
        //
        // Y NO se puede salir cuando «folder» viene vacío: al hacer clic derecho SOBRE una
        // carpeta (no dentro de ella) Windows no manda carpeta y la carpeta elegida llega
        // en el objeto de datos. Ese caso se resuelve más abajo.
        if (folder)
        {
            // El escritorio no es una ruta normal: su carpeta de sistema es la de escritorio
            // del usuario. Sin esto, pegar en el escritorio no mostraba «Pegar» y seguía
            // copiando Windows.
            if (SHGetPathFromIDListW(folder, path) && path[0]) destination_ = path;
            else if (SHGetSpecialFolderPathW(nullptr, path, CSIDL_DESKTOPDIRECTORY, FALSE) && path[0])
                destination_ = path;
        }
        // LO QUE VIENE EN EL OBJETO DE DATOS. Son dos situaciones distintas:
        //   · menú contextual sobre una carpeta → es la carpeta elegida (el destino)
        //   · ARRASTRAR Y SOLTAR               → son los archivos que se sueltan (el origen)
        // Esto último es lo que hace SuperCopier: se registra como manejador de
        // arrastrar-soltar y ahí es donde se le quita el trabajo al copiador de Windows.
        sources_.clear();
        dragDrop_ = false;
        if (selection)
        {
            FORMATETC format = { CF_HDROP, nullptr, DVASPECT_CONTENT, -1, TYMED_HGLOBAL };
            STGMEDIUM medium = {};
            if (SUCCEEDED(selection->GetData(&format, &medium)))
            {
                auto drop = reinterpret_cast<HDROP>(medium.hGlobal);
                const UINT count = DragQueryFileW(drop, 0xFFFFFFFF, nullptr, 0);
                for (UINT i = 0; i < count; ++i)
                {
                    wchar_t item[MAX_PATH] = {};
                    if (DragQueryFileW(drop, i, item, MAX_PATH) && item[0]) sources_.push_back(item);
                }
                ReleaseStgMedium(&medium);
            }
        }

        if (!destination_.empty() && !sources_.empty())
        {
            // Carpeta de destino + archivos: es un arrastre soltado encima.
            dragDrop_ = true;
        }
        else if (destination_.empty() && sources_.size() == 1)
        {
            // Clic derecho sobre una carpeta: esa carpeta es el destino del pegado.
            const DWORD attrs = GetFileAttributesW(sources_[0].c_str());
            if (attrs != INVALID_FILE_ATTRIBUTES && (attrs & FILE_ATTRIBUTE_DIRECTORY) != 0)
                destination_ = sources_[0];
            sources_.clear();
        }
        else if (destination_.empty())
        {
            sources_.clear();
        }
        return destination_.empty() ? E_INVALIDARG : S_OK;
    }

    /// Escribe la lista de archivos en un archivo temporal UTF-8 y devuelve su ruta: una
    /// carpeta con miles de archivos no cabe en una línea de comandos.
    std::wstring WriteListFile() const
    {
        wchar_t folder[MAX_PATH] = {};
        if (!GetTempPathW(MAX_PATH, folder)) return L"";
        wchar_t file[MAX_PATH] = {};
        if (!GetTempFileNameW(folder, L"xop", 0, file)) return L"";
        HANDLE handle = CreateFileW(file, GENERIC_WRITE, 0, nullptr, CREATE_ALWAYS, FILE_ATTRIBUTE_TEMPORARY, nullptr);
        if (handle == INVALID_HANDLE_VALUE) return L"";
        const unsigned char bom[3] = { 0xEF, 0xBB, 0xBF };
        DWORD written = 0;
        WriteFile(handle, bom, 3, &written, nullptr);
        for (const auto& path : sources_)
        {
            const int bytes = WideCharToMultiByte(CP_UTF8, 0, path.c_str(), -1, nullptr, 0, nullptr, nullptr);
            if (bytes <= 1) continue;
            // Búfer con sitio para el cero final (&buffer[0] y no .data(): el compilador
            // usa C++14 por omisión y ahí data() devuelve «const char*»).
            std::vector<char> buffer(static_cast<size_t>(bytes), '\0');
            WideCharToMultiByte(CP_UTF8, 0, path.c_str(), -1, &buffer[0], bytes, nullptr, nullptr);
            std::string utf8(&buffer[0]);
            utf8 += "\r\n";
            WriteFile(handle, utf8.c_str(), static_cast<DWORD>(utf8.size()), &written, nullptr);
        }
        CloseHandle(handle);
        return file;
    }

    HRESULT Launch(const std::wstring& args) const
    {
        const std::wstring exe = ReadAppPath();
        // NUNCA un cuadro de diálogo desde aquí: este código se ejecuta DENTRO del
        // Explorador y un modal suyo puede dejar el escritorio bloqueado.
        if (exe.empty()) return HRESULT_FROM_WIN32(ERROR_FILE_NOT_FOUND);
        SHELLEXECUTEINFOW launch = {};
        launch.cbSize = sizeof(launch);
        launch.fMask = SEE_MASK_FLAG_NO_UI;
        launch.lpVerb = L"open";
        launch.lpFile = exe.c_str();
        launch.lpParameters = args.c_str();
        launch.nShow = SW_SHOWNORMAL;
        if (!ShellExecuteExW(&launch)) return HRESULT_FROM_WIN32(GetLastError());
        return S_OK;
    }

    HRESULT STDMETHODCALLTYPE QueryContextMenu(HMENU menu, UINT index, UINT first, UINT last, UINT flags) override
    {
        if ((flags & CMF_DEFAULTONLY) || first > last || destination_.empty()) return MAKE_HRESULT(SEVERITY_SUCCESS, 0, 0);

        // ---- ARRASTRAR Y SOLTAR (el truco de SuperCopier) ----------------------------
        // Windows construye este menú TAMBIÉN cuando se suelta con el botón izquierdo (y
        // al pegar en el Explorador): monta el menú, mira cuál es el elemento
        // predeterminado y lo ejecuta sin enseñarlo. Si el predeterminado es el nuestro,
        // la copia la hace XoPy en lugar del copiador de Windows. Eso es exactamente lo
        // que lleva haciendo SuperCopier desde hace más de diez años.
        if (dragDrop_)
        {
            InsertMenuW(menu, index, MF_BYPOSITION | MF_STRING, first + kCmdCopyHere, L"Copiar aquí con XoPy");
            InsertMenuW(menu, index + 1, MF_BYPOSITION | MF_STRING, first + kCmdMoveHere, L"Mover aquí con XoPy");
            if (IsDefaultCopier())
            {
                // Se respeta lo que Windows había elegido: si iba a mover, se mueve con
                // XoPy; si iba a copiar, se copia con XoPy.
                const int previous = GetMenuDefaultItem(menu, FALSE, 0);
                wchar_t text[128] = {};
                bool move = false;
                if (previous >= 0 && GetMenuStringW(menu, static_cast<UINT>(previous), text, 128, MF_BYCOMMAND) > 0)
                {
                    const std::wstring label = text;
                    move = label.find(L"Mover") != std::wstring::npos || label.find(L"Move") != std::wstring::npos;
                }
                SetMenuDefaultItem(menu, first + (move ? kCmdMoveHere : kCmdCopyHere), FALSE);
            }
            return MAKE_HRESULT(SEVERITY_SUCCESS, 0, kCmdPaste + 1);
        }
        // ------------------------------------------------------------------------------

        // Sin archivos en el portapapeles no hay nada que pegar. La entrada se llama
        // «Pegar» a secas y se reconoce POR EL ICONO de XoPy: el «(XoPy)» entre paréntesis
        // que llevaba antes sobraba y afeaba el menú del Explorador.
        if (!IsClipboardFormatAvailable(CF_HDROP)) return MAKE_HRESULT(SEVERITY_SUCCESS, 0, 0);

        // SI XOPY ES EL GESTOR DE COPIAS PREDETERMINADO, el «Pegar» de Windows se quita del
        // menú y el de XoPy ocupa su sitio: pulsar «Pegar» copia con XoPy, que es lo que el
        // usuario espera al marcar esa casilla. Sin el ajuste puesto, el de Windows se
        // queda donde está y XoPy solo añade su entrada.
        const bool replaceNative = IsDefaultCopier();
        UINT insertAt = index;
        if (replaceNative)
        {
            const int nativePaste = FindNativePaste(menu);
            if (nativePaste >= 0)
            {
                insertAt = static_cast<UINT>(nativePaste);
                DeleteMenu(menu, static_cast<UINT>(nativePaste), MF_BYPOSITION);
            }
        }
        // El texto es «Pegar», sin paréntesis ni coletillas: lo que distingue a esta
        // entrada es el icono de XoPy que se le pone justo debajo. Cuando XoPy NO es el
        // copiador predeterminado, el «Pegar» de Windows sigue en el menú, así que la
        // entrada se llama «Pegar con XoPy» para que no haya dos «Pegar» iguales.
        if (!InsertMenuW(menu, insertAt, MF_BYPOSITION | MF_STRING, first + kCmdPaste,
                         replaceNative ? L"Pegar" : L"Pegar con XoPy"))
            return HRESULT_FROM_WIN32(GetLastError());
        // Icono de la aplicación junto al texto (si se puede cargar; si no, el texto basta).
        wchar_t iconPath[MAX_PATH] = {};
        const std::wstring exe = ReadAppPath();
        if (!exe.empty())
        {
            StringCchCopyW(iconPath, MAX_PATH, exe.c_str());
            MENUITEMINFOW item = {};
            item.cbSize = sizeof(item);
            item.fMask = MIIM_BITMAP;
            // El mapa de bits se cachea: QueryContextMenu se llama cada vez que se abre el
            // menú y crear uno nuevo por vez filtraba recursos GDI. Un .exe NUNCA se puede
            // cargar con LoadImage/IMAGE_BITMAP (eso devolvía siempre NULL y además el
            // ExtractIconEx «grande» podía venir vacío): se pide solo el icono pequeño.
            static HBITMAP s_bitmap = nullptr;
            static bool s_bitmapTried = false;
            if (!s_bitmapTried)
            {
                s_bitmapTried = true;
                HICON icon = nullptr;
                if (ExtractIconExW(iconPath, 0, nullptr, &icon, 1) > 0 && icon)
                {
                    s_bitmap = IconToBitmap(icon, 16);
                    DestroyIcon(icon);
                }
            }
            if (s_bitmap) { item.hbmpItem = s_bitmap; SetMenuItemInfoW(menu, first + kCmdPaste, FALSE, &item); }
        }
        return MAKE_HRESULT(SEVERITY_SUCCESS, 0, kCmdPaste + 1);
    }

    HRESULT STDMETHODCALLTYPE InvokeCommand(LPCMINVOKECOMMANDINFO info) override
    {
        if (!info) return E_INVALIDARG;
        if (HIWORD(info->lpVerb) != 0) return E_INVALIDARG;
        const UINT verb = LOWORD(info->lpVerb);

        if (verb == kCmdCopyHere || verb == kCmdMoveHere)
        {
            if (sources_.empty()) return E_FAIL;
            const std::wstring list = WriteListFile();
            if (list.empty()) return E_FAIL;
            std::wstring args = (verb == kCmdMoveHere ? L"--move" : L"--copy");
            args += L" --to " + Quote(destination_) + L" --lista " + Quote(list);
            return Launch(args);
        }

        if (verb != kCmdPaste) return E_FAIL;
        // «--paste --replace»: el pegado sustituye al copiador de Windows también aquí.
        return Launch(L"--paste --replace --to " + Quote(destination_));
    }

    HRESULT STDMETHODCALLTYPE GetCommandString(UINT_PTR id, UINT type, UINT*, LPSTR buffer, UINT size) override
    {
        // El Explorador pide el texto de la barra de estado; si no se contesta, algunos
        // contextos descartan el elemento entero.
        if (type != GCS_HELPTEXTW || !buffer) return E_NOTIMPL;
        const wchar_t* help = id == kCmdMoveHere ? L"Mover aquí los archivos con XoPy"
            : id == kCmdCopyHere ? L"Copiar aquí los archivos con XoPy"
            : L"Pegar con XoPy los archivos del portapapeles";
        return StringCchCopyW(reinterpret_cast<wchar_t*>(buffer), size, help);
    }

private:
    static const UINT kCmdCopyHere = 0;
    static const UINT kCmdMoveHere = 1;
    static const UINT kCmdPaste = 2;

    volatile long refs_ = 1;
    bool dragDrop_ = false;
    std::wstring destination_;
    std::vector<std::wstring> sources_;
};

class XoPyClassFactory final : public IClassFactory
{
public:
    XoPyClassFactory() { InterlockedIncrement(&g_objects); }
    ~XoPyClassFactory() { InterlockedDecrement(&g_objects); }
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID iid, void** out) override
    {
        if (!out) return E_POINTER;
        *out = nullptr;
        if (IsEqualIID(iid, IID_IUnknown) || IsEqualIID(iid, IID_IClassFactory)) *out = static_cast<IClassFactory*>(this);
        else return E_NOINTERFACE;
        AddRef(); return S_OK;
    }
    ULONG STDMETHODCALLTYPE AddRef() override { return static_cast<ULONG>(InterlockedIncrement(&refs_)); }
    ULONG STDMETHODCALLTYPE Release() override
    {
        const ULONG left = static_cast<ULONG>(InterlockedDecrement(&refs_));
        if (!left) delete this;
        return left;
    }
    HRESULT STDMETHODCALLTYPE CreateInstance(IUnknown* outer, REFIID iid, void** out) override
    {
        if (outer) return CLASS_E_NOAGGREGATION;
        auto* handler = new (std::nothrow) XoPyPasteHandler();
        if (!handler) return E_OUTOFMEMORY;
        const HRESULT result = handler->QueryInterface(iid, out);
        handler->Release();
        return result;
    }
    HRESULT STDMETHODCALLTYPE LockServer(BOOL lock) override
    {
        if (lock) InterlockedIncrement(&g_objects); else InterlockedDecrement(&g_objects);
        return S_OK;
    }
private:
    volatile long refs_ = 1;
};

STDAPI DllGetClassObject(REFCLSID clsid, REFIID iid, LPVOID* out)
{
    if (!IsEqualCLSID(clsid, CLSID_XoPyPaste)) return CLASS_E_CLASSNOTAVAILABLE;
    auto* factory = new (std::nothrow) XoPyClassFactory();
    if (!factory) return E_OUTOFMEMORY;
    const HRESULT result = factory->QueryInterface(iid, out);
    factory->Release();
    return result;
}

STDAPI DllCanUnloadNow()
{
    return InterlockedCompareExchange(&g_objects, 0, 0) == 0 ? S_OK : S_FALSE;
}

// Registro COM de la extensión. Sin estas dos exportaciones, ni «regsvr32» ni la marca
// «regserver» de Inno Setup podían registrar el CLSID: la integración se anunciaba pero
// Windows seguía usando su copiador. El instalador y XoPy llaman a estas funciones.
static const wchar_t* kClsidText = L"{3B1E92A7-7AD6-4E71-A218-75449CE13061}";

static HRESULT WriteKey(HKEY root, const std::wstring& subkey, const wchar_t* name, const std::wstring& value)
{
    HKEY key = nullptr;
    if (RegCreateKeyExW(root, subkey.c_str(), 0, nullptr, 0, KEY_WRITE, nullptr, &key, nullptr) != ERROR_SUCCESS)
        return E_ACCESSDENIED;
    const DWORD bytes = static_cast<DWORD>((value.size() + 1) * sizeof(wchar_t));
    const LONG result = RegSetValueExW(key, name, 0, REG_SZ, reinterpret_cast<const BYTE*>(value.c_str()), bytes);
    RegCloseKey(key);
    return result == ERROR_SUCCESS ? S_OK : E_FAIL;
}

static std::wstring ModulePath()
{
    wchar_t path[MAX_PATH] = {};
    GetModuleFileNameW(g_module, path, MAX_PATH);
    return path;
}

STDAPI DllRegisterServer()
{
    const std::wstring clsidKey = std::wstring(L"Software\\Classes\\CLSID\\") + kClsidText;
    HRESULT hr = WriteKey(HKEY_CURRENT_USER, clsidKey, nullptr, L"XoPy Shell Extension");
    if (FAILED(hr)) return hr;
    hr = WriteKey(HKEY_CURRENT_USER, clsidKey + L"\\InprocServer32", nullptr, ModulePath());
    if (FAILED(hr)) return hr;
    hr = WriteKey(HKEY_CURRENT_USER, clsidKey + L"\\InprocServer32", L"ThreadingModel", L"Apartment");
    if (FAILED(hr)) return hr;
    hr = WriteKey(HKEY_CURRENT_USER, L"Software\\Classes\\Directory\\Background\\shellex\\ContextMenuHandlers\\XoPyPaste",
                  nullptr, kClsidText);
    if (FAILED(hr)) return hr;
    // MANEJADORES DE ARRASTRAR Y SOLTAR. Es la vía por la que SuperCopier lleva años
    // quitándole la copia al Explorador: Windows monta este menú también cuando se suelta
    // con el botón izquierdo y ejecuta su elemento predeterminado, que pasa a ser el
    // nuestro. Se registra para carpetas, unidades y el tipo genérico «Folder».
    for (const wchar_t* target : { L"Directory", L"Drive", L"Folder" })
    {
        WriteKey(HKEY_CURRENT_USER,
                 std::wstring(L"Software\\Classes\\") + target + L"\\shellex\\DragDropHandlers\\XoPyCopier",
                 nullptr, kClsidText);
    }
    WriteKey(HKEY_CURRENT_USER, L"Software\\Microsoft\\Windows\\CurrentVersion\\Shell Extensions\\Approved",
             kClsidText, L"XoPy Pegar");
    return S_OK;
}

STDAPI DllUnregisterServer()
{
    RegDeleteKeyW(HKEY_CURRENT_USER, (std::wstring(L"Software\\Classes\\CLSID\\") + kClsidText).c_str());
    RegDeleteKeyW(HKEY_CURRENT_USER, (std::wstring(L"Software\\Classes\\CLSID\\") + kClsidText + L"\\InprocServer32").c_str());
    RegDeleteKeyW(HKEY_CURRENT_USER, L"Software\\Classes\\Directory\\Background\\shellex\\ContextMenuHandlers\\XoPyPaste");
    for (const wchar_t* target : { L"Directory", L"Drive", L"Folder" })
    {
        RegDeleteKeyW(HKEY_CURRENT_USER,
                      (std::wstring(L"Software\\Classes\\") + target + L"\\shellex\\DragDropHandlers\\XoPyCopier").c_str());
    }
    return S_OK;
}

BOOL WINAPI DllMain(HINSTANCE instance, DWORD reason, LPVOID)
{
    if (reason == DLL_PROCESS_ATTACH) { g_module = instance; DisableThreadLibraryCalls(instance); }
    return TRUE;
}
