# mcp-apis

Projeto de estudo que demonstra como expor um cluster Kubernetes para agentes de IA usando o **Model Context Protocol (MCP)**. Dois microsserviços .NET (PrecoAPI e ProdutoAPI) rodam em um cluster k3d local com observabilidade completa. Um servidor MCP fornece ferramentas que permitem a LLMs consultar o estado vivo do sistema: rotas disponíveis, traces, saúde dos pods e dependências entre serviços.

---

## Índice

- [Visão Geral da Arquitetura](#visão-geral-da-arquitetura)
- [Serviços](#serviços)
- [Stack Tecnológica](#stack-tecnológica)
- [Estrutura de Arquivos](#estrutura-de-arquivos)
- [MCP Server — Ferramentas](#mcp-server--ferramentas)
- [Configuração do MCP Server](#configuração-do-mcp-server)
- [Dashboard](#dashboard)
- [Observabilidade](#observabilidade)
- [Kubernetes](#kubernetes)
- [Helm Charts](#helm-charts)
- [Scripts de Automação](#scripts-de-automação)
- [Decisões de Implementação](#decisões-de-implementação)
- [Portas e Acessos](#portas-e-acessos)
- [Configuração do Ambiente](#configuração-do-ambiente)
- [Desenvolvimento com Tilt](#desenvolvimento-com-tilt)

---

## Visão Geral da Arquitetura

```
┌─────────────────────────────────────────────────────────────────────┐
│  Cliente (VS Code / Claude / Cursor)                                │
│  ── MCP over HTTP ──────────────────────────────────────────────►  │
└─────────────────────┬───────────────────────────────────────────────┘
                      │  POST http://localhost:4000/
                      ▼
┌─────────────────────────────────────────────────────────────────────┐
│  McpServer  (port 4000)                                             │
│  • 9 ferramentas MCP                                                │
│  • Dashboard React em /dashboard (com switch de indexação)         │
│  • Descoberta automática de aplicações (deployments/rede/OTel)     │
│  • Consulta Kubernetes API (in-cluster, read-only)                  │
│  • Consulta Jaeger API (traces e dependências)                      │
│  • Consulta Prometheus API (métricas)                               │
│  • Consulta OpenAPI de cada serviço                                 │
└──────────┬───────────────────────────┬─────────────────────────────┬┘
           │                           │                             │
           ▼                           ▼                             ▼
┌──────────────────────┐   ┌──────────────────────┐   ┌──────────────────────┐
│  Kubernetes API      │   │  Jaeger             │   │  Prometheus          │
│  (in-cluster RBAC)   │   │  (port 16686)       │   │  (port 9090)         │
│                      │   │  OTLP (port 4317)   │   │  Scrape /metrics     │
└──────────────────────┘   └──────────────────────┘   └──────────────────────┘

┌──────────────────────────────────────────────────────────────────────┐
│  Namespace: mcp-apis                                                 │
│                                                                      │
│  ┌──────────────────┐   HTTP    ┌──────────────────────────────────┐ │
│  │  ProdutoAPI      │ ────────► │  PrecoAPI                        │ │
│  │  port 8080       │           │  port 8080                       │ │
│  │  PostgreSQL      │           │  PostgreSQL                      │ │
│  │  (produto_db)    │           │  (preco_db)                      │ │
│  └──────────────────┘           └──────────────────────────────────┘ │
│                                                                      │
│  Observabilidade:  Prometheus · Grafana · Loki · Promtail · Jaeger   │
└──────────────────────────────────────────────────────────────────────┘
```

**Fluxo de dados:**
1. ProdutoAPI recebe `GET /api/products` → busca produtos no PostgreSQL próprio
2. Para cada produto, chama `PrecoAPI GET /api/prices/{productId}` via `PriceClient` (HttpClient tipado)
3. Retorna produto enriquecido com preço
4. Ambas as APIs exportam traces OTLP para Jaeger e métricas Prometheus
5. O MCP Server (e seu Dashboard) usam esses dados para responder perguntas de LLMs e exibir informações em tempo real

---

## Serviços

### PrecoAPI

Responsável exclusivamente pelo gerenciamento de preços de produtos.

| Endpoint | Método | Descrição |
|---|---|---|
| `/api/prices/{productId}` | GET | Busca preço por produto |
| `/api/prices` | POST | Cria entrada de preço |
| `/api/prices/{productId}` | PUT | Atualiza preço |
| `/scalar/v1` | GET | UI interativa da API (Scalar) |
| `/openapi/v1.json` | GET | Spec OpenAPI — **obrigatório para indexação pelo MCP** |
| `/metrics` | GET | Métricas Prometheus |

**Banco:** PostgreSQL separado (`preco_db`). Modelo: `Price { Id, ProductId, Value, Currency, UpdatedAt }`.

### ProdutoAPI

Gerencia produtos e enriquece as respostas com dados de preço vindos da PrecoAPI.

| Endpoint | Método | Descrição |
|---|---|---|
| `/api/products` | GET | Lista todos os produtos com preços |
| `/api/products/{id}` | GET | Produto por ID com preço |
| `/api/products` | POST | Cria produto |
| `/api/products/{id}` | PUT | Atualiza produto |
| `/api/products/{id}` | DELETE | Remove produto |
| `/scalar/v1` | GET | UI interativa da API (Scalar) |
| `/openapi/v1.json` | GET | Spec OpenAPI — **obrigatório para indexação pelo MCP** |
| `/metrics` | GET | Métricas Prometheus |

**Banco:** PostgreSQL separado (`produto_db`). Modelo: `Product { Id, Name, Description, CreatedAt }`.

**Integração:** usa `PriceClient` (HttpClient tipado) para chamar PrecoAPI. Falhas na PrecoAPI são tratadas graciosamente — o produto é retornado com `price: null`.

### McpServer

Servidor MCP que expõe o sistema para agentes de IA. Não possui banco de dados — é um orquestrador que agrega informações de:
- **Kubernetes API** (pods, services, deployments)
- **Jaeger** (traces, dependências)
- **OpenAPI** de cada serviço (rotas, schemas)

Transporte: **Streamable HTTP** via `POST /` (protocolo MCP 2025-03-26).

---

## Stack Tecnológica

### Aplicação

| Tecnologia | Versão | Uso |
|---|---|---|
| .NET / ASP.NET Core | 10 | Runtime de todas as APIs |
| Entity Framework Core | 10 | ORM, `EnsureCreatedAsync` para migrations simples |
| Npgsql.EFCore | 10 | Provider PostgreSQL |
| ModelContextProtocol.AspNetCore | 1.3.0 | SDK MCP para o McpServer |
| KubernetesClient | 19.0.2 | Consulta à Kubernetes API (in-cluster) |
| OpenTelemetry SDK | latest | Tracing + Metrics exportados via OTLP |
| OpenTelemetry.Instrumentation.EFCore | 1.12.0-beta.1 | Instrumentação automática de queries SQL |
| Scalar.AspNetCore | 2.5.0 | UI de documentação da API (substitui Swagger UI) |
| Microsoft.AspNetCore.OpenApi | 10 | Geração de spec OpenAPI nativa do .NET |

### Infraestrutura

| Tecnologia | Uso |
|---|---|
| k3d + k3s | Cluster Kubernetes local em Docker |
| Docker | Runtime dos containers (buildado localmente, `imagePullPolicy: Never`) |
| nginx Ingress Controller | Roteamento externo de requests para os serviços |
| Jaeger | Distributed tracing — recebe spans OTLP, expõe UI e API REST |
| Prometheus | Scraping de métricas dos endpoints `/metrics` |
| Grafana | Visualização de métricas e logs |
| Loki | Armazenamento de logs (schema v13, storage filesystem) |
| Promtail | Coleta de logs dos pods via DaemonSet |
| PostgreSQL 16 | Banco de dados — duas instâncias separadas (StatefulSet) |
| Helm 3 | Empacotamento alternativo dos manifestos |

---

## Estrutura de Arquivos

```
mcp-apis/
├── src/
│   ├── mcp-apis.slnx                     # Solution file (.NET 10 slnx)
│   ├── BuildingBlocks/
│   │   ├── Http/
│   │   │   ├── HttpClientFactoryExtensions.cs   # AddHttpClientWithCorrelation<T>
│   │   │   └── DelegatingHandlers/
│   │   │       └── CorrelationHandler.cs         # Propagação de correlation-id entre serviços
│   │   └── Observability/
│   │       ├── ObservabilityExtensions.cs        # AddObservability() — OTel tracing + metrics
│   │       ├── BodyCaptureOptions.cs             # Feature flag para captura de body
│   │       ├── RequestBodyTelemetryMiddleware.cs  # Captura body da request como span tag
│   │       └── ResponseBodyTelemetryMiddleware.cs # Captura body da response como span tag
│   └── Services/
│       ├── PrecoAPI/
│       │   ├── Program.cs                # Minimal API setup
│       │   ├── Controllers/
│       │   │   └── PricesController.cs   # CRUD de preços
│       │   ├── Models/Price.cs
│       │   ├── Data/PriceDbContext.cs
│       │   ├── Dtos/                     # Request/Response records
│       │   ├── Contracts/                # Interfaces de request
│       │   └── Dockerfile
│       ├── ProdutoAPI/
│       │   ├── Program.cs
│       │   ├── Controllers/
│       │   │   └── ProductsController.cs  # CRUD de produtos com enriquecimento de preço
│       │   ├── Models/Product.cs
│       │   ├── Data/ProductDbContext.cs
│       │   ├── Dtos/
│       │   ├── Integration/PrecoApi/
│       │   │   └── PriceClient.cs         # HttpClient tipado para PrecoAPI
│       │   └── Dockerfile
│       └── McpServer/
│           ├── Program.cs                 # MapMcp() + registro das ferramentas
│           ├── Services/
│           │   ├── KubernetesService.cs   # Wrapper do KubernetesClient (cluster-wide)
│           │   ├── JaegerService.cs       # Wrapper da Jaeger REST API
│           │   ├── OpenApiService.cs      # Fetch e parse de specs OpenAPI
│           │   ├── ApplicationCatalog.cs  # Inventário vivo de aplicações descobertas
│           │   ├── DiscoveryOrchestrator.cs      # Scan multi-fonte + correlação
│           │   ├── DiscoveryBackgroundService.cs # Re-scan periódico/sob demanda
│           │   └── KubernetesIndexingStateStore.cs # Persistência do toggle (ConfigMap)
│           ├── Tools/
│           │   ├── ListServicesTool.cs
│           │   ├── ListDiscoveredApplicationsTool.cs
│           │   ├── GetOpenApiTool.cs
│           │   ├── TraceRouteTool.cs
│           │   ├── ExplainApiTool.cs
│           │   ├── GetHealthTool.cs
│           │   ├── FindDependenciesTool.cs
│           │   ├── FindDataOriginTool.cs
│           │   └── ToolGuard.cs           # Gate central do switch de indexação
│           └── Dockerfile
│
├── k8s/                                   # Manifestos Kubernetes (kubectl apply -f)
│   ├── namespace.yaml
│   ├── aplicacao/                         # Serviços da aplicação (APIs + MCP Server)
│   │   ├── precoapi/                      # configmap, deployment, ingress, secret, service
│   │   ├── produtoapi/
│   │   └── mcpserver/                     # + rbac.yaml (ServiceAccount mcp-reader)
│   ├── banco/                             # Bancos de dados (PostgreSQL)
│   │   ├── postgres-preco/                # StatefulSet + configmap-init + secret + service
│   │   └── postgres-produto/
│   └── observabilidade/                   # Stack de observabilidade complementar ao MCP
│       ├── jaeger/
│       ├── prometheus/                    # + configmap com prometheus.yml
│       ├── loki/                          # + configmap com loki.yaml
│       ├── promtail/                      # DaemonSet
│       └── grafana/                       # + configmap-datasources.yaml
│
├── helm/                                  # Charts Helm (alternativa aos manifestos k8s/)
│   ├── mcpserver/
│   ├── precoapi/
│   └── produtoapi/
│
├── scripts/
│   ├── ps/                                # PowerShell (Windows)
│   │   ├── up-k8s.ps1                    # Sobe o ambiente completo
│   │   ├── down-k8s.ps1                  # Derruba o cluster
│   │   ├── port-forward.ps1              # Port-forwards para acesso local
│   │   ├── validate.ps1                  # Smoke tests de endpoints
│   │   ├── wsl-check.ps1                 # Valida ambiente WSL
│   │   └── wsl-run.ps1                   # Helper para executar no WSL
│   └── sh/                               # Shell (WSL/Linux)
│       ├── deploy-k8s.sh                 # Deploy via kubectl
│       ├── deploy-helm.sh                # Deploy via Helm
│       ├── teardown-k8s.sh
│       ├── teardown-helm.sh
│       ├── port-forward.sh
│       ├── validate-phase3.sh
│       ├── validate-phase4.sh
│       └── validate-routes.sh
│
└── doc/
    ├── casos_de_uso.md
    ├── guia_configuracao_mcp.md
    ├── mcp_orquestrador_docker.md
    ├── mcp_orquestrador_k8s.md
    ├── tilt.md                            # Guia de desenvolvimento local com Tilt
    └── features/
        ├── 001_implementacao_base.md
        ├── 002_tracing_with_body.md
        ├── 003_service_discovery_and_validation.md
        └── 004_automatic_application_discovery.md
```

---

## MCP Server — Ferramentas

O McpServer expõe 9 ferramentas para agentes de IA. Todas recebem dependências via injeção (DI do .NET — o SDK MCP resolve automaticamente parâmetros que são serviços registrados).

| Ferramenta | Descrição | Fontes de Dados |
|---|---|---|
| `list_services` | Lista todos os services, pods e deployments no namespace com status | Kubernetes API + OpenApiService |
| `list_discovered_applications` | Inventário completo de aplicações auto-descobertas no cluster (fontes, estado do toggle, motivos de não-indexabilidade) | ApplicationCatalog |
| `get_openapi` | Retorna a spec OpenAPI completa de um serviço | HTTP → `/openapi/v1.json` |
| `trace_route` | Busca traces recentes de um serviço/rota com call chain e timings | Jaeger REST API |
| `explain_api` | Explica o que uma API faz combinando OpenAPI + dados de traces recentes | OpenAPI + Jaeger |
| `get_health` | Verifica a saúde de um serviço via estado dos pods (ready, restarts) | Kubernetes API |
| `find_dependencies` | Mapa de dependências entre serviços usando o grafo do Jaeger | Jaeger `/api/dependencies` |
| `find_data_origin` | Rastreia a origem dos dados de uma rota: API → chamadas HTTP → queries SQL | OpenAPI + Jaeger + Kubernetes |
| `query_metrics` | Executa queries PromQL arbitrárias contra Prometheus (instant ou range) | Prometheus `/api/v1/query*` |

> 🔒 **Switch de indexação:** aplicações desabilitadas no dashboard ficam invisíveis para as tools — `get_openapi`/`explain_api`/`find_data_origin`/`get_health`/`trace_route` recusam com erro explicativo, `list_services`/`find_dependencies` omitem os dados (informando em `disabledApplications`), e `query_metrics` recusa queries que mencionem a aplicação (best-effort). Detalhes em [`doc/features/004_automatic_application_discovery.md`](doc/features/004_automatic_application_discovery.md).

### Registro das ferramentas

```csharp
builder.Services
    .AddMcpServer(opts => { opts.ServerInfo = new() { Name = "mcp-apis-server", Version = "1.0.0" }; })
    .WithHttpTransport()
    .WithTools<ListServicesTool>()
    // ...
```

Cada tool é uma classe estática com método `Execute` anotado com `[McpServerTool]`. Os parâmetros que são serviços do DI (ex: `IKubernetesCollector k8s`) são resolvidos automaticamente. Parâmetros primitivos viram argumentos da ferramenta visíveis ao LLM.

---

## Configuração do MCP Server

### Pipeline de inicialização

Ao subir, o McpServer executa automaticamente um scan de descoberta (e re-executa a cada 60s ou sob demanda):

```
DiscoveryOrchestrator (startup bloqueante + BackgroundService)
  └─► coleta fontes (Deployments + Services/Endpoints cluster-wide, Jaeger, Config)
       └─► correlação de identidade (matching estrutural + normalização de nomes)
              └─► Validation (ServiceValidator) — por aplicação com base URL
                     └─► estado do toggle (ConfigMap mcpserver-state)
                            └─► ApplicationCatalog.ReplaceSnapshot()
```

O `ApplicationCatalog` é a fonte de verdade: o dashboard lista **todas** as aplicações descobertas (inclusive as não indexáveis, com o motivo), e as tools MCP enxergam apenas as **habilitadas** no switch de indexação. Serviços que falham na validação OpenAPI seguem visíveis no dashboard e utilizáveis por tools de traces/health — apenas as tools baseadas em spec ficam indisponíveis.

---

### Modos de descoberta

Controlado pela variável `Discovery__Mode` (ou `Discovery:Mode` no `appsettings.json`):

| Modo         | Fonte                                                                          |
|--------------|--------------------------------------------------------------------------------|
| `Auto` ⭐    | **Descoberta automática cluster-wide**: Deployments + Services/Endpoints (todos os namespaces) + serviços que emitem traces (Jaeger `/api/services`) + seção `Services` |
| `Config`     | Seção `Services` do appsettings / variáveis de ambiente `Services__<nome>`     |
| `Kubernetes` | Services no namespace com label `mcp-apis/indexed=true`                        |
| `Both`       | Mescla Config + label — K8s sobrescreve Config em caso de conflito de nome     |

**Padrão:** `Auto`. Aplicações auto-descobertas nascem **desabilitadas** para indexação (opt-in via switch no dashboard); as declaradas em `Services__*` ou com label `mcp-apis/indexed=true` nascem habilitadas. A label `mcp-apis/indexed=false` trava a aplicação como desabilitada (o switch fica bloqueado).

> 📄 Detalhes da correlação de identidade (deployment ↔ service ↔ OTel), persistência do toggle e semântica por tool: [`doc/features/004_automatic_application_discovery.md`](doc/features/004_automatic_application_discovery.md)

---

### Modo `Config`

Cada entrada em `Services` vira um serviço candidato. A **chave** é o alias que o MCP expõe para o LLM (aparece nas tools e nos logs); o **valor** é a URL real do Service K8s — os dois não precisam ser iguais:

```yaml
# infra/k8s/aplicacao/mcpserver/configmap.yaml
#
#   chave (alias MCP)          valor (URL do Service K8s)
#         ↓                              ↓
Services__pricing-api: "http://precoapi.mcp-apis.svc.cluster.local"
Services__product-api: "http://produtoapi.mcp-apis.svc.cluster.local"
Discovery__Mode:       "Config"
```

O LLM verá `pricing-api` e `product-api` nas ferramentas, enquanto o DNS resolve para os Services `precoapi` e `produtoapi` no namespace `mcp-apis`.

Use sempre FQDNs (`http://<service>.<namespace>.svc.cluster.local`) para garantir que o MCP Server alcance o serviço independentemente do namespace onde ele roda. URLs curtas (`http://precoapi`) só resolvem se o MCP Server estiver no **mesmo** namespace.

Para adicionar um serviço em outro namespace basta apontar para o FQDN correto:
```yaml
Services__minha-api: "http://minha-api.outro-namespace.svc.cluster.local"
```

---

### Modo `Kubernetes`

O McpServer lista todos os `Service` no namespace com a label configurada em `Discovery__KubernetesLabel` (padrão: `mcp-apis/indexed`) e valor `"true"`.

A URL base de cada serviço é resolvida em ordem:
1. Annotation `mcp-apis/base-url` no objeto `Service` (use FQDN para suporte multi-namespace)
2. Fallback: FQDN gerado automaticamente como `http://<name>.<namespace>.svc.cluster.local`

**Ativar o modo:**
```yaml
Discovery__Mode:           "Kubernetes"
Discovery__KubernetesLabel: "mcp-apis/indexed"
```

**Anotar um serviço para ser descoberto:**
```yaml
# infra/k8s/aplicacao/<meuservico>/service.yaml
metadata:
  labels:
    mcp-apis/indexed: "true"
  annotations:
    mcp-apis/base-url: "http://meuservico"
```

> ⚠️ Requer permissão `list` em `services` para o `ServiceAccount` do McpServer. O RBAC em `infra/k8s/aplicacao/mcpserver/rbac.yaml` já contempla essa permissão.

---

### Validação antes de indexar

Antes de registrar qualquer candidato, o `ServiceValidator` verifica três critérios. Se qualquer um falhar, o serviço é ignorado:

| # | Critério                          | Detalhe                                                                               |
|---|-----------------------------------|---------------------------------------------------------------------------------------|
| 1 | **Serviço acessível**             | Responde HTTP < 500 em `/health` ou `/` (timeout 10 s)                                |
| 2 | **OpenAPI spec acessível**        | Primeiro path de `DataSources:OpenApiSpecPaths` que retorne HTTP 200                  |
| 3 | **Spec com conteúdo válido**      | JSON parseável com pelo menos um `path` definido                                      |

O critério 2 testa os caminhos em ordem e usa o primeiro bem-sucedido. O caminho resolvido é gravado no `ServiceRegistry` e reutilizado em todas as chamadas da tool.

**Exemplo de log no startup:**
```
info: Service discovery (Config) found 2 candidate(s): precoapi, produtoapi
info: ✓ Registered service 'precoapi' at http://precoapi (spec: /openapi/v1.json)
warn: ✗ Skipped service 'produtoapi' at http://produtoapi: OpenAPI spec not found. Probed paths: /openapi/v1.json → HTTP 404, /swagger/v1/swagger.json → HTTP 404
info: Service discovery complete. 1 service(s) registered: precoapi
```

---

### Requisitos mínimos de uma API para ser indexada

Para que qualquer serviço passe na validação e seja exposto pelas tools do MCP, ele deve oferecer:

| Endpoint                           | Requisito                                                              |
|------------------------------------|------------------------------------------------------------------------|
| `/health` ou `/`                   | Responder HTTP < 500 (prova que o serviço está no ar)                  |
| Um dos `DataSources:OpenApiSpecPaths` | Retornar a spec OpenAPI em JSON com pelo menos um `path` definido   |

**No .NET com `Microsoft.AspNetCore.OpenApi`:**
```csharp
// Program.cs
builder.Services.AddOpenApi();
// ...
app.MapOpenApi(); // expõe /openapi/v1.json por padrão
app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));
```

Se o serviço expõe a spec em outro caminho (ex: `/swagger/v1/swagger.json`), basta que esse path esteja listado em `DataSources__OpenApiSpecPaths` no configmap — não é necessário alterar o serviço.

---

### Referência completa de configuração

| Variável de ambiente                     | Padrão                 | Descrição                                                       |
|------------------------------------------|------------------------|-----------------------------------------------------------------|
| `DataSources__Jaeger__BaseUrl`           | `http://jaeger:16686`  | URL da API REST do Jaeger                                       |
| `DataSources__Kubernetes__Namespace`     | `mcp-apis`             | Namespace onde o McpServer roda (estado + health padrão)        |
| `DataSources__OpenApiSpecPaths__0..N`    | `/openapi/v1.json`     | Caminhos candidatos para spec OpenAPI (primeiro 200 vence)      |
| `Discovery__Mode`                        | `Auto`                 | Fonte de descoberta: `Auto`, `Config`, `Kubernetes` ou `Both`   |
| `Discovery__KubernetesLabel`             | `mcp-apis/indexed`     | Label K8s de opt-in (`true`) / hard-off (`false`)               |
| `Discovery__RescanSeconds`               | `60`                   | Intervalo do re-scan periódico (`0` = só startup + manual)      |
| `Discovery__RevalidateSeconds`           | `300`                  | Reuso de validações OpenAPI bem-sucedidas                       |
| `Discovery__ForgetAfterMinutes`          | `60`                   | Tempo até esquecer aplicações não vistas pelos scans            |
| `Discovery__StateConfigMap`              | `mcpserver-state`      | ConfigMap que persiste o switch de indexação                    |
| `Discovery__ExcludeNamespaces__0..N`     | `kube-system`, ...     | Namespaces nunca escaneados                                     |
| `Discovery__ExcludeApps__0..N`           | `mcpserver`, `jaeger`, ... | Infra excluída do catálogo de aplicações                    |
| `Discovery__ExcludeOtelServices__0..N`   | `jaeger-query`, `McpServer` | Nomes OTel ignorados na descoberta                         |
| `Services__<nome>`                       | —                      | URL base de um serviço (habilitado por padrão em qualquer modo) |

> 📄 Documentação detalhada: [`doc/features/003_service_discovery_and_validation.md`](doc/features/003_service_discovery_and_validation.md) e [`doc/features/004_automatic_application_discovery.md`](doc/features/004_automatic_application_discovery.md)

---

## Dashboard

O McpServer também expõe um **Dashboard web interativo** em `/dashboard` que oferece uma interface visual para monitorar o cluster, visualizar métricas e acessar ferramentas de observabilidade.

### Acesso

| Ambiente | URL |
|---|---|
| Desenvolvimento (Tilt) | `http://localhost:4000/dashboard` |
| Kubernetes (Ingress) | `http://mcpserver.local:8080/dashboard` |
| Port-forward | `http://localhost:4000/dashboard` |

### Funcionalidades

**1. Header**
- **Status de Saúde:** badge em tempo real mostrando a saúde geral do cluster
- **Modo Escuro/Claro:** toggle com sincronização com preferência do sistema
- **Atualização Manual:** botão refresh que recarrega dados de todos os painéis
- **Horário da Última Atualização:** timestamp local

**2. Quick Links**
- Links rápidos para **Jaeger**, **Prometheus** e **Grafana** abrindo em novas abas
- Usa as URLs configuradas em `Dashboard:Links` (acessíveis via navegador)
- Ícones temáticos com cores que remetem a cada ferramenta

**3. Cards de Estatísticas**
- **Serviços Indexados:** número total de serviços descobertos pelo MCP
- **Pods Prontos:** percentual de pods em estado `Running` e `Ready`
- **Deployments Prontos:** percentual de deployments com todas as réplicas ready
- **Saúde do Cluster:** status geral agregado (verde/amarelo/vermelho)

**4. Painel de Aplicações Descobertas**
- Lista **todas** as aplicações auto-descobertas no cluster (deployments, rede, OTel, config)
- **Switch de indexação MCP** por aplicação (optimistic update; persiste no ConfigMap `mcpserver-state`)
  - Travado (com tooltip) quando o Service tem a label `mcp-apis/indexed: "false"`
- Por aplicação, mostra:
  - **Nome e Status de Saúde** (badge Saudável/Degradado/Sem Pods)
  - **Badges de fonte:** `Deploy` / `Rede` / `OTel` / `Config` + namespace
  - **Badge OpenAPI:** verde com o path quando validada; "Não indexável" com tooltip dos motivos quando não
  - **URL Base**, contagem de pods, restarts (se > 0)
  - Badge **"Não vista há Xmin"** quando a aplicação sumiu do cluster
- Botão **Re-scan** força uma nova descoberta imediata
- Click em uma aplicação a seleciona para visualizar seus traces e métricas

**5. Painel de Traces**
- Mostra os **12 traces mais recentes** do serviço selecionado via Jaeger
- Para cada trace:
  - **Duração:** tempo total da request (ms/s)
  - **Span Count:** número de spans
  - **Timestamp:** quando ocorreu
- Botão **"Abrir no Jaeger"** redireciona para a busca completa no Jaeger UI

**6. Painel de Dependências**
- **Grafo de serviços:** mostra que serviço chama qual outro
- Lista os **pais → filhos** com ícone `→` representando a direção da chamada
- **Call Count:** quantas vezes a chamada ocorreu (agregado)
- Ajuda a entender o fluxo de dados na arquitetura

**7. Painel de Métricas (PromQL)**
- **Presets:** dropdown com queries pré-configuradas:
  - `up` — status do serviço (1=up, 0=down)
  - `requestRate` — taxa de requests por segundo
  - `errorRate` — taxa de erros (HTTP 5xx)
  - `memory` — uso de memória do container
  - `custom` — campo livre para digitar PromQL
- **Série Temporal:** gráfico em linha mostrando 30 minutos de histórico com resolução de 15s
- Renderizado via **recharts** com grid, eixos e tooltips interativas

### Stack Técnico

**Frontend (em `src/Services/McpServer/dashboard/`):**
- **React 19** + TypeScript 5
- **Vite 8** (rolldown) — bundler com dev server + build otimizado
- **Tailwind CSS v4** com `@tailwindcss/vite` plugin — CSS-first, tokens OKLCH, dark mode
- **Shadcn-style components** — componentes hand-built seguindo padrões shadcn (cva variants, Radix primitives)
- **TanStack React Query v5** — gerenciamento de estado das requisições, refetch automático a cada 15s
- **Recharts** — visualização de gráficos (LineChart com tooltips)
- **Lucide React** — ícones (Activity, RefreshCw, Moon, Sun, Server, HeartPulse, etc.)

**Backend (em `src/Services/McpServer/`):**
- **IPrometheusCollector / PrometheusService** — wrapper da Prometheus HTTP API (`/api/v1/query`, `/api/v1/query_range`, `/api/v1/targets`)
- **DashboardEndpoints.cs / ApplicationsEndpoints.cs** — rotas REST que proxificam os dados:
  - `GET /api/dashboard/overview` — cluster summary (pods, deployments, saúde)
  - `GET /api/dashboard/services` — lista de serviços com health por pod
  - `GET /api/dashboard/applications` — inventário de aplicações descobertas (fontes, validação, toggle)
  - `PUT /api/dashboard/applications/{name}/indexing` — habilita/desabilita a indexação MCP (`{"enabled": bool}`)
  - `POST /api/dashboard/discovery/rescan` — força um novo scan de descoberta
  - `GET /api/dashboard/traces?service=&limit=15` — traces do Jaeger agrupados
  - `GET /api/dashboard/dependencies` — grafo do Jaeger
  - `GET /api/dashboard/metrics?query=` — instant query do Prometheus
  - `GET /api/dashboard/metrics/range?query=&minutes=30&step=15s` — range query do Prometheus
  - `GET /api/dashboard/links` — URLs dos dashboards externos (Jaeger, Prometheus, Grafana)
- **Static Files:** Vite build output em `wwwroot/dashboard/`, servido via `UseDefaultFiles()` + `UseStaticFiles()` + SPA fallback routes

### Configuração

**Em `appsettings.json` (credenciais do servidor):**
```json
{
  "DataSources": {
    "Prometheus": {
      "BaseUrl": "http://prometheus.mcp-apis.svc.cluster.local:9090"
    }
  },
  "Dashboard": {
    "Links": {
      "Jaeger": "http://jaeger.local:8080",
      "Prometheus": "http://prometheus.local:8080",
      "Grafana": "http://grafana.local:8080"
    }
  }
}
```

> ℹ️ As URLs em `DataSources.*` são **server-side** (cluster-internal, FQDNs) para o backend acessar as APIs.
> As URLs em `Dashboard:Links` são **browser-facing** (passadas ao frontend) — devem ser acessíveis do seu navegador host (via Ingress/port-forward).

**Em `infra/k8s/aplicacao/mcpserver/configmap.yaml`:**
```yaml
data:
  DataSources__Prometheus__BaseUrl: "http://prometheus.mcp-apis.svc.cluster.local:9090"
  Dashboard__Links__Jaeger: "http://jaeger.local:8080"
  Dashboard__Links__Prometheus: "http://prometheus.local:8080"
  Dashboard__Links__Grafana: "http://grafana.local:8080"
```

### Desenvolvimento Local

**Build do frontend:**
```bash
cd src/Services/McpServer/dashboard
npm ci
npm run build      # output em ../wwwroot/dashboard
```

**Dev server com hot reload:**
```bash
npm run dev        # localhost:5173, proxy /api/dashboard → localhost:4000
```

Após `npm run dev`, abra o navegador em `http://localhost:5173` — o Vite vai proxificar as chamadas de API para o backend em `localhost:4000` (configure `-CaptureBody` ou Tilt para que o McpServer esteja rodando em background).

**Build Docker:**
O `Dockerfile` do McpServer inclui um estágio `dashboard-build` (node:22-alpine) que constrói o React e overlay o output (`wwwroot/dashboard`) no estágio SDK do .NET antes do `dotnet publish`.

---

## Observabilidade

### OpenTelemetry

Ambas as APIs (PrecoAPI e ProdutoAPI) usam o BuildingBlock `Observability` que centraliza a configuração de tracing e métricas:

```csharp
builder.Services.AddObservability("PrecoAPI", builder.Configuration);
```

**Tracing (→ Jaeger via OTLP gRPC porta 4317):**
- Instrumentação automática: ASP.NET Core, HttpClient, Entity Framework Core
- `RecordException = true` — exceções aparecem nos spans
- EF Core: `SetDbStatementForText = true` — queries SQL visíveis nos traces

**Métricas (→ Prometheus via scraping do `/metrics`):**
- Instrumentação automática: ASP.NET Core, HttpClient, Runtime .NET
- `MapPrometheusScrapingEndpoint()` expõe o endpoint

**Captura de Body (feature flag):**

Ativada via `Otel:CaptureBody: true` no ConfigMap ou pelo parâmetro `-CaptureBody` do script de deploy:

```csharp
app.UseBodyCaptureTelemetry(); // registra Request + Response body middleware
```

Os bodies são truncados a 1000 caracteres e adicionados como tags nos spans: `http.request.body`, `http.response.body`.

### Propagação de Correlation ID

O `CorrelationHandler` (DelegatingHandler) propaga automaticamente o `traceparent` (W3C Trace Context) e o `X-Correlation-Id` nas chamadas HTTP de ProdutoAPI → PrecoAPI. Isso garante que a chamada apareça como um único trace distribuído no Jaeger.

### Stack de Observabilidade

```
PrecoAPI/ProdutoAPI
   │ OTLP gRPC :4317
   ▼
Jaeger (:16686)         ← traces e dependency graph
   
PrecoAPI/ProdutoAPI
   │ Prometheus scrape /metrics
   ▼
Prometheus (:9090)      ← métricas de request rate, latência, runtime

Promtail (DaemonSet)
   │ coleta logs dos pods
   ▼
Loki (:3100)            ← armazenamento de logs

Grafana (:3000)         ← dashboards para Prometheus + Loki
```

---

## Kubernetes

### Namespace

Todos os recursos vivem no namespace `mcp-apis`.

### Databases (StatefulSet)

Duas instâncias PostgreSQL 16 independentes, cada uma com:
- `StatefulSet` com 1 réplica e `PersistentVolumeClaim`
- `ConfigMap` com script SQL de inicialização do schema
- `Secret` com credenciais (base64)
- `Service` ClusterIP

### APIs (Deployment)

PrecoAPI e ProdutoAPI com:
- `replicas: 2`
- `imagePullPolicy: Never` — imagens buildadas localmente e importadas com `k3d image import`
- Resources com `requests` e `limits` definidos
- `readinessProbe` e `livenessProbe` via HTTP no `/health`
- `ConfigMap` para variáveis de ambiente (endpoint OTLP, URL da PrecoAPI, etc.)
- `Secret` para connection string do banco

### Ingress

nginx Ingress Controller com:
- Host-based routing: `precoapi.local`, `produtoapi.local`
- Annotation `nginx.ingress.kubernetes.io/rewrite-target: /$2`
- k3d expõe as portas `8080:80` e `8443:443` via loadbalancer no host

### RBAC do McpServer

```yaml
ServiceAccount: mcp-reader
ClusterRole: mcp-reader-cluster-role          # descoberta cluster-wide (feature 004)
  - pods, services, endpoints: get, list
  - deployments: get, list
Role: mcp-reader-role (namespace mcp-apis)
  - configmaps: get, list
  - configmaps [mcpserver-state]: update, patch   # persiste o switch de indexação
ClusterRoleBinding: mcp-reader-cluster-binding
RoleBinding: mcp-reader-binding
```

O McpServer usa `KubernetesClientConfiguration.InClusterConfig()` — não precisa de kubeconfig montado, o ServiceAccount é injetado automaticamente pelo Kubernetes. A única permissão de escrita é `update/patch` no ConfigMap `mcpserver-state` (restrita via `resourceNames`).

---

## Helm Charts

Os charts em `helm/` são uma alternativa idempotente ao deploy direto via `k8s/`. Cobrem apenas os três serviços da aplicação (não a stack de observabilidade).

Cada chart segue a estrutura padrão Helm com `_helpers.tpl` para labels comuns. O `values.yaml` expõe:

- `image.tag`, `image.pullPolicy`
- `replicaCount`
- `resources` (requests/limits)
- `ingress.enabled`, `ingress.host`
- Configurações de banco e observabilidade como values

Deploy via Helm:
```bash
./scripts/sh/deploy-helm.sh
```

---

## Scripts de Automação

### `scripts/ps/up-k8s.ps1` (principal)

Sobe o ambiente completo do zero ou a partir de um estado parcial. Cada etapa é idempotente.

```
Seção 0 — Valida ambiente WSL (wsl-check.ps1)
Seção 1 — Cria/inicia cluster k3d
Seção 2 — Instala nginx Ingress Controller
Seção 3 — Build Docker + k3d image import  (pulável com -SkipBuild)
Seção 4 — kubectl apply de todos os manifests
Seção 5 — kubectl rollout status de todos os workloads
Seção 6 — Health check completo com port-forwards  (pulável com -SkipHealthCheck)
```

Parâmetros:
```powershell
.\scripts\ps\up-k8s.ps1                   # deploy completo
.\scripts\ps\up-k8s.ps1 -SkipBuild        # pula build (imagens já importadas)
.\scripts\ps\up-k8s.ps1 -SkipHealthCheck  # pula verificação final
.\scripts\ps\up-k8s.ps1 -CaptureBody      # habilita captura de body no OTEL
```

**Detalhe de implementação:** Todos os comandos `k3d`, `kubectl` e `docker` são executados via WSL (instalados apenas no WSL, não no Windows). O helper `RunInWSL` usa `wsl.exe -- bash -lc` (login shell para carregar `~/.local/bin` no PATH).

### `scripts/ps/wsl-check.ps1`

Valida o ambiente WSL antes do deploy: versão do WSL, distro, tools disponíveis, Docker daemon. Executado automaticamente pela seção 0 do `up-k8s.ps1`.

### `scripts/ps/port-forward.ps1`

Inicia todos os port-forwards para acesso local sem precisar subir o ambiente completo.

### `scripts/ps/validate.ps1`

Smoke tests: chama os endpoints das APIs, verifica Prometheus, Grafana, Jaeger e o protocolo MCP (initialize handshake).

---

## Decisões de Implementação

### Por que dois bancos PostgreSQL separados?

Isolamento de dados entre serviços — reflete o padrão de microsserviços onde cada serviço é dono do seu schema. ProdutoAPI não acessa diretamente o banco de PrecoAPI; a integração é sempre via HTTP.

### Por que `EnsureCreatedAsync` em vez de migrations?

Para este projeto de estudo, `EnsureCreatedAsync` na inicialização é suficiente e elimina a necessidade de gerenciar arquivos de migration. O schema é criado automaticamente na primeira execução.

### Por que Scalar em vez de Swagger UI?

Scalar oferece uma UI mais moderna e é a recomendação do ecossistema .NET 10. Em PrecoAPI e ProdutoAPI, `app.MapOpenApi()` e `app.MapScalarApiReference()` são chamados incondicionalmente (sem checar `IsDevelopment()`) — as duas são APIs de exemplo criadas para testar o MCP, não serviços reais expostos a usuários finais, então `/scalar/v1` e `/openapi/v1.json` ficam acessíveis em qualquer ambiente, incluindo `Production`. As sondas de readiness/liveness usam o endpoint dedicado `/health`, independente do Scalar.

### Por que `imagePullPolicy: Never`?

As imagens são buildadas localmente e importadas diretamente no cache do k3d com `k3d image import`. Isso elimina a necessidade de um registry externo para desenvolvimento local.

### Por que o McpServer usa `InClusterConfig`?

O McpServer roda dentro do cluster. `InClusterConfig` usa automaticamente o ServiceAccount montado no pod (`/var/run/secrets/kubernetes.io/serviceaccount/`). O ServiceAccount `mcp-reader` tem apenas permissões de leitura (get, list) nos recursos necessários.

### Por que RunInWSL usa `bash -lc` (login shell)?

`bash -c` não carrega `~/.profile`, então ferramentas instaladas em `~/.local/bin` (k3d, kubectl) não seriam encontradas. `bash -lc` carrega o profile e garante o PATH correto.

### Por que o nome da função helper é `RunInWSL` e não `Wsl`?

PowerShell resolve nomes de função case-insensitivamente. Uma função chamada `Wsl` interceptaria chamadas a `wsl` dentro de scripts filhos (como `wsl-check.ps1` que chama `wsl -- bash -c ...`), causando recursão infinita ou argumentos corrompidos. O nome `RunInWSL` é suficientemente distinto do executável `wsl.exe`.

### Captura de Body como feature flag

A captura de body HTTP nos spans OTLP está desabilitada por padrão para evitar logging acidental de dados sensíveis. É ativada explicitamente via `-CaptureBody` no script de deploy ou via `Otel:CaptureBody: true` no ConfigMap.

---

## Portas e Acessos

| Serviço | Port-forward local | Acesso |
|---|---|---|
| PrecoAPI | `localhost:5001` | `http://localhost:5001/scalar/v1` |
| ProdutoAPI | `localhost:5002` | `http://localhost:5002/scalar/v1` |
| McpServer | `localhost:4000` | `http://localhost:4000/health` |
| **Dashboard** | `localhost:4000` | **`http://localhost:4000/dashboard`** |
| Jaeger UI | `localhost:16686` | `http://localhost:16686` |
| Prometheus | `localhost:9090` | `http://localhost:9090` |
| Grafana | `localhost:3000` | `http://localhost:3000` (admin/admin) |

Via Ingress (requer entrada no `/etc/hosts` ou `C:\Windows\System32\drivers\etc\hosts`):
```
127.0.0.1  precoapi.local produtoapi.local
```

---

## Configuração do Ambiente

### Pré-requisitos

- Windows com WSL2 (Ubuntu)
- No WSL: `k3d`, `kubectl`, `docker` em `~/.local/bin` ou `/usr/local/bin`
- Docker daemon rodando no WSL
- .NET 10 SDK (para desenvolvimento local)

### Subir o ambiente

```powershell
# Na raiz do repositório
.\scripts\ps\up-k8s.ps1
```

### Configurar MCP no VS Code

Adicionar ao `.vscode/mcp.json`:
```json
{
  "servers": {
    "mcp-apis": {
      "type": "http",
      "url": "http://localhost:4000/"
    }
  }
}
```

Inicie os port-forwards antes de usar o MCP:
```powershell
.\scripts\ps\port-forward.ps1
```

### Derrubar o ambiente

```powershell
.\scripts\ps\down-k8s.ps1
```

---

## Desenvolvimento com Tilt

O [Tilt](https://tilt.dev) é a forma recomendada para desenvolvimento ativo — ele automatiza o ciclo build → deploy → port-forward e faz hot reload ao detectar mudanças nos arquivos fonte.

```bash
tilt up    # sobe tudo e abre a UI em http://localhost:10350
tilt down  # encerra e remove os recursos do cluster
```

| Serviço | Port-forward (Tilt) |
|---|---|
| PrecoAPI | `http://localhost:8081` |
| ProdutoAPI | `http://localhost:8082` |
| McpServer | `http://localhost:4000` |
| **Dashboard** | **`http://localhost:4000/dashboard`** |
| Grafana | `http://localhost:3000` |
| Prometheus | `http://localhost:9090` |
| Jaeger UI | `http://localhost:16686` |

📄 Documentação completa: [`doc/tilt.md`](doc/tilt.md)
