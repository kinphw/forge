"""hwp-api-mcp 서버를 stdio로 실행하고 6개 도구를 한 번씩 호출해 응답을 확인."""
from __future__ import annotations

import json
import os
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
SERVER_JS = ROOT / "dev-support" / "hwp-api-mcp" / "dist" / "index.js"

ENV = {
    **os.environ,
    "MYSQL_HOST": "localhost",
    "MYSQL_PORT": "3306",
    "MYSQL_USER": "hdbuser",
    "MYSQL_PASSWORD": "1226",
    "DB_NAME": "hwp_api_db",
}


def make_msg(rid: int, method: str, params: dict) -> bytes:
    body = json.dumps({"jsonrpc": "2.0", "id": rid, "method": method, "params": params})
    return (body + "\n").encode("utf-8")


def main() -> int:
    proc = subprocess.Popen(
        ["node", str(SERVER_JS)],
        stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE,
        env=ENV,
    )

    msgs = [
        make_msg(1, "initialize", {
            "protocolVersion": "2024-11-05",
            "capabilities": {},
            "clientInfo": {"name": "smoke-test", "version": "1"},
        }),
        # MCP 규약상 initialized 알림 (id 없음)
        (json.dumps({"jsonrpc": "2.0", "method": "notifications/initialized", "params": {}}) + "\n").encode(),
        make_msg(2, "tools/list", {}),
        make_msg(3, "tools/call", {"name": "search_hwp_action", "arguments": {"query": "찾기 바꾸기", "limit": 3}}),
        make_msg(4, "tools/call", {"name": "search_hwp_parameterset", "arguments": {"query": "FindReplace", "limit": 3}}),
        # FindReplace ParameterSet 단건 조회 (검색 결과로 받은 id 가정)
        make_msg(5, "tools/call", {"name": "search_hwp_member", "arguments": {"query": "텍스트", "kind": "Method", "limit": 3}}),
        make_msg(6, "tools/call", {"name": "get_hwp_action", "arguments": {"id": 1}}),
        make_msg(7, "tools/call", {"name": "get_hwp_parameterset", "arguments": {"id": 1}}),
        make_msg(8, "tools/call", {"name": "get_hwp_member", "arguments": {"id": 1}}),
    ]

    payload = b"".join(msgs)
    try:
        out, err = proc.communicate(payload, timeout=15)
    except subprocess.TimeoutExpired:
        proc.kill()
        out, err = proc.communicate()

    print("=== STDERR ===")
    print(err.decode("utf-8", errors="replace"))
    print("=== STDOUT ===")
    text = out.decode("utf-8", errors="replace")
    # 각 줄이 JSON-RPC 응답
    for line in text.splitlines():
        if not line.strip():
            continue
        try:
            obj = json.loads(line)
            rid = obj.get("id", "-")
            if "error" in obj:
                print(f"[id {rid}] ERROR: {obj['error']}")
                continue
            result = obj.get("result", {})
            if "tools" in result:
                names = [t["name"] for t in result["tools"]]
                print(f"[id {rid}] tools: {names}")
            elif "content" in result:
                txt = result["content"][0]["text"]
                preview = txt[:400] + ("..." if len(txt) > 400 else "")
                print(f"[id {rid}] content (len={len(txt)}):")
                print(preview)
                print()
            else:
                print(f"[id {rid}] result: {json.dumps(result, ensure_ascii=False)[:200]}")
        except json.JSONDecodeError:
            print(f"NON-JSON: {line[:200]}")

    return proc.returncode or 0


if __name__ == "__main__":
    sys.exit(main())
