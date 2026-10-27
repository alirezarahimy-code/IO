using Aria.Web.Data;

namespace Aria.Web.Security;

/// <summary>
/// UNWIRED Aria-only admission sequence. A single shared Personnel credential
/// identifies ONE badge/name; a second Personnel read rechecks eligibility and
/// an independent T5 read checks manager rights for THAT badge. It issues NO
/// session/cookie and is NOT an HTTP login or a SQL Server 2016 integration.
/// </summary>
internal sealed class AriaSharedAccountAdmission
{
    private readonly ReadOnlyAriaCredentialDirectory _credentials;
    private readonly IAriaPersonnelDirectory _personnel;
    private readonly ReadOnlyAriaManagerDirectory _managers;

    internal AriaSharedAccountAdmission(ReadOnlyAriaCredentialDirectory credentials,
        IAriaPersonnelDirectory personnel, ReadOnlyAriaManagerDirectory managers)
    {
        _credentials = credentials ?? throw new ArgumentNullException(nameof(credentials));
        _personnel = personnel ?? throw new ArgumentNullException(nameof(personnel));
        _managers = managers ?? throw new ArgumentNullException(nameof(managers));
    }

    internal async Task<AriaAdmissionResolution> AuthenticateAsync(string? name, string? password,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AriaCredentialResolution credential;
        try { credential = await _credentials.VerifyAsync(name, password, cancellationToken); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception) { return AriaAdmissionResolution.Unavailable(); }
        if (credential == null || credential.State == AriaCredentialState.Unavailable)
            return AriaAdmissionResolution.Unavailable();
        if (credential.State != AriaCredentialState.Verified || credential.Account is null ||
            credential.Account.BadgeNumber <= 0 ||
            !string.Equals(credential.Account.AccountName, name, StringComparison.OrdinalIgnoreCase))
            return AriaAdmissionResolution.Denied();

        PersonnelResolution person;
        try { person = await _personnel.RecheckAsync(credential.Account, cancellationToken); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception) { return AriaAdmissionResolution.Unavailable(); }
        if (person == null || person.State == PersonnelState.Unavailable)
            return AriaAdmissionResolution.Unavailable();
        if (person.State != PersonnelState.Active || person.Person is null ||
            person.Person.BadgeNumber != credential.Account.BadgeNumber ||
            !string.Equals(person.Person.AccountName, credential.Account.AccountName,
                StringComparison.OrdinalIgnoreCase))
            return AriaAdmissionResolution.Denied();

        AriaManagerDecision role;
        try { role = await _managers.CheckAsync(person.Person.BadgeNumber, cancellationToken); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception) { return AriaAdmissionResolution.Unavailable(); }
        if (role == AriaManagerDecision.Unavailable)
            return AriaAdmissionResolution.Unavailable();
        if (role != AriaManagerDecision.Regular && role != AriaManagerDecision.Manager)
            return AriaAdmissionResolution.Denied();
        // Do NOT pass a role claim to the current legacy-cookie middleware:
        // no revocable fresh session/CSRF/SQL2016 acceptance has been established.
        return AriaAdmissionResolution.Verified(person.Person, role == AriaManagerDecision.Manager);
    }
}

internal enum AriaAdmissionState { Unavailable, Denied, Verified }

internal sealed record AriaAdmissionResolution(AriaAdmissionState State,
    VerifiedAriaAccount? Account, bool IsManager)
{
    internal static AriaAdmissionResolution Unavailable() => new(AriaAdmissionState.Unavailable, null, false);
    internal static AriaAdmissionResolution Denied() => new(AriaAdmissionState.Denied, null, false);
    internal static AriaAdmissionResolution Verified(VerifiedAriaAccount account, bool isManager) =>
        new(AriaAdmissionState.Verified, account, isManager);
}
