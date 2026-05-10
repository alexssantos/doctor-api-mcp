# Guia de Configuração e Teste — MCP APIs

## Pré-requisitos

| Ferramenta | Versão mínima | Verificação |
|---|---|---|
| Docker | 24+ | `docker --version` |
| k3d | 5.x | `k3d --version` |
| kubectl | 1.28+ | `kubectl version --client` |
| Helm | 3.x | `helm version` |
| .NET SDK | 10.0 | `dotnet --version` |
| WSL2 | — | `wsl --version` |

---

## 1. Setup do Ambiente

### 1.1 Instalar ferramentas no WSL

```bash
bash scripts/install-tools-wsl.sh
```

### 1.2 Configurar kubeconfig (Lens)

```bash
bash scripts/setup-kubeconfig-link.sh
```

---

## 2. Deploy Completo

### Opção A — kubectl (manifests diretos)

```bash
# WSL
bash scripts/deploy-k8s.sh
```

### Opção B — Helm

```bash
# WSL
bash scripts/deploy-helm.sh
```

Ambos os scripts:
- Criam o cluster k3d `mcp-apis`
- Instalam nginx ingress
- Instalam PostgreSQL (Helm: Bitnami chart)
- Deployam todas as APIs e serviços de observabilidade
- Constroem e importam as imagens Docker
- Aguardam todos os rollouts completarem

### Verificar pods

```bash
kubectl get pods -n mcp-apis
```

Resultado esperado: **12 pods** Running:
- `postgres-produto-*` (1)
- `postgres-preco-*` (1)
- `jaeger-*` (1)
- `prometheus-*` (1)
- `loki-*` (1)
- `promtail-*` (1)
- `grafana-*` (1)
- `precoapi-*` (2)
- `produtoapi-*` (2)
- `mcpserver-*` (1)

---

## 3. Port-Forward

```bash
# WSL
bash scripts/port-forward.sh
```

Serviços acessíveis:

| Serviço | URL | Descrição |
|---|---|---|
| PrecoAPI | http://localhost:5001 | API de preços |
| ProdutoAPI | http://localhost:5002 | API de produtos |
| Jaeger | http://localhost:16686 | UI de tracing |
| Prometheus | http://localhost:9090 | Métricas |
| Grafana | http://localhost:3000 | Dashboards (admin/admin) |
| MCP Server | http://localhost:4000 | Streamable HTTP MCP |

---

## 4. Validação por Fase

### Fase 1+2: APIs e Tracing

```bash
bash scripts/validate-routes.sh
```

Testes:
- Criação de preços e produtos (CRUD)
- Consulta de produto com preço (integração entre APIs)
- Scalar UI acessível
- Total: 9 testes

### Fase 3: Observabilidade

```bash
bash scripts/validate-phase3.sh
```

Testes:
1. PrecoAPI `/metrics` — endpoint Prometheus
2. ProdutoAPI `/metrics` — endpoint Prometheus
3. Prometheus API acessível
4. Prometheus targets UP (2 targets)
5. Grafana healthcheck
6. Grafana datasources configurados (3: Prometheus, Loki, Jaeger)
7. Jaeger API acessível

### Fase 4: MCP Server

```bash
bash scripts/validate-phase4.sh
```

Testes:
1. `/health` endpoint
2. MCP `initialize` (Streamable HTTP)
3. `tools/list` retorna 7 tools
4. Nomes esperados das tools
5. Pod mcpserver ready
6. RBAC (ServiceAccount `mcp-reader`)

---

## 5. Configuração do AI Agent

### VS Code (GitHub Copilot)

O arquivo `.vscode/mcp.json` já está configurado no repositório:

```json
{
  "servers": {
    "mcp-apis": {
      "url": "http://localhost:4000/",
      "type": "streamable-http"
    }
  }
}
```

**Pré-requisito:** o port-forward deve estar ativo (`bash scripts/port-forward.sh`).

Para usar:
1. Abra o VS Code no diretório do projeto
2. O Copilot detecta automaticamente o `mcp.json`
3. As 7 tools ficam disponíveis no chat do Copilot

### Claude Desktop

Adicione ao arquivo de configuração (`%APPDATA%\Claude\claude_desktop_config.json` no Windows):

```json
{
  "mcpServers": {
    "mcp-apis": {
      "url": "http://localhost:4000/"
    }
  }
}
```

---

## 6. MCP Tools Disponíveis

