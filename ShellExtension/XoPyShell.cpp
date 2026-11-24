#define UNICODE
#define _UNICODE
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <shlobj.h>
#include <shlguid.h>
#include <shellapi.h>
#include <strsafe.h>
#include <string>
#include <new>

// Classic Windows Explorer context-menu handler. It contributes exactly one verb,
// captioned "Pegar" (never "Pegar con XoPy"), and forwards the selected folder to XoPy.
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
        if (!folder || !path[0]) { destination_.clear(); return E_INVALIDARG; }

        // El escritorio no es una ruta normal: su carpeta de sistema es la de escritorio del
        // usuario. Sin esto, pegar en el escritorio no mostraba «Pegar» y seguía copiando Windows.
        if (!SHGetPathFromIDListW(folder, path) || !path[0])
        {
            if (SHGetSpecialFolderPathW(nullptr, path, CSIDL_DESKTOPDIRECTORY, FALSE) && path[0])
                destination_ = path;
            else { destination_.clear(); return E_INVALIDARG; }
        }
        else
        {
            destination_ = path;
        }
        // For a selected directory, mirror Explorer's drop-target semantics and paste into it.
        if (selection)
        {
            FORMATETC format = { CF_HDROP, nullptr, DVASPECT_CONTENT, -1, TYMED_HGLOBAL };
            STGMEDIUM medium = {};
            if (SUCCEEDED(selection->GetData(&format, &medium)))
            {
                auto drop = reinterpret_cast<HDROP>(medium.hGlobal);
                wchar_t selected[MAX_PATH] = {};
                const DWORD attrs = DragQueryFileW(drop, 0, selected, MAX_PATH) ? GetFileAttributesW(selected) : INVALID_FILE_ATTRIBUTES;
                if (attrs != INVALID_FILE_ATTRIBUTES && (attrs & FILE_ATTRIBUTE_DIRECTORY) != 0)
                    destination_ = selected;
                ReleaseStgMedium(&medium);
            }
        }
        return S_OK;
    }

    HRESULT STDMETHODCALLTYPE QueryContextMenu(HMENU menu, UINT index, UINT first, UINT last, UINT flags) override
    {
        if ((flags & CMF_DEFAULTONLY) || first > last || destination_.empty()) return MAKE_HRESULT(SEVERITY_SUCCESS, 0, 0);
        if (!IsClipboardFormatAvailable(CF_HDROP)) return MAKE_HRESULT(SEVERITY_SUCCESS, 0, 0);
        if (!InsertMenuW(menu, index, MF_BYPOSITION | MF_STRING, first, L"Pegar")) return HRESULT_FROM_WIN32(GetLastError());
        return MAKE_HRESULT(SEVERITY_SUCCESS, 0, 1);
    }

    HRESULT STDMETHODCALLTYPE InvokeCommand(LPCMINVOKECOMMANDINFO info) override
    {
        if (!info) return E_INVALIDARG;
        if (HIWORD(info->lpVerb) != 0 || LOWORD(info->lpVerb) != 0) return E_FAIL;
        const std::wstring exe = ReadAppPath();
        if (exe.empty()) return HRESULT_FROM_WIN32(ERROR_FILE_NOT_FOUND);
        std::wstring args = L"--paste --to " + Quote(destination_);
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

    HRESULT STDMETHODCALLTYPE GetCommandString(UINT_PTR, UINT, UINT*, LPSTR, UINT) override { return E_NOTIMPL; }

private:
    volatile long refs_ = 1;
    std::wstring destination_;
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
    WriteKey(HKEY_CURRENT_USER, L"Software\\Microsoft\\Windows\\CurrentVersion\\Shell Extensions\\Approved",
             kClsidText, L"XoPy Pegar");
    return S_OK;
}

STDAPI DllUnregisterServer()
{
    RegDeleteKeyW(HKEY_CURRENT_USER, (std::wstring(L"Software\\Classes\\CLSID\\") + kClsidText).c_str());
    RegDeleteKeyW(HKEY_CURRENT_USER, (std::wstring(L"Software\\Classes\\CLSID\\") + kClsidText + L"\\InprocServer32").c_str());
    RegDeleteKeyW(HKEY_CURRENT_USER, L"Software\\Classes\\Directory\\Background\\shellex\\ContextMenuHandlers\\XoPyPaste");
    return S_OK;
}

BOOL WINAPI DllMain(HINSTANCE instance, DWORD reason, LPVOID)
{
    if (reason == DLL_PROCESS_ATTACH) { g_module = instance; DisableThreadLibraryCalls(instance); }
    return TRUE;
}
