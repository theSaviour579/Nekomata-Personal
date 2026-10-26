namespace Nekomata.Models.Integrations;

public enum AuthenticationFailureKind
{
    AuthenticationRequired,
    TokenExpired,
    ConsentMissing,
    CredentialUnavailable
}

public sealed class UnattendedAuthenticationException(AuthenticationFailureKind kind,string integration,Exception? inner=null)
    : Exception($"{integration} unattended authentication failed: {kind}.",inner)
{
    public AuthenticationFailureKind Kind { get; }=kind;
    public string Integration { get; }=integration;
}
