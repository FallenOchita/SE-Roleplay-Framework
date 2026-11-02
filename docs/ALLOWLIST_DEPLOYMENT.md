# Allowlist deployment requirements

The launcher manages the directory's allowlist data and policy. The Torch adapter enforces that policy during SE1's pre-admission authentication callback. **Both sides must be configured**; enabling the checkbox in the launcher alone does not install or activate the Torch hook.

## 1. Configure the Torch server

Set these environment variables in the environment used to start the Torch process:

| Variable | Required | Purpose |
| --- | --- | --- |
| `SERPF_DIRECTORY_URL` | Yes | HTTPS base URL of the directory API |
| `SERPF_SERVER_ID` | Yes | ID of this registered server |
| `SERPF_SERVER_API_KEY` | Yes | Private API key issued for this server |
| `SERPF_ADMISSION_ENFORCEMENT` | Yes, to enforce | Set to `true` to register the pre-admission handler |

Example PowerShell configuration for a Torch process started from the same shell:

```powershell
$env:SERPF_DIRECTORY_URL = "https://directory.example.net"
$env:SERPF_SERVER_ID = "your-server-id"
$env:SERPF_SERVER_API_KEY = "your-private-server-api-key"
$env:SERPF_ADMISSION_ENFORCEMENT = "true"
```

Use your service manager's environment configuration for a persistent deployment. Restart Torch after changing these values. Never put the server API key in the launcher, a public repository, or a client-side mod.

## 2. Configure the directory policy

1. Sign in to the launcher with the Steam account that owns the registered server.
2. Open **Profile & Owner**, select the server, and add the SteamID64 values that should be admitted.
3. Confirm the allowlist contains the intended players before enabling it.
4. Enable **Require players to be on this server's allowlist** and save the admission setting.

The directory policy is disabled by default. When disabled, the directory's admission decision allows players regardless of membership. When enabled, only listed Steam accounts are admitted.

## 3. Understand connection behavior

- Torch checks the existing Steam authentication verdict first.
- If that verdict is successful, the adapter asks the directory for the decision using the SteamID64 supplied by the server-side Torch event.
- The directory validates the server API key and returns the authoritative decision.
- If enforcement is enabled but the directory is unavailable, the response is invalid, or required configuration is incomplete, the Torch hook rejects the pending connection (fail closed).
- If `SERPF_ADMISSION_ENFORCEMENT` is not `true`, the pre-admission hook is not registered. The launcher may still show the directory policy as enabled, but the server will not apply it.

## 4. Verify before relying on enforcement

- Confirm Torch starts with all four environment variables set.
- Check the Torch log for the message that pre-admission allowlist enforcement is enabled.
- Keep the directory reachable from the server host over HTTPS.
- Test with an allowed account and an account not on the list in a private staging server before using the policy on a live community.
- If configuration is invalid while enforcement is requested, the fail-closed handler rejects pending connections. Correct the environment and restart Torch to recover.

The hosted CI can validate the framework and directory code, but it does not include the proprietary Torch and SE1 assemblies. Build and test the adapter against the exact Torch/SE1 installation before production deployment.
