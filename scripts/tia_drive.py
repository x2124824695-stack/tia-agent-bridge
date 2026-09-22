#!/usr/bin/env python
"""
TiaAgentBridge MCP driver.

Speaks the MCP stdio protocol to TiaAgent.Host.exe and drives the full
read -> preview -> apply -> compile chain against the TIA Portal V21 instance
that must already be running with the project open.

Usage:
    python tia_drive.py status
    python tia_drive.py tree
    python tia_drive.py src   "<blockPath>"
    python tia_drive.py preview "<blockPath>" <newSourceFile>
    python tia_drive.py apply   "<blockPath>" <newSourceFile>
    python tia_drive.py compile [--plc <plcName>] [--block "<blockPath>"]

Block path format is deterministic: PLC_1/Blocks/Motion/FB_Axis
"""

import json
import os
import queue
import shutil
import subprocess
import sys
import threading

PROJ = os.environ.get("TIA_BRIDGE_ROOT", os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
HOST = os.path.join(PROJ, r"src\TiaAgent.Host\bin\Release\net10.0\TiaAgent.Host.exe")
DOTNET_ROOT = os.environ.get('DOTNET_ROOT') or (
    os.path.join(PROJ, r'.tools\dotnet') if os.path.isdir(os.path.join(PROJ, r'.tools\dotnet'))
    else os.path.dirname(shutil.which('dotnet') or r'C:\Program Files\dotnet\dotnet.exe'))
API = os.environ.get(
    "TIA_PORTAL_PUBLIC_API",
    r"D:\Program Files\Siemens\Automation\Portal V21\PublicAPI\V21\net48",
)
PROJECT = os.environ.get("TIA_PROJECT_PATH", r"C:\PLC\Example\Example.ap21")
STATE = os.path.join(os.environ.get("TEMP", "/tmp"), "tia_preview_state.json")

ENV = dict(os.environ)
ENV["DOTNET_ROOT"] = DOTNET_ROOT
ENV["DOTNET_ROOT_X64"] = DOTNET_ROOT
ENV["TIA_PORTAL_PUBLIC_API"] = API


class Mcp:
    def __init__(self, access="engineering"):
        self.proc = subprocess.Popen(
            [HOST, "--access", access],
            stdin=subprocess.PIPE,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            env=ENV,
            text=True,
            encoding="utf-8",
            errors="replace",
            bufsize=1,
        )
        self.stderr_lines = []
        threading.Thread(
            target=lambda: [self.stderr_lines.append(l.rstrip()) for l in self.proc.stderr],
            daemon=True,
        ).start()
        self._send(
            {
                "jsonrpc": "2.0",
                "id": 1,
                "method": "initialize",
                "params": {
                    "protocolVersion": "2024-11-05",
                    "capabilities": {},
                    "clientInfo": {"name": "tia-drive", "version": "1.0"},
                },
            }
        )
        self._read(120)
        self._id = 1
        self._send({"jsonrpc": "2.0", "method": "notifications/initialized"})

    def _send(self, obj):
        self.proc.stdin.write(json.dumps(obj) + "\n")
        self.proc.stdin.flush()

    def _read(self, timeout=600):
        q = queue.Queue()
        threading.Thread(target=lambda: q.put(self.proc.stdout.readline()), daemon=True).start()
        try:
            return q.get(timeout=timeout)
        except queue.Empty:
            return None

    def call(self, name, args=None):
        self._id += 1
        self._send(
            {
                "jsonrpc": "2.0",
                "id": self._id,
                "method": "tools/call",
                "params": {"name": name, "arguments": args or {}},
            }
        )
        raw = self._read()
        if raw is None:
            return {"_timeout": True, "isError": True}
        data = json.loads(raw)
        if "error" in data:
            return {"_jsonrpc_error": data["error"], "isError": True}
        result = data.get("result", {})
        return {
            "isError": bool(result.get("isError")),
            "structured": result.get("structuredContent"),
        }

    def close(self):
        try:
            self.proc.stdin.close()
            self.proc.wait(timeout=15)
        except Exception:
            self.proc.kill()


def show(title, res):
    print("=" * 78)
    print(title)
    print("=" * 78)
    if res.get("_timeout"):
        print("!! TIMEOUT waiting for worker response")
        return False
    if res.get("_jsonrpc_error"):
        print("!! JSON-RPC error:", json.dumps(res["_jsonrpc_error"], ensure_ascii=False))
        return False
    sc = res.get("structured") or {}
    print("success:", sc.get("success"))
    err = sc.get("error")
    if err:
        print("error code:", err.get("code"))
        print("error message:", err.get("message"))
    for w in sc.get("warnings") or []:
        print("warning:", w)
    return bool(sc.get("success"))


def load_state():
    if os.path.exists(STATE):
        try:
            with open(STATE, encoding="utf-8") as f:
                return json.load(f)
        except Exception:
            pass
    return {}


def save_state(d):
    with open(STATE, "w", encoding="utf-8") as f:
        json.dump(d, f, ensure_ascii=False, indent=2)


def main():
    if len(sys.argv) < 2:
        print(__doc__)
        return 2
    cmd = sys.argv[1]
    rest = sys.argv[2:]
    m = Mcp()
    ok = True
    try:
        if cmd == "status":
            r = m.call("get_project_status", {"projectPath": PROJECT})
            ok = show("get_project_status", r)
            if ok:
                print(json.dumps(r["structured"]["data"], ensure_ascii=False, indent=2))

        elif cmd == "tree":
            r = m.call("browse_project_tree", {"projectPath": PROJECT})
            ok = show("browse_project_tree", r)
            if ok:
                d = r["structured"]["data"] or {}
                print("project:", d.get("projectName"), "|", d.get("projectPath"))
                for plc in d.get("plcs", []):
                    print(f"\nPLC {plc.get('name')}  ({plc.get('typeName')})")
                    blocks = plc.get("blocks", [])
                    print(f"  {len(blocks)} block(s)")
                    for b in blocks:
                        print(
                            "   {kind:<4} {lang:<5} num={num:<6} {path}".format(
                                kind=b.get("kind") or "",
                                lang=b.get("language") or "",
                                num=str(b.get("number") or ""),
                                path=b.get("path") or "",
                            )
                        )

        elif cmd == "src":
            r = m.call("get_block_source", {"blockPath": rest[0], "projectPath": PROJECT})
            ok = show("get_block_source " + rest[0], r)
            if ok:
                d = r["structured"]["data"] or {}
                print("sha256:", d.get("sha256"))
                print("-" * 78)
                print(d.get("content"))

        elif cmd == "preview":
            block, srcfile = rest[0], rest[1]
            new_src = open(srcfile, encoding="utf-8").read()
            r = m.call(
                "preview_block_update",
                {"blockPath": block, "newSource": new_src, "projectPath": PROJECT},
            )
            ok = show("preview_block_update " + block, r)
            if ok:
                d = r["structured"]["data"] or {}
                st = load_state()
                st[block] = {
                    "currentSha256": d.get("currentSha256"),
                    "proposedSha256": d.get("proposedSha256"),
                    "previewToken": d.get("previewToken"),
                    "expiresAtUtc": d.get("expiresAtUtc"),
                    "sourceFile": os.path.abspath(srcfile),
                }
                save_state(st)
                print("current  sha256:", d.get("currentSha256"))
                print("proposed sha256:", d.get("proposedSha256"))
                print("token expires :", d.get("expiresAtUtc"))
                print("-" * 78)
                print("DIFF:")
                print(d.get("diff"))

        elif cmd == "apply":
            block, srcfile = rest[0], rest[1]
            st = load_state().get(block)
            if not st:
                print("!! no preview state for", block, "- run preview first")
                return 3
            new_src = open(srcfile, encoding="utf-8").read()
            # Host tokens are intentionally process-local. A prior CLI invocation
            # has exited: obtain a new token, but only for the exact reviewed hashes.
            fresh = m.call("preview_block_update", {"blockPath": block, "newSource": new_src, "projectPath": PROJECT})
            if not show("revalidate reviewed preview", fresh):
                return 3
            preview = fresh["structured"]["data"]
            if preview["currentSha256"] != st["currentSha256"] or preview["proposedSha256"] != st["proposedSha256"]:
                print("!! source or project changed since review; run preview again")
                return 3
            st["previewToken"] = preview["previewToken"]
            r = m.call(
                "apply_block_update",
                {
                    "blockPath": block,
                    "newSource": new_src,
                    "expectedCurrentSha256": st["currentSha256"],
                    "previewToken": st["previewToken"],
                    "saveAfterSuccess": False,
                    "projectPath": PROJECT,
                },
            )
            ok = show("apply_block_update " + block, r)
            if ok:
                print(json.dumps(r["structured"]["data"], ensure_ascii=False, indent=2))

        elif cmd == "compile":
            plc = None
            block = None
            i = 0
            while i < len(rest):
                if rest[i] == "--plc":
                    plc = rest[i + 1]
                    i += 2
                elif rest[i] == "--block":
                    block = rest[i + 1]
                    i += 2
                else:
                    i += 1
            args = {"projectPath": PROJECT}
            if plc:
                args["plcName"] = plc
            if block:
                args["blockPath"] = block
            r = m.call("compile", args)
            ok = show("compile", r)
            if r.get("structured", {}).get("data"):
                print(json.dumps(r["structured"]["data"], ensure_ascii=False, indent=2))

        else:
            print(__doc__)
            return 2
    finally:
        m.close()
        if not ok:
            print("\n--- host stderr (last 25) ---")
            for line in m.stderr_lines[-25:]:
                print(" ", line)
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
