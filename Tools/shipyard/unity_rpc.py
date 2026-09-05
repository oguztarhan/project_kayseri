"""Call the project's running Unity MCP endpoint without changing its configuration.

Pass a JSON request on stdin: {"tool": "read_console", "arguments": {...}},
{"resource": "mcpforunity://editor/state"}, or {"schemas": ["run_tests"]}.
"""
import json
import sys
import urllib.request

def main():
    request = json.load(sys.stdin)
    headers = {'Content-Type': 'application/json', 'Accept': 'application/json, text/event-stream'}
    def rpc(method, params):
        body = json.dumps({'jsonrpc': '2.0', 'id': 1, 'method': method, 'params': params}).encode()
        req = urllib.request.Request('http://127.0.0.1:8080/mcp', data=body, headers=headers)
        with urllib.request.urlopen(req, timeout=55) as response:
            session = response.headers.get('Mcp-Session-Id')
            if session:
                headers['Mcp-Session-Id'] = session
            raw = response.read().decode()
        if raw.startswith('event:'):
            messages = [json.loads(line[6:]) for line in raw.splitlines() if line.startswith('data: ')]
            return messages[-1]
        return json.loads(raw)
    rpc('initialize', {'protocolVersion': '2024-11-05', 'capabilities': {},
                       'clientInfo': {'name': 'codex-shipyard', 'version': '1'}})
    if 'resource' in request:
        result = rpc('resources/read', {'uri': request['resource']})
    elif 'schemas' in request:
        result = rpc('tools/list', {})
        result = [t for t in result['result']['tools'] if t['name'] in request['schemas']]
    else:
        result = rpc('tools/call', {'name': request['tool'], 'arguments': request.get('arguments', {})})
    print(json.dumps(result, ensure_ascii=False))

if __name__ == '__main__':
    main()
