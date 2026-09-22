"""Live stdio MCP demo. No IDE launch, project edits, or PLC connection."""
import json
import os
from pathlib import Path
import queue
import shutil
import subprocess
import tempfile
import threading

ROOT = Path(__file__).resolve().parents[1]
TIA = (ROOT / "TiaAgentBridge.sln").exists()


def require(condition, message):
    if not condition:
        raise RuntimeError(message)


def main():
    with tempfile.TemporaryDirectory(prefix="mcp-demo-") as workspace:
        env = dict(os.environ)
        if TIA:
            host = ROOT / "src/TiaAgent.Host/bin/Release/net10.0/TiaAgent.Host.exe"
            require(host.exists(), "Build the solution in Release first; see demos/README.md")
            env["TIA_AGENT_STATE_DIRECTORY"] = workspace
            command = [str(host), "--access", "inspect"]
        else:
            node = shutil.which("node")
            require(node and (ROOT / "dist/bin.js").exists(), "Install Node and run npm ci then npm run build first")
            # Existing executable satisfies path validation; auto-launch is disabled.
            command = [node, str(ROOT / "dist/bin.js"), "--codesys-path", node,
                       "--codesys-profile", "DEMO-NO-IDE", "--workspace", workspace,
                       "--no-auto-launch", "--read-only"]
        process = subprocess.Popen(command, stdin=subprocess.PIPE, stdout=subprocess.PIPE,
                                   stderr=subprocess.PIPE, text=True, encoding="utf-8", env=env)
        replies = queue.Queue()
        errors = []

        def read_stdout():
            for line in process.stdout:
                replies.put(line)
            replies.put(None)

        threading.Thread(target=read_stdout, daemon=True).start()
        threading.Thread(target=lambda: errors.extend(process.stderr), daemon=True).start()
        request_id = 0

        def send(message):
            process.stdin.write(json.dumps(message) + "\n")
            process.stdin.flush()

        def rpc(method, params):
            nonlocal request_id
            request_id += 1
            send({"jsonrpc": "2.0", "id": request_id, "method": method, "params": params})
            while True:
                try:
                    line = replies.get(timeout=20)
                except queue.Empty as exc:
                    raise RuntimeError("MCP response timed out; no automatic retry") from exc
                require(line is not None, "MCP exited: " + "".join(errors)[-2000:])
                reply = json.loads(line.lstrip("\ufeff"))
                if reply.get("id") == request_id:
                    return reply

        def call(name, arguments=None):
            return rpc("tools/call", {"name": name, "arguments": arguments or {}})

        def payload(reply):
            require("error" not in reply, "JSON-RPC request failed")
            result = reply["result"]
            require(not result.get("isError"), "MCP tool failed")
            structured = result["structuredContent"]
            return structured["data" if TIA else "result"]

        try:
            initialized = rpc("initialize", {"protocolVersion": "2024-11-05", "capabilities": {},
                              "clientInfo": {"name": "public-demo", "version": "1.0.0"}})
            require("result" in initialized, "Handshake failed")
            send({"jsonrpc": "2.0", "method": "notifications/initialized"})
            print("PASS initialize: live MCP stdio handshake")
            names = set()
            cursor = None
            while True:
                listing = rpc("tools/list", {"cursor": cursor} if cursor else {})["result"]
                names.update(tool["name"] for tool in listing["tools"])
                cursor = listing.get("nextCursor")
                if not cursor:
                    break
            print(f"PASS tools/list: {len(names)} registered tools")
            for name in sorted(names):
                print("  " + name)
            capabilities = payload(call("get_capabilities"))
            require(capabilities is not None, "Capabilities missing")
            print("PASS get_capabilities: structured response received")
            blocked = {"apply_engineering_change", "connect_to_device"} if TIA else {
                "set_pou_code", "write_variable", "eval_python", "download_to_device"}
            require(not names.intersection(blocked), "Unexpected write/online tools exposed")
            print("PASS access: write/online tools absent from read-only discovery")
            status = payload(call("get_command_status" if TIA else "get_codesys_status"))
            require("idle" in json.dumps(status) if TIA else "stopped" in json.dumps(status),
                    "Unexpected initial session state")
            print("PASS status: " + ("idle" if TIA else "stopped"))
            if TIA:
                denied = call("apply_engineering_change", {"previewToken": "demo-invalid"})
            else:
                denied = call("get_pou_code", {"projectFilePath": "demo.project", "pouPath": "Application/PLC_PRG"})
            require("error" in denied or denied.get("result", {}).get("isError"), "Expected explicit refusal")
            print("PASS refusal: " + ("write unavailable in inspect mode" if TIA else "IDE not ready reported as failure"))
            print("DONE: real protocol demonstration; no IDE or PLC acceptance claimed")
        finally:
            process.stdin.close()
            try:
                process.wait(timeout=5)
            except subprocess.TimeoutExpired:
                process.terminate()
                process.wait(timeout=5)


if __name__ == "__main__":
    main()
