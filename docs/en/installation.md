# Installation and connection

[Documentation](../../README.md#documentation)

Choose **the Windows installer** for normal use. Choose **manual ZIP setup** if you want to control the file location and application settings yourself. Both connect the same local MCP server and nanoCAD bridge.

## Before you start

You need:

- A 64-bit Windows installation supported by nanoCAD 26.
- nanoCAD 26 installed and working. The functions you request must be available in your installed, licensed nanoCAD configuration.
- Codex desktop or Claude Desktop installed and signed in, with permission to use local MCP servers.
- Access to change nanoCAD's `nCad.ini`. Windows may require administrator approval for that file.

The release includes the MCP server runtime. You do not need Python, Node.js, a .NET SDK or the nanoCAD SDK to use it. Building from source is a separate [developer workflow](development.md).

Run nanoCAD and the assistant as the **same Windows user, in the same desktop session, at the same elevation level**. Normally, start both without **Run as administrator**. Installation can request administrator approval without requiring either application to run elevated afterward.

Download the installer or release ZIP from the [download section](../../README.md#download). GitHub's **Source code (zip)** archive contains source files; it is not the ready-to-use Windows package. The installer is unsigned. If Windows or your organization's policy blocks it, ask your administrator; do not disable your security protections.

## Install with the Windows installer

1. Save **all** open drawings and close nanoCAD. Close the assistant you want to connect.
2. Run `NanoCAD-MCP-Setup-453.exe` from [release 453](https://github.com/NanocadMCP/nanocad-mcp/releases/tag/453).
3. Check the nanoCAD 26 installation folder. It must contain `nCad.exe` and `nCad.ini`.
4. Select **Codex**, **Claude Desktop**, or both, then click **Install**. For an update, select every assistant connected by the previous installation.
5. Approve the Windows permission request if it appears.
6. Click **Finish**. The installer can be closed; it does not need to remain running.
7. Open nanoCAD, finish its startup dialogs and open a drawing. Start the selected assistant again.
8. Complete [Verify the connection](#verify-the-connection).

The installer places the program in `%LOCALAPPDATA%\Programs\NanoCADMCP`, registers the bridge for automatic loading and adds the `nanocad` MCP entry to the selected assistant's settings. It backs up changed settings and retains the previous `nanocad` entry for removal. Other MCP entries are preserved. No manual DLL loading is required after normal installation.

For build 453, installed documentation starts at `%LOCALAPPDATA%\Programs\NanoCADMCP\versions\453\server\README.md`. Keep the accompanying `docs` and `licenses` folders so their links work offline.

## Manual ZIP setup

This route does not create a Windows Apps uninstall entry. Keep your backups so you can reverse the changes.

### 1. Extract the complete package

Save all drawings and close nanoCAD and your assistant. If you are switching from a managed installation, [uninstall it](#uninstall-an-installer-managed-copy) before setting up the portable copy.

Extract the **entire release ZIP** to a permanent folder you can read and write. The examples below use `C:\Tools\nanoCAD-MCP`; replace that path with your chosen folder. Do not run files inside the ZIP or copy only a DLL or EXE. Keep the `bridge` and `server` folders together, including their supporting files.

Check that these files exist:

```text
C:\Tools\nanoCAD-MCP\bridge\NanoCADBridge.dll
C:\Tools\nanoCAD-MCP\server\NanoCAD.McpServer.exe
C:\Tools\nanoCAD-MCP\manifest.json
```

### 2. Register the bridge in nanoCAD

1. Locate the `nCad.ini` **beside `nCad.exe` in your nanoCAD installation folder**. A common folder is `C:\Program Files\Nanosoft AS\nanoCAD x64 26.0`; use your actual installation path.
2. Make a backup copy of `nCad.ini`.
3. Open it in a text editor. Administrator approval may be needed to save this file.
4. Find the existing `[\NetModules]` section. Add the absolute DLL path on its own line. If the section does not exist, add it once:

```ini
[\NetModules]
C:\Tools\nanoCAD-MCP\bridge\NanoCADBridge.dll
```

Keep other entries in the section. Do not put quotes around the DLL path or create a duplicate section. Remove any previous **nanoCAD MCP bridge** path you are replacing, after making the backup; only one version of this bridge should load. Leave unrelated plugins alone.

5. Save the file. Close an elevated text editor when finished. Start nanoCAD normally; the bridge loads at startup.

The server setting in the next step does not load a DLL into nanoCAD by itself. Both parts of setup are required.

### 3. Connect your assistant

Configure one or both supported clients. Use the executable from the **same extracted release** as the registered bridge. The assistant starts the MCP server when it needs the connection; you do not need to keep an extra terminal running.

#### Codex

1. Close Codex and back up `%USERPROFILE%\.codex\config.toml` if it exists. If you explicitly use a custom `CODEX_HOME`, edit `config.toml` there instead.
2. Add this table, or replace only the existing `mcp_servers.nanocad` table:

```toml
[mcp_servers.nanocad]
command = 'C:\Tools\nanoCAD-MCP\server\NanoCAD.McpServer.exe'
args = []
enabled = true
```

Preserve other settings and MCP servers. Do not add the table twice. The single quotes shown above are TOML literal-string quotes: keep Windows backslashes as written. If you have set a project-specific `nanocad` entry, check that it does not override this path with another release.

3. Save the file and reopen Codex.

#### Claude Desktop

1. Open **Settings → Developer → Edit Config**, or locate `%APPDATA%\Claude\claude_desktop_config.json`. Back it up, then fully quit Claude Desktop before editing.
2. If the file is empty or new, use:

```json
{
  "mcpServers": {
    "nanocad": {
      "command": "C:\\Tools\\nanoCAD-MCP\\server\\NanoCAD.McpServer.exe",
      "args": []
    }
  }
}
```

If the file already has settings, merge only the `nanocad` entry into its existing `mcpServers` object. Preserve all other entries. JSON requires double backslashes in Windows paths, double quotes and no trailing commas.

3. Save the file and reopen Claude Desktop.

The installer targets Codex and Claude Desktop. Other MCP clients may support the same local STDIO transport, but their setup and compatibility are not established by this guide. The Claude website does not read this local configuration.

## Verify the connection

In nanoCAD, open a drawing, finish active commands and close any modal dialog. Then ask your assistant:

> Check the nanoCAD connection and report the server, bridge and contract versions. Show the current drawing without changing or saving it.

Confirm that:

- The assistant finds a reachable bridge and reports nanoCAD ready.
- The reported versions match the [current release](../../README.md#download).
- The document is the drawing you intended, and the image is an actual capture of it.

With several nanoCAD processes open, ask the assistant to list them and select the correct instance and drawing before editing. A successful connection check alone does not select your intended drawing. A screenshot does not save a DWG.

The ZIP also contains `Check-Connection.ps1`. From its extracted root, this optional check reads connection status without changing the drawing:

```powershell
powershell.exe -NoProfile -File .\Check-Connection.ps1
```

It reports whether its own server process can reach nanoCAD; it does **not** prove that your assistant has reloaded its settings. Complete the assistant check above as well. If script execution is blocked by policy, use your assistant or contact your administrator rather than changing the policy.

## Update

Save all drawings and close nanoCAD and connected assistants first. Keep the server and bridge from one release together.

- **Installer:** run the new installer and select all previously connected assistants. Reopen the applications and verify the versions.
- **Manual ZIP:** extract the new release into a new permanent folder. Update both the DLL path in `nCad.ini` and the server path in each assistant configuration. Retain backups and the old folder until the new connection is verified. Do not replace DLLs while nanoCAD is running.

## Remove the connection

### Uninstall an installer-managed copy

1. Save all drawings and close nanoCAD, Codex and Claude Desktop.
2. Open **Windows Settings → Apps → Installed apps**, select **nanoCAD MCP**, then **Uninstall**.
3. Follow the prompts. If connection settings were changed manually after installation, the uninstaller may preserve them and stop; see [troubleshooting](troubleshooting.md#uninstall-cannot-finish).

The uninstaller removes owned program files and restores the previous connection entries it recorded. Drawings and other applications are preserved. Downloaded packages and diagnostic backups can remain.

### Remove a manual ZIP connection

1. Save all drawings and close nanoCAD and connected assistants.
2. Back up `nCad.ini` and remove only the line pointing to this copy's `NanoCADBridge.dll`. Preserve other plugins and the section itself.
3. Remove only `[mcp_servers.nanocad]` and its associated configuration from Codex, or only `mcpServers.nanocad` from Claude Desktop. If you replaced an earlier entry, restore it from your backup instead. Preserve valid TOML/JSON and all unrelated settings.
4. Reopen the applications. Once this copy is disconnected and its files are no longer in use, you can delete its extracted folder. Keep any drawings you saved there.

## Client configuration references

- [Official OpenAI documentation: local MCP configuration](https://learn.chatgpt.com/docs/extend/mcp?surface=cli).
- [Model Context Protocol: connect a local server to Claude Desktop](https://modelcontextprotocol.io/docs/develop/connect-local-servers).

Next: [first tasks and everyday use](user-guide.md). If connection fails: [troubleshooting](troubleshooting.md).
