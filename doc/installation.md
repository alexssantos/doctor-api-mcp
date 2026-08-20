# Instalação do doctor-api-mcp

O pacote público instala o **MCP Server** no contexto Kubernetes atual. Ele inclui dashboard, probes, validação de requisitos e configuração dos providers. RBAC, token do ServiceAccount, estado e volume temporário são gerados de acordo com o perfil de acesso escolhido. Prometheus, Jaeger e Loki podem estar no mesmo namespace ou em qualquer endereço interno permitido pelo cluster.

> O chart não substitui sua stack de observabilidade. Para subir o ambiente de demonstração completo deste repositório em WSL/k3d, continue usando `./infra/scripts/ps/up-k8s.ps1 -Build`.

## Pré-requisitos

- Um cluster Kubernetes acessível pelo contexto atual do `kubectl`.
- Helm 3.
- Acesso do cluster ao Docker Hub.
- Para o comando PowerShell: Helm e kubectl no Windows ou dentro do WSL.

Os instaladores executam um preflight antes do Helm e um teste de readiness depois do rollout. Se a identidade instaladora ou o ServiceAccount não atenderem ao perfil, a instalação termina com a capacidade ausente identificada.

## Instalação em uma linha

### Linux ou WSL

```bash
curl -fsSL https://raw.githubusercontent.com/alexssantos/doctor-api-mcp/master/install.sh | bash
```

O script baixa apenas o snapshot do repositório necessário para executar o chart, valida a identidade instaladora, instala a release `doctor-api-mcp` no namespace `mcp-apis`, aguarda os pods, executa `helm test` e remove os arquivos temporários.

Valores podem ser passados ao Helm depois de `bash`:

```bash
curl -fsSL https://raw.githubusercontent.com/alexssantos/doctor-api-mcp/master/install.sh | bash -s -- --set replicaCount=1
```

As variáveis `DOCTOR_API_MCP_REF`, `DOCTOR_API_MCP_RELEASE` e `DOCTOR_API_MCP_NAMESPACE` alteram, respectivamente, a referência Git, o nome da release e o namespace. `DOCTOR_API_MCP_MODE` escolhe um dos perfis descritos abaixo.

### PowerShell

```powershell
irm https://raw.githubusercontent.com/alexssantos/doctor-api-mcp/master/install.ps1 | iex
```

Se `helm` e `kubectl` não estiverem no PATH do Windows, o instalador os procura no WSL e usa o contexto Kubernetes de lá. Isso mantém compatibilidade com o fluxo k3d/WSL do projeto.

## Perfis de instalação

| `DOCTOR_API_MCP_MODE` | Acesso Kubernetes | Discovery | Estado | Volume gravável | Réplicas |
|---|---|---|---|---|---:|
| `cluster` (padrão) | leitura cluster-wide | Services/Endpoints | ConfigMap | `/tmp` | 2 |
| `namespace` | somente namespace da release | Services/Endpoints | ConfigMap | `/tmp` | 2 |
| `no-volumes` | leitura cluster-wide | Services/Endpoints | ConfigMap | nenhum | 2 |
| `no-service-discovery` | somente namespace da release | configuração explícita | ConfigMap | `/tmp` | 2 |
| `restricted` | nenhuma API Kubernetes | configuração explícita | memória | nenhum, inclusive sem token | 1 |

### Namespace único

Linux/WSL:

```bash
curl -fsSL https://raw.githubusercontent.com/alexssantos/doctor-api-mcp/master/install.sh \
  | env DOCTOR_API_MCP_MODE=namespace \
      DOCTOR_API_MCP_NAMESPACE=apps \
      bash
```

PowerShell:

```powershell
$env:DOCTOR_API_MCP_MODE = 'namespace'
$env:DOCTOR_API_MCP_NAMESPACE = 'apps'
irm https://raw.githubusercontent.com/alexssantos/doctor-api-mcp/master/install.ps1 | iex
```

O chart cria apenas Role e RoleBinding em `apps`. O valor efetivo de `security.allowedNamespaces` é limitado ao mesmo namespace.

### Sem volume gravável

