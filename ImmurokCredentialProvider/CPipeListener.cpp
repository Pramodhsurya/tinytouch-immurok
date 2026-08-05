// Copyright 2026 immurok
// SPDX-License-Identifier: Apache-2.0
//
// Independent implementation of the immurok Credential Provider pipe listener.
// See CPipeListener.h for the contract. The Credential Provider skeleton this
// plugs into derives from Microsoft's MIT-licensed Credential Provider sample;
// this file is original immurok code and carries no third-party copyright.

#include "CPipeListener.h"
#include "CSampleProvider.h"

#include <sddl.h>
#include <strsafe.h>

static const WCHAR c_szPipeName[] = L"\\\\.\\pipe\\ImmurokCredentialProvider";

// Largest payload we will accept in one connection (bytes). A username plus a
// password is tiny; this is a generous ceiling that also caps a hostile writer.
static const DWORD c_cbMaxPayload = 8192;

CPipeListener::CPipeListener() :
    _pProvider(NULL),
    _hThread(NULL),
    _hStopEvent(NULL),
    _fReady(0),
    _pwzUsername(NULL),
    _pwzPassword(NULL)
{
    InitializeCriticalSection(&_lock);
}

CPipeListener::~CPipeListener()
{
    // Ask the loop to stop and wait for it, so the thread never touches us
    // after we are gone.
    if (_hStopEvent != NULL)
    {
        SetEvent(_hStopEvent);
    }
    if (_hThread != NULL)
    {
        WaitForSingleObject(_hThread, 5000);
        CloseHandle(_hThread);
        _hThread = NULL;
    }
    if (_hStopEvent != NULL)
    {
        CloseHandle(_hStopEvent);
        _hStopEvent = NULL;
    }

    EnterCriticalSection(&_lock);
    _ClearCredentialLocked();
    LeaveCriticalSection(&_lock);
    DeleteCriticalSection(&_lock);
}

HRESULT CPipeListener::Initialize(CSampleProvider* pProvider)
{
    _pProvider = pProvider;

    _hStopEvent = CreateEvent(NULL, TRUE /*manual reset*/, FALSE, NULL);
    if (_hStopEvent == NULL)
    {
        return HRESULT_FROM_WIN32(GetLastError());
    }

    _hThread = CreateThread(NULL, 0, _ThreadProc, this, 0, NULL);
    if (_hThread == NULL)
    {
        return HRESULT_FROM_WIN32(GetLastError());
    }

    return S_OK;
}

BOOL CPipeListener::GetUnlockingStatus()
{
    return InterlockedCompareExchange(&_fReady, 0, 0) != 0;
}

void CPipeListener::GetCredential(PWSTR* ppwzUsername, PWSTR* ppwzPassword)
{
    EnterCriticalSection(&_lock);
    if (ppwzUsername != NULL)
    {
        *ppwzUsername = _pwzUsername;
    }
    if (ppwzPassword != NULL)
    {
        *ppwzPassword = _pwzPassword;
    }
    LeaveCriticalSection(&_lock);
}

DWORD WINAPI CPipeListener::_ThreadProc(LPVOID lpParameter)
{
    CPipeListener* pThis = static_cast<CPipeListener*>(lpParameter);
    if (pThis != NULL)
    {
        pThis->_RunLoop();
    }
    return 0;
}

