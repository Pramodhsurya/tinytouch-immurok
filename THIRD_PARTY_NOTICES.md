# Third-Party Notices

immurok for Windows (`app-win`) is licensed under the Apache License 2.0
(see [LICENSE](./LICENSE)). It incorporates or depends on the third-party
components listed below, each under its own license.

---

## Bundled source

### Microsoft Credential Provider sample

The C++ files under `ImmurokCredentialProvider/` — `CSampleProvider.{cpp,h}`,
`CSampleCredential.{cpp,h}`, `Dll.{cpp,h}`, `helpers.{cpp,h}`, and `common.h` —
are derived from Microsoft's official Credential Provider sample and retain
their original Microsoft copyright headers.

- Source: <https://github.com/microsoft/Windows-classic-samples/tree/main/Samples/CredentialProvider>
- License: MIT

`ImmurokCredentialProvider/CPipeListener.{cpp,h}` is **not** part of the
Microsoft sample; it is an original immurok implementation licensed under
Apache 2.0.

```
MIT License

Copyright (c) Microsoft Corporation

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

---

## NuGet dependencies

These are referenced as NuGet packages (not vendored into this repository) and
are restored at build time. All are compatible with Apache 2.0.

| Package | License |
|---------|---------|
| WPF-UI (`lepoco/wpfui`) | MIT |
| CommunityToolkit.Mvvm | MIT |
| H.NotifyIcon.Wpf | MIT |
| BouncyCastle.Cryptography (`bc-csharp`) | MIT |
| Serilog and Serilog.* sinks | Apache-2.0 |
| Microsoft.Extensions.Hosting[.WindowsServices] | MIT |
| System.Security.Cryptography.ProtectedData | MIT |

Refer to each package's own repository for the full license text.
