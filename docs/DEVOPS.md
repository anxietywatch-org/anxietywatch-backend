# AnxietyWatch Backend DevOps

## Branches

- `develop` is the deployed branch for the public API.
- `main` is the stable integration branch.
- Pull requests should target `develop` unless the change is repository maintenance only.

## GitHub Actions

The `CI/CD Pipeline` workflow runs on pushes and pull requests for `main` and `develop`, plus manual `workflow_dispatch`.

Jobs:

- `build-and-test`: restores, builds, tests, and checks vulnerable packages.
- `build-docker-image`: publishes the API image to GitHub Container Registry.
- `build-docker-image-do`: optional DigitalOcean Container Registry push. It only runs when repository variable `PUSH_DOCR` is set to `true`.
- `deploy-droplet`: deploys `develop` to the DigitalOcean Droplet through the restricted SSH command.

Required secrets:

- `DO_DEPLOY_SSH_KEY`: restricted SSH key for the Droplet deploy command.

Optional secrets and variables:

- `DO_PAT`: DigitalOcean personal access token for DigitalOcean Container Registry.
- `PUSH_DOCR=true`: repository variable that enables the optional DOCR image push.

## Production Runtime

Public API:

```text
https://api.mangoon.xyz
```

The Droplet keeps port `8080` bound to localhost only. Public traffic goes through the Caddy service declared in
`docker-compose.prod.yml`. Caddy stores certificates in named Docker volumes and uses `restart: unless-stopped`, so
both the API and HTTPS proxy recover automatically after a Droplet reboot or a normal redeployment.

Required runtime environment:

```text
ASPNETCORE_ENVIRONMENT=Production
ASPNETCORE_FORWARDEDHEADERS_ENABLED=true
Persistence__Provider=Mongo
Mongo__ConnectionString=<Atlas connection string>
Mongo__DatabaseName=anxietywatch
Jwt__SigningKey=<managed secret, at least 32 bytes>
Cors__AllowedOrigins=https://mangoon.xyz,https://anxietywatch-web-g5mjb.ondigitalocean.app,http://localhost:5222
Email__Provider=Resend
Email__Resend__ApiKey=<Resend API key>
Email__From=AnxietyWatch <no-reply@mail.mangoon.xyz>
```

## Smoke Test

After a deploy, verify:

```powershell
curl.exe -fsS https://api.mangoon.xyz/health
curl.exe -fsS https://api.mangoon.xyz/api/plans
```

Then create a temporary user, confirm `GET /api/auth/session`, and test telemetry/SOS idempotency with a new and repeated id.
