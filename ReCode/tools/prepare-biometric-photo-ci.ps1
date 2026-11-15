param([switch]$Cleanup)
$ErrorActionPreference='Stop'
if (!$IsWindows -or $env:GITHUB_ACTIONS -ne 'true') { throw 'Disposable Windows GitHub runner only; not a deployment provisioner.' }
$vhd=Join-Path $env:RUNNER_TEMP 'io-synthetic-photo.vhdx'
$script=Join-Path $env:RUNNER_TEMP 'io-synthetic-photo-diskpart.txt'
if ($Cleanup) {
 if (Test-Path $vhd) {
  @("select vdisk file=`"$vhd`"",'detach vdisk') | Set-Content $script
  & diskpart /s $script
  if ($LASTEXITCODE -ne 0) { throw 'Synthetic photo VHD detach failed.' }
  Remove-Item $vhd -Force
 }
 exit
}
if (Test-Path $vhd) { throw 'Refusing to reuse a preexisting VHD.' }
$letter=([char[]](90..69) | Where-Object { !(Test-Path ($_+':\')) } | Select-Object -First 1)
if (!$letter) { throw 'No unused drive letter for isolated synthetic storage.' }
@("create vdisk file=`"$vhd`" maximum=128 type=expandable", "select vdisk file=`"$vhd`"",'attach vdisk','create partition primary','format fs=ntfs quick label=IO_SYNTHETIC_PHOTO',"assign letter=$letter") | Set-Content $script
& diskpart /s $script
if ($LASTEXITCODE -ne 0 -or !(Test-Path ($letter+':\'))) { throw 'Disposable NTFS VHD provisioning failed.' }
$root=$letter+':\io-photo'
$null=New-Item -ItemType Directory -Path $root
$sid=[System.Security.Principal.WindowsIdentity]::GetCurrent().User
$acl=[System.Security.AccessControl.DirectorySecurity]::new()
$acl.SetOwner($sid);$acl.SetAccessRuleProtection($true,$false)
$inherit=[System.Security.AccessControl.InheritanceFlags]'ContainerInherit,ObjectInherit'
$acl.AddAccessRule([System.Security.AccessControl.FileSystemAccessRule]::new($sid,'FullControl',$inherit,'None','Allow'))
$acl.AddAccessRule([System.Security.AccessControl.FileSystemAccessRule]::new([System.Security.Principal.SecurityIdentifier]::new('S-1-5-18'),'FullControl',$inherit,'None','Allow'))
Set-Acl -LiteralPath $root -AclObject $acl
# The runtime tests must actually exercise EFS, not mock encryption or skip on unavailable EFS.
[System.IO.File]::Encrypt($root)
if (!((Get-Item $root).Attributes -band [System.IO.FileAttributes]::Encrypted)) { throw 'EFS unavailable on the isolated synthetic volume.' }
"IO_SYNTHETIC_PHOTO_ROOT=$root" | Out-File -FilePath $env:GITHUB_ENV -Encoding utf8 -Append
Write-Host 'PASS dedicated 128 MiB non-system NTFS VHD, private service ACL and actual EFS prepared for synthetic photo tests'
