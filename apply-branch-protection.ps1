# Script para aplicar branch protection rules a todos los repos de la org anxietywatch-org
# Ejecuta en PowerShell: .\apply-branch-protection.ps1

$ORG = "anxietywatch-org"
$RULESET_NAME = "Protect main branch"
$REPOS = @(
  "anxietywatch-backend"
  "anxietywatch-ml"
  "anxietywatch-web"
  "anxietywatch-mobile"
  "anxietywatch-wearable"
  "demo-repository"
  "mobile-app-in-background"
  "mobile-app-close-up"
)

foreach ($repo in $REPOS) {
  Write-Host "Aplicando branch protection a $ORG/${repo}..." -ForegroundColor Cyan

  $ruleset = @{
    name = $RULESET_NAME
    target = "branch"
    enforcement = "active"
    conditions = @{
      ref_name = @{
        include = @("refs/heads/main")
        exclude = @()
      }
    }
    rules = @(
      @{
        type = "pull_request"
        parameters = @{
          required_approving_review_count     = 1
          dismiss_stale_reviews_on_push       = $true
          require_code_owner_review           = $false
          require_last_push_approval          = $false
          required_review_thread_resolution   = $false
        }
      },
      @{
        type = "required_status_checks"
        parameters = @{
          strict_required_status_checks_policy = $true
          required_status_checks = @(
            @{ context = "build-and-test" },
            @{ context = "build-docker-image" }
          )
        }
      }
    )
  }

  $json = $ruleset | ConvertTo-Json -Depth 6 -Compress
  $tempFile = [System.IO.Path]::GetTempFileName()
  [System.IO.File]::WriteAllText($tempFile, $json)

  $existing = gh api "/repos/$ORG/$repo/rulesets" --jq '.[] | select(.name=="'"$RULESET_NAME"'") | .id' 2>$null

  try {
    if ($existing) {
      gh api --method PUT "/repos/$ORG/$repo/rulesets/$existing" --input $tempFile | Out-Null
      Write-Host "✅ Actualizada en $repo" -ForegroundColor Green
    }
    else {
      gh api --method POST "/repos/$ORG/$repo/rulesets" --input $tempFile | Out-Null
      Write-Host "✅ Creada en $repo" -ForegroundColor Green
    }
  }
  catch {
    Write-Host "❌ Error en ${repo}: $($_.Exception.Message)" -ForegroundColor Red
  }
  finally {
    Remove-Item -Force $tempFile -ErrorAction SilentlyContinue
  }
}

Write-Host "🎉 Completado para todos los repositorios" -ForegroundColor Green