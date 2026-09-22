# TiaAgentBridge 0.2

Siemens TIA Portal V21 MCP bridge: .NET 10 Host → persistent .NET Framework 4.8 Worker → Siemens Openness.

本次按本机汇川 InoProShop MCP 1.1 源码逐项对照后扩展。**工程模式从 6 个工具扩展到 27 个；尚未全部对齐汇川，也未完成真实工程/PLC 验收。** 完整映射、原生 API 依据、操作示例和未完成项见 [功能对照与实现记录](docs/V0.2_PARITY.zh-CN.md)。

## Capabilities

- inspect (default, 16 tools): project/block inspection, paginated object tree, block/UDT/tag-table export, batch source reads, literal search, native cross references, OB configuration, libraries, hardware catalog, compile history, capabilities and command recovery.
- engineering (27 tools): original preview/apply block update and compile; launch/open/create/save/archive projects; detach; preview/apply native engineering changes.
- online/control (30 tools): additionally connect/disconnect and read the selected CPU's connection state. No download, runtime-variable I/O or independent RUN/STOP implementation.

`preview_engineering_change` supports `write_source_batch`, `create_source`, `import_xml`, `create_folder`, `create_tag_table`, `create_tag`, `delete_object`, `rename_object`, `move_object`, `set_attribute`, `add_device`. Sources include SCL/STL/DB/UDT; supported graphic blocks use Siemens XML. Device parameters, I/O addresses/channels and OB attributes retain Siemens-native meanings.

## Build and test

Requirements: Windows, TIA V21 with Openness, .NET 10 SDK, net48 targeting assemblies (provided through a NuGet reference package). Siemens DLLs are referenced from the installed PublicAPI and are never redistributed.

```powershell
dotnet build TiaAgentBridge.sln -c Release
.\scripts\test.ps1
```

Use installed `dotnet` if the local portable SDK is absent. Override `/p:TiaPortalV21Dir="D:\Your\PublicAPI\V21\net48"` if needed. The project detects standard C: and D: installs. The host build copies the worker to `openness-worker`.

Offline tests do not launch TIA or connect a PLC. They exercise validation, MCP registration, host/worker permissions, timeout no-replay behavior and durable uncertain-result recovery. Real project acceptance is still required.

## Run

```powershell
.\src\TiaAgent.Host\bin\Release\net10.0\TiaAgent.Host.exe --access inspect
```

Use `--access engineering` for engineering writes, `--access online` for explicit connection tools. Register using `scripts/install-codex.ps1`. Installation/configuration scripts are optional and were not run by this development task.

For real Openness access, the Windows login must have active `Siemens TIA Openness` group membership, and TIA's access prompt must be handled. Read calls attach to an already-open project; they do not open/switch it. `launch_tia` and `open_project` are explicit operations.

## Editing and recovery

1. Call `get_project_structure` and use its exact URL-escaped paths.
2. Read/export the target, then preview the change.
3. Apply its single-use 10-minute token in the same MCP session.
4. For a complete source change, preview/apply `write_source_batch` with ordered `sources` items (`path`, `name`, `format`, `content`). Each item has one declaration, all belong to one PLC. Explicit existing targets can be replaced; duplicate names, cross-PLC targets, hidden overwrites and mismatched kinds are rejected. All writes share one rollback transaction and no intermediate compilation.
5. After the complete batch is written, call `compile` with `plcName` and without `blockPath`, inspect the whole-PLC diagnostics, then explicitly `save_project`. Group compiler fixes into the next complete batch; do not compile after each individual write.

Generic changes use exclusive access and an Openness transaction, returning `saved=false, compiled=false`. Siemens forbids compile/save inside transactions. The original SCL/global-DB update workflow still compiles and attempts restoration on failure; worker-side hash checks now close the concurrent-edit window. A save failure reports applied-but-unsaved status explicitly.

On timeout, inspect `get_command_status`; never repeat the write. A completed uncertain result can be acknowledged using `recover_session`. Unknown outcomes remain locked across restarts. `TIA_AGENT_TIMEOUT_MS` controls the wait (1000–600000; default 60000). State is stored below `%LOCALAPPDATA%/TiaAgentBridge`, or `TIA_AGENT_STATE_DIRECTORY` when explicitly configured. One host per installation/state directory is allowed.

Software Units and V20 are not implemented. Partial source coverage is explicit; text search is not a semantic refactor. CPU-specific online I/O, download policy, simulation, global-library installation and semantic symbol rename remain outstanding. See the [51-tool comparison](docs/V0.2_PARITY.zh-CN.md) rather than treating registration as live validation.

## Public preview status

This is an independently maintained derivative; see [NOTICE.md](NOTICE.md) for upstream attribution. No Siemens SDK, local PLC project, access token or private machine configuration is included.

The source batch API builds successfully and preserves explicit target snapshots and a single transaction. Its end-to-end batch acceptance is still pending: a local Openness reconnect timed out before batch application. Offline tests and earlier individual-block acceptance do not prove full hardware validation. Compile history currently lacks project identity metadata; treat it as historical only.
