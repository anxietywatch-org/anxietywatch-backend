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
- `deploy-droplet`: streams the tested `develop` image and validated Compose file to the DigitalOcean Droplet through a restricted SSH command. The Droplet does not store a GitHub token.

Required secrets:

- `DO_DEPLOY_SSH_KEY`: restricted SSH key for the Droplet deploy command.

## Production Runtime

Public API:

```text
https://api.mangoon.xyz
```

The Droplet keeps port `8080` bound to localhost only. Public traffic goes through the Caddy service declared in
`docker-compose.prod.yml`. Caddy stores certificates in named Docker volumes and uses `restart: unless-stopped`, so
both the API and HTTPS proxy recover automatically after a Droplet reboot or a normal redeployment.

The production deployment key is restricted in `root/.ssh/authorized_keys` to `ops/anxietywatch-deploy`. It cannot open an interactive shell or forward ports. The deploy script accepts only `upload-compose`, `load-image`, and `deploy`, keeps one rollback image, and restores the previous Compose file if container health checks fail.

## Human Operations Access

Human access uses the `anxietyops` account with individual SSH public keys. Password authentication is disabled. The account has no general `sudo` or Docker access; it can invoke only these audited commands:

```bash
sudo /usr/local/sbin/anxietywatch-status
sudo /usr/local/sbin/anxietywatch-logs
sudo /usr/local/sbin/anxietywatch-restart
```

Connect with:

```powershell
ssh -i C:\Users\crepe\.ssh\anxietywatch_digitalocean_ed25519 anxietyops@161.35.110.44
```

Add each collaborator's public key as a separate line in `/home/anxietyops/.ssh/authorized_keys`; never share a private key or enable a common password.

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
