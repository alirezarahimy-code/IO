#!/usr/bin/env python3
"""Exercise the bootstrap command on a PTY with disposable synthetic input only."""
import os
import pty
import select
import subprocess
import sys
import termios
import time

password = 'Synthetic!Bootstrap-2026'
command = ['dotnet', 'run', '--project', 'src/IO.Bootstrap/IO.Bootstrap.csproj', '--configuration', 'Release', '--no-build']
if os.environ.get('GITHUB_ACTIONS') != 'true' or os.environ.get('IO_SQL_DISPOSABLE_TEST') != 'yes':
    raise SystemExit('Disposable CI environment required.')
env = dict(os.environ)
env.pop('MSSQL_SA_PASSWORD', None)
if not env.get('ConnectionStrings__IO'):
    raise SystemExit('Disposable connection must be provided by the test harness.')
# Standard redirected input must be rejected before any database access.
result = subprocess.run(command, input='', text=True, stdout=subprocess.PIPE, stderr=subprocess.PIPE, env=env, timeout=30)
if result.returncode != 2 or 'interactive terminal is required' not in result.stderr:
    raise SystemExit('Redirected bootstrap did not fail closed.')

def run_once():
    pid, fd = pty.fork()
    if pid == 0:
        os.execvpe(command[0], command, env)
    output = bytearray()
    position = 0
    try:
        def prompt(text, secret=False):
            nonlocal position
            deadline = time.monotonic() + 35
            while text.encode() not in output[position:]:
                if time.monotonic() > deadline:
                    raise RuntimeError('Terminal prompt timed out.')
                ready, _, _ = select.select([fd], [], [], 0.2)
                if ready:
                    output.extend(os.read(fd, 4096))
            position = len(output)
            if secret:
                while termios.tcgetattr(fd)[3] & termios.ECHO:
                    if time.monotonic() > deadline:
                        raise RuntimeError('Terminal did not disable echo.')
                    select.select([], [], [], 0.01)
        prompt('Type INITIALIZE to continue: ')
        os.write(fd, b'INITIALIZE\n')
        prompt('Administrator username: ')
        os.write(fd, b'terminal-admin\n')
        prompt('Password (not echoed): ', secret=True)
        os.write(fd, (password + '\n').encode())
        prompt('Repeat password (not echoed): ', secret=True)
        os.write(fd, (password + '\n').encode())
        deadline = time.monotonic() + 35
        while True:
            ready, _, _ = select.select([fd], [], [], 0.1)
            if ready:
                try:
                    data = os.read(fd, 4096)
                    if data: output.extend(data)
                except OSError:
                    pass
            done, status = os.waitpid(pid, os.WNOHANG)
            if done:
                if password.encode() in output:
                    raise RuntimeError('Synthetic password was echoed to the terminal.')
                return os.waitstatus_to_exitcode(status), bytes(output)
            if time.monotonic() > deadline:
                raise RuntimeError('Bootstrap did not exit.')
    finally:
        os.close(fd)
        try:
            os.kill(pid, 9)
        except ProcessLookupError:
            pass
        try:
            os.waitpid(pid, 0)
        except ChildProcessError:
            pass

status, output = run_once()
if status != 0 or b'Initial administrator created.' not in output:
    raise SystemExit('Interactive bootstrap did not succeed.')
status, output = run_once()
if status != 1 or b'Initialization failed.' not in output:
    raise SystemExit('Second interactive bootstrap did not fail closed.')
print('PASS terminal bootstrap accepts only interactive input, hides password, and is one-time')
