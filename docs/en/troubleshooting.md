# Troubleshooting

[Documentation](../../README.md#documentation)

Start with the symptom below. Preserve the exact error message. If a drawing change may already have started, use [An operation is pending or its outcome is unknown](#an-operation-is-pending-or-its-outcome-is-unknown) before restarting anything.

## The assistant has no nanoCAD tools

1. Confirm that you are using the locally installed assistant you configured. A website does not read local MCP configuration files.
2. Check that the `nanocad` MCP server is enabled in that assistant.
3. Check that its command points to an existing `server\NanoCAD.McpServer.exe` from the complete extracted or installed package.
4. Check TOML or JSON syntax against [manual connection examples](installation.md#3-connect-your-assistant). Keep only one `nanocad` entry; preserve other servers.
5. After saving the settings, fully close and reopen the assistant.

Do not add Python, Node.js, an HTTP port or a second terminal process to this package's configuration. Its packaged server uses STDIO and includes its runtime.

## Tools exist, but nanoCAD is unavailable

Ask the assistant to check the connection without modifying anything. A status response with `available: false` means the MCP server answered, but it did not find a reachable nanoCAD bridge.

Check in this order:

1. Open nanoCAD 26 and a drawing. Complete startup/license dialogs and finish active commands.
2. Confirm that nanoCAD and the assistant run as the same Windows user, in the same desktop session and with the same elevation level. Normally both should run without administrator privileges.
3. For manual setup, check the registered DLL path in `nCad.ini` beside `nCad.exe`. The file must exist and belong to the same release as the server.
4. After a bridge registration or DLL change, save all drawings and reopen nanoCAD. Reloading only the assistant does not replace a DLL already loaded by nanoCAD.
5. If you deliberately set `NANOCAD_MCP_HOME`, ensure both applications receive the same value. Leave it unset for ordinary use.

See [installation](installation.md) for exact paths and registration syntax. Do not remove discovery files, change IPC permissions or disable security checks as a connection fix.

### ACCESS_DENIED when both applications run elevated

In packages 446 and 447, a connection can fail even when both processes run as the same Windows user at the same elevation. This occurs when Windows assigns the Administrators group as the elevated token's default owner, a policy that may apply on Windows Server or Remote Desktop hosts.

If your deployment allows it, save your drawings and reopen nanoCAD and the assistant in the same desktop session without administrator privileges. The source server version 0.251.0 checks the pipe owner's individual Windows user SID directly. It still verifies the peer process, session, user and elevation before sending a request.

## Connected, but the wrong drawing or instance is selected

Ask the assistant to list the available nanoCAD instances and open documents, then identify the drawing by its actual path and content. With several nanoCAD processes, it must select a specific bridge instance. `AMBIGUOUS_INSTANCE` asks for that choice; it does not mean that the installation failed.

Check the document again before approving an edit. A new session or a copied drawing can have different object identifiers; ask the assistant to read the current document instead of reusing identifiers from a previous session.

## An operation is pending or its outcome is unknown

`pending` means that work is still queued or running. A timeout is not proof of failure. `unknown` means that the tool has not established the complete outcome; some changes may already exist in the drawing.

Ask the assistant:

> Check the original operation's result using its operation ID. Read the actual drawing state before continuing. Do not repeat the change with a new operation ID.

The assistant should use `nanocad.get_operation_result` with the original operation ID and bridge instance. Preserve that ID, the original request and any returned error. Do not submit the same task again, restart nanoCAD, force-close a process or delete an object just because the first response was inconclusive. Inspect the original operation and its actual result first.

An operation's recorded outcome can remain `unknown` even after the current drawing state has been established. In that case, the assistant must reconcile the affected geometry, ownership and dependencies, retain the original unknown outcome, and continue only the work that remains, using freshly read references. It must not repeat completed changes or relabel the original operation as successful.

If the application is unresponsive, preserve diagnostic information and seek help before a forced shutdown. Successful completion still needs a check of the intended geometry. The absence of guaranteed Undo is not evidence that a change failed.

## The document changed during a task

`STALE_DOCUMENT` means that the drawing changed since the state used by the request. Manual edits, another tool call or document activation can invalidate that state.

If the response confirms that the write did not start, ask the assistant to reread the affected objects and prepare the remaining work from the current state. If the outcome is pending or unknown, resolve the original operation first. Do not turn a stale request into a new write by changing identifiers or revision values blindly.

## The requested function is unavailable

Check [capabilities and limits](capabilities.md). The existence of an entity type does not imply that every edit of that type is supported. Some functions also require the corresponding nanoCAD feature to be installed and licensed.

Give the assistant the exact message and intended engineering result. It may choose another supported native construction when appropriate, but it should explain a material difference. Do not use screen automation, simulate clicks or run unreviewed code inside nanoCAD to bypass a missing operation.

## A screenshot fails, is blank or shows the wrong view

Make the nanoCAD window available in the current desktop session. Finish modal dialogs and ask for an appropriate view. An image cannot establish hidden geometry, retained dependencies or whether the drawing has been saved; request native measurements and an explicit DWG save when needed.

## Geometry or dimensions look wrong

Describe the expected result and the observed difference, including units and the affected feature. Ask the assistant to inspect the actual geometry and dependencies, not just a displayed parameter value.

- After reopening a drawing, a path-array count edit can update the stored count without regenerating the visible items. Check the actual items; do not rely on the stored count alone.
- A missing dimensional annotation on screen does not prove that the driving constraint is absent. Its native annotation may be unavailable in the current context.
- A solver notice about excessive or conflicting constraints needs inspection of the actual constraints. A lost sketch support is a different condition.

Check the full [current limits](capabilities.md) before choosing a recovery action.

## Installation is blocked or reports damaged files

The installer is unsigned. If Windows or your organization's policy blocks it, use your organization's normal approval process. Do not turn off security protections.

For a checksum or missing-file error, obtain a fresh complete package and compare its SHA-256 with the published checksum. Avoid mixing files from different releases. A request for administrator approval normally concerns nanoCAD registration; it does not require running the assistant as administrator afterward.

## Uninstall cannot finish

Save all drawings and close nanoCAD and connected assistants, then retry removal through Windows Apps.

If the message says the connection settings changed after installation, the uninstaller stops to preserve your changes. Back up the settings and compare the current `nanocad` entry with the recorded installation in `%LOCALAPPDATA%\Programs\NanoCADMCP\installation.json`. Ask for help if the correct restoration is unclear. Do not delete unrelated configuration or plugin entries to force removal.

For a manually extracted ZIP, use the [manual removal instructions](installation.md#remove-a-manual-zip-connection).

## Information to include in a report

Use the repository's issue form or follow [Contributing](../../CONTRIBUTING.md). Include:

- The package number and reported server, bridge and contract versions.
- Your nanoCAD version, assistant and installation method.
- What you asked for, what happened, and what you expected.
- Exact error text and the original operation ID, if a change was submitted.
- A small drawing and an actual screenshot only when they are needed and safe to share.

Setup messages identify their own diagnostic file. Setup diagnostics normally appear under `%LOCALAPPDATA%\NanoCADMCP\Logs`; MCP/bridge routine logs use `%LOCALAPPDATA%\NanoCADMcp\logs` unless `NANOCAD_MCP_HOME` overrides the latter root. These spellings refer to the same directory on a normal Windows installation. For Claude Desktop, MCP client logs are under `%APPDATA%\Claude\logs`.

Review every attachment before sharing it. DWG files, images, paths, text and logs can reveal project or personal information. Do not attach assistant credentials or a complete unredacted client configuration. Report a suspected security issue according to [Security](../../SECURITY.md), rather than posting sensitive details publicly.
