# refs\<year>: private Navisworks reference assemblies

Per-version reference DLLs for building pyNavis without that Navisworks release
installed. **These DLLs are Autodesk's and are not redistributable: never commit or
publish this folder's contents.**

Layout (create per year as needed):

```
refs\2023\Autodesk.Navisworks.Api.dll
refs\2023\AdWindows.dll              (optional but preferred - see below)
refs\2024\...
refs\2025\...
```

How the build finds references for `-p:NavisVersion=<year>` (see Directory.Build.props):

1. `-p:NavisApiDir=...` / `-p:AdWindowsDir=...` explicit overrides
2. `NAVIS_API_DIR_<year>` environment variable
3. Registry `HKLM\SOFTWARE\Autodesk\Navisworks Manage\<year-2003>.0\Location`
4. `refs\<year>\`
5. Default install paths (`C:`/`D:\Program Files\Autodesk\Navisworks Manage <year>`)

`AdWindows.dll` ships only with full Navisworks installs. If a `refs\<year>` (or an
API-drop install folder) lacks it, the build automatically compiles against the newest
installed release's copy and emits a "cross-version" warning. That is safe at runtime:
the pyNavis loader resolves AdWindows to the copy the host Navisworks process has
already loaded (same-simple-name fallback in AssemblyResolver, regression-tested).

Source both DLLs from the root install folder of the matching Navisworks release,
e.g. `C:\Program Files\Autodesk\Navisworks Manage 2024\`.
