# nanoCAD MCP release notes

[Documentation](docs/en/index.md)

## Build 450

Aligned dimensions can follow a selected line of an ordinary unit-scale
drawing view in Model Space or on an active Paper sheet. The native reference
retains the selected line at shared corners. An A4 dimension updated from 12 to
18 mm after a change to the source extrusion. Paper view creation verifies its
resident view and source history after the nanoCAD command completes.

The fixed-center native Hole feature applies and verifies through extent.
This package uses server 0.253.0, bridge 0.208.0 and IPC protocol 0.144.

Tag `450` identifies the packaged source. Download the installer, ZIPs and
checksums from [GitHub Release 450](https://github.com/NanocadMCP/nanocad-mcp/releases/tag/450).

## Build 449

Server 0.252.0 publishes top-level input fields for `manage_document` and
`analyze_geometry` in the default MCP catalog. Clients that discover arguments
from those fields can call both tools. The bridge still checks each action or
mode against its exact input contract. This package uses bridge 0.207.0 and IPC
protocol 0.144.

Tag `449` identifies the source used to build the package. Download the EXE,
portable ZIP and checksums from [GitHub Release 449](https://github.com/NanocadMCP/nanocad-mcp/releases/tag/449).

## Build 448

Server 0.251.0 verifies the named pipe owner's Windows user SID directly, so
the Administrators group can be the elevated token's default owner. The
per-user pipe permissions and peer process, session and elevation checks
remain in force. Bridge 0.207.0 and IPC protocol 0.144 are carried forward.
The default tool catalog uses compact operation indexes for entity and
structure edits. Exact variants remain available through focused guidance.

Tag `448` identifies the source used to build the package. The EXE, portable ZIP
and checksums are available from [GitHub Release 448](https://github.com/NanocadMCP/nanocad-mcp/releases/tag/448).
The installer is unsigned. Installation on a clean Windows machine has not
been verified.

## Package 447

Tag `447` identifies the source of the preceding maintenance package.

## Package contents

| Component | Version |
|---|---|
| MCP server | 0.250.0 |
| nanoCAD bridge | 0.207.0 |
| IPC protocol | 0.144 |

The Windows x64 release contains a self-contained MCP server, the nanoCAD bridge and native helpers, the view-capture worker, installation and connection-check scripts, and English documentation. Original project code and documentation use [MIT](LICENSE); [third-party terms](THIRD-PARTY-NOTICES.md) remain separate.

Supported workflows include ordinary 2D drawing, direct and retained parametric modeling of individual parts, editable DWG documentation, local references and loaded point-cloud interpretation. Profile consumers include extrusion, revolution, sweep and loft within their documented requirements. Read [capabilities](docs/en/capabilities.md).

## Changes in 447

- The initial MCP tool list is smaller. The same tools and input rules are available; request the focused schema and operation help for the full details of a selected write.
- The server resolves exact published tool names. Input validation reports malformed geometry and invalid text as request errors.
- The developer workflow includes a Windows managed build and test job, structured test reports, and a way to rerun failed checks by name. See [development](docs/en/development.md).

The nanoCAD bridge and protocol versions are unchanged. The package carries forward the native modeling capabilities and limits documented for [release 446](https://github.com/NanocadMCP/nanocad-mcp/releases/tag/446). The shared validation library includes the request fixes; packaging does not add new native CAD acceptance.

Use the EXE for guided installation or the ready-to-run ZIP for manual setup. The installer is unsigned; installation on a clean computer has not been verified. Read [installation](docs/en/installation.md) for setup, updates and removal.
