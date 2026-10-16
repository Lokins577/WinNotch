# MSIX packaging

The release workflow publishes the self-contained application, signs
`WinNotch.exe`, builds both Inno and MSIX packages, and signs both installers.

Required GitHub secrets:

- `AZURE_TENANT_ID`
- `AZURE_CLIENT_ID`
- `AZURE_CLIENT_SECRET`
- `AZURE_TRUSTED_SIGNING_ENDPOINT`
- `AZURE_CODE_SIGNING_NAME`
- `AZURE_CERT_PROFILE_NAME`
- `MSIX_PUBLISHER` — exact certificate subject used in package identity

For local test packages, run `Build-Msix.ps1` after `dotnet publish` and sign
the result with a certificate whose subject exactly matches the Publisher.
Self-signed packages require the public certificate to be trusted before
installation.
