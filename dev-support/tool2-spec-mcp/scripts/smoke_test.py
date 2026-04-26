"""
tool2-spec-mcp smoke test.
MCP 서버를 stdio 로 띄우고 6개 도구를 1번씩 호출해 응답 확인.
"""
import json
import subprocess
import sys
import io

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8')

MCP_DIST = r"C:/projects/sentinel-forge/dev-support/tool2-spec-mcp/dist/index.js"

env = {
    "MYSQL_HOST": "localhost",
    "MYSQL_PORT": "3306",
    "MYSQL_USER": "hdbuser",
    "MYSQL_PASSWORD": "1226",
    "DB_NAME": "tool2_spec_db",
    "PATH": __import__("os").environ.get("PATH", ""),
    "SystemRoot": __import__("os").environ.get("SystemRoot", "C:/Windows"),
}

proc = subprocess.Popen(
    ["node", MCP_DIST],
    stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE,
    env=env, bufsize=0,
)


def send(msg: dict, expect_response: bool = True) -> dict | None:
    """JSON-RPC 메시지 송신. 응답 기다릴지 여부."""
    line = json.dumps(msg) + "\n"
    try:
        proc.stdin.write(line.encode("utf-8"))
        proc.stdin.flush()
    except OSError as e:
        # stderr 출력
        err = proc.stderr.read(4000).decode("utf-8", errors="replace")
        print(f"# STDERR: {err}")
        raise RuntimeError(f"server died — stdin write failed: {e}")
    if not expect_response:
        return None
    raw = proc.stdout.readline()
    if not raw:
        err = proc.stderr.read(4000).decode("utf-8", errors="replace")
        print(f"# STDERR: {err}")
        return None
    return json.loads(raw.decode("utf-8"))


def call_tool(name: str, args: dict) -> str:
    resp = send({
        "jsonrpc": "2.0", "id": 1,
        "method": "tools/call",
        "params": {"name": name, "arguments": args},
    })
    if resp is None:
        return "(no response)"
    if "error" in resp:
        return f"ERROR: {resp['error']}"
    items = resp.get("result", {}).get("content", [])
    return "\n".join(it.get("text", "") for it in items)


# initialize
init = send({
    "jsonrpc": "2.0", "id": 0,
    "method": "initialize",
    "params": {
        "protocolVersion": "2025-03-26",
        "capabilities": {},
        "clientInfo": {"name": "smoke-test", "version": "0.1"},
    },
})
print("# initialize:", "OK" if init and "result" in init else f"FAIL ({init})")

send({"jsonrpc": "2.0", "method": "notifications/initialized", "params": {}},
     expect_response=False)

tests = [
    ("search_tool2_methods",   {"query": "자간",                  "limit": 5}),
    ("get_tool2_method",       {"name": "자간헌터"}),
    ("get_tool2_method_source",{"name": "자간헌터"}),
    ("list_tool2_templates",   {}),
    ("get_tool2_template",     {"name": "금감원페이지"}),
    ("list_tool2_directives",  {"category": "bullet"}),
    ("get_tool2_action_usage", {"action": "ParagraphShape"}),
]

for name, args in tests:
    print(f"\n{'='*70}\n# {name}({args})\n{'='*70}")
    out = call_tool(name, args)
    # 출력 너무 길면 자르기
    print(out[:1500] + (f"\n...[+{len(out)-1500} bytes]" if len(out) > 1500 else ""))

proc.terminate()
print("\n# smoke test complete")
