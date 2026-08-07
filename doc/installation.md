# Instalação do doctor-api-mcp

O pacote público instala o **MCP Server** no contexto Kubernetes atual. Ele inclui dashboard, RBAC de descoberta, probes, estado persistido de indexação e configuração dos providers. Prometheus, Jaeger e Loki podem estar no mesmo namespace ou em qualquer endereço interno permitido pelo cluster.

> O chart não substitui sua stack de observabilidade. Para subir o ambiente de demonstração completo deste repositório em WSL/k3d, continue usando `./infra/scripts/ps/up-k8s.ps1 -Build`.

## Pré-requisitos

- Um cluster Kubernetes acessível pelo contexto atual do `kubectl`.
- Helm 3.
- Acesso do cluster ao Docker Hub.
- Para o comando PowerShell: Helm e kubectl no Windows ou dentro do WSL.

## Instalação em uma linha

### Linux ou WSL

```bash
curl -fsSL https://raw.githubusercontent.com/alexssantos/doctor-api-mcp/master/install.sh | bash
```

O script baixa apenas o snapshot do repositório necessário para executar o chart, instala a release `doctor-api-mcp` no namespace `mcp-apis`, aguarda os pods e remove os arquivos temporários.

Valores podem ser passados ao Helm depois de `bash`:

```bash
curl -fsSL https://raw.githubusercontent.com/alexssantos/doctor-api-mcp/master/install.sh | bash -s -- --set replicaCount=1
```

As variáveis `DOCTOR_API_MCP_REF`, `DOCTOR_API_MCP_RELEASE` e `DOCTOR_API_MCP_NAMESPACE` alteram, respectivamente, a referência Git, o nome da release e o namespace.

### PowerShell

```powershell
irm https://raw.githubusercontent.com/alexssantos/doctor-api-mcp/master/install.ps1 | iex
```

Se `helm` e `kubectl` não estiverem no PATH do Windows, o instalador os procura no WSL e usa o contexto Kubernetes de lá. Isso mantém compatibilidade com o fluxo k3d/WSL do projeto.

### Helm via OCI

```bash
helm upgrade --install doctor-api-mcp oci://registry-1.docker.io/alexssantos/doctor-api-mcp-chart --version 0.1.1 --namespace mcp-apis --create-namespace
```

O chart também pode ser instalado diretamente a partir do clone:

```bash
helm upgrade --install doctor-api-mcp ./infra/helm/doctor-api-mcp --namespace mcp-apis --create-namespace
```

## Verificação e acesso local

```bash
kubectl rollout status deployment/doctor-api-mcp -n mcp-apis --timeout=180s
kubectl port-forward service/doctor-api-mcp 4000:4000 -n mcp-apis
```

Com o port-forward ativo:

| Recurso | URL |
|---|---|
| Dashboard | `http://localhost:4000/dashboard` |
| Endpoint MCP | `http://localhost:4000/` |
| Health | `http://localhost:4000/health` |

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
