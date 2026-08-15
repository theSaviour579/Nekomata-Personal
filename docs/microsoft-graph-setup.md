# Microsoft account registration

Nekomata Personal uses one centrally owned Microsoft Entra application registration. People installing the application do **not** create registrations of their own; they select **Connect Microsoft**, sign in, review the delegated permissions, and consent.

## One-time publisher setup

1. In the Microsoft Entra admin centre, create an app registration named `Nekomata Personal`.
2. Select **Accounts in any organisational directory and personal Microsoft accounts** as the supported account type.
3. Under **Authentication**, add the **Mobile and desktop applications** platform with the redirect URI `http://localhost`.
4. Enable **Allow public client flows**. Do not create or distribute a client secret; this is a public desktop client.
5. Add these Microsoft Graph **delegated** permissions:
   - `User.Read`
   - `Calendars.ReadWrite`
   - `Mail.ReadWrite`
   - `Mail.Send`
6. Copy the Application (client) ID into `MicrosoftGraph:ClientId` in `Nekomata/Nekomata/appsettings.json`. Leave `MicrosoftGraph:TenantId` empty so MSAL uses the work, school, and personal-account authority.
7. Build a private test release and sign in with both a personal Microsoft account and a permitted organisational account before wider distribution.

Organisations can still restrict user consent or require administrator approval. Nekomata reports that sign-in failure without affecting its local planning features.

Microsoft references:

- https://learn.microsoft.com/en-us/graph/auth-register-app-v2
- https://learn.microsoft.com/en-us/entra/identity-platform/scenario-desktop-app-registration
- https://learn.microsoft.com/en-us/graph/permissions-reference