```bash
curl -fsSL https://raw.githubusercontent.com/alexssantos/doctor-api-mcp/master/install.sh \
  | env DOCTOR_API_MCP_MODE=no-volumes bash
```

O chart nunca cria PV ou PVC. Esse perfil também remove o `emptyDir` de `/tmp`. Como o MCP ainda consulta a API Kubernetes, o token projetado do ServiceAccount continua presente; use `restricted` quando nem esse volume/token puder ser montado.

### Sem service discovery

É obrigatório informar ao menos um alias e URL explícitos:

```bash
curl -fsSL https://raw.githubusercontent.com/alexssantos/doctor-api-mcp/master/install.sh \
  | env DOCTOR_API_MCP_MODE=no-service-discovery \
      DOCTOR_API_MCP_NAMESPACE=apps \
      DOCTOR_API_MCP_SERVICE_NAME=pricing_api \
      DOCTOR_API_MCP_SERVICE_URL=http://pricing.apps.svc.cluster.local \
      bash
```

```powershell
$env:DOCTOR_API_MCP_MODE = 'no-service-discovery'
$env:DOCTOR_API_MCP_NAMESPACE = 'apps'
$env:DOCTOR_API_MCP_SERVICE_NAME = 'pricing_api'
$env:DOCTOR_API_MCP_SERVICE_URL = 'http://pricing.apps.svc.cluster.local'
irm https://raw.githubusercontent.com/alexssantos/doctor-api-mcp/master/install.ps1 | iex
```

O runtime força `Discovery:Mode=Config`, não recebe acesso a Services/Endpoints e não publica a ferramenta `list_services`.

### Restrito: sem API Kubernetes, discovery ou volumes

```bash
curl -fsSL https://raw.githubusercontent.com/alexssantos/doctor-api-mcp/master/install.sh \
  | env DOCTOR_API_MCP_MODE=restricted \
      DOCTOR_API_MCP_SERVICE_NAME=pricing_api \
      DOCTOR_API_MCP_SERVICE_URL=https://pricing.internal.example \
      bash
```

Nesse perfil não há Role, ClusterRole ou bindings de runtime, o automount do token é desabilitado e o estado fica na memória de uma única réplica. `list_services`, `get_health` e `find_data_origin` não são publicadas. Providers HTTP externos continuam utilizáveis quando configurados e alcançáveis.

### Overrides avançados

Os presets podem ser refinados com:

- `DOCTOR_API_MCP_ACCESS_SCOPE`: `Cluster`, `Namespace` ou `None`;
- `DOCTOR_API_MCP_SERVICE_DISCOVERY`: `true` ou `false`;
- `DOCTOR_API_MCP_STATE_STORAGE`: `ConfigMap` ou `Memory`;
- `DOCTOR_API_MCP_ALLOW_VOLUMES`: `true` ou `false`;
- `DOCTOR_API_MCP_DEPLOYMENT_EVENTS`: `true` ou `false`;
- `DOCTOR_API_MCP_REPLICAS` e `DOCTOR_API_MCP_PDB`;
- `DOCTOR_API_MCP_PREFLIGHT=false` para ignorar somente o preflight do instalador. O teste de runtime continua obrigatório.

Combinações inseguras ou inconsistentes são rejeitadas pelo schema Helm e pela validação do processo.

## Helm via OCI ou chart local

```bash
helm upgrade --install doctor-api-mcp oci://registry-1.docker.io/alexssantos/doctor-api-mcp-chart --version 0.1.1 --namespace mcp-apis --create-namespace
```

O chart também pode ser instalado diretamente a partir do clone:

```bash
helm upgrade --install doctor-api-mcp ./infra/helm/doctor-api-mcp --namespace mcp-apis --create-namespace
```

Exemplo equivalente para namespace único:

```bash
helm upgrade --install doctor-api-mcp ./infra/helm/doctor-api-mcp \
  --namespace apps \
  --create-namespace \
  --set-string clusterAccess.scope=Namespace \
  --set-string security.allowedNamespaces[0]=apps
```

Exemplo sem service discovery:

```yaml
clusterAccess:
  scope: Namespace
  serviceDiscovery: false
  stateStorage: ConfigMap

security:
  allowedNamespaces:
    - apps

services:
  pricing_api: http://pricing.apps.svc.cluster.local
```

