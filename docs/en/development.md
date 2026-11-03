# Build and develop nanoCAD MCP

[Documentation](index.md)

This guide is for developers working from a source checkout. To use a packaged release, follow [Installation](installation.md); an SDK or compiler is not required for normal use.

## Architecture

The assistant launches `NanoCAD.McpServer.exe` as a local process and exchanges MCP messages over standard input and output. The server finds the nanoCAD bridge through local discovery files and communicates with it through Windows named pipes. The bridge runs inside nanoCAD and calls the native drawing and modeling APIs. It schedules drawing work on the host's permitted execution context.

| Source directory | Responsibility |
|---|---|
| `src/NanoCAD.McpServer` | MCP tools, input schemas, validation, agent guidance and presentation |
| `src/NanoCAD.Contracts` | Shared request, response and operation contracts |
| `src/NanoCAD.Ipc` | Local discovery, framing, queues and communication security |
| `src/NanoCADBridge` | Managed nanoCAD integration and native operations |
| `src/NanoCAD.NativeParameters` | Native parameter access and SDK compatibility declarations |
| `src/NanoCAD.NativeModelHistory` | Native feature-history and body-property adapters |
| `src/NanoCAD.VisualWorker` | Capture of the nanoCAD window for visual verification |
| `deployment` | Windows setup, connection and removal scripts, and installer launcher |
| `tests/NanoCAD.Tests` | Managed tests, including schema and contract checks |

The public tool list groups many operations into a small set of tools. For operation details, use the running server's `nanocad.get_guidance` tool with a topic of `operation` or `schema`. Obtain the current schemas before writing an integration. Handles, native identities and revision tokens are observations of a particular drawing state; do not treat them as permanent identifiers across arbitrary edits or drawings.

## Prerequisites

For a full build on Windows x64, install:

1. The .NET SDK version specified by `global.json` (currently `10.0.400`, with patch roll-forward allowed).
2. Visual Studio or Build Tools with the MSVC x64/x86 C++ tools. The build scripts locate them with `vswhere.exe` in the standard Visual Studio Installer directory.
3. Windows SDK `10.0.26100.0`, which the visual-worker build script explicitly selects.
4. nanoCAD 26 and the modules needed by the features you will test. Managed references are read from the installed nanoCAD `bin` directory.
5. An authorized copy of nanoCAD SDK `NCadSDK_260_EN_7355.4942.259`, extracted to a directory of that name at the checkout root. The native build scripts use this exact directory and verify selected SDK headers.
6. Network access to download the .NET SDK and restore NuGet packages, unless those dependencies are already available locally.

Obtain nanoCAD and its SDK from their vendor under the applicable terms. They are not included in this source checkout. Generated SDK compatibility files also remain vendor-derived material; generate them locally from your authorized SDK rather than treating them as MIT-licensed project code.

## Full build

Run the following commands in PowerShell from the checkout root:

```powershell
.\scripts\bootstrap.ps1
.\scripts\build.ps1 -Configuration Release -NanoCadManagedDir 'C:\Program Files\Nanosoft AS\nanoCAD x64 26.0\bin'
```

Replace the nanoCAD path if your installation is elsewhere. `bootstrap.ps1` downloads the SDK pinned by `global.json` into `.tools/dotnet` and verifies its archive against Microsoft's published SHA-512 value. If that SDK is already in `.tools/dotnet`, the build can use it directly.

The build compiles the managed solution and native helpers, builds the visual worker, and writes runtime outputs under `artifacts/server` and `artifacts/bridge`. These are developer outputs: the server publish performed by this script is not a complete self-contained installer package. The build does not load a bridge into nanoCAD, change the assistant connection, or establish that CAD operations work.

Before trying your own bridge, save every open drawing and close nanoCAD. Keep the working release separate. A native test needs a complete matching bridge/helper set and a server with its dependencies; the framework-dependent server also needs a suitable local .NET runtime. Use the registration and client-configuration steps in [manual ZIP setup](installation.md#manual-zip-setup) as a reference, adjusting paths for the developer outputs. The release ZIP's manifest and self-contained-runtime claims do not apply to loose build outputs. Do not replace DLLs while nanoCAD has them loaded.

## Server-only build and managed tests

Work on MCP contracts and guidance can start without the nanoCAD SDK or a running CAD session. With the pinned .NET SDK available, run:

```powershell
.\.tools\dotnet\dotnet.exe build .\src\NanoCAD.McpServer\NanoCAD.McpServer.csproj -c Release
.\.tools\dotnet\dotnet.exe run --project .\tests\NanoCAD.Tests\NanoCAD.Tests.csproj -c Release -- --filter 'published input schemas expose geometry without references'
```

The test project is an executable test runner, so use `dotnet run` rather than assuming `dotnet test` will discover its checks. `--filter` selects test-name substrings and may be repeated. The full suite includes checks that expect additional build products or repository resources. Inspect those prerequisites before running it in a reduced source checkout. A passing schema test verifies the published contract, not native CAD execution.

For native integration changes, test the engineering result in a deliberately selected drawing, including dependent geometry and a real MCP image. A saved drawing, a correct API response and an image answer different verification questions. Use a copy when the task is to test or experiment. Avoid replaying writes whose final outcome is unknown; first establish the actual drawing state.

## Working on the integration

- Prefer an applicable specialized operation over a simulated sequence of unrelated drawing operations.
- Read the matching nanoCAD SDK declaration and example before relying on native behavior. Similar method names in different API families do not establish equivalent behavior.
- Keep server diagnostics on standard error. Standard output carries MCP messages.
- Preserve ownership, dependency and revision checks. A broader schema must not silently bypass native safeguards.
- Update the English user documentation when observable behavior changes. Describe capabilities and concrete limits without exposing internal test chronology to users.

## Source and release boundaries

The project's original code and documentation use the [MIT License](../../LICENSE). Third-party components and vendor-derived SDK material retain their own terms; see [Third-party notices](../../THIRD-PARTY-NOTICES.md). Keep SDK distributions, generated SDK header slices, local configuration, credentials, test drawings, build outputs and diagnostic archives out of public source commits.

A source build can differ from the distributed binaries because of dependency resolution and toolchain versions. Do not claim byte-for-byte reproduction unless you have verified it. A release should contain a complete matching server/bridge/helper set, required runtime files, current guidance, this documentation and license notices. Verify its manifest and packaged MCP publication independently of the source build.

See [Contributing](contributing.md) for change proposals and [Privacy](privacy.md) before sharing diagnostic data.
