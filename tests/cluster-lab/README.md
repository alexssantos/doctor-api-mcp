# Cluster installation lab

Laboratório reproduzível para validar o MCP Server nos perfis `cluster`, `namespace-only`, `no-volumes`, `no-service-discovery` e `restricted`.

## Validação sem cluster

```bash
bash tests/cluster-lab/scripts/validate-installation-scenarios.sh
dotnet test src/Services/McpServer.Tests/McpApis.McpServer.Tests.csproj
```

## Matriz em k3d

Crie ou selecione um cluster dedicado e execute:

```powershell
.\tests\cluster-lab\scripts\Invoke-InstallationMatrix.ps1 `
  -Context k3d-mcp-test-access `
  -BuildImage
```

O runner cria um namespace por cenário, importa `doctor-api-mcp-test:local`, valida RBAC, readiness e a superfície MCP, salva evidências em `reports/` e remove somente os namespaces marcados pela própria execução.

Use `-PreserveOnFailure` para manter o namespace que falhou. No WSL, os argumentos equivalentes são:

```bash
bash tests/cluster-lab/scripts/run-installation-matrix.sh \
  --context k3d-mcp-test-access \
  --build-image \
  --preserve-on-failure
```

## Cenário individual e carga

```bash
bash tests/cluster-lab/scripts/run-installation-scenario.sh \
  --scenario restricted \
  --context k3d-mcp-test-access \
  --namespace mcp-install-restricted
```

Adicione `--load-profile smoke|average|spike|soak` quando o k6 estiver instalado. Os relatórios locais não são versionados.

Veja o contrato, os requisitos e os critérios de aceite em [../../doc/006_infraestrutura_testes_clusters.md](../../doc/006_infraestrutura_testes_clusters.md).
