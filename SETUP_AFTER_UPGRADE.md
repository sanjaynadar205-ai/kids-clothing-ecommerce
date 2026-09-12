# KidsWearStore upgrade setup

## Registration OTP
Registration now requires email OTP verification. Public registration never exposes a role selector; verified registrations are automatically assigned the `Customer` role.

Configure SMTP in `appsettings.json`, preferably through environment variables/user secrets for real deployment:

- `Smtp__Host`
- `Smtp__Port`
- `Smtp__Username`
- `Smtp__Password`
- `Smtp__From`
- `Smtp__EnableSsl`

For local Development, if SMTP is not configured, the generated OTP is displayed on the verification page so the complete flow can be tested. Do not rely on this fallback in production.

## Product Managers
Only an Admin can appoint Product Managers. The system prevents more than 4 users from having the `ProductManager` role at the same time.

No database migration is required for these changes because OTP state is kept in server memory and the role already exists.

## Security note
For production with multiple app instances, replace the in-memory OTP cache with a distributed cache (for example Redis) so an OTP session is available across instances.
