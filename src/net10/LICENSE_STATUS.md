# Licensing / locks — scope freeze, not a migration target

* The current `src/TWF/TWF.Infrastructure/Licensing/UnrestrictedLicenseService.cs` is the existing zero-lock implementation. Do not add a hardware/software lock, validation service, personnel cap, dongle, or activation step to either new web application or its proposed agent. Preserve this existing behaviour; do **not** modify the reference implementation.
* `src/T5Web/` does **not** contain a class named `UnrestrictedLicenseService`. Its existing no-license/no-lock behaviour is the reference for Aria Admin.
* The originals contain different legacy locks. `TWF 4.28`, `T5Web [3.5.7]`, `T5V3.9.2`, and `Link5 v4.0.4` are read-only. In particular do not edit Link5 Tiny/LockIP/LockType/ChangeLock logic or claim its binaries were ported.
* Security and functional equivalence **other than licensing** are independently checked; a zero-lock label is not proof they work.

Status: inventory only. No new .NET 10 license code has been written or tested.
