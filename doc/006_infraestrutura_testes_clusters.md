# Infraestrutura de testes para restrições de cluster

## Objetivo

Validar que o MCP Server pode ser instalado e operar com sucesso em clusters com políticas diferentes, sem solicitar capacidades além das declaradas. O contrato cobre configuração, objetos Helm, RBAC efetivo, readiness, superfície MCP e volumetria.

Este plano foi implementado em `tests/cluster-lab`, no chart Helm e no próprio MCP Server. Cada etapa abaixo funciona como um gate independente e produz erro explícito quando um requisito mínimo não é atendido.

## Princípios

- Menor privilégio: RBAC de runtime é gerado apenas para as capacidades habilitadas.
- Falha antecipada: combinações inválidas são rejeitadas pelo schema e pelos templates Helm.
- Falha observável: `/ready` retorna HTTP 503 quando os requisitos declarados não são atendidos.
- Degradação honesta: uma fonte indisponível permanece `unavailable` ou `partial`; ausência de acesso nunca é interpretada como saúde.
- Perfis reproduzíveis: os mesmos arquivos de cenário são usados no render estático e no cluster real.
- Isolamento: a automação usa namespaces próprios e só remove recursos marcados com o identificador da execução.

## Contrato de configuração

O bloco `clusterAccess` controla o acesso do processo:

```yaml
clusterAccess:
  scope: Cluster # Cluster, Namespace ou None
  serviceDiscovery: true
  stateStorage: ConfigMap # ConfigMap ou Memory
  allowVolumes: true
  validateOnStart: true
  validationCacheSeconds: 30
```

As dimensões são combináveis, respeitando estas invariantes:

- `scope: None` exige `serviceDiscovery: false`, `stateStorage: Memory` e eventos de deployment desabilitados;
- `stateStorage: Memory` exige uma única réplica, pois o estado é local ao processo;
- `serviceDiscovery: false` exige pelo menos uma entrada em `services`;
- `scope: Namespace` exige exatamente um `security.allowedNamespaces`, igual ao namespace da release.

## Matriz de cenários

| Cenário | Scope | Service discovery | Estado | Volume gravável do MCP | Réplicas | RBAC de runtime |
|---|---|---:|---|---:|---:|---|
| `cluster` | `Cluster` | sim | `ConfigMap` | `emptyDir` em `/tmp` | 2 | ClusterRole de leitura + Role do ConfigMap |
| `namespace-only` | `Namespace` | sim | `ConfigMap` | `emptyDir` em `/tmp` | 2 | Role somente no namespace da release |
| `no-volumes` | `Cluster` | sim | `ConfigMap` | nenhum | 2 | Igual ao modo cluster |
| `no-service-discovery` | `Namespace` | não | `ConfigMap` | `emptyDir` em `/tmp` | 2 | Pods, Deployments, Events e estado; sem Services/Endpoints |
| `restricted` | `None` | não | `Memory` | nenhum | 1 | nenhum RBAC e nenhum token de ServiceAccount |

O chart nunca cria PersistentVolume ou PersistentVolumeClaim. No cenário `no-volumes`, o volume gravável `/tmp` é removido, mas o Kubernetes ainda projeta o token do ServiceAccount porque esse cenário consulta a API do cluster. O cenário `restricted` também desabilita o automount desse token.

Sem service discovery, as aplicações são fornecidas explicitamente:

```yaml
clusterAccess:
  scope: Namespace
  serviceDiscovery: false

security:
  allowedNamespaces:
    - apps

services:
  pricing_api: http://pricing.apps.svc.cluster.local
```

Esse modo não concede `get/list` em Services ou Endpoints. A URL explícita ainda precisa ser alcançável pela rede e resolvível pelo DNS escolhido pelo operador.

## Requisitos mínimos

### Identidade instaladora

O preflight valida acesso à API, criação do namespace quando ele ainda não existe e as operações necessárias ao fluxo `helm upgrade --install --wait` e `helm test`:

- recursos comuns: ServiceAccount, ConfigMap, Secret de release Helm, Service, Deployment e Pod de teste;
- PDB, NetworkPolicy e Ingress apenas quando habilitados;
- Role/RoleBinding somente quando há acesso namespaced ou estado em ConfigMap;
- ClusterRole/ClusterRoleBinding somente em `scope: Cluster`.

O comando pode ser executado antes da instalação:

```bash
bash infra/scripts/sh/validate-install-requirements.sh \
  --phase installer \
  --namespace mcp-apis \
  --scope Namespace
```

```powershell
.\infra\scripts\ps\validate-install-requirements.ps1 `
  -Phase installer `
  -Namespace mcp-apis `
  -Scope Namespace
