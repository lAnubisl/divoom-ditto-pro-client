# GitHub Actions guidance

- `workflows/docker-publish.yml` runs on pushes to `main` and tags matching `v*`,
  and supports manual runs. Keep the build job restricted to `main` and `v*` tags,
  including manual runs. Do not add pull-request publishing without a requested scope change.
- Build and test natively on `ubuntu-24.04-arm`; publish `linux/arm64` for 64-bit Pi OS.
- CI builds the portable API and runs both NUnit projects before publishing. Do not
  replace the portable build with a Linux build of the Windows-containing solution.
- Build from the root `Dockerfile`. Deployment pulls that image from Docker Hub.
- Keep `latest` enabled specifically for `refs/heads/main`. Semantic version tags
  publish version tags and commit tags without replacing `latest`.
- `DOCKERHUB_USERNAME` is a repository variable; `DOCKERHUB_TOKEN` is a secret;
  the image defaults to `panfilenok/divoom-ditto-pro-client`, and optional
  `DOCKERHUB_IMAGE` overrides the full image name. Never print tokens.
- Preserve minimal read permissions, disabled checkout credential persistence,
  configuration validation, and the workflow timeout.
- Keep README setup instructions synchronized with workflow inputs and triggers.
  Validate YAML/expressions with `actionlint` when available; report an actual
  hosted build/push separately from local lint and tests.
