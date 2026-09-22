"""Offline protocol, registration, access and recovery tests. Never opens TIA/PLC."""
import json
import os
from pathlib import Path
import queue
import subprocess
import tempfile
import threading
import time

ROOT = Path(__file__).resolve().parents[1]
HOST = ROOT / 'src/TiaAgent.Host/bin/Release/net10.0/TiaAgent.Host.exe'
WORKER = ROOT / 'src/TiaAgent.Worker.V21/bin/Release/net48/TiaAgent.Worker.V21.exe'
SDK_ROOT = os.environ.get('DOTNET_ROOT', str(ROOT / '.tools/dotnet'))
ENV = dict(os.environ, DOTNET_ROOT=SDK_ROOT, DOTNET_ROOT_X64=SDK_ROOT,
           TIA_PORTAL_PUBLIC_API=r'D:\Program Files\Siemens\Automation\Portal V21\PublicAPI\V21\net48')

class Client:
    def __init__(self, mode, state, overrides=None):
        env = dict(ENV, TIA_AGENT_STATE_DIRECTORY=str(state))
        env.update(overrides or {})
        self.p = subprocess.Popen([str(HOST), '--access', mode], stdin=subprocess.PIPE, stdout=subprocess.PIPE,
                                  stderr=subprocess.PIPE, text=True, encoding='utf-8', env=env)
        self.lines = queue.Queue()
        self.errors = []
        threading.Thread(target=lambda: [self.lines.put(l) for l in self.p.stdout], daemon=True).start()
        threading.Thread(target=lambda: [self.errors.append(l) for l in self.p.stderr], daemon=True).start()
        self.id = 0
        self.rpc('initialize', {'protocolVersion': '2024-11-05', 'capabilities': {}, 'clientInfo': {'name': 'offline-test', 'version': '1'}})
        self.p.stdin.write(json.dumps({'jsonrpc': '2.0', 'method': 'notifications/initialized'}) + '\n')
        self.p.stdin.flush()

    def rpc(self, method, params=None):
        self.id += 1
        self.p.stdin.write(json.dumps({'jsonrpc': '2.0', 'id': self.id, 'method': method, 'params': params or {}}, ensure_ascii=False) + '\n')
        self.p.stdin.flush()
        while True:
            r = json.loads(self.lines.get(timeout=20).lstrip('\ufeff'))
            if r.get('id') == self.id:
                return r

    def call(self, name, **arguments):
        return self.rpc('tools/call', {'name': name, 'arguments': arguments})

    def close(self):
        self.p.stdin.close()
        try: self.p.wait(timeout=5)
        except subprocess.TimeoutExpired:
            self.p.terminate()
            self.p.wait(timeout=5)

def data(response):
    return response['result']['structuredContent']['data']

def main():
    counts = {}
    for mode in ['inspect', 'engineering', 'online', 'control']:
        with tempfile.TemporaryDirectory(prefix='tia-test-') as state:
            c = Client(mode, state)
            try:
                registered = c.rpc('tools/list')['result']['tools']
                names = {t['name'] for t in registered}
                cap = data(c.call('get_capabilities'))
                assert set(cap['tools']) == names, (mode, cap['tools'], names)
                assert ('apply_engineering_change' in names) == (mode != 'inspect')
                assert ('connect_to_device' in names) == (mode in ['online', 'control'])
                assert {'find_references', 'export_object', 'get_command_status', 'get_compile_messages'} <= names
                assert data(c.call('get_command_status'))['state'] == 'idle'
                counts[mode] = len(names)
                if mode == 'inspect':
                    denied = c.call('apply_engineering_change', previewToken='invalid')
                    assert 'error' in denied or denied.get('result', {}).get('isError')
                no_compile = c.call('get_compile_messages')
                assert no_compile['result']['isError']
                assert no_compile['result']['structuredContent']['error']['code'] == 'no_compile_result'
            finally: c.close()

    # Durable unknown outcome must not be unlocked or dispatched after restart.
    with tempfile.TemporaryDirectory(prefix='tia-test-') as directory:
        state = Path(directory)
        journal = state / 'pending.json'
        journal.write_text(json.dumps({'state': 'unknown', 'requestId': 'unresolved'}))
        c = Client('engineering', state)
        try:
            assert data(c.call('get_command_status'))['state'] == 'unknown'
            result = c.call('get_project_status')['result']
            assert result['isError'] and result['structuredContent']['error']['code'] == 'session_uncertain'
            recovery = c.call('recover_session', acknowledge=True)
            assert 'error' in recovery or recovery.get('result', {}).get('isError')
            assert journal.exists()
        finally: c.close()
        journal.write_text(json.dumps({'state': 'completed', 'requestId': 'finished', 'response': {'success': False}}))
        c = Client('inspect', state)
        try:
            assert data(c.call('get_command_status'))['state'] == 'completed'
            assert data(c.call('recover_session', acknowledge=True))['recovered']
            assert not journal.exists()
        finally: c.close()

    # Worker permission checks are independent of host tool registration.
    p = subprocess.Popen([str(WORKER), '--access', 'inspect'], stdin=subprocess.PIPE, stdout=subprocess.PIPE,
                         stderr=subprocess.PIPE, text=True, encoding='utf-8', env=ENV)
    requests = [dict(protocolVersion='0.2', requestId=str(i), method=m) for i, m in enumerate(
        ['apply_engineering_change', 'create_project', 'save_project', 'update_block_source', 'connect_to_device'])]
    output, _ = p.communicate('\n'.join(json.dumps(r) for r in requests) + '\n', timeout=20)
    responses = [json.loads(l.lstrip('\ufeff')) for l in output.splitlines() if l.strip()]
    assert len(responses) == len(requests)
    assert all(r['errorCode'] == 'access_denied' for r in responses), responses
    fake = ROOT / 'tests/FakeWorker/bin/Release/net10.0/FakeWorker.exe'
    if fake.exists():
        for bad_id in [False, True]:
            with tempfile.TemporaryDirectory(prefix='tia-fault-') as directory:
                log = Path(directory) / 'dispatch.log'
                c = Client('engineering', directory, {'TIA_AGENT_WORKER_V21': str(fake), 'TIA_AGENT_TIMEOUT_MS': '1000',
                    'TIA_TEST_DISPATCH_LOG': str(log), 'TIA_TEST_BAD_ID': '1' if bad_id else '0'})
                try:
                    result = c.call('get_project_status')['result']['structuredContent']
                    assert result['error']['code'] == 'result_pending'
                    result = c.call('get_project_status')['result']['structuredContent']
                    assert result['error']['code'] == 'session_uncertain'
                    time.sleep(1.2)
                    status = data(c.call('get_command_status'))
                    assert status['state'] == ('unknown' if bad_id else 'completed')
                    assert log.read_text().splitlines() == ['get_project_status'], 'must not replay dispatch'
                    if not bad_id:
                        assert data(c.call('recover_session', acknowledge=True))['recovered']
                finally: c.close()
    print('PASS: MCP registration/capability parity, inspect gates, worker gates, durable recovery:', counts)

if __name__ == '__main__':
    main()
