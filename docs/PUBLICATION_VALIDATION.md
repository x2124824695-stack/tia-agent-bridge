# Public source validation

Validated on Windows on 2026-09-22 with .NET 10 and locally installed TIA V21 Openness references:

- Solution build: passed, 0 warnings and 0 errors.
- Source/XML/compile/token checks: 22 passed.
- MCP registration, access gates and durable recovery regression: passed (16 inspect, 27 engineering, 30 online/control registrations).

This validates the published source copy. The tests did not launch TIA or connect a PLC.
The new source-batch API still requires end-to-end IDE acceptance. Earlier project
and block checks are historical results, not full validation of every tool.

Local configurations, project files, compiled binaries and proprietary Siemens DLLs
are excluded. The source driver resolves the repository location instead of using a
developer's private path. Upstream copyright and MIT notices are retained.
