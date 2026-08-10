#!/bin/bash
# Script para aplicar branch protection rules a todos los repos de la org anxietywatch-org
# Ejecuta: chmod +x apply-branch-protection.sh && ./apply-branch-protection.sh

ORG="anxietywatch-org"
REPOS=(
  "anxietywatch-backend"
  "anxietywatch-ml"
  "anxietywatch-web"
  "anxietywatch-mobile"
  "anxietywatch-wearable"
  "demo-repository"
  "mobile-app-in-background"
  "mobile-app-close-up"
)

for repo in "${REPOS[@]}"; do
  echo "Aplicando branch protection a $ORG/$repo..."
  gh api \
    --method PUT \
    /repos/$ORG/$repo/rulesets \
    -f name="Protect main branch" \
    -f target="branch" \
    -f enforcement="active" \
    -f conditions='{"ref_name":{"include":["refs/heads/main"],"exclude":[]}}' \
    -f rules='[{"type":"required_pull_request","parameters":{"required_approving_review_count":1}},{"type":"required_status_checks","parameters":{"required_status_checks":[{"context":"build-and-test"},{"context":"build-docker-image"}]}}]'
  
  echo "✅ Branch protection aplicada a $repo"
done

echo "🎉 Completado para todos los repositorios"