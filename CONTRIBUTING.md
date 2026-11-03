# Contributing

[Documentation](docs/en/index.md)

Bug reports, clear examples, documentation improvements and focused code changes are welcome. This community project has no guaranteed support response time.

## Report a problem

Open an issue in the project's GitHub repository using the bug report form. Include:

1. Windows, nanoCAD and AI-client versions, and MCP server/bridge/protocol versions reported by the assistant.
2. Your task, what you expected, and what actually happened.
3. The smallest reproduction steps, units and relevant dimensions.
4. The exact error and operation ID, if available, and whether the operation is pending or its outcome is uncertain.
5. A sanitized DWG or image only if you are allowed to share it.

An unresolved operation can have changed a drawing. Inspect the current state before repeating a write. See [troubleshooting](docs/en/troubleshooting.md). For vulnerabilities, follow [SECURITY.md](SECURITY.md) instead of posting technical details publicly.

## Suggest a feature

Describe the engineering result you need, your current workflow and why it is difficult. Give dimensions or a small example where useful. A list of command names alone rarely explains the need. Check [capabilities](docs/en/capabilities.md) first.

## Submit a change

- Use English for documentation, code comments, messages and example labels.
- Read the [development guide](docs/en/development.md). Discuss substantial changes in an issue before investing in them.
- Keep a pull request focused. Explain the user-visible problem, resulting behavior and relevant validation.
- Preserve document ownership, parameter dependencies and operation-outcome protections.
- Update the affected public documentation.
- Check links and formatting with `python scripts/check-public-docs.py`.
- Run checks appropriate to the change. A documentation edit does not require CAD tests, a nanoCAD restart or a real drawing change.
- Never include credentials, user drawings, proprietary SDK files or generated build output.

By submitting original code or documentation for inclusion, you agree to license your contribution under this project's [MIT License](LICENSE). Identify third-party material and its license explicitly; do not relicense it as your own work.
