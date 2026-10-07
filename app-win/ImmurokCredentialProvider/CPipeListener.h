// Copyright 2026 immurok
// SPDX-License-Identifier: Apache-2.0
//
// Named-pipe listener for the immurok Credential Provider.
//
// This is an independent implementation written against the Win32 named-pipe
// API. The surrounding Credential Provider skeleton (CSampleProvider /
// CSampleCredential / helpers / Dll) derives from Microsoft's MIT-licensed
// Credential Provider sample; this listener is original immurok code.
//
// Contract with the immurok service:
//   - The provider hosts a byte pipe at \\.\pipe\ImmurokCredentialProvider.
//   - When the service wants the locked session unlocked, it connects and
//     writes a single payload of UTF-16LE text: <username>\0<password>\0.
//   - On receipt the listener caches the credentials, flips its "ready" flag,
//     and asks the provider to re-enumerate its tiles so LogonUI submits them.
//
// The pipe's DACL grants only SYSTEM and Administrators (see the .cpp), so an
// unprivileged process cannot push a credential payload and drive an auto-logon.
// The immurok service runs as LocalSystem, so it still connects normally.

#pragma once

#include <windows.h>

class CSampleProvider;  // owner; full definition pulled in by the .cpp

class CPipeListener
{
public:
    CPipeListener();
    ~CPipeListener();

    // Starts the background listener thread. Safe to call once per instance.
    HRESULT Initialize(CSampleProvider* pProvider);

    // TRUE once a complete credential payload has arrived over the pipe.
    BOOL GetUnlockingStatus();

    // Hands back weak references to the cached username/password. The listener
    // keeps ownership; callers must copy what they need and must not free them.
    void GetCredential(PWSTR* ppwzUsername, PWSTR* ppwzPassword);

    // Consumes the current authorization: clears the "ready" flag and wipes the
    // cached credential, so one fingerprint unlock authorizes exactly one
    // submission. The credential calls this once LogonUI has taken the
    // serialized credential (and again, defensively, on a failed logon). After
    // it, GetUnlockingStatus() is FALSE until the next payload arrives -- which
    // requires a fresh touch on the device, not a click on the Cancel button.
    void ConsumeCredential();

private:
    static DWORD WINAPI _ThreadProc(LPVOID lpParameter);
    void _RunLoop();
    void _StoreCredential(const BYTE* pbData, DWORD cbData);
    void _ClearCredentialLocked();

    CSampleProvider* _pProvider;   // owner, notified when credentials arrive
    HANDLE           _hThread;     // listener thread
    HANDLE           _hStopEvent;  // signalled by the destructor to stop the loop
    volatile LONG    _fReady;      // 0 until a payload has been received
    PWSTR            _pwzUsername;  // cached, owned by this object
    PWSTR            _pwzPassword;  // cached, owned by this object
    CRITICAL_SECTION _lock;        // guards the cached credential pointers
};