void CPipeListener::_RunLoop()
{
    // The service connects only briefly to push a credential, so we recreate a
    // fresh pipe instance for each connection.
    //
    // The DACL is left NULL (any caller may connect and write). This mirrors
    // the alpha's behavior and is intentionally permissive; tightening it to
    // grant only SYSTEM is a tracked hardening item. Because a NULL DACL means
    // "no protection", never widen what the pipe trusts on the back of it.
    SECURITY_ATTRIBUTES sa = {};
    SECURITY_DESCRIPTOR sd = {};
    LPSECURITY_ATTRIBUTES psa = NULL;
    if (InitializeSecurityDescriptor(&sd, SECURITY_DESCRIPTOR_REVISION) &&
        SetSecurityDescriptorDacl(&sd, TRUE /*present*/, NULL /*NULL DACL*/, FALSE))
    {
        sa.nLength = sizeof(sa);
        sa.lpSecurityDescriptor = &sd;
        sa.bInheritHandle = FALSE;
        psa = &sa;
    }

    for (;;)
    {
        if (WaitForSingleObject(_hStopEvent, 0) == WAIT_OBJECT_0)
        {
            break;
        }

        HANDLE hPipe = CreateNamedPipeW(
            c_szPipeName,
            PIPE_ACCESS_INBOUND | FILE_FLAG_OVERLAPPED,
            PIPE_TYPE_BYTE | PIPE_READMODE_BYTE | PIPE_WAIT,
            1 /*max instances*/,
            0 /*out buffer*/,
            c_cbMaxPayload /*in buffer*/,
            0 /*default timeout*/,
            psa);

        if (hPipe == INVALID_HANDLE_VALUE)
        {
            // Transient (e.g. another instance owns the name). Back off, but
            // stay responsive to a stop request.
            if (WaitForSingleObject(_hStopEvent, 1000) == WAIT_OBJECT_0)
            {
                break;
            }
            continue;
        }

        OVERLAPPED ov = {};
        ov.hEvent = CreateEvent(NULL, TRUE, FALSE, NULL);
        if (ov.hEvent == NULL)
        {
            CloseHandle(hPipe);
            break;
        }

        // --- Wait for a client, cancelable via the stop event. ---
        BOOL fConnected = ConnectNamedPipe(hPipe, &ov);
        DWORD dwErr = fConnected ? ERROR_SUCCESS : GetLastError();
        if (!fConnected && dwErr == ERROR_IO_PENDING)
        {
            HANDLE waits[2] = { _hStopEvent, ov.hEvent };
            DWORD w = WaitForMultipleObjects(2, waits, FALSE, INFINITE);
            if (w == WAIT_OBJECT_0)
            {
                CancelIo(hPipe);
                CloseHandle(ov.hEvent);
                CloseHandle(hPipe);
                break;
            }
            DWORD cb = 0;
            if (!GetOverlappedResult(hPipe, &ov, &cb, FALSE))
            {
                CloseHandle(ov.hEvent);
                CloseHandle(hPipe);
                continue;
            }
        }
        else if (!fConnected && dwErr != ERROR_PIPE_CONNECTED)
        {
            CloseHandle(ov.hEvent);
            CloseHandle(hPipe);
            continue;
        }

        // --- Read the payload, cancelable via the stop event. ---
        BYTE* pbBuf = static_cast<BYTE*>(HeapAlloc(GetProcessHeap(), HEAP_ZERO_MEMORY, c_cbMaxPayload));
        if (pbBuf != NULL)
        {
            ResetEvent(ov.hEvent);
            DWORD cbRead = 0;
            BOOL fRead = ReadFile(hPipe, pbBuf, c_cbMaxPayload, &cbRead, &ov);
            DWORD dwReadErr = fRead ? ERROR_SUCCESS : GetLastError();
            BOOL fStop = FALSE;
            if (!fRead && dwReadErr == ERROR_IO_PENDING)
            {
                HANDLE waits[2] = { _hStopEvent, ov.hEvent };
                DWORD w = WaitForMultipleObjects(2, waits, FALSE, INFINITE);
                if (w == WAIT_OBJECT_0)
                {
                    CancelIo(hPipe);
                    fStop = TRUE;
                }
                else
                {
                    fRead = GetOverlappedResult(hPipe, &ov, &cbRead, FALSE);
                }
            }

            if (!fStop && fRead && cbRead >= sizeof(WCHAR))
            {
                _StoreCredential(pbBuf, cbRead);
                InterlockedExchange(&_fReady, 1);
                if (_pProvider != NULL)
                {
                    // Ask LogonUI to re-enumerate; it will pull the credential
                    // back out via GetCredential and submit it.
                    _pProvider->OnUnlockingStatusChanged();
                }
            }

            SecureZeroMemory(pbBuf, c_cbMaxPayload);
            HeapFree(GetProcessHeap(), 0, pbBuf);

            if (fStop)
            {
                CloseHandle(ov.hEvent);
                DisconnectNamedPipe(hPipe);
                CloseHandle(hPipe);
                break;
            }
        }

        CloseHandle(ov.hEvent);
        DisconnectNamedPipe(hPipe);
        CloseHandle(hPipe);
    }
}

// Parse a UTF-16LE "<username>\0<password>\0" payload and cache copies. Works on
// a zero-padded copy so a truncated or terminator-less payload can't read past
// the end.
void CPipeListener::_StoreCredential(const BYTE* pbData, DWORD cbData)
{
    DWORD cchData = cbData / sizeof(WCHAR);

    // +2 guarantees terminators for both the username and password scans even
    // if the sender omitted them.
    size_t cchBuf = cchData + 2;
    PWSTR pwzBuf = static_cast<PWSTR>(HeapAlloc(GetProcessHeap(), HEAP_ZERO_MEMORY, cchBuf * sizeof(WCHAR)));
    if (pwzBuf == NULL)
    {
        return;
    }
    memcpy(pwzBuf, pbData, cchData * sizeof(WCHAR));

    const WCHAR* pwzUser = pwzBuf;
    size_t cchUser = 0;
    while (pwzUser[cchUser] != L'\0' && cchUser < cchBuf)
    {
        cchUser++;
    }
    const WCHAR* pwzPass = pwzUser + cchUser + 1;  // within [pwzBuf, pwzBuf+cchBuf)

    EnterCriticalSection(&_lock);
    _ClearCredentialLocked();
    HANDLE hHeap = GetProcessHeap();
    size_t cbUser = (cchUser + 1) * sizeof(WCHAR);
    _pwzUsername = static_cast<PWSTR>(HeapAlloc(hHeap, 0, cbUser));
    if (_pwzUsername != NULL)
    {
        StringCbCopyW(_pwzUsername, cbUser, pwzUser);
    }
    size_t cchPass = 0;
    while (pwzPass[cchPass] != L'\0' && (pwzPass + cchPass) < (pwzBuf + cchBuf))
    {
        cchPass++;
    }
    size_t cbPass = (cchPass + 1) * sizeof(WCHAR);
    _pwzPassword = static_cast<PWSTR>(HeapAlloc(hHeap, 0, cbPass));
    if (_pwzPassword != NULL)
    {
        StringCbCopyW(_pwzPassword, cbPass, pwzPass);
    }
    LeaveCriticalSection(&_lock);

    SecureZeroMemory(pwzBuf, cchBuf * sizeof(WCHAR));
    HeapFree(hHeap, 0, pwzBuf);
}

// Caller must hold _lock.
void CPipeListener::_ClearCredentialLocked()
{
    HANDLE hHeap = GetProcessHeap();
    if (_pwzPassword != NULL)
    {
        size_t cch = 0;
        if (SUCCEEDED(StringCchLengthW(_pwzPassword, STRSAFE_MAX_CCH, &cch)))
        {
            SecureZeroMemory(_pwzPassword, cch * sizeof(WCHAR));
        }
        HeapFree(hHeap, 0, _pwzPassword);
        _pwzPassword = NULL;
    }
    if (_pwzUsername != NULL)
    {
        HeapFree(hHeap, 0, _pwzUsername);
        _pwzUsername = NULL;
    }
}