| Tool | Descrição | Exemplo de pergunta |
|---|---|---|
| `list_services` | Lista serviços, pods e deployments K8s | "Quais serviços estão rodando?" |
| `get_openapi` | Retorna a spec OpenAPI de um serviço | "Me mostre a API do precoapi" |
| `trace_route` | Busca traces recentes no Jaeger | "Quem chama o PrecoAPI?" |
| `explain_api` | Combina OpenAPI + traces para explicar uma API | "Explique a API de produtos" |
| `get_health` | Verifica saúde dos pods de um serviço | "O precoapi está saudável?" |
| `find_dependencies` | Mostra grafo de dependências via Jaeger | "Quais são as dependências entre os serviços?" |
| `find_data_origin` | Rastreia origem dos dados de uma rota | "De onde vem o preço no GET /api/produtos/{id}?" |

---

## 7. Exemplos de Uso com o Agente

### Exemplo 1: Explicar uma API

> **Pergunta:** "Explique a API de preços"
>
> O agente chama `explain_api(serviceName: "precoapi")` e retorna as rotas disponíveis com seus métodos HTTP, descrições e atividade recente de traces.

### Exemplo 2: Verificar saúde

> **Pergunta:** "O produtoapi está saudável?"
>
> O agente chama `get_health(appName: "produtoapi")` e retorna status dos pods, restarts e estado dos containers.

### Exemplo 3: Rastrear dependências

> **Pergunta:** "Quem chama o precoapi?"
>
> O agente chama `find_dependencies()` e retorna o grafo de dependências entre serviços, mostrando que o produtoapi chama o precoapi.

### Exemplo 4: Origem dos dados

> **Pergunta:** "De onde vem o dado do GET /api/produtos/{id}?"
>
> O agente chama `find_data_origin(serviceName: "produtoapi", route: "/api/produtos/{id}")` e mostra a cadeia: produtoapi → precoapi → postgres.

---

## 8. Troubleshooting

### Port-forward falha com "address already in use"

```bash
# Matar port-forwards existentes
pkill -f "kubectl port-forward"
# Tentar novamente
bash scripts/port-forward.sh
```

### Pod em CrashLoopBackOff

```bash
kubectl logs -n mcp-apis <pod-name> --previous
kubectl describe pod -n mcp-apis <pod-name>
```

### MCP Server retorna 404

O endpoint MCP usa **Streamable HTTP** na raiz `/` (POST). Não existe endpoint `/sse` ou `/mcp` — o endpoint correto é:

```
POST http://localhost:4000/
Content-Type: application/json
Accept: application/json, text/event-stream
```

### Prometheus não mostra targets

```bash
# Verificar se as APIs expõem /metrics
curl http://localhost:5001/metrics
curl http://localhost:5002/metrics

# Verificar config do Prometheus
kubectl get configmap prometheus-config -n mcp-apis -o yaml
```

### Grafana sem datasources

```bash
# Verificar datasources provisionados
curl -u admin:admin http://localhost:3000/api/datasources
```

---

## 9. Teardown

```bash
# Remover tudo (namespace + cluster)
bash scripts/teardown-k8s.sh
# ou
bash scripts/teardown-helm.sh
```

---

## 10. Arquitetura

```
┌──────────────────────────────────────────────────┐
│                  AI Agent (LLM)                  │
│           VS Code Copilot / Claude Desktop       │
└──────────────┬───────────────────────────────────┘
               │ MCP (Streamable HTTP)
               ▼
┌──────────────────────────────────────────────────┐
│              MCP Server (:4000)                  │
│  Tools: list_services, get_openapi, trace_route, │
│  explain_api, get_health, find_dependencies,     │
│  find_data_origin                                │
└──────┬──────────┬──────────────┬─────────────────┘
       │          │              │
       ▼          ▼              ▼
  K8s API     Jaeger API    OpenAPI specs
  (RBAC)     (:16686)     (precoapi, produtoapi)
       │          │              │
       ▼          ▼              ▼
┌────────────────────────────────────────────────┐
│           Kubernetes (k3d)  namespace: mcp-apis│
│                                                │
│  ┌──────────┐  ┌───────────┐  ┌──────────────┐│
│  │PrecoAPI  │←─│ProdutoAPI │  │   Jaeger     ││
│  │ (x2)     │  │  (x2)     │  │              ││
│  └────┬─────┘  └─────┬─────┘  └──────────────┘│
│       │              │                         │
│  ┌────▼─────┐  ┌─────▼─────┐  ┌──────────────┐│
│  │PostgreSQL│  │PostgreSQL │  │ Prometheus   ││
│  │(preco_db)│  │(produto_db)│ │ Grafana      ││
│  └──────────┘  └───────────┘  │ Loki+Promtail││
│                               └──────────────┘│
└────────────────────────────────────────────────┘
```
