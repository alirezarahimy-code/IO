#!/usr/bin/env bash
# Real systemd lifecycle of the published worker, only in an expendable CI VM.
set -euo pipefail
if [[ "${1:-}" != --allow-isolated-ci-installation || "${GITHUB_ACTIONS:-}" != true || "${RUNNER_OS:-}" != Linux || $# != 3 ]]; then
    echo 'Explicit isolated GitHub Linux installation opt-in required.' >&2; exit 1
fi
published=$2
host=$(readlink -f "$3")
if [[ "$published" != /* || ! -f "$published/IO.DeviceWorker.dll" || ! -f "$published/IO.DeviceWorker.runtimeconfig.json" || ! -f "$published/IO.DeviceWorker.deps.json" || ! "$host" =~ ^/[a-zA-Z0-9_./-]+/dotnet$ || ! -x "$host" ]]; then
    echo 'Complete absolute publish directory and trusted dotnet host required.' >&2; exit 1
fi
if [[ $(ps -p 1 -o comm=) != systemd ]]; then echo 'Real systemd required; refusing to substitute a process simulator.' >&2; exit 1; fi
sudo -n true
name="io-ci-$(python3 -c 'import secrets;print(secrets.token_hex(6))')"
root="/opt/$name"
unit="$name.service"
unit_path="/etc/systemd/system/$unit"
created_user=0; created_directory=0; created_unit=0
cleanup() {
    status=$?; trap - EXIT; set +e
    if [[ $created_unit == 1 ]]; then
        if [[ $status != 0 ]]; then sudo -n journalctl -u "$unit" -n 30 --no-pager >&2; fi
        sudo -n systemctl stop "$unit" || status=1
        sudo -n rm -- "$unit_path" || status=1
        sudo -n systemctl daemon-reload || status=1
    fi
    if [[ $created_directory == 1 ]]; then sudo -n rm -rf -- "$root" || status=1; fi
    if [[ $created_user == 1 ]]; then sudo -n userdel "$name" || status=1; fi
    exit "$status"
}
trap cleanup EXIT
if getent passwd "$name" >/dev/null || sudo -n test -e "$root" || sudo -n test -e "$unit_path"; then echo 'Refusing to replace existing CI resources.' >&2; exit 1; fi
sudo -n useradd --system --user-group --no-create-home --home-dir /nonexistent --shell /usr/sbin/nologin "$name"
created_user=1
sudo -n mkdir -- "$root"; created_directory=1
sudo -n cp -a -- "$published/." "$root/"
# Generate only synthetic/unavailable DB configuration. No native endpoints,
# service grants, real credentials, key ring or migration are provisioned.
python3 - <<'PY' | sudo -n tee "$root/appsettings.json" >/dev/null
import json,uuid
print(json.dumps({'ConnectionStrings':{'IO':'Server=127.0.0.1,14331;Database=IO_CI_UNAVAILABLE;User Id=io_ci_nonexistent;Password=not-a-real-credential;Connect Timeout=1;Encrypt=True;TrustServerCertificate=False'},'IO':{'DeviceWorker':{'ServiceUserId':str(uuid.uuid4()),'AllowedEndpoints':[],'NativeEndpoints':[],'EnableHealthReports':False}}}))
PY
sudo -n chown -R "root:$name" "$root"
sudo -n chmod -R u+rwX,g+rX,o-rwx "$root"
# Exercise the shipped hardening template under isolated names/paths. No restart
# is allowed in this smoke: an unexpected process exit must fail, not be masked.
created_unit=1
python3 - "$name" "$root" "$host" "$(dirname "$0")/../deploy/device-worker/io-device-worker.service" <<'PY' | sudo -n tee "$unit_path" >/dev/null
import pathlib,sys
name,root,host,template=sys.argv[1:]
s=pathlib.Path(template).read_text().replace('User=io-device-worker','User='+name).replace('Group=io-device-worker','Group='+name).replace('WorkingDirectory=/opt/io/device-worker','WorkingDirectory='+root).replace('ExecStart=/usr/bin/dotnet /opt/io/device-worker/IO.DeviceWorker.dll','ExecStart='+host+' '+root+'/IO.DeviceWorker.dll').replace('EnvironmentFile=/etc/io/device-worker.env','').replace('Restart=on-failure','Restart=no')
print(s)
PY
sudo -n systemctl daemon-reload
sudo -n systemctl start "$unit"
sleep 3
sudo -n systemctl is-active "$unit"
[[ $(sudo -n systemctl show "$unit" -p User --value) == "$name" ]]
[[ $(sudo -n systemctl show "$unit" -p MainPID --value) -gt 0 ]]
sudo -n systemctl stop "$unit"
if sudo -n systemctl is-active --quiet "$unit"; then echo 'Worker did not stop under systemd.' >&2; exit 1; fi
[[ $(sudo -n systemctl show "$unit" -p MainPID --value) == 0 ]]
echo 'PASS actual Linux systemd install/start/stop of published worker under dedicated restricted user with unavailable synthetic DB and no native endpoints; not SDK, device or deployment acceptance'