```

### Identidade de runtime

O MCP executa `SelfSubjectAccessReview` no startup e periodicamente, com cache configurável. O relatório é exposto em:

- `GET /ready`: HTTP 503 e `missingRequirements` se algum requisito obrigatório falhar;
- `GET /api/requirements?refresh=true`: relatório completo por capacidade;
- `GET /api/status`: modo efetivo e resumo das restrições.

O teste Helm consulta `/ready` de dentro do cluster. Portanto, ele valida o ServiceAccount real sem exigir que a identidade instaladora tenha permissão Kubernetes de impersonation.

O script de laboratório também suporta `--phase runtime` e usa impersonation para afirmar permissões positivas e negativas. Essa auditoria detalhada deve ser executada com uma identidade administrativa de testes.

## Superfície MCP esperada

As oito ferramentas vNext e as ferramentas legadas que não dependem diretamente da API Kubernetes permanecem registradas. A superfície é reduzida de acordo com o contrato:

- sem service discovery: `list_services` não é publicada;
- `scope: None`: `list_services`, `get_health` e `find_data_origin` não são publicadas;
- `query_metrics` continua condicionada a `Observability:Features:EnableRawQueries` e permanece desabilitada nos cenários padrão.

As ferramentas vNext continuam disponíveis para correlacionar providers configurados. Quando uma fonte exigida não pode ser consultada, o envelope informa cobertura e disponibilidade em vez de fabricar um resultado saudável.

## Gates automatizados

### Gate 1 — configuração e render Helm

`validate-installation-scenarios.sh` executa lint e render dos cinco perfis, verifica objetos presentes/ausentes e confirma que configurações inválidas são rejeitadas.

```bash
bash tests/cluster-lab/scripts/validate-installation-scenarios.sh
```

### Gate 2 — testes do runtime

Os testes .NET validam as regras do `ClusterRequirementsValidator`, o estado em memória e a seleção de permissões namespaced.

```bash
dotnet test src/Services/McpServer.Tests/McpApis.McpServer.Tests.csproj
```

### Gate 3 — instalação em cluster real

Cada cenário executa:

1. preflight da identidade instaladora;
2. instalação Helm com imagem local;
3. auditoria do ServiceAccount;
4. `helm test` de readiness;
5. consulta a `/ready` e `/api/requirements`;
6. handshake MCP, `tools/list` e chamada real a `system_get_health_summary`;
7. verificação das ferramentas obrigatórias/proibidas e do envelope `schemaVersion`;
8. coleta de values, manifest, recursos, pods e eventos em `tests/cluster-lab/reports`.

Para executar toda a matriz em um contexto k3d dedicado:

```powershell
.\tests\cluster-lab\scripts\Invoke-InstallationMatrix.ps1 `
  -Context k3d-mcp-test-access `
  -BuildImage
```

Ou dentro do WSL:

```bash
bash tests/cluster-lab/scripts/run-installation-matrix.sh \
  --context k3d-mcp-test-access \
  --build-image
```

O nome do cluster deve usar o prefixo `mcp-test-` quando o runner receber `--create-cluster`. A matriz não altera o contexto global e não reutiliza namespaces de aplicação.

### Gate 4 — volumetria

O teste k6 abre sessões MCP reais, executa `initialize`, `notifications/initialized`, `tools/list` e uma chamada de saúde sistêmica. Os perfis disponíveis são:

| Perfil | Uso |
|---|---|
| `smoke` | sanidade rápida após a instalação |
| `average` | taxa constante para baseline |
| `spike` | aumento abrupto de sessões e requisições |
| `soak` | estabilidade e consumo ao longo do tempo |

Exemplo:

```bash
bash tests/cluster-lab/scripts/run-installation-scenario.sh \
  --scenario namespace-only \
  --context k3d-mcp-test-access \
  --namespace mcp-load-namespace \
  --load-profile average
```

Os thresholds padrão verificam taxa de erro, checks, iterações descartadas e p95. Podem ser ajustados por `REQUEST_RATE`, `P95_MS` e `MAX_ERROR_RATE`.

## Critérios de aceite

Um cenário só é aprovado quando:

- o preflight confirma as permissões mínimas da identidade instaladora;
- o chart não contém objetos incompatíveis com as restrições declaradas;
- o Deployment fica disponível e o Helm test retorna sucesso;
- `/api/requirements` retorna `meetsMinimumRequirements: true` e o modo esperado;
- a identidade de runtime possui somente o escopo esperado nas verificações do laboratório;
- a lista de ferramentas MCP corresponde ao perfil e uma chamada vNext retorna envelope versionado;
- quando habilitada, a volumetria atende aos thresholds configurados;
- os relatórios de diagnóstico são produzidos sem alterar recursos fora do namespace da execução.

## Fases implementadas

- [x] Definir o contrato e os presets de restrição.
- [x] Tornar RBAC, token, estado e volume condicionais no chart.
- [x] Validar requisitos dentro do runtime.
- [x] Condicionar a superfície de ferramentas MCP.
- [x] Criar preflight Bash e PowerShell.
- [x] Criar cenários estáticos e matriz k3d.
- [x] Criar teste Helm e handshake MCP funcional.
- [x] Criar perfis de volumetria k6.
- [x] Integrar os gates estáticos à CI.
