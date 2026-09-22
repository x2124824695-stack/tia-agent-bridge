# TiaAgentBridge architecture

> This is the original v0.1 design. For v0.2 changes, actual tool registration, transaction boundaries, durable recovery and unresolved capabilities, see [V0.2_PARITY.zh-CN.md](V0.2_PARITY.zh-CN.md).

## 1. Process boundary

TIA Portal V21 Openness is consumed from a .NET Framework 4.8 worker. The MCP-facing host targets modern .NET and never references Siemens assemblies directly.

```text
MCP client (Codex)
    |
    | MCP stdio
    v
TiaAgent.Host (net10.0)
    |
    | newline-delimited private JSON
    v
TiaAgent.Worker.V21 (net48)
    |
    | Siemens.Engineering.*
    v
TIA Portal V21
```

The worker is persistent for the host process lifetime. Calls are serialized by the host so concurrent AI tool calls cannot race through one TIA Portal session.

## 2. Access model

Four levels are reserved:

- `inspect`: project observation and source export only. Default.
- `engineering`: source mutation and compilation.
- `online`: reserved for future online diagnostics/download staging.
- `control`: reserved for future PLC RUN/STOP and other explicit control actions.

v0.1 exposes no online/control tool. Write tools are absent from MCP discovery in `inspect` mode, and the worker independently refuses mutation when its access mode is below `engineering`.

## 3. Source update transaction

The public API uses preview/apply instead of direct blind replacement.

```text
preview_block_update
    -> export current source
    -> SHA-256 current
    -> SHA-256 proposed
    -> bounded diff
    -> single-use 10 minute token

apply_block_update
    -> export current source again
    -> reject if current hash changed
    -> consume token
    -> worker import external source
    -> compile PLC
    -> if compile fails: best-effort import original source + compile
    -> if compile succeeds: re-export and hash
    -> save only if caller explicitly requested saveAfterSuccess
```

The worker uses the PLC's `PlcExternalSourceSystemGroup`; v0.1 does not yet address Software Unit-local source groups.

## 4. Deterministic block addressing

v0.1 paths are:

```text
<PLC device name>/Blocks/<folder...>/<block>
```

Example:

```text
PLC_1/Blocks/Motion/FB_Axis
```

The tree API returns the exact path to be used by content/compile/write operations.

## 5. Structured results

Every public MCP tool returns both a text JSON block and MCP `structuredContent` built from the same DTO. Tool envelopes use:

```json
{
  "success": true,
  "data": {},
  "warnings": [],
  "error": null
}
```

Transport errors and engineering failures are distinct. A compiler error can therefore return a valid structured compile report while the MCP result is marked as an error.

## 6. Version strategy

v0.1 implements only `TiaAgent.Worker.V21`. The intended multi-version layout is:

```text
TiaAgent.Host
  -> installation/version router
      -> TiaAgent.Worker.V20
      -> TiaAgent.Worker.V21
```

Each worker is compiled against the matching Siemens PublicAPI version. This is intentionally preferred over reflection-only calls across incompatible Openness versions.

## 7. Next architectural layer: text workspace

After live V21 acceptance, add a content-provider abstraction:

```text
IEngineeringContentProvider
  |- DirectOpennessContentProvider
  `- VciWorkspaceContentProvider
```

Openness remains the control plane; VCI becomes an optional content plane for scalable search/diff and incremental context loading.