Os recursos da versão corrente no repositório podem ainda não estar presentes em uma versão OCI publicada anteriormente. Ao usar OCI, escolha uma tag que documente suporte a `clusterAccess`.

## Verificação e acesso local

```bash
kubectl rollout status deployment/doctor-api-mcp -n mcp-apis --timeout=180s
helm test doctor-api-mcp -n mcp-apis
kubectl port-forward service/doctor-api-mcp 4000:4000 -n mcp-apis
```

Com o port-forward ativo:

| Recurso | URL |
|---|---|
| Dashboard | `http://localhost:4000/dashboard` |
| Endpoint MCP | `http://localhost:4000/` |
| Health | `http://localhost:4000/health` |
| Readiness e requisitos mínimos | `http://localhost:4000/ready` |
| Relatório de requisitos | `http://localhost:4000/api/requirements?refresh=true` |

Exemplo para VS Code:

```json
{
  "servers": {
    "doctor-api-mcp": {
      "type": "http",
      "url": "http://localhost:4000/"
    }
  }
}
```

## Configurar as fontes de observabilidade

Por padrão, o chart procura `jaeger`, `prometheus` e `loki` no namespace da release. Para usar serviços em outros namespaces ou URLs, crie um arquivo `values-production.yaml`:

```yaml
dataSources:
  kubernetesNamespace: apps
  jaegerBaseUrl: http://jaeger-query.observability.svc.cluster.local:16686
  prometheusBaseUrl: http://kube-prometheus-stack-prometheus.observability.svc.cluster.local:9090
  lokiBaseUrl: http://loki-gateway.observability.svc.cluster.local

otel:
  endpoint: http://jaeger-collector.observability.svc.cluster.local:4317

security:
  allowedNamespaces:
    - apps
    - payments
```

Instale ou atualize:

```bash
helm upgrade --install doctor-api-mcp oci://registry-1.docker.io/alexssantos/doctor-api-mcp-chart --version 0.1.1 -n mcp-apis --create-namespace -f values-production.yaml
```

## Autenticação para ambientes expostos

O quick start deixa a autenticação desabilitada para reduzir atrito em um cluster local confiável. Antes de publicar um Ingress, habilite reader/admin e forneça chaves fortes:

```yaml
security:
  authentication:
    enabled: true
    readerKey: troque-por-uma-chave-reader-longa
    adminKey: troque-por-uma-chave-admin-longa

ingress:
  enabled: true
  className: traefik
  host: mcp.example.com
```

Em produção, prefira criar um Secret fora do chart e usar `security.authentication.existingSecret`. O Secret deve fornecer:

- `Security__Authentication__ReaderApiKey`
- `Security__Authentication__AdminApiKey`

## Imagem Docker

A imagem do MCP Server é multi-arquitetura (`linux/amd64` e `linux/arm64`):

```bash
docker pull alexssantos/doctor-api-mcp:latest
```

Ela é destinada a execução dentro de Kubernetes, pois o provider de cluster usa a configuração in-cluster e o ServiceAccount criado pelo chart. Para versões reproduzíveis, prefira tags SemVer ou `sha-<commit>` em vez de `latest`.

## Desinstalação

```bash
helm uninstall doctor-api-mcp -n mcp-apis
```

O namespace não é removido automaticamente, evitando apagar outros workloads. Se ele foi criado apenas para este pacote e estiver vazio, remova-o explicitamente:

```bash
kubectl delete namespace mcp-apis
```

## Publicação no Docker Hub para mantenedores

O workflow `.github/workflows/docker-publish.yml` publica:

- `alexssantos/doctor-api-mcp:latest` em alterações da branch principal;
- tags SemVer e `sha-<commit>` em releases `v*`;
- o chart OCI `alexssantos/doctor-api-mcp-chart` em tags `v*`.

Configure os secrets `DOCKERHUB_USERNAME` e `DOCKERHUB_TOKEN` no GitHub e mantenha os repositórios `doctor-api-mcp` e `doctor-api-mcp-chart` públicos no Docker Hub. Uma tag como `v0.1.1` publica imagem e chart com a mesma versão.
